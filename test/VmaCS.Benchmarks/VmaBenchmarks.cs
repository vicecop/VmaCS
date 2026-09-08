using BenchmarkDotNet.Attributes;
using Silk.NET.Core;
using Silk.NET.Vulkan;
using System.Runtime.InteropServices;

namespace VmaCS.Benchmarks;

public unsafe class VmaBenchmarks : IDisposable
{
    private Vk _vk = default!;
    private Instance _instance = default!;
    private PhysicalDevice _physicalDevice = default!;
    private Device _device = default!;
    private VulkanMemoryAllocator _allocatorLockOn = default!;
    private VulkanMemoryAllocator _allocatorLockOff = default!;

    private BufferCreateInfo _bufferInfo = default!;
    private ImageCreateInfo _imageInfo = default!;
    private AllocationCreateInfo _allocInfo = default!;
    private AllocationPoolCreateInfo _poolInfo = default!;
    private int _memType;

    [GlobalSetup]
    public void Setup()
    {
        _vk = Vk.GetApi();

        uint apiVersion = 0;
        _vk.EnumerateInstanceVersion(&apiVersion);
        var version = (Version32)apiVersion;

        var appName = Marshal.StringToHGlobalAnsi("VmaCS.Benchmarks");
        var engineName = Marshal.StringToHGlobalAnsi("Benchmarks");

        var appInfo = new ApplicationInfo
        {
            SType = StructureType.ApplicationInfo,
            ApiVersion = version,
            PApplicationName = (byte*)appName,
            ApplicationVersion = 1,
            PEngineName = (byte*)engineName,
            EngineVersion = 1,
        };

        var instanceCreateInfo = new InstanceCreateInfo
        {
            SType = StructureType.InstanceCreateInfo,
            PApplicationInfo = &appInfo,
            EnabledLayerCount = 0,
            PpEnabledLayerNames = null,
            EnabledExtensionCount = 0,
            PpEnabledExtensionNames = null,
        };

        if (_vk.CreateInstance(&instanceCreateInfo, null, out _instance).IsError())
        {
            throw new InvalidOperationException("Failed to create Vulkan instance.");
        }

        Marshal.FreeHGlobal(appName);
        Marshal.FreeHGlobal(engineName);

        uint physCount = 0;
        _vk.EnumeratePhysicalDevices(_instance, &physCount, null);
        if (physCount == 0)
        {
            throw new InvalidOperationException("No Vulkan physical devices found.");
        }

        var physDevices = new PhysicalDevice[physCount];
        fixed (PhysicalDevice* p = physDevices)
        {
            _vk.EnumeratePhysicalDevices(_instance, &physCount, p);
        }

        _physicalDevice = physDevices[0];

        var queuePriority = stackalloc float[] { 1.0f };
        var queueCreateInfo = new DeviceQueueCreateInfo
        {
            SType = StructureType.DeviceQueueCreateInfo,
            QueueFamilyIndex = 0,
            QueueCount = 1,
            PQueuePriorities = queuePriority,
        };

        var deviceCreateInfo = new DeviceCreateInfo
        {
            SType = StructureType.DeviceCreateInfo,
            QueueCreateInfoCount = 1,
            PQueueCreateInfos = &queueCreateInfo,
        };

        if (_vk.CreateDevice(_physicalDevice, &deviceCreateInfo, null, out _device).IsError())
        {
            throw new InvalidOperationException("Failed to create Vulkan device.");
        }

        var allocatorCreateInfoLockOn = new VulkanMemoryAllocatorCreateInfo
        {
            VulkanAPIVersion = version,
            VulkanAPIObject = _vk,
            Instance = _instance,
            PhysicalDevice = _physicalDevice,
            LogicalDevice = _device,
            Flags = AllocatorCreateFlags.BufferDeviceAddress,
        };

        _allocatorLockOn = new VulkanMemoryAllocator(in allocatorCreateInfoLockOn);

        var allocatorCreateInfoLockOff = new VulkanMemoryAllocatorCreateInfo
        {
            VulkanAPIVersion = version,
            VulkanAPIObject = _vk,
            Instance = _instance,
            PhysicalDevice = _physicalDevice,
            LogicalDevice = _device,
            Flags = AllocatorCreateFlags.BufferDeviceAddress | AllocatorCreateFlags.ExternallySyncronized,
        };

        _allocatorLockOff = new VulkanMemoryAllocator(in allocatorCreateInfoLockOff);

        _bufferInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = 65536,
            Usage = BufferUsageFlags.VertexBufferBit | BufferUsageFlags.TransferDstBit,
        };

        _imageInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = Format.R8G8B8A8Unorm,
            Extent = new Extent3D { Width = 256, Height = 256, Depth = 1 },
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.SampledBit | ImageUsageFlags.TransferDstBit,
            InitialLayout = ImageLayout.Undefined,
        };

        _allocInfo = new AllocationCreateInfo { Usage = MemoryUsage.GpuOnly };

        _memType = _allocatorLockOn.FindMemoryTypeIndexForBufferInfo(in _bufferInfo, in _allocInfo, out var idx)
            .IsSuccess() ? idx!.Value : 0;

        _poolInfo = new AllocationPoolCreateInfo
        {
            MemoryTypeIndex = _memType,
            MinBlockCount = 1,
            MaxBlockCount = 16,
            BlockSize = 4 * 1024 * 1024,
        };
    }

    [Benchmark]
    public void Buffer_Allocate_Free()
    {
        _allocatorLockOn.CreateBuffer(in _bufferInfo, in _allocInfo, out var buffer, out var allocation);
        allocation!.Dispose();
        _vk.DestroyBuffer(_device, buffer, ReadOnlySpan<AllocationCallbacks>.Empty);
    }

    [Benchmark]
    public void Image_Allocate_Free()
    {
        _allocatorLockOn.CreateImage(in _imageInfo, in _allocInfo, out var image, out var allocation);
        allocation!.Dispose();
        _vk.DestroyImage(_device, image, ReadOnlySpan<AllocationCallbacks>.Empty);
    }

    [Benchmark]
    public void Defrag_Fast()
    {
        using var pool = _allocatorLockOn.CreatePool(in _poolInfo);

        var req = new MemoryRequirements { Size = 65536, Alignment = 1, MemoryTypeBits = uint.MaxValue };
        var info = new AllocationCreateInfo { Usage = MemoryUsage.GpuOnly, Pool = pool };

        var live = new Allocation[32];
        for (var i = 0; i < live.Length; i++)
        {
            _allocatorLockOn.AllocateMemory(in req, in info, out live[i]!);
        }

        for (var i = 0; i < live.Length; i += 2)
        {
            live[i].Dispose();
        }

        var handles = new Allocation[live.Length / 2];
        var j = 0;
        for (var i = 0; i < live.Length; i += 2, j++)
        {
            handles[j] = live[i + 1];
        }

        var defragInfo = new DefragmentationInfo
        {
            Allocations = handles,
            Flags = DefragmentationFlags.AlgorithmFast,
        };

        var ctx = _allocatorLockOn.DefragmentationBegin(in defragInfo);
        while (true)
        {
            var pass = ctx.PassBegin();
            if (pass.Moves == null || pass.Moves.Length == 0)
            {
                break;
            }
            ctx.PassEnd();
        }
        ctx.End();

        foreach (var a in handles)
        {
            a.Dispose();
        }

    }

    [Benchmark]
    public void FindMemoryType_ForBuffer() => _allocatorLockOn.FindMemoryTypeIndexForBufferInfo(in _bufferInfo, in _allocInfo, out _);

    [Benchmark]
    public void Map_Unmap()
    {
        _allocatorLockOn.CreateBuffer(in _bufferInfo, in _allocInfo, out var buffer, out var allocation);

        var mapRes = allocation!.Map(out _);
        if (mapRes.IsSuccess())
        {
            allocation.Unmap();
        }
        allocation.Dispose();
        _vk.DestroyBuffer(_device, buffer, ReadOnlySpan<AllocationCallbacks>.Empty);
    }

    [Benchmark]
    public void Buffer_Allocate_Free_LockOff()
    {
        _allocatorLockOff.CreateBuffer(in _bufferInfo, in _allocInfo, out var buffer, out var allocation);
        allocation!.Dispose();
        _vk.DestroyBuffer(_device, buffer, ReadOnlySpan<AllocationCallbacks>.Empty);
    }

    [Benchmark]
    public void Image_Allocate_Free_LockOff()
    {
        _allocatorLockOff.CreateImage(in _imageInfo, in _allocInfo, out var image, out var allocation);
        allocation!.Dispose();
        _vk.DestroyImage(_device, image, ReadOnlySpan<AllocationCallbacks>.Empty);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _allocatorLockOn.Dispose();
        _allocatorLockOff.Dispose();
        _vk.DestroyDevice(_device, null);
        _vk.DestroyInstance(_instance, null);
    }

    public void Dispose() => Cleanup();
}
