using System.Collections.Generic;
using Combat;
using Core;
using Interaction;
using Role;
using UnityEngine;

namespace Loot
{
    /// <summary> 监听所属敌人的生命事件，每轮生命只结算一次配置掉落。 </summary>
    [DisallowMultipleComponent]
    public sealed class EnemyLootDrop : MonoBehaviour
    {
        private const float DEFAULT_DROP_RADIUS = 0.65f;
        private const float GROUND_PROBE_HEIGHT = 1.5f;
        private const float GROUND_PROBE_DISTANCE = 4f;

        [Header("掉落配置")]
        [SerializeField] private LootTable lootTable;
        [Tooltip("可选；应包含 PickupItem 与 Collider。为空时生成带球体占位外观的可拾取物。")]
        [SerializeField] private GameObject pickupPrefab;
        [Min(0f)] [SerializeField] private float dropRadius = DEFAULT_DROP_RADIUS;
        [SerializeField] private LayerMask groundMask = 1;

        private readonly List<LootDropResult> _dropResults = new List<LootDropResult>(4);
        private readonly RaycastHit[] _groundHits = new RaycastHit[16];
        private CharacterRoot _character;
        private HealthController _health;
        private bool _isSubscribed;
        private bool _needsSubscription;
        private bool _hasDropped;
        private uint _handledDeathSequence;
        private bool _hasWarnedMissingCharacter;
        private bool _hasWarnedMissingHealth;

        /// <summary> 当前敌人是否已经在本轮生命中完成掉落结算。 </summary>
        public bool HasDropped => _hasDropped;

        /// <summary> 当前掉落表，供场景审计读取。 </summary>
        public LootTable Table => lootTable;

        /// <summary> 当前配置的拾取预制体；为空时使用运行时占位物。 </summary>
        public GameObject PickupPrefab => pickupPrefab;

        /// <summary> 地面投射使用的层掩码，供场景审计验证。 </summary>
        public LayerMask GroundMask => groundMask;

        private void OnEnable()
        {
            _needsSubscription = true;
            if (_health != null) Subscribe();
        }

        private void Start()
        {
            TryResolveAndSubscribe();
        }

        private void Update()
        {
            if (_needsSubscription) TryResolveAndSubscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        /// <summary> 延迟解析角色生命组件，规避跨组件 Awake 顺序不确定。 </summary>
        private void TryResolveAndSubscribe()
        {
            if (_isSubscribed)
            {
                _needsSubscription = false;
                return;
            }
            if (_character == null)
                _character = GetComponentInParent<CharacterRoot>();
            if (_character == null)
            {
                if (!_hasWarnedMissingCharacter)
                {
                    Debug.LogWarning("[EnemyLootDrop] 未找到 CharacterRoot，掉落功能不可用", this);
                    _hasWarnedMissingCharacter = true;
                }
                return;
            }

            _health = _character.Health;
            if (_health == null)
            {
                if (!_hasWarnedMissingHealth)
                {
                    Debug.LogWarning("[EnemyLootDrop] CharacterRoot 缺少 HealthController，掉落功能不可用", this);
                    _hasWarnedMissingHealth = true;
                }
                return;
            }

            Subscribe();
        }

        /// <summary> 订阅已缓存生命组件的死亡与重置事件。 </summary>
        private void Subscribe()
        {
            if (_isSubscribed || _health == null) return;
            _health.DiedWithContext += HandleDied;
            _health.HealthReset += HandleHealthReset;
            _isSubscribed = true;
            _needsSubscription = false;

            if (_health.IsDead && _health.DeathSequence > _handledDeathSequence)
            {
                // 禁用期间可能已完成复活与再次死亡，序列前进即代表新生命周期。
                _hasDropped = false;
                HandleDied(_health.LastLethalContext);
            }
            else
            {
                _hasDropped = _health.IsDead;
            }
        }

        /// <summary> 解除生命事件订阅，避免对象禁用或销毁后残留回调。 </summary>
        private void Unsubscribe()
        {
            _needsSubscription = false;
            if (!_isSubscribed || _health == null) return;
            _health.DiedWithContext -= HandleDied;
            _health.HealthReset -= HandleHealthReset;
            _isSubscribed = false;
        }

        /// <summary> 首次死亡时结算并生成世界拾取物。 </summary>
        private void HandleDied(DamageContext context)
        {
            if (_health != null && _health.DeathSequence <= _handledDeathSequence) return;
            if (_hasDropped) return;

            _hasDropped = true;
            if (_health != null) _handledDeathSequence = _health.DeathSequence;

            if (lootTable == null || !lootTable.HasValidEntry())
            {
                Debug.LogWarning("[EnemyLootDrop] LootTable 未配置或没有合法条目，本次死亡无掉落", this);
                return;
            }

            lootTable.RollInto(_dropResults);
            int spawnedCount = 0;
            for (int i = 0; i < _dropResults.Count; i++)
            {
                LootDropResult result = _dropResults[i];
                if (TrySpawnPickup(result, i, _dropResults.Count))
                    spawnedCount++;
            }

            EventBus.Emit(EventName.Loot_Dropped, _character, context.attacker, spawnedCount);
        }

        /// <summary> 复活后开启下一轮掉落资格。 </summary>
        private void HandleHealthReset()
        {
            _hasDropped = false;
        }

        /// <summary> 生成并配置单个世界拾取物。 </summary>
        private bool TrySpawnPickup(LootDropResult result, int index, int total)
        {
            if (result.ItemId <= 0 || result.Count <= 0) return false;

            Vector3 position = ResolveDropPosition(index, total);
            GameObject instance = pickupPrefab != null
                ? Instantiate(pickupPrefab, position, Quaternion.identity)
                : CreateFallbackPickup(position);
            if (instance == null)
            {
                Debug.LogWarning($"[EnemyLootDrop] 无法生成掉落物 ID:{result.ItemId}", this);
                return false;
            }

            PickupItem pickup = instance.GetComponent<PickupItem>();
            if (pickup == null || !pickup.Configure(result.ItemId, result.Count))
            {
                Debug.LogWarning($"[EnemyLootDrop] 拾取预制体缺少 PickupItem 或配置失败 ID:{result.ItemId}", instance);
                Destroy(instance);
                return false;
            }

            EventBus.Emit(EventName.Loot_ItemSpawned, _character, pickup, result.ItemId, result.Count);
            return true;
        }

        /// <summary> 将多项掉落分散到死亡位置周围，并向地面投射修正高度。 </summary>
        private Vector3 ResolveDropPosition(int index, int total)
        {
            Vector3 center = _character != null ? _character.transform.position : transform.position;
            float angle = total > 0 ? index * Mathf.PI * 2f / total : 0f;
            Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * dropRadius;
            Vector3 probeOrigin = center + offset + Vector3.up * GROUND_PROBE_HEIGHT;
            int hitCount = Physics.RaycastNonAlloc(
                probeOrigin,
                Vector3.down,
                _groundHits,
                GROUND_PROBE_DISTANCE,
                groundMask,
                QueryTriggerInteraction.Ignore);
            if (hitCount <= 0) return center + offset + Vector3.up * 0.2f;

            float nearestDistance = float.MaxValue;
            Vector3 nearestPoint = center + offset;
            for (int i = 0; i < hitCount; i++)
            {
                Transform hitTransform = _groundHits[i].collider.transform;
                if (_character != null && hitTransform.IsChildOf(_character.transform)) continue;
                if (_groundHits[i].distance >= nearestDistance) continue;

                nearestDistance = _groundHits[i].distance;
                nearestPoint = _groundHits[i].point;
            }
            return nearestDistance < float.MaxValue
                ? nearestPoint + Vector3.up * 0.15f
                : center + offset + Vector3.up * 0.2f;
        }

        /// <summary> 无预制体时创建可交互的可见占位物，保证代码链路仍可运行。 </summary>
        private static GameObject CreateFallbackPickup(Vector3 position)
        {
            GameObject instance = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            instance.name = "RuntimeLootPickup";
            instance.transform.position = position;
            instance.transform.localScale = Vector3.one * 0.35f;
            SphereCollider collider = instance.GetComponent<SphereCollider>();
            if (collider != null) collider.isTrigger = false;
            instance.AddComponent<PickupItem>();
            return instance;
        }

        /// <summary> 在编辑器中约束掉落半径，避免负值导致位置反转。 </summary>
        private void OnValidate()
        {
            dropRadius = Mathf.Max(0f, dropRadius);
        }
    }
}
