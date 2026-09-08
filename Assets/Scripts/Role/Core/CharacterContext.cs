using Combat;

namespace Role.Core
{
    /// <summary>
    /// 角色共享上下文——聚合单个角色所有实例级运行时数据（瞬时移动状态 + 战斗属性）。
    /// 由 CharacterRoot 在 Awake 时组装，注入给状态机与各控制器，取代根节点逐个暴露字段；
    /// 未来敌人 AI 的感知/决策数据（目标、视线、威胁等）也可归入此上下文。
    /// </summary>
    public class CharacterContext
    {
        /// <summary> 瞬时移动状态（当前水平移动速度、空中垂直速度） </summary>
        public CharacterRuntimeData Runtime { get; }

        /// <summary> 实例级战斗属性（攻击/攻速倍率），玩家由 BonusController 累加 </summary>
        public CombatStats CombatStats { get; }

        public CharacterContext(CharacterRuntimeData runtime, CombatStats combatStats)
        {
            Runtime = runtime;
            CombatStats = combatStats;
        }
    }
}
