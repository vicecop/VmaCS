namespace VmaCS;

/// <summary>
/// Parameters for creating a <see cref="VirtualBlock"/>.
/// </summary>
public struct VirtualBlockCreateInfo
{
    /// <summary>
    /// Size of the virtual block, in bytes.
    /// </summary>
    public required long Size;

    /// <summary>
    /// Use combination of <see cref="VirtualBlockCreateFlags"/>.
    /// </summary>
    public VirtualBlockCreateFlags Flags;
}
