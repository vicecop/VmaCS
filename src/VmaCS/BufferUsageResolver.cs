using Silk.NET.Vulkan;

namespace VmaCS;

internal sealed unsafe class BufferUsageResolver
{
    // VMA's VmaBufferImageUsage(const VkBufferCreateInfo&, bool useKhrMaintenance5):
    // when VK_KHR_maintenance5 is enabled and the buffer's pNext chain carries a
    // VkBufferUsageFlags2CreateInfoKHR, the buffer usage is taken from it (and the base
    // VkBufferCreateInfo::usage is ignored), per the VK_KHR_maintenance5 specification.
    // In VmaCS this behavior is always active; no allocator flag is required.
    public static BufferImageUsageInfo Resolve(in BufferCreateInfo bufferInfo)
    {
        for (var pNext = (BaseInStructure*)bufferInfo.PNext; pNext != null; pNext = pNext->PNext)
        {
            if (pNext->SType == StructureType.BufferUsageFlags2CreateInfoKhr)
            {
                var flags2 = (BufferUsageFlags2CreateInfoKHR*)pNext;
                return new BufferImageUsageInfo((uint)flags2->Usage);
            }
        }

        return new BufferImageUsageInfo((uint)bufferInfo.Usage);
    }
}
