namespace Core
{
    /// <summary> 库存写操作失败原因，供交互、UI 和任务系统提供准确反馈。 </summary>
    public enum InventoryOperationResult
    {
        Success,
        InvalidArgument,
        NotInitialized,
        ItemNotFound,
        InsufficientSpace,
        InsufficientItems,
        PersistenceFailed
    }
}
