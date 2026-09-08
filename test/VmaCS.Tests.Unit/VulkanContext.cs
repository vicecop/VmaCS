using Silk.NET.Vulkan;
using System.Runtime.InteropServices;

namespace VmaCS.Tests.Unit;

public sealed unsafe class VulkanContext : IDisposable
{
    private readonly Vk _vk;
    private bool _disposed;
    private readonly List<nint> _extensionNamePointers = new();

    public Instance Instance { get; }
    public PhysicalDevice PhysicalDevice { get; }
    public Device Device { get; }
    public VulkanMemoryAllocator Allocator { get; }

    /// <summary>
    /// True when the device was created with <c>VK_KHR_external_memory_win32</c> (+ <c>VK_KHR_external_memory</c>),
    /// so Win32 handle export/import tests can run. Mirrors <c>VulkanPortedContext.ExternalMemoryWin32Supported</c>.
    /// </summary>
    public bool ExternalMemoryWin32Supported { get; }

    /// <summary>
    /// True when the device supports <c>VK_KHR_maintenance4</c> (either as extension or Vulkan 1.3+ core),
    /// so maintenance4 tests can run.
    /// </summary>
    public bool Maintenance4Supported { get; }

    public VulkanContext()
    {
        _vk = Vk.GetApi();

        // VmaCS is configured for Vulkan 1.1 (VulkanAPIVersion below) and internally calls
        // vkGetBufferMemoryRequirements2, which is core since Vulkan 1.1. The instance/device API
        // version is taken from the ApplicationInfo: if it is omitted the loader defaults to Vulkan
        // 1.0, the 1.1 entry points are never exposed, and VmaCS then invokes a null function
        // pointer and aborts the native host. Always request at least 1.1 here.
        uint maxInstanceVersion = 0;
        _vk.EnumerateInstanceVersion(&maxInstanceVersion);
        var requestedVersion = (uint)Vk.Version11;
        if (maxInstanceVersion != 0 && maxInstanceVersion < requestedVersion)
        {
            var major = maxInstanceVersion >> 22;
            var minor = (maxInstanceVersion >> 12) & 0x3ff;
            throw new InvalidOperationException(
                $"Vulkan 1.1 is required by VmaCS, but the environment only provides Vulkan {major}.{minor}.");
        }

        var appInfo = new ApplicationInfo
        {
            SType = StructureType.ApplicationInfo,
            ApiVersion = requestedVersion,
        };

        var instanceCi = new InstanceCreateInfo
        {
            SType = StructureType.InstanceCreateInfo,
            PApplicationInfo = &appInfo,
        };

        if (_vk.CreateInstance(in instanceCi, null, out var instance) != Result.Success)
        {

            throw new InvalidOperationException("Failed to create Vulkan instance.");
        }

        Instance = instance;

        uint physicalDeviceCount = 0;
        _vk.EnumeratePhysicalDevices(Instance, &physicalDeviceCount, null);
        if (physicalDeviceCount == 0)
        {

            throw new InvalidOperationException("No Vulkan physical devices found.");
        }

        var physicalDevices = new PhysicalDevice[physicalDeviceCount];
        fixed (PhysicalDevice* pDevices = physicalDevices)
        {
            _vk.EnumeratePhysicalDevices(Instance, &physicalDeviceCount, pDevices);
        }
        PhysicalDevice = physicalDevices[0];

        uint queueFamilyCount = 0;
        _vk.GetPhysicalDeviceQueueFamilyProperties(PhysicalDevice, &queueFamilyCount, null);
        var queueFamilies = new QueueFamilyProperties[queueFamilyCount];
        fixed (QueueFamilyProperties* pFamilies = queueFamilies)
        {
            _vk.GetPhysicalDeviceQueueFamilyProperties(PhysicalDevice, &queueFamilyCount, pFamilies);
        }

        var graphicsFamily = uint.MaxValue;
        for (uint i = 0; i < queueFamilyCount; i++)
        {
            if ((queueFamilies[i].QueueFlags & QueueFlags.GraphicsBit) != 0)
            {
                graphicsFamily = i;
                break;
            }
        }

        if (graphicsFamily == uint.MaxValue)
        {

            throw new InvalidOperationException("No graphics queue family found.");
        }

        var queuePriority = 1.0f;
        var queueCi = new DeviceQueueCreateInfo
        {
            SType = StructureType.DeviceQueueCreateInfo,
            QueueFamilyIndex = graphicsFamily,
            QueueCount = 1,
            PQueuePriorities = &queuePriority,
        };

        // Detect VK_KHR_external_memory_win32 support; when available, enable it (and VK_KHR_external_memory)
        // so Win32 handle export/import GAP tests can run. Mirrors VulkanPortedContext.
        var externalMemoryWin32Supported = false;
        {
            uint extCount = 0;
            _vk.EnumerateDeviceExtensionProperties(PhysicalDevice, (byte*)null, &extCount, null);
            if (extCount > 0)
            {
                var extProps = new ExtensionProperties[extCount];
                fixed (ExtensionProperties* p = extProps)
                {
                    _vk.EnumerateDeviceExtensionProperties(PhysicalDevice, (byte*)null, &extCount, p);
                    var hasWin32 = false;
                    var hasExternal = false;
                    for (uint i = 0; i < extCount; i++)
                    {
                        var name = Marshal.PtrToStringAnsi((nint)p[i].ExtensionName);
                        if (name == "VK_KHR_external_memory_win32")
                        {
                            hasWin32 = true;
                        }
                        else if (name == "VK_KHR_external_memory")
                        {
                            hasExternal = true;
                        }
                    }

                    externalMemoryWin32Supported = hasWin32 && hasExternal;
                }
            }
        }

        ExternalMemoryWin32Supported = externalMemoryWin32Supported;

        var maintenance4Supported = false;
        {
            uint extCount = 0;
            _vk.EnumerateDeviceExtensionProperties(PhysicalDevice, (byte*)null, &extCount, null);
            if (extCount > 0)
            {
                var extProps = new ExtensionProperties[extCount];
                fixed (ExtensionProperties* p = extProps)
                {
                    _vk.EnumerateDeviceExtensionProperties(PhysicalDevice, (byte*)null, &extCount, p);
                    for (uint i = 0; i < extCount; i++)
                    {
                        var name = Marshal.PtrToStringAnsi((nint)p[i].ExtensionName);
                        if (name == "VK_KHR_maintenance4")
                        {
                            maintenance4Supported = true;
                            break;
                        }
                    }
                }
            }
        }

        Maintenance4Supported = maintenance4Supported;

        var extPtrs = System.Array.Empty<nint>();
        if (externalMemoryWin32Supported)
        {
            var enabledExtensionNames = new[] { "VK_KHR_external_memory_win32", "VK_KHR_external_memory" };
            extPtrs = new nint[enabledExtensionNames.Length];
            for (var i = 0; i < enabledExtensionNames.Length; i++)
            {
                var ptr = Marshal.StringToHGlobalAnsi(enabledExtensionNames[i]);
                extPtrs[i] = ptr;
                _extensionNamePointers.Add(ptr);
            }
        }

        if (maintenance4Supported)
        {
            var oldLen = extPtrs.Length;
            Array.Resize(ref extPtrs, oldLen + 1);
            var ptr = Marshal.StringToHGlobalAnsi("VK_KHR_maintenance4");
            extPtrs[oldLen] = ptr;
            _extensionNamePointers.Add(ptr);
        }

        var deviceCi = new DeviceCreateInfo
        {
            SType = StructureType.DeviceCreateInfo,
            QueueCreateInfoCount = 1,
            PQueueCreateInfos = &queueCi,
        };

        fixed (nint* pExt = extPtrs)
        {
            if (extPtrs.Length > 0)
            {
                deviceCi.EnabledExtensionCount = (uint)extPtrs.Length;
                deviceCi.PpEnabledExtensionNames = (byte**)pExt;
            }

            if (_vk.CreateDevice(PhysicalDevice, in deviceCi, null, out var device) != Result.Success)
            {
                throw new InvalidOperationException("Failed to create Vulkan device.");
            }

            Device = device;
        }

        Allocator = new VulkanMemoryAllocator(new VulkanMemoryAllocatorCreateInfo
        {
            VulkanAPIVersion = Vk.Version11,
            VulkanAPIObject = _vk,
            Instance = Instance,
            PhysicalDevice = PhysicalDevice,
            LogicalDevice = Device,
        });
    }

    public Vk Vk => _vk;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        Allocator?.Dispose();
        if (Device.Handle != 0)
        {
            _vk.DestroyDevice(Device, null);
        }

        foreach (var ptr in _extensionNamePointers)
        {
            Marshal.FreeHGlobal(ptr);
        }

        if (Instance.Handle != 0)
        {
            _vk.DestroyInstance(Instance, null);
        }

        // Intentionally NOT disposing _vk: Vk.GetApi() wraps the process-wide native Vulkan loader,
        // shared by every test context. Disposing it here would unload the native library while other
        // tests (running in parallel) still issue Vulkan calls, crashing the native test host.
    }
}
