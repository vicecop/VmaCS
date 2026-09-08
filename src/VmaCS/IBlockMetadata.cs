using Silk.NET.Vulkan;

namespace VmaCS;

/// <summary>
/// Public interface for block metadata.
/// </summary>
public interface IBlockMetadata
{
    /// <summary>
    /// Total size of the memory block, in bytes.
    /// </summary>
    public long Size { get; }

    /// <summary>
    /// Total number of active allocations in this block.
    /// </summary>
    public int AllocationCount { get; }

    /// <summary>
    /// Sum of sizes of all free regions in this block, in bytes.
    /// </summary>
    public long SumFreeSize { get; }

    /// <summary>
    /// Size of the largest unused range in this block, in bytes.
    /// </summary>
    public long UnusedRangeSizeMax { get; }

    /// <summary>
    /// Indicates whether the block is empty - contains 0 allocations and all its space is free.
    /// </summary>
    public bool IsEmpty { get; }

    /// <summary>
    /// Checks whether a buffer-image granularity conflict is possible for the given allocation type.
    /// </summary>
    /// <param name="bufferImageGranularity">The buffer-image granularity to check.</param>
    /// <param name="type">The suballocation type being considered.</param>
    /// <returns>True if a conflict is possible; false otherwise.</returns>
    public bool IsBufferImageGranularityConflictPossible(long bufferImageGranularity, ref SuballocationType type);

    /// <summary>
    /// Validates the internal state of the metadata.
    /// </summary>
    public void Validate();

    /// <summary>
    /// Buffer-image granularity used by this block metadata.
    /// </summary>
    public long BufferImageGranularity { get; internal set; }

    /// <summary>
    /// Debug margin size used to detect memory corruption, in bytes.
    /// </summary>
    public long DebugMargin { get; internal set; }

    /// <summary>
    /// Calculates statistics for this block metadata.
    /// </summary>
    /// <param name="outInfo">Receives the calculated statistics.</param>
    public void CalcAllocationStatInfo(out StatInfo outInfo);

    /// <summary>
    /// Adds the statistics of this block metadata to the given pool statistics.
    /// </summary>
    /// <param name="stats">The accumulator to update.</param>
    public void AddPoolStats(ref PoolStats stats);

    /// <summary>
    /// Tries to create an allocation request for the given context.
    /// </summary>
    /// <param name="context">The allocation context describing the request.</param>
    /// <param name="request">Receives the allocation request if successful.</param>
    /// <returns>True if a suitable free range was found; false otherwise.</returns>
    public bool TryCreateAllocationRequest(in AllocationContext context, out AllocationRequest request);

    /// <summary>
    /// Checks for memory corruption in the debug margins of this block.
    /// </summary>
    /// <param name="blockDataPointer">Pointer to the block's data for margin checking.</param>
    /// <returns>The Vulkan Result of the corruption check.</returns>
    public Result CheckCorruption(nuint blockDataPointer);

    /// <summary>
    /// Allocates a suballocation at the given request.
    /// </summary>
    /// <param name="request">The allocation request to fulfill.</param>
    /// <param name="type">The suballocation type.</param>
    /// <param name="allocSize">The size of the allocation.</param>
    /// <param name="allocation">The allocation object to associate with the suballocation.</param>
    public void Alloc(in AllocationRequest request, SuballocationType type, long allocSize, IAllocation allocation);

    /// <summary>
    /// Frees a suballocation.
    /// </summary>
    /// <param name="allocation">The allocation to free.</param>
    public void Free(IAllocation allocation);

    /// <summary>
    /// Frees a suballocation at the given offset.
    /// </summary>
    /// <param name="offset">Offset of the suballocation to free.</param>
    public void FreeAtOffset(long offset);

    /// <summary>
    /// Frees all suballocations in this block.
    /// </summary>
    public void Clear();

    /// <summary>
    /// Returns all active allocations in this block.
    /// </summary>
    /// <returns>An enumeration of allocations.</returns>
    public IEnumerable<IAllocation> GetAllocations();

    /// <summary>
    /// Sets the user data of one allocation to another.
    /// </summary>
    /// <param name="from">The source allocation.</param>
    /// <param name="to">The target allocation.</param>
    public void SetAllocationUserData(IAllocation from, IAllocation to);

    /// <summary>
    /// Sets the user data of an allocation at the given offset.
    /// </summary>
    /// <param name="offset">Offset of the allocation to update.</param>
    /// <param name="to">The allocation to associate.</param>
    public void SetAllocationUserData(long offset, IAllocation to);

    /// <summary>
    /// Returns the size of the next free region after the given allocation.
    /// </summary>
    /// <param name="allocation">The allocation to query.</param>
    /// <returns>The size of the next free region, in bytes.</returns>
    public long GetNextFreeRegionSize(IAllocation allocation);
}
