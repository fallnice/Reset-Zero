using System;
using Combat;
using Core;
using DAO;
using Model;
using Role;
using UnityEngine;

namespace Controller
{
    /// <summary> 管理单任务接取、事件进度、交付奖励与持久化。 </summary>
    public sealed class QuestController
    {
        private readonly IInventory _inventory;
        private readonly IQuestRepository _questRepository;
        private QuestDefinition _definition;
        private int[] _progress = Array.Empty<int>();
        private QuestState _state;
        private bool _isSubscribed;

        private EventBus.SubscriptionToken _pickupToken;
        private EventBus.SubscriptionToken _craftToken;
        private EventBus.SubscriptionToken _deathToken;
        private EventBus.SubscriptionToken _queryToken;

        /// <summary> 当前任务定义。 </summary>
        public QuestDefinition Definition => _definition;

        /// <summary> 当前任务状态。 </summary>
        public QuestState State => _state;

        /// <summary> 当前目标进度只读副本。 </summary>
        public int[] Progress => (int[])_progress.Clone();

        /// <summary> 当前任务是否已经满足全部目标。 </summary>
        public bool IsReadyToTurnIn => _state == QuestState.ReadyToTurnIn;

        /// <summary> 创建任务控制器并注入背包与存档依赖。 </summary>
        public QuestController(IInventory inventory, IQuestRepository questRepository)
        {
            _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            _questRepository = questRepository ?? throw new ArgumentNullException(nameof(questRepository));
        }

        /// <summary> 初始化任务定义、读取存档并开始监听领域事件。 </summary>
        public bool Initialize(QuestDefinition definition)
        {
            if (definition == null || !definition.IsValid())
            {
                Debug.LogWarning("[QuestController] 任务定义为空或参数非法");
                return false;
            }

            DisposeSubscriptions();
            _definition = definition;
            _progress = new int[definition.Objectives.Count];

            try
            {
                _questRepository.EnsureTable();
                QuestProgressData saved = _questRepository.Load(definition.QuestId);
                ApplySavedProgress(saved);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[QuestController] 任务存档初始化失败: " + exception.Message);
                _definition = null;
                _progress = Array.Empty<int>();
                return false;
            }

            Subscribe();
            PublishChanged();
            return true;
        }

        /// <summary> 接取尚未开始的任务。 </summary>
        public bool TryAccept()
        {
            if (!EnsureInitialized() || _state != QuestState.NotAccepted) return false;

            if (!TrySave(QuestState.Active, _progress)) return false;
            _state = QuestState.Active;
            PublishChanged();
            EventBus.Emit(EventName.Quest_Accepted, _definition.QuestId);
            EventBus.Emit(EventName.UI_Toast, "已接取任务：" + _definition.Title);
            return true;
        }

        /// <summary> 交付已完成目标的任务并发放物品奖励。 </summary>
        public bool TryTurnIn()
        {
            if (!EnsureInitialized() || _state != QuestState.ReadyToTurnIn) return false;
            bool rewarded = _inventory.TryAddItemAtomic(
                _definition.RewardItemId,
                _definition.RewardCount,
                () => _questRepository.Save(_definition.QuestId, _definition.DefinitionVersion,
                    QuestState.Completed, _progress),
                out InventoryOperationResult result);
            if (!rewarded)
            {
                string message = result == InventoryOperationResult.InsufficientSpace
                    ? "背包空间不足，无法领取任务奖励"
                    : "任务奖励发放失败，请稍后重试";
                Debug.LogWarning("[QuestController] 任务交付原子提交失败: " + result);
                EventBus.Emit(EventName.UI_Toast, message);
                return false;
            }

            _state = QuestState.Completed;
            PublishChanged();
            EventBus.Emit(EventName.Quest_Completed, _definition.QuestId,
                _definition.RewardItemId, _definition.RewardCount);
            return true;
        }

        /// <summary> 根据当前状态执行接取或交付。 </summary>
        public bool TryInteract()
        {
            if (_state == QuestState.NotAccepted) return TryAccept();
            if (_state == QuestState.ReadyToTurnIn) return TryTurnIn();
            return false;
        }

        /// <summary> 返回任务交互提示。 </summary>
        public string GetInteractionPrompt()
        {
            if (_definition == null) return "任务未就绪";
            switch (_state)
            {
                case QuestState.NotAccepted:
                    return "接取任务：" + _definition.Title;
                case QuestState.Active:
                    return "任务进行中：" + _definition.Title;
                case QuestState.ReadyToTurnIn:
                    return "交付任务：" + _definition.Title;
                case QuestState.Completed:
                    return "任务已完成：" + _definition.Title;
                default:
                    return _definition.Title;
            }
        }

        /// <summary> 释放领域事件订阅。 </summary>
        public void Dispose()
        {
            DisposeSubscriptions();
        }

        /// <summary> 应用存档并对目标数量和状态做防御性修正。 </summary>
        private void ApplySavedProgress(QuestProgressData saved)
        {
            bool isCompatibleSave = saved != null
                && saved.DefinitionVersion == _definition.DefinitionVersion;
            _state = isCompatibleSave ? saved.State : QuestState.NotAccepted;
            if (!Enum.IsDefined(typeof(QuestState), _state))
                _state = QuestState.NotAccepted;

            for (int i = 0; i < _progress.Length; i++)
            {
                int savedValue = isCompatibleSave && saved.Progress != null && i < saved.Progress.Length
                    ? saved.Progress[i]
                    : 0;
                _progress[i] = Mathf.Clamp(savedValue, 0, _definition.Objectives[i].RequiredCount);
            }

            if (_state == QuestState.Completed) return;
            if (_state != QuestState.NotAccepted)
                _state = AreAllObjectivesComplete() ? QuestState.ReadyToTurnIn : QuestState.Active;
        }

        /// <summary> 订阅拾取、制造和角色死亡领域事件。 </summary>
        private void Subscribe()
        {
            if (_isSubscribed) return;
            _pickupToken = EventBus.Subscribe(EventName.Pickup_Completed, HandlePickupCompleted);
            _craftToken = EventBus.Subscribe(EventName.Craft_Success, HandleCraftSuccess);
            _deathToken = EventBus.Subscribe(EventName.Character_Died, HandleCharacterDied);
            _queryToken = EventBus.Subscribe(EventName.Quest_Query, HandleQuestQuery);
            _isSubscribed = true;
        }

        /// <summary> 统计由玩家成功放入背包的拾取数量。 </summary>
        private void HandlePickupCompleted(object[] args)
        {
            if (_state != QuestState.Active || args == null || args.Length < 3
                || !(args[1] is int itemId) || !(args[2] is int count) || count <= 0)
                return;
            ApplyProgress(QuestObjectiveType.Collect, itemId, count);
        }

        /// <summary> 统计制造成功产生的成品数量。 </summary>
        private void HandleCraftSuccess(object[] args)
        {
            if (_state != QuestState.Active || args == null || args.Length < 3
                || !(args[1] is int itemId) || !(args[2] is int count) || count <= 0)
                return;
            ApplyProgress(QuestObjectiveType.Craft, itemId, count);
        }

        /// <summary> 统计由玩家造成致死伤害的敌对角色死亡。 </summary>
        private void HandleCharacterDied(object[] args)
        {
            if (_state != QuestState.Active || args == null || args.Length < 2
                || !(args[0] is CharacterRoot victim) || !(args[1] is DamageContext context))
                return;
            if (victim.Faction != Faction.Enemy || context.sourceFaction != Faction.Player
                || context.attacker == null)
                return;

            CharacterRoot attacker = context.attacker.GetComponentInParent<CharacterRoot>();
            if (attacker == null || !attacker.IsPlayerControlled || attacker.Faction != Faction.Player)
                return;
            ApplyProgress(QuestObjectiveType.Kill, 0, 1);
        }

        /// <summary> 响应 View 查询并重新广播当前任务状态。 </summary>
        private void HandleQuestQuery(object[] args)
        {
            PublishChanged();
        }

        /// <summary> 更新匹配目标并在状态变化后持久化和广播。 </summary>
        private void ApplyProgress(QuestObjectiveType type, int targetId, int amount)
        {
            int[] nextProgress = (int[])_progress.Clone();
            bool hasChanged = false;
            for (int i = 0; i < _definition.Objectives.Count; i++)
            {
                QuestObjective objective = _definition.Objectives[i];
                bool matchesTarget = objective.TargetId == targetId
                    || (type == QuestObjectiveType.Kill && objective.TargetId == 0);
                if (objective.Type != type || !matchesTarget || nextProgress[i] >= objective.RequiredCount)
                    continue;

                nextProgress[i] = Mathf.Min(objective.RequiredCount, nextProgress[i] + amount);
                hasChanged = true;
            }
            if (!hasChanged) return;

            QuestState nextState = AreAllObjectivesComplete(nextProgress)
                ? QuestState.ReadyToTurnIn
                : QuestState.Active;
            if (!TrySave(nextState, nextProgress)) return;

            _progress = nextProgress;
            _state = nextState;
            PublishChanged();
        }

        /// <summary> 检查全部目标是否完成。 </summary>
        private bool AreAllObjectivesComplete(int[] progress = null)
        {
            int[] values = progress ?? _progress;
            for (int i = 0; i < _definition.Objectives.Count; i++)
            {
                if (values[i] < _definition.Objectives[i].RequiredCount) return false;
            }
            return true;
        }

        /// <summary> 尝试保存任务事实，失败时不修改运行时状态。 </summary>
        private bool TrySave(QuestState state, int[] progress)
        {
            try
            {
                _questRepository.Save(
                    _definition.QuestId, _definition.DefinitionVersion, state, progress);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[QuestController] 保存任务进度失败: " + exception.Message);
                EventBus.Emit(EventName.UI_Toast, "任务进度保存失败，请稍后重试");
                return false;
            }
        }

        /// <summary> 广播当前任务定义、状态和进度副本。 </summary>
        private void PublishChanged()
        {
            if (_definition == null) return;
            EventBus.Emit(EventName.Quest_Changed, _definition, _state, Progress);
        }

        /// <summary> 检查任务控制器是否已经完成初始化。 </summary>
        private bool EnsureInitialized()
        {
            if (_definition != null) return true;
            Debug.LogWarning("[QuestController] 任务尚未初始化");
            return false;
        }

        /// <summary> 成对释放全部领域事件订阅。 </summary>
        private void DisposeSubscriptions()
        {
            _pickupToken?.Dispose();
            _craftToken?.Dispose();
            _deathToken?.Dispose();
            _queryToken?.Dispose();
            _pickupToken = null;
            _craftToken = null;
            _deathToken = null;
            _queryToken = null;
            _isSubscribed = false;
        }
    }
}
