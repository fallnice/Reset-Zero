using UnityEngine;

/// <summary>
/// 测试专用脚本
/// 运行时用键盘快捷键快速加物品/清背包
/// </summary>
public class DebugTool : MonoBehaviour
{
    private void Update()
    {
        // ========== 单物品添加 ==========
        if (Input.GetKeyDown(KeyCode.F1))
            Add(1001, 5);     // F1：+5个生铁

        if (Input.GetKeyDown(KeyCode.F2))
            Add(1002, 5);     // F2：+5个木头

        if (Input.GetKeyDown(KeyCode.F3))
            Add(1003, 5);     // F3：+5个胶带

        // ========== 批量添加 ==========
        if (Input.GetKeyDown(KeyCode.F4))
            AddAll(10);    // F4：每种基础材料 +10

        // ========== 清空背包 ==========
        if (Input.GetKeyDown(KeyCode.F5))
            ClearBag();
    }

    private void Add(int itemId, int count)
    {
        var inv = GameRoot.Instance?.Inventory;
        if (inv == null) return;

        bool ok = inv.AddItem(itemId, count);
        Debug.Log($"添加物品 ID:{itemId} ×{count}  {(ok ? "成功" : "失败")}");
    }

    private void AddAll(int count)
    {
        // 物品表 ID 
        int[] allIds = { 1001, 1002,1003, 1004, 1005,};
        foreach (int id in allIds)
            Add(id, count);
    }

    private void ClearBag()
    {
        var ctrl = GameRoot.Instance?.BagController;
        if (ctrl == null) return;

        var slots = ctrl.GetAllSlots();
        foreach (var slot in slots)
        {
            if (slot.ItemId != 0)
                ctrl.RemoveItem(slot.ItemId, slot.ItemCount);
        }
        Debug.Log("背包已清空");
    }
}
