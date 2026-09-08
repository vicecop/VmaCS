using Silk.NET.Vulkan;

namespace VmaCS;

/// <summary>
/// Delegate invoked when a new VkDeviceMemory block is allocated.
/// </summary>
/// <param name="allocator">The allocator performing the allocation.</param>
/// <param name="memoryType">Index of the Vulkan memory type being allocated.</param>
/// <param name="memory">The VkDeviceMemory object being allocated.</param>
/// <param name="size">Size of the allocation, in bytes.</param>
/// <param name="userData">User data pointer passed during registration.</param>
public delegate void AllocateDeviceMemoryFunction(VulkanMemoryAllocator allocator,
                                                  uint memoryType,
                                                  DeviceMemory memory,
                                                  ulong size,
                                                  IntPtr userData);

/// <summary>
/// Delegate invoked when a VkDeviceMemory block is freed.
/// </summary>
/// <param name="allocator">The allocator performing the free.</param>
/// <param name="memoryType">Index of the Vulkan memory type being freed.</param>
/// <param name="memory">The VkDeviceMemory object being freed.</param>
/// <param name="size">Size of the allocation, in bytes.</param>
/// <param name="userData">User data pointer passed during registration.</param>
public delegate void FreeDeviceMemoryFunction(VulkanMemoryAllocator allocator,
                                              uint memoryType,
                                              DeviceMemory memory,
                                              ulong size,
                                              IntPtr userData);

/// <summary>
/// Callbacks for device memory allocation and deallocation.
/// </summary>
public struct DeviceMemoryCallbacks
{
    /// <summary>
    /// Callback invoked when a new VkDeviceMemory block is allocated.
    /// </summary>
    public AllocateDeviceMemoryFunction? Allocate;

    /// <summary>
    /// Callback invoked when a VkDeviceMemory block is freed.
    /// </summary>
    public FreeDeviceMemoryFunction? Free;

    /// <summary>
    /// User data pointer passed to the callbacks.
    /// </summary>
    public IntPtr UserData;
}
