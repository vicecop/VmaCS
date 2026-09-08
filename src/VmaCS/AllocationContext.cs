namespace VmaCS;

/// <summary>
/// Internal context describing an allocation request.
/// </summary>
public struct AllocationContext
{
    /// <summary>
    /// Buffer-image granularity that must be respected for this allocation.
    /// </summary>
    public required long BufferImageGranularity;

    /// <summary>
    /// Size of the allocation to be made, in bytes.
    /// </summary>
    public required long AllocationSize;

    /// <summary>
    /// Minimum alignment required for the allocation, in bytes.
    /// </summary>
    public required long AllocationAlignment;

    /// <summary>
    /// Allocation strategy to be used for this allocation.
    /// </summary>
    public required AllocationStrategyFlags Strategy;

    /// <summary>
    /// Suballocation type of the allocation.
    /// </summary>
    public required SuballocationType SuballocationType;

    /// <summary>
    /// If true, the allocation will be created from the upper stack in a double stack pool.
    /// </summary>
    public bool UpperAddress;
}