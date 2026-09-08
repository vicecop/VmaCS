namespace VmaCS;

/// <summary>
/// Parameters for creating a virtual allocation.
/// </summary>
public struct VirtualAllocationCreateInfo
{
    /// <summary>
    /// Size of the allocation, in bytes.
    /// </summary>
    public required long Size;

    /// <summary>
    /// Minimum alignment of the allocation, in bytes. Optional, leave 0 for default.
    /// </summary>
    public long Alignment;

    /// <summary>
    /// Use combination of <see cref="VirtualAllocationCreateFlags"/>.
    /// </summary>
    public VirtualAllocationCreateFlags Flags;

    /// <summary>
    /// User data to associate with the allocation. Optional.
    /// </summary>
    public object? UserData;
}
