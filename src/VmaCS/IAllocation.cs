namespace VmaCS;

/// <summary>
/// Public interface for an allocation.
/// </summary>
public interface IAllocation : IDisposable
{
    /// <summary>
    /// Size of the allocation, in bytes.
    /// </summary>
    public long Size { get; }

    /// <summary>
    /// Index of the Vulkan memory type this allocation was created from.
    /// </summary>
    public int MemoryTypeIndex { get; }

    /// <summary>
    /// Offset of this allocation within its parent memory block, in bytes.
    /// </summary>
    public long Offset { get; }

    /// <summary>
    /// Alignment of the allocation within its memory block, in bytes.
    /// </summary>
    public long Alignment { get; }

    /// <summary>
    /// Suballocation type of this allocation.
    /// </summary>
    public SuballocationType SuballocationType { get; }

    /// <summary>
    /// User data associated with this allocation.
    /// </summary>
    public object? UserData { get; set; }

    /// <summary>
    /// Name associated with this allocation.
    /// </summary>
    public string? Name { get; set; }
}
