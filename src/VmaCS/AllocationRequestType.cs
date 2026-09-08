namespace VmaCS;

/// <summary>
/// Type of allocation request used internally by the allocator.
/// </summary>
public enum AllocationRequestType
{
    /// <summary>
    /// Normal allocation request.
    /// </summary>
    Normal,

    /// <summary>
    /// Allocation request from the upper stack in a double stack pool.
    /// </summary>
    UpperAddress,

    /// <summary>
    /// Indicates end of the first list of free suballocations.
    /// </summary>
    EndOfList1,

    /// <summary>
    /// Indicates end of the second list of free suballocations.
    /// </summary>
    EndOfList2
}
