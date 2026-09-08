namespace VmaCS;

/// <summary>
/// Represents an allocation request within a memory block.
/// </summary>
public struct AllocationRequest
{
    /// <summary>
    /// Offset of the allocation within the memory block, in bytes.
    /// </summary>
    public long Offset;

    /// <summary>
    /// Sum of sizes of all free suballocations considered for this request.
    /// </summary>
    public long SumFreeSize;

    /// <summary>
    /// Sum of sizes of all items considered for this request.
    /// </summary>
    public long SumItemSize;

    /// <summary>
    /// Associated item, if any.
    /// </summary>
    public object? Item;

    /// <summary>
    /// Suballocation type associated with this request.
    /// </summary>
    public SuballocationType CustomData;

    /// <summary>
    /// Type of the allocation request.
    /// </summary>
    public AllocationRequestType Type;
}
