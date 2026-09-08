using System.Runtime.InteropServices;
using Silk.NET.Core;
using Silk.NET.Vulkan;
using VmaCS;

Console.WriteLine("VmaCS Example");

unsafe
{
    uint apiVersion = 0;
    var vk = Vk.GetApi();
    vk.EnumerateInstanceVersion(&apiVersion);
    var version = (Version32)apiVersion;

    var appName = Marshal.StringToHGlobalAnsi("VmaCS.Example");
    var engineName = Marshal.StringToHGlobalAnsi("Example");

    try
    {
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

        var res = vk.CreateInstance(&instanceCreateInfo, null, out var instance);
        if (res.IsError())
        {
            Console.WriteLine($"Failed to create Vulkan instance: {res}");
            return;
        }

        uint physCount = 0;
        res = vk.EnumeratePhysicalDevices(instance, &physCount, null);
        if (res.IsError() || physCount == 0)
        {
            Console.WriteLine("No Vulkan physical devices found.");
            vk.DestroyInstance(instance, null);
            return;
        }

        var physDevices = new PhysicalDevice[physCount];
        fixed (PhysicalDevice* p = physDevices)
        {
            vk.EnumeratePhysicalDevices(instance, &physCount, p);
        }

        var physicalDevice = physDevices[0];

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

        res = vk.CreateDevice(physicalDevice, &deviceCreateInfo, null, out var device);
        if (res.IsError())
        {
            Console.WriteLine($"Failed to create Vulkan device: {res}");
            vk.DestroyInstance(instance, null);
            return;
        }

        var allocatorCreateInfo = new VulkanMemoryAllocatorCreateInfo
        {
            VulkanAPIVersion = version,
            VulkanAPIObject = vk,
            Instance = instance,
            PhysicalDevice = physicalDevice,
            LogicalDevice = device,
            ThrowOnError = false,
        };

        using var allocator = new VulkanMemoryAllocator(in allocatorCreateInfo);

        Console.WriteLine($"Maintenance4 supported: {allocator.IsMaintenance4Supported}");
        Console.WriteLine($"Maintenance5 supported: {allocator.IsMaintenance5Supported}");
        Console.WriteLine($"ExternalMemoryWin32 supported: {allocator.IsExternalMemoryWin32Supported}");

        var bufferInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = 65536,
            Usage = BufferUsageFlags.VertexBufferBit | BufferUsageFlags.TransferDstBit,
        };

        var allocInfo = new AllocationCreateInfo { Usage = MemoryUsage.GpuOnly };

        res = allocator.CreateBuffer(in bufferInfo, in allocInfo, out var buffer, out var allocation);
        if (res.IsError())
        {
            Console.WriteLine($"Failed to allocate buffer: {res}");
            allocator.Dispose();
            vk.DestroyDevice(device, null);
            vk.DestroyInstance(instance, null);
            return;
        }

        Console.WriteLine($"Buffer allocated, size: {allocation!.Size}, offset: {allocation!.Offset}");

        var imageInfo = new ImageCreateInfo
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

        res = allocator.CreateImage(in imageInfo, in allocInfo, out var image, out var imageAllocation);
        if (res.IsError())
        {
            Console.WriteLine($"Failed to allocate image: {res}");
            allocation!.Dispose();
            vk.DestroyBuffer(device, buffer, ReadOnlySpan<AllocationCallbacks>.Empty);
            allocator.Dispose();
            vk.DestroyDevice(device, null);
            vk.DestroyInstance(instance, null);
            return;
        }

        Console.WriteLine($"Image allocated, size: {imageAllocation!.Size}, offset: {imageAllocation!.Offset}");

        allocation.Dispose();
        vk.DestroyBuffer(device, buffer, ReadOnlySpan<AllocationCallbacks>.Empty);

        imageAllocation.Dispose();
        vk.DestroyImage(device, image, ReadOnlySpan<AllocationCallbacks>.Empty);

        allocator.Dispose();
        vk.DestroyDevice(device, null);
        vk.DestroyInstance(instance, null);

        Console.WriteLine("Done.");
    }
    finally
    {
        Marshal.FreeHGlobal(appName);
        Marshal.FreeHGlobal(engineName);
    }
}
