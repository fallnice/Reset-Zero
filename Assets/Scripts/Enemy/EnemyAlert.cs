using Combat;
using Role;
using UnityEngine;

namespace Enemy
{
    /// <summary> 单跳敌群报警值对象；只表达发送者亲眼确认的目标位置，不授予接收者目标锁定。 </summary>
    public readonly struct EnemyAlert
    {
        /// <summary> 创建一条带全局序号和绝对过期时间的报警。 </summary>
        public EnemyAlert(
            long sequence,
            CharacterRoot sender,
            CharacterRoot target,
            Vector3 position,
            Faction faction,
            float expiresAt,
            float suspicionBoost)
        {
            Sequence = sequence;
            Sender = sender;
            Target = target;
            Position = position;
            Faction = faction;
            ExpiresAt = expiresAt;
            SuspicionBoost = suspicionBoost;
        }

        public long Sequence { get; }
        public CharacterRoot Sender { get; }
        public CharacterRoot Target { get; }
        public Vector3 Position { get; }
        public Faction Faction { get; }
        public float ExpiresAt { get; }
        public float SuspicionBoost { get; }
    }
}
