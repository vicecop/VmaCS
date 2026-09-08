using Silk.NET.Vulkan;

namespace VmaCS;

internal readonly struct BufferImageUsageInfo
{
    public readonly uint Value;
    public readonly bool IsUnknown;

    public BufferImageUsageInfo(uint value, bool isUnknown = false)
    {
        Value = value;
        IsUnknown = isUnknown;
    }

    public static readonly BufferImageUsageInfo Unknown = new(0, true);

    // BufferUsageFlags and ImageUsageFlags share the same bit positions for TRANSFER_*.
    public bool ContainsDeviceAccess()
    {
        const uint transferMask = (uint)(BufferUsageFlags.TransferSrcBit | BufferUsageFlags.TransferDstBit);
        return (Value & ~transferMask) != 0;
    }
}
