using System.Collections.Generic;

namespace Role.Core
{
    /// <summary>
    /// 数据黑板——子控制器之间共享状态的轻量容器
    /// 约定键名格式：模块前缀_字段名，如 "IK_LookTarget"、"Weapon_CurrentType"
    /// </summary>
    public static class Blackboard
    {
        private static readonly Dictionary<string, object> Data = new Dictionary<string, object>();

        public static void Set<T>(string key, T value)
        {
            Data[key] = value;
        }

        public static T Get<T>(string key, T defaultValue = default)
        {
            return Data.TryGetValue(key, out var v) && v is T tv ? tv : defaultValue;
        }

        public static bool Has(string key) => Data.ContainsKey(key);

        public static void Remove(string key) => Data.Remove(key);

        /// <summary> 清空所有数据（角色销毁时调用） </summary>
        public static void Clear() => Data.Clear();
    }
}
