namespace VmaCS;

/// <summary>
/// Numeric statistics for a custom memory pool.
/// </summary>
public struct PoolStats
{
    /// <summary>
    /// Total size of all VkDeviceMemory blocks in this pool, in bytes.
    /// </summary>
    public long Size;

    /// <summary>
    /// Total size of unused space in this pool, in bytes.
    /// </summary>
    public long UnusedSize;

    /// <summary>
    /// Total number of allocations in this pool.
    /// </summary>
    public int AllocationCount;

    /// <summary>
    /// Total number of unused ranges in this pool.
    /// </summary>
    public int UnusedRangeCount;

    /// <summary>
    /// Size of the largest unused range in this pool, in bytes.
    /// </summary>
    public long UnusedRangeSizeMax;

    /// <summary>
    /// Total number of VkDeviceMemory blocks in this pool.
    /// </summary>
    public int BlockCount;

    /// <summary>
    /// Total size of all allocations in this pool, in bytes. Equals Size - UnusedSize.
    /// </summary>
    public long AllocationBytes;

    /// <summary>
    /// Size of the smallest allocation in this pool, in bytes.
    /// </summary>
    public long AllocationSizeMin;

    /// <summary>
    /// Size of the largest allocation in this pool, in bytes.
    /// </summary>
    public long AllocationSizeMax;
}
