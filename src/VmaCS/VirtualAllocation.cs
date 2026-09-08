namespace VmaCS;

/// <summary>
/// Represents a single allocation from a <see cref="VirtualBlock"/>.
/// </summary>
public class VirtualAllocation : IAllocation
{
    internal VulkanMemoryAllocator Allocator { get; }

    private bool _disposed;

    /// <summary>
    /// Indicates whether this allocation has been disposed.
    /// </summary>
    internal bool IsDisposed => _disposed;

    /// <summary>
    /// Marks this allocation as disposed.
    /// </summary>
    internal void MarkDisposed() => _disposed = true;

    /// <summary>
    /// Alignment of the allocation within its virtual block, in bytes.
    /// </summary>
    public long Alignment { get; internal set; }

    /// <summary>
    /// Suballocation type of this allocation.
    /// </summary>
    public SuballocationType SuballocationType { get; internal set; }

    internal VirtualBlock? Owner;
    internal bool Freed;

    /// <summary>
    /// Size of the allocation, in bytes.
    /// </summary>
    public long Size { get; protected set; }

    /// <summary>
    /// Memory type index. Always 0 for virtual allocations since no real Vulkan memory is used.
    /// </summary>
    public int MemoryTypeIndex => 0;

    /// <summary>
    /// Offset of this allocation within its parent virtual block, in bytes.
    /// </summary>
    public long Offset { get; internal set; }

    private object? _userData;

    /// <summary>
    /// User data associated with this allocation.
    /// </summary>
    public object? UserData
    {
        get => _userData;
        set
        {
            _userData = value;
            Owner?.Metadata.SetAllocationUserData(Offset, this);
        }
    }

    /// <summary>
    /// Name associated with this allocation.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="VirtualAllocation"/> class.
    /// </summary>
    /// <param name="allocator">The allocator that owns this allocation.</param>
    internal VirtualAllocation(VulkanMemoryAllocator allocator)
    {
        Allocator = allocator;
    }

    /// <summary>
    /// Initializes the allocation with its offset, size, alignment, and suballocation type.
    /// </summary>
    /// <param name="offset">Offset within the virtual block.</param>
    /// <param name="size">Size of the allocation.</param>
    /// <param name="alignment">Alignment of the allocation.</param>
    /// <param name="subType">Suballocation type.</param>
    internal void InitVirtualAllocation(long offset, long size, long alignment, SuballocationType subType)
    {
        Offset = offset;
        Size = size;
        Alignment = alignment;
        SuballocationType = subType;
    }

    /// <summary>
    /// Marks this allocation as freed.
    /// </summary>
    internal void MarkFreed() => Freed = true;

    /// <summary>
    /// Disposes this virtual allocation and frees it from its owning block.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases unmanaged and optionally managed resources used by this allocation.
    /// </summary>
    /// <param name="disposing">True to release managed resources; False to release only unmanaged resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (IsDisposed)
        {
            return;
        }

        MarkDisposed();

        if (disposing && !Freed)
        {
            Owner?.Metadata.Free(this);
            MarkFreed();
        }
    }
}
