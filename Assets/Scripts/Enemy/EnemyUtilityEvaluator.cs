using UnityEngine;

namespace Enemy
{
    /// <summary>
    /// Utility AI 评分器（4.3）——在「已经在战斗」的前提下，决定用哪种战术打。
    ///
    /// 职责边界（重要，避免和 HFSM 混淆）：
    ///   HFSM（EnemyBrain + Enemy/States）决定大方向：待机 / 巡逻 / 追击 / 攻击 / 调查 / 归位 / 眩晕 / 死亡。
    ///   Utility 只决定战斗时的打法：压上(Engage) / 对峙(Hold) / 包抄(Flank) / 后撤(Retreat)。
    /// 它不会把敌人从追击切到调查，也不会替 HFSM 决定何时进入攻击。
    ///
    /// 评分公式（分数越大越想做，nd = 距离 / detectionRange）：
    ///   Engage  = 1 - nd                              → 离得越近越想压上
    ///   Hold    = 0.55 + 无视线 0.2 + 有掩体 0.1       → 看不见目标或需要等冷却时停下对峙
    ///   Flank   = (0.5 + 有掩体 0.15 + 有视线 0.1) × (1 - nd×0.4)，冷却中 -0.5 → 正面吃亏时绕侧翼
    ///   Retreat = (1 - 生命比例) + 威胁加成            → 越残血、威胁越高越想退
    ///
    /// 防抖与退出：
    ///   选中后保持 tacticalCommitSeconds，避免评分抖动导致左右横跳；
    ///   撤退额外受 retreatDurationSeconds（单次最长）与 retreatCooldownSeconds（两次间隔）约束，
    ///   否则残血敌人会一直退、再也不打人。
    /// </summary>
    public sealed class EnemyUtilityEvaluator
    {
        private readonly EnemyConfig _config;

        public EnemyUtilityEvaluator(EnemyConfig config)
        {
            _config = config;
        }

        /// <summary> 每帧评估一次；结果写入黑板，由 Chase 行为读取执行 </summary>
        public void Evaluate(EnemyAIContext context)
        {
            if (context == null || _config == null || context.Blackboard == null) return;

            EnemyAIBlackboard blackboard = context.Blackboard;

            // 没有目标时不存在战术选择：回到压上并清掉可能残留的战术点
            if (!blackboard.HasTarget)
            {
                blackboard.ForceTactical(EnemyTacticalChoice.Engage, 0f);
                return;
            }

            bool isCritical = blackboard.HealthRatio <= _config.retreatHealthRatio;

            // 撤退中：超时或冷却结束就回压上，避免永久逃跑
            if (blackboard.TacticalChoice == EnemyTacticalChoice.Retreat)
            {
                if (context.RetreatElapsed >= _config.retreatDurationSeconds
                    || context.RetreatCooldownRemaining <= 0f)
                {
                    context.RetreatCooldownRemaining = _config.retreatCooldownSeconds;
                    blackboard.ForceTactical(EnemyTacticalChoice.Engage, _config.tacticalCommitSeconds);
                    return;
                }

                // 撤退点只在进入撤退的第一帧算一次：
                // 若每帧按当前自身位置重算，点会跟着自己后退，敌人永远到不了、也永远退不出来。
                if (context.RetreatElapsed <= 0f)
                {
                    blackboard.SetTacticalDecision(
                        EnemyTacticalChoice.Retreat,
                        blackboard.TacticalCommitRemaining,
                        ComputeRetreatPoint(context),
                        Vector3.zero);
                }
                return;
            }

            bool shouldRetreat = isCritical || blackboard.Threat == ThreatLevel.High;

            // 紧急情况优先：残血或高威胁立刻后撤；冷却中允许残血强制覆盖，否则残血敌人会一直贴脸送。
            if (shouldRetreat && (context.RetreatCooldownRemaining <= 0f || isCritical))
            {
                blackboard.SetTacticalDecision(
                    EnemyTacticalChoice.Retreat,
                    0f,
                    ComputeRetreatPoint(context),
                    Vector3.zero);
                context.RetreatElapsed = 0f;
                return;
            }

            // 非紧急时保留当前战术一段时间，避免评分抖动导致左右横跳
            if (blackboard.TacticalCommitRemaining > 0f)
            {
                WriteTacticalPoints(context);
                return;
            }

            blackboard.SetTacticalDecision(
                SelectBest(context),
                _config.tacticalCommitSeconds,
                Vector3.zero,
                Vector3.zero);
            WriteTacticalPoints(context);
        }

        /// <summary> 选分数最高的战术 </summary>
        private EnemyTacticalChoice SelectBest(EnemyAIContext context)
        {
            EnemyAIBlackboard blackboard = context.Blackboard;

            float engage = ScoreEngage(blackboard);
            float hold = ScoreHold(blackboard);
            float flank = ScoreFlank(context);
            float retreat = ScoreRetreat(blackboard);

            EnemyTacticalChoice best = EnemyTacticalChoice.Engage;
            float bestScore = engage;

            if (hold > bestScore)
            {
                best = EnemyTacticalChoice.Hold;
                bestScore = hold;
            }
            if (flank > bestScore)
            {
                best = EnemyTacticalChoice.Flank;
                bestScore = flank;
            }
            if (retreat > bestScore)
            {
                best = EnemyTacticalChoice.Retreat;
            }

            return best;
        }

        /// <summary> 压上：越近越想打 </summary>
        private float ScoreEngage(EnemyAIBlackboard blackboard)
        {
            return 1f - NormalizedDistance(blackboard);
        }

        /// <summary>
        /// 对峙：看不见目标时停下来等，别盲冲；有掩体时也倾向守着掩体而不是压出去。
        /// </summary>
        private float ScoreHold(EnemyAIBlackboard blackboard)
        {
            float score = 0.55f;
            if (!blackboard.HasLineOfSight) score += 0.2f;
            if (blackboard.Cover != CoverStatus.None) score += 0.1f;
            return score;
        }

        /// <summary>
        /// 包抄：刚用过要先等冷却；有掩体/有视线时更愿意绕。
        /// 距离越远绕行成本越高，用 (1 - nd×0.4) 衰减。
        /// </summary>
        private float ScoreFlank(EnemyAIContext context)
        {
            EnemyAIBlackboard blackboard = context.Blackboard;

            float score = 0.5f;
            if (context.FlankCooldownRemaining > 0f) score -= 0.5f;
            if (blackboard.Cover != CoverStatus.None) score += 0.15f;
            if (blackboard.HasLineOfSight) score += 0.1f;

            score *= 1f - NormalizedDistance(blackboard) * 0.4f;
            return score;
        }

        /// <summary> 后撤：越残血越想退，威胁高再加成 </summary>
        private float ScoreRetreat(EnemyAIBlackboard blackboard)
        {
            float score = 1f - blackboard.HealthRatio;
            if (blackboard.Threat == ThreatLevel.High) score += 0.3f;
            return score;
        }

        /// <summary> 距离按 detectionRange 归一化到 0~1 </summary>
        private float NormalizedDistance(EnemyAIBlackboard blackboard)
        {
            if (_config.detectionRange <= 0f) return 0f;
            return Mathf.Clamp01(blackboard.DistanceToTarget / _config.detectionRange);
        }

        /// <summary> 只有撤退/包抄需要目标点，避免压上或对峙时也算向量 </summary>
        private void WriteTacticalPoints(EnemyAIContext context)
        {
            EnemyTacticalChoice choice = context.Blackboard.TacticalChoice;

            if (choice == EnemyTacticalChoice.Retreat)
            {
                context.Blackboard.SetTacticalDecision(choice, context.Blackboard.TacticalCommitRemaining,
                    ComputeRetreatPoint(context), Vector3.zero);
                return;
            }

            if (choice == EnemyTacticalChoice.Flank)
            {
                context.Blackboard.SetTacticalDecision(choice, context.Blackboard.TacticalCommitRemaining,
                    Vector3.zero, ComputeFlankPoint(context));
                context.TacticalElapsed = 0f;
            }
        }

        /// <summary>
        /// 撤退点：从目标指向自己的方向往外推一段，并夹在防区内。
        /// 只在进入撤退的第一帧调用一次（见 Evaluate 的 TacticalElapsed 判断）：
        /// 每帧重算会让它随自身同步后退，敌人就永远退不到点、也退不出这个战术。
        /// </summary>
        private Vector3 ComputeRetreatPoint(EnemyAIContext context)
        {
            if (context.Character == null) return context.HomePosition;

            Vector3 selfPosition = context.Character.transform.position;
            Vector3 away = selfPosition - context.Blackboard.Target.transform.position;
            away.y = 0f;
            if (away.sqrMagnitude < 0.0001f)
                away = -context.Character.transform.forward;
            away.Normalize();

            float retreatDistance = Mathf.Max(_config.attackRange * 1.5f, _config.flankDistance);
            Vector3 point = selfPosition + away * retreatDistance;

            if (_config.returnHomeDistance > 0f)
            {
                Vector3 fromHome = point - context.HomePosition;
                fromHome.y = 0f;
                if (fromHome.sqrMagnitude > _config.returnHomeDistance * _config.returnHomeDistance)
                    point = context.HomePosition + fromHome.normalized * _config.returnHomeDistance;
            }

            return point;
        }

        /// <summary> 包抄点：目标侧前方（把「目标→自己」绕 Y 轴转 flankAngle）的一段距离 </summary>
        private Vector3 ComputeFlankPoint(EnemyAIContext context)
        {
            if (context.Character == null || context.Blackboard.Target == null)
                return context.HomePosition;

            Transform target = context.Blackboard.Target.transform;
            Vector3 toSelf = context.Character.transform.position - target.position;
            toSelf.y = 0f;
            if (toSelf.sqrMagnitude < 0.0001f)
                toSelf = -target.forward;
            toSelf.Normalize();

            Vector3 flankDirection = Quaternion.Euler(0f, _config.flankAngle, 0f) * toSelf;
            float distance = Mathf.Max(_config.attackRange, _config.flankDistance);
            return target.position + flankDirection * distance;
        }
    }
}
