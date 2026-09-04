using System.Collections.Generic;
using UnityEngine;

namespace Core
{
    public class UIManager : MonoBehaviour
    {
        public static UIManager Instance;

        // 缓存所有面板
        private Dictionary<string, MonoBehaviour> _panelDict = new Dictionary<string, MonoBehaviour>();

        // 当前处于打开状态的模态面板名（模态面板打开时阻断角色战斗输入）
        private readonly HashSet<string> _activeModalPanels = new HashSet<string>();

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else
            {
                Destroy(gameObject);
            }
        }

        /// <summary>
        /// 注册面板
        /// GameRoot初始化调用
        /// </summary>
        public void RegisterPanel<T>(T panel) where T : MonoBehaviour
        {
            string panelName = typeof(T).Name;
            if (!_panelDict.ContainsKey(panelName))
            {
                _panelDict.Add(panelName, panel);
            }
        }

        /// <summary>
        /// 打开面板
        /// </summary>
        public void OpenPanel<T>() where T : MonoBehaviour
        {
            string panelName = typeof(T).Name;
            if (!_panelDict.TryGetValue(panelName, out var panel)) return;

            panel.gameObject.SetActive(true);

            // 模态面板打开时进入 UI 模态：从「无模态」变为「有模态」才广播一次
            if (panel is IModalPanel)
            {
                bool wasModalClosed = _activeModalPanels.Count == 0;
                _activeModalPanels.Add(panelName);
                if (wasModalClosed)
                    EventBus.Emit(EventName.UI_ModalChanged, true);
            }
        }

        /// <summary>
        /// 关闭面板
        /// </summary>
        public void ClosePanel<T>() where T : MonoBehaviour
        {
            string panelName = typeof(T).Name;
            if (!_panelDict.TryGetValue(panelName, out var panel)) return;

            panel.gameObject.SetActive(false);

            // 最后一个模态面板关闭时退出 UI 模态
            if (panel is IModalPanel && _activeModalPanels.Remove(panelName) && _activeModalPanels.Count == 0)
                EventBus.Emit(EventName.UI_ModalChanged, false);
        }

        /// <summary>
        /// 获取面板
        /// </summary>
        public T GetPanel<T>() where T : MonoBehaviour
        {
            string panelName = typeof(T).Name;
            _panelDict.TryGetValue(panelName, out var panel);
            return panel as T;
        }
    }
}