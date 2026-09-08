namespace VmaCS;

/// <summary>
/// Flags to be passed as VmaDefragmentationInfo::flags.
/// </summary>
[Flags]
public enum DefragmentationFlags
{
    /// <summary>
    /// Use simple but fast algorithm for defragmentation.
    /// May not achieve best results but will require least time to compute and least allocations to copy.
    /// </summary>
    AlgorithmFast = 0x1,

    /// <summary>
    /// Default defragmentation algorithm, applied also when no ALGORITHM flag is specified.
    /// Offers a balance between defragmentation quality and the amount of allocations and bytes that need to be moved.
    /// </summary>
    AlgorithmBalanced = 0x2,

    /// <summary>
    /// Perform full defragmentation of memory.
    /// Can result in notably more time to compute and allocations to copy, but will achieve best memory packing.
    /// </summary>
    AlgorithmFull = 0x4,

    /// <summary>
    /// Use the most robust algorithm at the cost of time to compute and number of copies to make.
    /// Only available when bufferImageGranularity is greater than 1, since it aims to reduce alignment issues between different types of resources.
    /// Otherwise falls back to same behavior as ALGORITHM_FULL_BIT.
    /// </summary>
    AlgorithmExtensive = 0x8,

    /// <summary>
    /// A bit mask to extract only ALGORITHM bits from entire set of flags.
    /// </summary>
    AlgorithmMask = AlgorithmFast | AlgorithmBalanced | AlgorithmFull | AlgorithmExtensive
}
