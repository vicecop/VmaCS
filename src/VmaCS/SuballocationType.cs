namespace VmaCS;

/// <summary>
/// Type of a suballocation within a memory block.
/// </summary>
public enum SuballocationType
{
    /// <summary>
    /// Free suballocation, not in use.
    /// </summary>
    Free = 0,

    /// <summary>
    /// Unknown suballocation type.
    /// </summary>
    Unknown,

    /// <summary>
    /// Suballocation for a buffer.
    /// </summary>
    Buffer,

    /// <summary>
    /// Suballocation for an image of unknown layout.
    /// </summary>
    ImageUnknown,

    /// <summary>
    /// Suballocation for a linear image.
    /// </summary>
    ImageLinear,

    /// <summary>
    /// Suballocation for an optimal image.
    /// </summary>
    ImageOptimal
}
