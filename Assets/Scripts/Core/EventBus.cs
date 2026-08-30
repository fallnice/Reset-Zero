using System;
using System.Collections.Generic;
using UnityEngine;

namespace Core
{
    /// <summary>
    /// 全局事件总线 — 系统间通信的唯一枢纽
    /// 
    /// 设计原则：
    /// 1. 零直接引用：任何系统不需要知道其他系统的存在
    /// 2. 错误隔离：一个监听器抛异常不影响其他监听器
    /// 3. 可追踪：Debug 模式打印所有事件流
    /// 4. 可取消订阅：防止内存泄漏
    /// 
    /// 使用示例：
    ///   // 订阅
    ///   var token = EventBus.Subscribe("item_add", args => {
    ///       int itemId = (int)args[0];
    ///       int count = (int)args[1];
    ///   });
    ///   // 取消订阅
    ///   token.Dispose();
    /// 
    ///   // 发送事件
    ///   EventBus.Emit("item_add", 5, 10);
    /// </summary>
    public static class EventBus
    {
        // ===== 订阅令牌 =====

        /// <summary>
        /// 订阅令牌 — Dispose 即取消订阅，比手动 Unsubscribe 更安全
        /// </summary>
        public sealed class SubscriptionToken : IDisposable
        {
            private readonly string _eventName;
            private readonly Action<object[]> _handler;
            private bool _disposed;

            internal SubscriptionToken(string eventName, Action<object[]> handler)
            {
                _eventName = eventName;
                _handler = handler;
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                Unsubscribe(_eventName, _handler);
            }
        }

        // ===== 核心数据 =====

        private static readonly Dictionary<string, List<Action<object[]>>> _listeners = new();

        /// <summary>
        /// 开启后 Console 打印所有事件发送（开发调试用）
        /// </summary>
        public static bool DebugMode { get; set; } = false;


        // ===== 公共 API =====

        /// <summary>
        /// 订阅事件
        /// </summary>
        /// <param name="eventName">事件名，建议用常量管理（如 EventName.ItemAdd）</param>
        /// <param name="handler">回调：object[] 按 Emit 传参顺序排列</param>
        /// <returns>订阅令牌，Dispose 即取消</returns>
        public static SubscriptionToken Subscribe(string eventName, Action<object[]> handler)
        {
            if (string.IsNullOrEmpty(eventName))
            {
                Debug.LogError("[EventBus] 事件名不能为空");
                return null;
            }
            if (handler == null)
            {
                Debug.LogError($"[EventBus] 订阅 [{eventName}] 时 handler 为 null");
                return null;
            }

            if (!_listeners.ContainsKey(eventName))
                _listeners[eventName] = new List<Action<object[]>>();

            _listeners[eventName].Add(handler);

            if (DebugMode)
                Debug.Log($"[EventBus] 订阅 [+{_listeners[eventName].Count}] {eventName}");

            return new SubscriptionToken(eventName, handler);
        }

        /// <summary>
        /// 取消订阅（推荐用 SubscriptionToken.Dispose() 代替手动调用）
        /// </summary>
        public static void Unsubscribe(string eventName, Action<object[]> handler)
        {
            if (!_listeners.TryGetValue(eventName, out var list)) return;

            list.Remove(handler);

            if (DebugMode)
                Debug.Log($"[EventBus] 取消订阅 [-{list.Count}] {eventName}");

            if (list.Count == 0)
                _listeners.Remove(eventName);
        }

        /// <summary>
        /// 发送事件（无参数版本）
        /// </summary>
        public static void Emit(string eventName)
        {
            EmitInternal(eventName, Array.Empty<object>());
        }

        /// <summary>
        /// 发送事件（带参数，类型安全由调用方保证）
        /// </summary>
        /// <example>
        /// EventBus.Emit("item_add", itemId, count);
        /// EventBus.Emit("damage", sourceId, targetId, damageValue);
        /// </example>
        public static void Emit(string eventName, params object[] args)
        {
            EmitInternal(eventName, args);
        }

        /// <summary>
        /// 清空所有订阅（场景切换时调用）
        /// </summary>
        public static void Clear()
        {
            if (DebugMode)
                Debug.Log($"[EventBus] 清空全部订阅（共 {_listeners.Count} 个事件）");

            _listeners.Clear();
        }

        /// <summary>
        /// 打印当前所有订阅（调试用）
        /// </summary>
        public static void DumpSubscriptions()
        {
            Debug.Log("===== EventBus 当前订阅 =====");
            foreach (var kv in _listeners)
                Debug.Log($"  [{kv.Value.Count}] {kv.Key}");
            Debug.Log("=============================");
        }


        // ===== 内部实现 =====

        private static void EmitInternal(string eventName, object[] args)
        {
            if (DebugMode)
                Debug.Log($"[EventBus] 发送 → {eventName} 参数:{string.Join(", ", args)}");

            if (!_listeners.TryGetValue(eventName, out var list)) return;

            // 复制一份再遍历：防止回调里修改列表导致迭代异常
            var snapshot = list.ToArray();

            foreach (var handler in snapshot)
            {
                try
                {
                    handler.Invoke(args);
                }
                catch (Exception ex)
                {
                    // 错误隔离：一个监听器崩了不影响其他
                    Debug.LogError($"[EventBus] 处理 [{eventName}] 时异常: {ex.Message}\n{ex.StackTrace}");
                }
            }
        }
    }
}
