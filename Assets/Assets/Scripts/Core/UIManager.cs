using System.Collections.Generic;
using UnityEngine;

namespace Core
{
    public class UIManager : MonoBehaviour
    {
        public static UIManager Instance;

        // 缓存所有面板
        private Dictionary<string, MonoBehaviour> _panelDict = new Dictionary<string, MonoBehaviour>();

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
            if (_panelDict.TryGetValue(panelName, out var panel))
            {
                panel.gameObject.SetActive(true);
            }
        }

        /// <summary>
        /// 关闭面板
        /// </summary>
        public void ClosePanel<T>() where T : MonoBehaviour
        {
            string panelName = typeof(T).Name;
            if (_panelDict.TryGetValue(panelName, out var panel))
            {
                panel.gameObject.SetActive(false);
            }
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