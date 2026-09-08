namespace VmaCS;

/// <summary>
/// Parameters for creating a custom memory pool.
/// </summary>
public unsafe struct AllocationPoolCreateInfo
{
    /// <summary>
    /// Vulkan memory type index to allocate this pool from.
    /// </summary>
    public required int MemoryTypeIndex;

    /// <summary>
    /// Use combination of <see cref="PoolCreateFlags"/>.
    /// </summary>
    public PoolCreateFlags Flags;

    /// <summary>
    /// Size of a single VkDeviceMemory block to be allocated as part of this pool, in bytes.
    /// Optional. Leave 0 (default) not to impose any limit.
    /// </summary>
    public long BlockSize;

    /// <summary>
    /// Minimum number of blocks to be always allocated in this pool, even if they stay empty.
    /// </summary>
    public int MinBlockCount;

    /// <summary>
    /// Maximum number of blocks that can be allocated in this pool.
    /// Optional. Leave 0 (default) for no limit.
    /// </summary>
    public int MaxBlockCount;

    /// <summary>
    /// Additional minimum alignment to be used for all allocations created from this pool.
    /// Can be 0. Leave 0 (default) not to impose any additional alignment.
    /// </summary>
    public long MinAllocationAlignment;

    /// <summary>
    /// Optional factory function used to create the block metadata for this pool.
    /// </summary>
    public Func<long, IBlockMetadata>? AllocationAlgorithmCreate;

    /// <summary>
    /// Additional pNext chain to be attached to VkMemoryAllocateInfo used for every allocation made by this pool.
    /// Optional.
    /// </summary>
    public void* MemoryAllocateNext;
}
