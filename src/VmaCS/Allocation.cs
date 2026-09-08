using System.Diagnostics;
using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace VmaCS;

/// <summary>
/// Represents a Vulkan memory allocation.
/// </summary>
public unsafe abstract class Allocation : IAllocation
{
    /// <summary>
    /// The allocator that owns this allocation.
    /// </summary>
    internal VulkanMemoryAllocator Allocator { get; }

    /// <summary>
    /// The Vulkan API interface used by this allocation.
    /// </summary>
    protected Vk VkApi => Allocator.VkApi;

    private bool _disposed;

    /// <summary>
    /// Indicates whether this allocation has been disposed.
    /// </summary>
    internal bool IsDisposed => _disposed;

    internal void MarkDisposed() => _disposed = true;

    /// <summary>
    /// Base handle for Win32 external memory, if applicable.
    /// </summary>
    internal nint Win32BaseHandle;

    /// <summary>
    /// Memory type index of the Vulkan memory type this allocation was created from.
    /// </summary>
    protected int MemoryTypeIndexValue;

    /// <summary>
    /// Alignment of the allocation within its memory block, in bytes.
    /// </summary>
    public long Alignment { get; internal set; }

    /// <summary>
    /// Suballocation type of this allocation.
    /// </summary>
    public SuballocationType SuballocationType { get; internal set; }

    /// <summary>
    /// Count of active maps on this allocation. Negative values indicate persistently mapped allocations.
    /// </summary>
    protected int MapCount;

    /// <summary>
    /// Indicates whether this allocation is persistently mapped.
    /// </summary>
    internal bool IsPersistantMapped => MapCount < 0;

    /// <summary>
    /// The VkDeviceMemory object backing this allocation.
    /// </summary>
    public abstract DeviceMemory DeviceMemory { get; }

    /// <summary>
    /// Pointer to the mapped data, if the allocation is persistently mapped. Otherwise null.
    /// </summary>
    public abstract void* MappedData { get; }

    /// <summary>
    /// Size of the allocation, in bytes.
    /// </summary>
    public long Size { get; protected set; }

    /// <summary>
    /// Index of the Vulkan memory type this allocation was created from.
    /// </summary>
    public int MemoryTypeIndex => MemoryTypeIndexValue;

    /// <summary>
    /// Memory property flags of the Vulkan memory type this allocation was created from.
    /// </summary>
    public MemoryPropertyFlags MemoryPropertyFlags => Allocator.GetMemoryTypeProperties(MemoryTypeIndex);

    /// <summary>
    /// Offset of this allocation within its parent memory block, in bytes.
    /// </summary>
    public long Offset { get; internal set; }

    /// <summary>
    /// User data associated with this allocation.
    /// </summary>
    public object? UserData { get; set; }

    /// <summary>
    /// Name associated with this allocation.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Size of the VkDeviceMemory block backing this allocation, in bytes. For non-dedicated allocations, this is the block size; for dedicated allocations, it equals <see cref="Size"/>.
    /// </summary>
    public long BlockSize => this is BlockAllocation blockAlloc ? blockAlloc.Block.MetaData.Size : Size;

    /// <summary>
    /// True if this allocation has its own dedicated VkDeviceMemory block; false if it is a suballocation within a larger block.
    /// </summary>
    public bool IsDedicated => this is DedicatedAllocation;

    internal Allocation(VulkanMemoryAllocator allocator)
    {
        Allocator = allocator;
    }

    /// <summary>
    /// Maps the memory represented by this allocation and returns a pointer to it.
    /// </summary>
    /// <param name="data">Receives the pointer to the mapped memory.</param>
    /// <returns>The Vulkan Result of the map operation.</returns>
    public abstract Result Map(out void* data);

    /// <summary>
    /// Unmaps the memory represented by this allocation, previously mapped using <see cref="Map(out void*)"/>.
    /// </summary>
    public abstract void Unmap();

    /// <summary>
    /// Disposes this allocation and frees the underlying Vulkan memory.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases unmanaged resources used by this allocation.
    /// </summary>
    /// <param name="disposing">True if called from <see cref="Dispose()"/>; false if called from the finalizer.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (IsDisposed)
        {
            return;
        }

        if (disposing)
        {
            Allocator.FreeMemory(this);
        }
    }

    /// <summary>
    /// Binds this allocation to a <c>VkBuffer</c>.
    /// </summary>
    /// <param name="buffer">The buffer to bind.</param>
    /// <returns>The Vulkan Result of the bind operation.</returns>
    public Result BindBufferMemory(Buffer buffer)
    {
        Debug.Assert(Offset >= 0);

        return Allocator.BindVulkanBuffer(buffer, DeviceMemory, Offset, null);
    }

    /// <summary>
    /// Binds this allocation to a <c>VkBuffer</c> with an optional local offset and pNext chain.
    /// </summary>
    /// <param name="buffer">The buffer to bind.</param>
    /// <param name="allocationLocalOffset">Local offset within the allocation to bind at.</param>
    /// <param name="pNext">Optional pNext chain for extended binding behavior.</param>
    /// <returns>The Vulkan Result of the bind operation.</returns>
    public Result BindBufferMemory(Buffer buffer, long allocationLocalOffset, void* pNext = null)
    {
        if ((ulong)allocationLocalOffset >= (ulong)Size)
        {
            throw new ArgumentOutOfRangeException(nameof(allocationLocalOffset));
        }

        return Allocator.BindVulkanBuffer(buffer, DeviceMemory, Offset + allocationLocalOffset, pNext);
    }

    /// <summary>
    /// Binds this allocation to a <c>VkImage</c>.
    /// </summary>
    /// <param name="image">The image to bind.</param>
    /// <returns>The Vulkan Result of the bind operation.</returns>
    public Result BindImageMemory(Image image) => Allocator.BindVulkanImage(image, DeviceMemory, Offset, null);

    /// <summary>
    /// Binds this allocation to a <c>VkImage</c> with an optional local offset and pNext chain.
    /// </summary>
    /// <param name="image">The image to bind.</param>
    /// <param name="allocationLocalOffset">Local offset within the allocation to bind at.</param>
    /// <param name="pNext">Optional pNext chain for extended binding behavior.</param>
    /// <returns>The Vulkan Result of the bind operation.</returns>
    public Result BindImageMemory(Image image, long allocationLocalOffset, void* pNext = null)
    {
        if ((ulong)allocationLocalOffset >= (ulong)Size)
        {
            throw new ArgumentOutOfRangeException(nameof(allocationLocalOffset));
        }

        return Allocator.BindVulkanImage(image, DeviceMemory, Offset + allocationLocalOffset, pNext);
    }

    /// <summary>
    /// Flushes a range of this allocation to make it visible to the device.
    /// </summary>
    /// <param name="offset">Offset within the allocation to start flushing, in bytes.</param>
    /// <param name="size">Number of bytes to flush.</param>
    /// <returns>The Vulkan Result of the flush operation.</returns>
    public Result Flush(long offset, long size) => Allocator.FlushOrInvalidateAllocation(this, offset, size, CacheOperation.Flush);

    /// <summary>
    /// Invalidates a range of this allocation to make it visible to the host.
    /// </summary>
    /// <param name="offset">Offset within the allocation to start invalidating, in bytes.</param>
    /// <param name="size">Number of bytes to invalidate.</param>
    /// <returns>The Vulkan Result of the invalidate operation.</returns>
    public Result Invalidate(long offset, long size) => Allocator.FlushOrInvalidateAllocation(this, offset, size, CacheOperation.Invalidate);

    /// <summary>
    /// Copies data from host memory into this allocation, mapping it if necessary.
    /// </summary>
    /// <param name="source">Source data to copy.</param>
    /// <param name="allocationLocalOffset">Offset within the allocation to write to, in bytes.</param>
    /// <returns>The Vulkan Result of the operation.</returns>
    public Result CopyMemoryToAllocation(ReadOnlySpan<byte> source, long allocationLocalOffset)
    {
        if (source.Length == 0)
        {
            return Result.Success;
        }

        var res = Map(out var mapped);

        if (res.IsError())
        {
            return res;
        }

        try
        {
            unsafe
            {
                var dst = (byte*)mapped + allocationLocalOffset;
                source.CopyTo(new Span<byte>(dst, source.Length));
            }
        }
        finally
        {
            Unmap();
        }

        return Flush(allocationLocalOffset, source.Length);
    }

    /// <summary>
    /// Copies data from this allocation into host memory, mapping it if necessary.
    /// </summary>
    /// <param name="allocationLocalOffset">Offset within the allocation to read from, in bytes.</param>
    /// <param name="destination">Destination buffer to receive the data.</param>
    /// <returns>The Vulkan Result of the operation.</returns>
    public Result CopyAllocationToMemory(long allocationLocalOffset, Span<byte> destination)
    {
        if (destination.Length == 0)
        {
            return Result.Success;
        }

        var res = Map(out var mapped);

        if (res.IsError())
        {
            return res;
        }

        try
        {
            var inv = Invalidate(allocationLocalOffset, destination.Length);

            if (inv.IsError())
            {
                return inv;
            }

            unsafe
            {
                var src = (byte*)mapped + allocationLocalOffset;
                new ReadOnlySpan<byte>(src, destination.Length).CopyTo(destination);
            }
        }
        finally
        {
            Unmap();
        }

        return Result.Success;
    }
}

