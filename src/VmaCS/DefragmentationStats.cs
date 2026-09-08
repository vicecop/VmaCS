namespace VmaCS;

/// <summary>
/// Statistics from a defragmentation operation.
/// </summary>
public struct DefragmentationStats
{
    /// <summary>
    /// Total number of bytes moved during defragmentation.
    /// </summary>
    public ulong BytesMoved;

    /// <summary>
    /// Total number of bytes freed during defragmentation.
    /// </summary>
    public ulong BytesFreed;

    /// <summary>
    /// Total number of allocations moved during defragmentation.
    /// </summary>
    public ulong AllocationsMoved;

    /// <summary>
    /// Total number of allocations freed during defragmentation.
    /// </summary>
    public ulong AllocationsFreed;

    /// <summary>
    /// Total number of VkDeviceMemory blocks freed during defragmentation.
    /// </summary>
    public ulong DeviceMemoryBlocksFreed;
}
