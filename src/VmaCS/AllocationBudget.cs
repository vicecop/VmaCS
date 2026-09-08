namespace VmaCS;

/// <summary>
/// Represents budget information for a Vulkan memory heap.
/// </summary>
public struct AllocationBudget
{
    /// <summary>
    /// Total size of all VkDeviceMemory blocks allocated by this allocator from this memory type, in bytes.
    /// </summary>
    public required long BlockBytes;

    /// <summary>
    /// Total size of all allocations created by this allocator from this memory type, in bytes.
    /// </summary>
    public required long AllocationBytes;

    /// <summary>
    /// Current memory usage reported by the device, in bytes. This is an estimation if VK_EXT_memory_budget is not used.
    /// </summary>
    public long Usage;

    /// <summary>
    /// Memory budget reported by the device, in bytes. This is an estimation if VK_EXT_memory_budget is not used.
    /// </summary>
    public long Budget;

    /// <summary>
    /// Total number of VkDeviceMemory blocks allocated by this allocator from this memory type.
    /// </summary>
    public long BlockCount;

    /// <summary>
    /// Total number of allocations created by this allocator from this memory type.
    /// </summary>
    public long AllocationCount;
}
