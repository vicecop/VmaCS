using Silk.NET.Vulkan;
using System.Runtime.InteropServices;

namespace VmaCS.Tests.Ported;

internal sealed unsafe class VulkanContext : IDisposable
{
    private readonly Vk _vk = Vk.GetApi();

    /// <summary>
    /// Deterministic RNG that mirrors C++ <c>RandomNumberGenerator</c> exactly, so the parity
    /// tests reproduce C++'s allocation layouts (sizes, deletion order, non-movable picks).
    /// C++: <c>Generate() = GenerateFast() ^ (GenerateFast() &gt;&gt; 7)</c> with
    /// <c>GenerateFast(): m_Value = m_Value * 196314165 + 907633515</c>.
    /// </summary>
    public PortedRandom Rand { get; private set; } = new(0);

    /// <summary>Re-seeds the parity RNG, mirroring C++ <c>RandomNumberGenerator rand(SEED)</c>.</summary>
    public void SeedRand(int seed) => Rand = new PortedRandom((uint)seed);

    /// <summary>Faithful port of C++ <c>RandomNumberGenerator</c> (see <c>Common.h</c>).</summary>
    internal sealed class PortedRandom
    {
        private uint _value;

        public PortedRandom(uint seed)
        {
            _value = seed;
        }

        public void Seed(uint seed) => _value = seed;

        private uint GenerateFast() => _value = _value * 196314165u + 907633515u;

        /// <summary>Mirrors <c>RandomNumberGenerator::Generate()</c> (full uint32, as C++ uses it in <c>% n</c>).</summary>
        public uint Generate()
        {
            var a = GenerateFast();
            var b = GenerateFast();
            return a ^ (b >> 7);
        }

        /// <summary>Mirrors C++ <c>rand.Generate() % n</c> (always non-negative, in [0, n)).</summary>
        public int Next(int maxValue) => (int)(Generate() % (uint)maxValue);

        /// <summary>Returns the raw int view of <c>Generate()</c> (round-trips through <c>(uint)</c>).</summary>
        public int Next() => (int)Generate();
    }

    public Instance Instance { get; }
    public PhysicalDevice PhysicalDevice { get; }
    public Device Device { get; }
    public VulkanMemoryAllocator Allocator { get; }
    public Vk Vk => _vk;
    public Queue Queue { get; }
    public uint QueueFamilyIndex { get; }
    public bool BufferDeviceAddressSupported { get; }
    public bool Maintenance5Supported { get; }
    public bool ExternalMemoryWin32Supported { get; }

    private readonly CommandPool _commandPool;
    private readonly List<nint> _extensionNamePointers = new();

    public VulkanContext()
    {
        // Determine the highest Vulkan instance API version the environment supports.
        uint maxInstanceVersion = 0;
        if (_vk.EnumerateInstanceVersion(&maxInstanceVersion) != Result.Success)
        {
            maxInstanceVersion = 0;
        }

        var version11 = (uint)Vk.Version11;
        var version13 = (uint)Vk.Version13;

        // VmaCS relies on vkGetBufferMemoryRequirements2, which is core since
        // Vulkan 1.1. Request at least 1.1 when available; otherwise fail fast with
        // a clear message instead of crashing deep inside a native dispatch table.
        if (maxInstanceVersion != 0 && maxInstanceVersion < version11)
        {
            var major = maxInstanceVersion >> 22;
            var minor = (maxInstanceVersion >> 12) & 0x3ff;
            throw new InvalidOperationException(
                $"Vulkan 1.1 is required by VmaCS, but the environment only provides Vulkan {major}.{minor}.");
        }

        var requestedVersion = maxInstanceVersion > version13 ? version13 : maxInstanceVersion;
        if (requestedVersion == 0)
        {
            requestedVersion = version11;
        }

        var appInfo = new ApplicationInfo
        {
            SType = StructureType.ApplicationInfo,
            ApiVersion = requestedVersion,
            ApplicationVersion = 1,
            EngineVersion = 1,
            PApplicationName = (byte*)null,
            PEngineName = (byte*)null,
        };
        var pAppInfo = &appInfo;

        var instanceInfo = new InstanceCreateInfo
        {
            SType = StructureType.InstanceCreateInfo,
            PApplicationInfo = pAppInfo,
        };
        ThrowOnFailure(_vk.CreateInstance(in instanceInfo, null, out var instance), "CreateInstance");
        Instance = instance;

        uint physicalDeviceCount = 0;
        ThrowOnFailure(_vk.EnumeratePhysicalDevices(Instance, &physicalDeviceCount, null), "EnumeratePhysicalDevices");
        if (physicalDeviceCount == 0)
        {
            throw new InvalidOperationException("No Vulkan physical devices found.");
        }

        var physicalDevices = new PhysicalDevice[physicalDeviceCount];
        fixed (PhysicalDevice* devices = physicalDevices)
        {
            ThrowOnFailure(_vk.EnumeratePhysicalDevices(Instance, &physicalDeviceCount, devices), "EnumeratePhysicalDevices");
        }

        PhysicalDevice = physicalDevices[0];
        uint queueFamilyCount = 0;
        _vk.GetPhysicalDeviceQueueFamilyProperties(PhysicalDevice, &queueFamilyCount, null);
        var queueFamilies = new QueueFamilyProperties[queueFamilyCount];
        fixed (QueueFamilyProperties* families = queueFamilies)
        {
            _vk.GetPhysicalDeviceQueueFamilyProperties(PhysicalDevice, &queueFamilyCount, families);
        }

        var queueFamily = uint.MaxValue;
        for (uint index = 0; index < queueFamilyCount; index++)
        {
            if ((queueFamilies[index].QueueFlags & QueueFlags.GraphicsBit) != 0)
            {
                queueFamily = index;
                break;
            }
        }

        if (queueFamily == uint.MaxValue)
        {
            throw new InvalidOperationException("No graphics queue family found.");
        }

        float priority = 1;
        var queueInfo = new DeviceQueueCreateInfo
        {
            SType = StructureType.DeviceQueueCreateInfo,
            QueueFamilyIndex = queueFamily,
            QueueCount = 1,
            PQueuePriorities = &priority,
        };
        // Detect VK_KHR_buffer_device_address support and enable it when available,
        // so buffer device address allocations can be exercised by tests.
        var bdaSupported = false;
        {
            uint extCount = 0;
            _vk.EnumerateDeviceExtensionProperties(PhysicalDevice, (byte*)null, &extCount, null);
            if (extCount > 0)
            {
                var extProps = new ExtensionProperties[extCount];
                fixed (ExtensionProperties* p = extProps)
                {
                    ThrowOnFailure(_vk.EnumerateDeviceExtensionProperties(PhysicalDevice, (byte*)null, &extCount, p), "EnumerateDeviceExtensionProperties");
                    for (uint i = 0; i < extCount; i++)
                    {
                        string? name = null;
                        var nm = p[i].ExtensionName;
                        name = Marshal.PtrToStringAnsi((nint)nm);
                        if (name == "VK_KHR_buffer_device_address")
                        {
                            bdaSupported = true;
                            break;
                        }
                    }
                }
            }
        }
        BufferDeviceAddressSupported = bdaSupported;

        // Detect VK_KHR_maintenance5 support; when available, enable it so the allocator
        // can exercise the VK_KHR_maintenance5 buffer-usage path (usage taken from
        // VkBufferUsageFlags2CreateInfoKHR in the buffer's pNext chain).
        var maintenance5Supported = false;
        {
            uint extCount = 0;
            _vk.EnumerateDeviceExtensionProperties(PhysicalDevice, (byte*)null, &extCount, null);
            if (extCount > 0)
            {
                var extProps = new ExtensionProperties[extCount];
                fixed (ExtensionProperties* p = extProps)
                {
                    ThrowOnFailure(_vk.EnumerateDeviceExtensionProperties(PhysicalDevice, (byte*)null, &extCount, p), "EnumerateDeviceExtensionProperties");
                    for (uint i = 0; i < extCount; i++)
                    {
                        var name = Marshal.PtrToStringAnsi((nint)p[i].ExtensionName);
                        if (name == "VK_KHR_maintenance5")
                        {
                            maintenance5Supported = true;
                            break;
                        }
                    }
                }
            }
        }
        Maintenance5Supported = maintenance5Supported;

        // Detect VK_KHR_external_memory_win32 support; when available, enable it (and the accompanying
        // VK_KHR_external_memory device extension) so exported/imported memory tests can run.
        var externalMemoryWin32Supported = false;
        {
            uint extCount = 0;
            _vk.EnumerateDeviceExtensionProperties(PhysicalDevice, (byte*)null, &extCount, null);
            if (extCount > 0)
            {
                var extProps = new ExtensionProperties[extCount];
                fixed (ExtensionProperties* p = extProps)
                {
                    ThrowOnFailure(_vk.EnumerateDeviceExtensionProperties(PhysicalDevice, (byte*)null, &extCount, p), "EnumerateDeviceExtensionProperties");
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

        var extNames = new System.Collections.Generic.List<string>();
        if (bdaSupported)
        {
            extNames.Add("VK_KHR_buffer_device_address");
        }
        if (maintenance5Supported)
        {
            extNames.Add("VK_KHR_maintenance5");
        }

        if (externalMemoryWin32Supported)
        {
            extNames.Add("VK_KHR_external_memory_win32");
            extNames.Add("VK_KHR_external_memory");
        }
        var enabledExtensionNames = extNames.ToArray();
        var extPtrs = new nint[enabledExtensionNames.Length];
        for (var i = 0; i < enabledExtensionNames.Length; i++)
        {
            var ptr = Marshal.StringToHGlobalAnsi(enabledExtensionNames[i]);
            extPtrs[i] = ptr;
            _extensionNamePointers.Add(ptr);
        }

        var bdaFeatures = new PhysicalDeviceBufferDeviceAddressFeaturesKHR
        {
            SType = StructureType.PhysicalDeviceBufferDeviceAddressFeaturesKhr,
            BufferDeviceAddress = true,
        };

        PhysicalDeviceMaintenance5FeaturesKHR maintenance5Features = default;
        if (maintenance5Supported)
        {
            maintenance5Features = new PhysicalDeviceMaintenance5FeaturesKHR
            {
                SType = StructureType.PhysicalDeviceMaintenance5FeaturesKhr,
                Maintenance5 = true,
            };
        }

        var deviceInfo = new DeviceCreateInfo
        {
            SType = StructureType.DeviceCreateInfo,
            QueueCreateInfoCount = 1,
            PQueueCreateInfos = &queueInfo,
        };

        var pBda = &bdaFeatures;
        var pM5 = &maintenance5Features;
        fixed (nint* pExt = extPtrs)
        {
            if (bdaSupported && maintenance5Supported)
            {
                bdaFeatures.PNext = pM5;
                deviceInfo.PNext = pBda;
                deviceInfo.EnabledExtensionCount = (uint)extPtrs.Length;
                deviceInfo.PpEnabledExtensionNames = (byte**)pExt;
            }
            else if (bdaSupported)
            {
                deviceInfo.PNext = pBda;
                deviceInfo.EnabledExtensionCount = (uint)extPtrs.Length;
                deviceInfo.PpEnabledExtensionNames = (byte**)pExt;
            }
            else if (maintenance5Supported)
            {
                deviceInfo.PNext = pM5;
                deviceInfo.EnabledExtensionCount = (uint)extPtrs.Length;
                deviceInfo.PpEnabledExtensionNames = (byte**)pExt;
            }

            ThrowOnFailure(_vk.CreateDevice(PhysicalDevice, in deviceInfo, null, out var device), "CreateDevice");
            Device = device;
        }
        QueueFamilyIndex = queueFamily;
        Queue = _vk.GetDeviceQueue(Device, QueueFamilyIndex, 0);

        var commandPoolInfo = new CommandPoolCreateInfo
        {
            SType = StructureType.CommandPoolCreateInfo,
            Flags = CommandPoolCreateFlags.TransientBit | CommandPoolCreateFlags.ResetCommandBufferBit,
            QueueFamilyIndex = QueueFamilyIndex,
        };
        ThrowOnFailure(_vk.CreateCommandPool(Device, in commandPoolInfo, null, out _commandPool), "CreateCommandPool");

        Allocator = new VulkanMemoryAllocator(new VulkanMemoryAllocatorCreateInfo
        {
            VulkanAPIVersion = Vk.Version11,
            VulkanAPIObject = _vk,
            Instance = Instance,
            PhysicalDevice = PhysicalDevice,
            LogicalDevice = Device
        });
    }

    public void Dispose()
    {
        foreach (var ptr in _extensionNamePointers)
        {
            Marshal.FreeHGlobal(ptr);
        }

        Allocator.Dispose();
        if (_commandPool.Handle != 0)
        {
            _vk.DestroyCommandPool(Device, _commandPool, null);
        }

        if (Device.Handle != 0)
        {
            _vk.DestroyDevice(Device, null);
        }

        if (Instance.Handle != 0)
        {
            _vk.DestroyInstance(Instance, null);
        }

        // Intentionally NOT disposing _vk: Vk.GetApi() wraps the process-wide native Vulkan loader,
        // shared by every test context. Disposing it here would unload the native library while other
        // tests (running in parallel) still issue Vulkan calls, crashing the native test host.
    }

    public void SubmitAndWait(Action<CommandBuffer> record)
    {
        ArgumentNullException.ThrowIfNull(record);

        var allocateInfo = new CommandBufferAllocateInfo
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = _commandPool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = 1,
        };
        ThrowOnFailure(_vk.AllocateCommandBuffers(Device, in allocateInfo, out var commandBuffer), "AllocateCommandBuffers");

        try
        {
            var beginInfo = new CommandBufferBeginInfo
            {
                SType = StructureType.CommandBufferBeginInfo,
                Flags = CommandBufferUsageFlags.OneTimeSubmitBit,
            };
            ThrowOnFailure(_vk.BeginCommandBuffer(commandBuffer, in beginInfo), "BeginCommandBuffer");
            record(commandBuffer);
            ThrowOnFailure(_vk.EndCommandBuffer(commandBuffer), "EndCommandBuffer");

            var submitInfo = new SubmitInfo
            {
                SType = StructureType.SubmitInfo,
                CommandBufferCount = 1,
                PCommandBuffers = &commandBuffer,
            };
            var fenceInfo = new FenceCreateInfo { SType = StructureType.FenceCreateInfo };
            ThrowOnFailure(_vk.CreateFence(Device, in fenceInfo, null, out var fence), "CreateFence");
            try
            {
                ThrowOnFailure(_vk.QueueSubmit(Queue, 1, in submitInfo, fence), "QueueSubmit");
                ThrowOnFailure(_vk.WaitForFences(Device, 1, in fence, true, ulong.MaxValue), "WaitForFences");
            }
            finally
            {
                _vk.DestroyFence(Device, fence, null);
            }
        }
        finally
        {
            _vk.FreeCommandBuffers(Device, _commandPool, 1, in commandBuffer);
        }
    }

    internal CommandBuffer AllocateCommandBuffer()
    {
        var allocateInfo = new CommandBufferAllocateInfo
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = _commandPool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = 1,
        };
        ThrowOnFailure(_vk.AllocateCommandBuffers(Device, in allocateInfo, out var commandBuffer), "AllocateCommandBuffers");
        return commandBuffer;
    }

    internal void FreeCommandBuffer(CommandBuffer commandBuffer)
    {
        var p = stackalloc CommandBuffer[1];
        p[0] = commandBuffer;
        _vk.FreeCommandBuffers(Device, _commandPool, 1, p);
    }

    internal void SubmitAndWait(CommandBuffer commandBuffer)
    {
        var p = stackalloc CommandBuffer[1];
        p[0] = commandBuffer;
        var submitInfo = new SubmitInfo
        {
            SType = StructureType.SubmitInfo,
            CommandBufferCount = 1,
            PCommandBuffers = p,
        };
        var fenceInfo = new FenceCreateInfo { SType = StructureType.FenceCreateInfo };
        ThrowOnFailure(_vk.CreateFence(Device, in fenceInfo, null, out var fence), "CreateFence");
        try
        {
            ThrowOnFailure(_vk.QueueSubmit(Queue, 1, in submitInfo, fence), "QueueSubmit");
            ThrowOnFailure(_vk.WaitForFences(Device, 1, in fence, true, ulong.MaxValue), "WaitForFences");
        }
        finally
        {
            _vk.DestroyFence(Device, fence, null);
        }
    }

    private static void ThrowOnFailure(Result result, string operation)
    {
        if (result != Result.Success)
        {
            throw new InvalidOperationException($"Vulkan {operation} failed: {result}.");
        }
    }
}