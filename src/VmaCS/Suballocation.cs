namespace VmaCS;

/// <summary>
/// Represents a suballocation within a memory block. Internal use only.
/// </summary>
internal struct Suballocation
{
    /// <summary>
    /// Offset of the suballocation within its memory block, in bytes.
    /// </summary>
    internal required long Offset;

    /// <summary>
    /// Size of the suballocation, in bytes.
    /// </summary>
    internal required long Size;

    /// <summary>
    /// Allocation associated with this suballocation, if any.
    /// </summary>
    internal IAllocation? Allocation;

    /// <summary>
    /// Type of the suballocation.
    /// </summary>
    internal SuballocationType Type;
}