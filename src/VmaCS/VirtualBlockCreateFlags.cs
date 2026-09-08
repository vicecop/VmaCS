namespace VmaCS;

/// <summary>
/// Flags to be passed as <c>VirtualBlockCreateInfo::flags</c>.
/// </summary>
[Flags]
public enum VirtualBlockCreateFlags
{
    /// <summary>
    /// Enables alternative, linear allocation algorithm in this virtual block.
    /// Specify this flag to enable linear allocation algorithm, which always creates new allocations after last one
    /// and doesn't reuse space from allocations freed in between. It trades memory consumption for simplified
    /// algorithm and data structure, which has better performance and uses less memory for metadata.
    /// By using this flag, you can achieve behavior of free-at-once, stack, ring buffer, and double stack.
    /// </summary>
    LinearAlgorithm = 0x00000001,

    /// <summary>
    /// A bit mask to extract only ALGORITHM bits from entire set of flags.
    /// </summary>
    AlgorithmMask = LinearAlgorithm
}
