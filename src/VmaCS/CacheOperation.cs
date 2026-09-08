namespace VmaCS;

/// <summary>
/// Cache operation to perform on a mapped allocation. Internal use only.
/// </summary>
internal enum CacheOperation
{
    /// <summary>
    /// Flush the CPU cache to make writes visible to the GPU.
    /// </summary>
    Flush,

    /// <summary>
    /// Invalidate the CPU cache to ensure reads see the latest data from the GPU.
    /// </summary>
    Invalidate
}