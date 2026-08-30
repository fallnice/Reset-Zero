using Core;
using UnityEngine;
using View;

namespace Controller
{
    /// <summary>
    /// UI控制器：负责所有UI面板的开关、切换等交互逻辑
    /// 作为业务层与UIManager之间的桥梁，View层不直接调用UIManager
    /// </summary>
    public class UIController
    {
        /// <summary>
        /// 初始化UI状态：设置各面板默认显示/隐藏状态
        /// 由GameRoot在所有面板注册完毕后调用
        /// </summary>
        public void Init()
        {
            // 默认全部关闭，进入游戏后由玩家通过快捷键（I/C/Tab）打开
            CloseAllPanels();
        }

        // ─── 背包面板 ───────────────────────────────────────────

        public void OpenBagPanel()
        {
            UIManager.Instance.OpenPanel<BagView>();
        }

        public void CloseBagPanel()
        {
            UIManager.Instance.ClosePanel<BagView>();
        }

        // ─── 制作面板 ───────────────────────────────────────────

        public void OpenCraftPanel()
        {
            UIManager.Instance.OpenPanel<CraftView>();
        }

        public void CloseCraftPanel()
        {
            UIManager.Instance.ClosePanel<CraftView>();
        }

        // ─── 切换逻辑 ───────────────────────────────────────────

        /// <summary>
        /// 切换背包/制作面板（二选一互斥显示）
        /// </summary>
        public void ToggleBagAndCraft()
        {
            var bagView = UIManager.Instance.GetPanel<BagView>();
            if (bagView == null) return;

            bool bagActive = bagView.gameObject.activeSelf;
            if (bagActive)
            {
                CloseBagPanel();
                OpenCraftPanel();
            }
            else
            {
                OpenBagPanel();
                CloseCraftPanel();
            }
        }

        /// <summary>
        /// 关闭所有已注册面板
        /// </summary>
        public void CloseAllPanels()
        {
            CloseBagPanel();
            CloseCraftPanel();
        }
    }
}
