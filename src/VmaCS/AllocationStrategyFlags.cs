namespace VmaCS;

/// <summary>
/// Allocation strategy flags.
/// </summary>
[Flags]
public enum AllocationStrategyFlags
{
    /// <summary>
    /// Alias to STRATEGY_MIN_MEMORY_BIT. Allocation strategy that chooses smallest possible free range for the allocation
    /// to minimize memory usage and fragmentation, possibly at the expense of allocation time.
    /// </summary>
    BestFit = 0x1,

    /// <summary>
    /// Allocation strategy that chooses the worst fit (largest free range) for the allocation.
    /// </summary>
    WorstFit = 0x2,

    /// <summary>
    /// Alias to STRATEGY_MIN_TIME_BIT. Allocation strategy that chooses first suitable free range for the allocation -
    /// not necessarily in terms of the smallest offset but the one that is easiest and fastest to find to minimize allocation time, possibly at the expense of allocation quality.
    /// </summary>
    FirstFit = 0x4,

    /// <summary>
    /// Alias to BestFit. Allocation strategy that chooses smallest possible free range for the allocation
    /// to minimize memory usage and fragmentation, possibly at the expense of allocation time.
    /// </summary>
    MinMemory = BestFit,

    /// <summary>
    /// Alias to FirstFit. Allocation strategy that chooses first suitable free range for the allocation.
    /// </summary>
    MinTime = FirstFit,

    /// <summary>
    /// Alias to WorstFit. Allocation strategy that chooses the worst fit (largest free range) for the allocation.
    /// </summary>
    MinFragmentation = WorstFit
}
