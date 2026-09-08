using System.Runtime.CompilerServices;

using Silk.NET.Vulkan;

namespace VmaCS;

/// <summary>
/// Represents a custom memory pool.
/// </summary>
public sealed unsafe class VulkanMemoryPool : IDisposable
{
    /// <summary>
    /// The allocator that owns this pool.
    /// </summary>
    public VulkanMemoryAllocator Allocator { get; }

    /// <summary>
    /// Name associated with this pool.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Unique identifier for this pool.
    /// </summary>
    internal uint Id { get; }

    /// <summary>
    /// Internal block list managing the memory blocks of this pool.
    /// </summary>
    internal readonly BlockList BlockList;

    /// <summary>
    /// Additional pNext chain to be attached to VkMemoryAllocateInfo for every allocation made by this pool.
    /// </summary>
    internal void* MemoryAllocateNext;

    /// <summary>
    /// Initializes a new instance of the <see cref="VulkanMemoryPool"/> class.
    /// </summary>
    /// <param name="allocator">The allocator that owns this pool.</param>
    /// <param name="poolInfo">Pool creation parameters.</param>
    /// <param name="preferredBlockSize">Preferred block size for this pool.</param>
    internal VulkanMemoryPool(VulkanMemoryAllocator allocator,
                              in AllocationPoolCreateInfo poolInfo,
                              long preferredBlockSize)
    {
        Allocator = allocator;

        ref var tmpRef = ref Unsafe.As<uint, int>(ref allocator.NextPoolId);

        Id = (uint)Interlocked.Increment(ref tmpRef);

        if (Id == 0)
        {
            throw new OverflowException();
        }

        BlockList = new BlockList(
            allocator,
            this,
            poolInfo.MemoryTypeIndex,
            poolInfo.BlockSize != 0 ? poolInfo.BlockSize : preferredBlockSize,
            poolInfo.MinBlockCount,
            poolInfo.MaxBlockCount,
            (poolInfo.Flags & PoolCreateFlags.IgnoreBufferImageGranularity) != 0 ? 1 : allocator.BufferImageGranularity,
            poolInfo.BlockSize != 0,
            Math.Max(allocator.GetMemoryTypeMinAlignment(poolInfo.MemoryTypeIndex), poolInfo.MinAllocationAlignment),
            poolInfo.AllocationAlgorithmCreate ?? CreateMetaObject(poolInfo.Flags, allocator.DebugMargin));

        var res = BlockList.CreateMinBlocks();

        if (res.IsError() && Allocator.ThrowOnError)
        {
            throw new AllocationException("Unable to create minimum pool blocks", res);
        }

        MemoryAllocateNext = poolInfo.MemoryAllocateNext;
    }

    /// <summary>
    /// Disposes this pool and frees all underlying Vulkan device memory.
    /// </summary>
    public void Dispose()
    {
        BlockList.Dispose();
        Allocator.RemovePool(this);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Checks for memory corruption in this pool.
    /// </summary>
    /// <returns>The Vulkan Result of the corruption check.</returns>
    public Result CheckForCorruption()
    {
        if (!Allocator.DebugDetectCorruption)
        {
            return Result.ErrorFeatureNotPresent;
        }

        return BlockList.CheckCorruption();
    }

    /// <summary>
    /// Returns numeric statistics for this pool.
    /// </summary>
    /// <returns>The pool statistics.</returns>
    public PoolStats GetPoolStats() => BlockList.GetPoolStats();

    private static Func<long, IBlockMetadata> CreateMetaObject(PoolCreateFlags flags, long debugMargin)
    {
        if ((flags & PoolCreateFlags.LinearAlgorithm) != 0)
        {
            return size => new BlockMetadataLinear(size, debugMargin);
        }
        return size => new BlockMetadataTlsf(size, debugMargin);
    }
}
