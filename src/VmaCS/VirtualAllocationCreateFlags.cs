namespace VmaCS;

/// <summary>
/// Flags to be passed as <c>VirtualAllocationCreateInfo::flags</c>.
/// These strategy flags are binary compatible with equivalent flags in <c>AllocationCreateFlags</c>.
/// </summary>
[Flags]
public enum VirtualAllocationCreateFlags
{
    /// <summary>
    /// Allocation will be created from upper stack in a double stack pool.
    /// This flag is only allowed for virtual blocks created with <c>VirtualBlockCreateFlags.LinearAlgorithm</c> flag.
    /// </summary>
    UpperAddress = 0x0040,

    /// <summary>
    /// Allocation strategy that tries to minimize memory usage.
    /// </summary>
    StrategyMinMemory = 0x0001,

    /// <summary>
    /// Allocation strategy that tries to minimize allocation time.
    /// </summary>
    StrategyMinTime = 0x0004,

    /// <summary>
    /// Allocation strategy that chooses always the lowest offset in available space.
    /// This is not the most efficient strategy but achieves highly packed data.
    /// </summary>
    StrategyMinOffset = 0x0008,

    /// <summary>
    /// A bit mask to extract only STRATEGY bits from entire set of flags.
    /// </summary>
    StrategyMask = StrategyMinMemory | StrategyMinTime | StrategyMinOffset
}
