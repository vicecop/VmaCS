using Silk.NET.Vulkan;

namespace VmaCS.Tests.Unit;

public unsafe class ExternalMemoryTests
{
    [Fact]
    public void TestGetMemoryWin32Handle2()
    {
        using var ctx = new VulkanContext();

        if (!ctx.ExternalMemoryWin32Supported)
        {
            return;
        }

        const ExternalMemoryHandleTypeFlags handleType = ExternalMemoryHandleTypeFlags.OpaqueWin32Bit;

        var exportMemAllocInfo = new ExportMemoryAllocateInfoKHR
        {
            SType = StructureType.ExportMemoryAllocateInfoKhr,
            HandleTypes = handleType,
        };

        var externalMemBufCreateInfo = new ExternalMemoryBufferCreateInfoKHR
        {
            SType = StructureType.ExternalMemoryBufferCreateInfoKhr,
            HandleTypes = handleType,
        };

        var bufCreateInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = 0x10000,
            Usage = BufferUsageFlags.TransferSrcBit | BufferUsageFlags.TransferDstBit,
            PNext = &externalMemBufCreateInfo,
        };

        var requiresDedicated = true;
        {
            var externalBufferInfo = new PhysicalDeviceExternalBufferInfo
            {
                SType = StructureType.PhysicalDeviceExternalBufferInfo,
                Flags = bufCreateInfo.Flags,
                Usage = bufCreateInfo.Usage,
                HandleType = handleType,
            };

            var externalBufferProperties = new ExternalBufferProperties
            {
                SType = StructureType.ExternalBufferProperties,
            };

            ctx.Vk.GetPhysicalDeviceExternalBufferProperties(ctx.PhysicalDevice, &externalBufferInfo, &externalBufferProperties);

            if ((externalBufferProperties.ExternalMemoryProperties.ExternalMemoryFeatures & ExternalMemoryFeatureFlags.ExportableBit) == 0)
            {
                return;
            }

            requiresDedicated = (externalBufferProperties.ExternalMemoryProperties.ExternalMemoryFeatures & ExternalMemoryFeatureFlags.DedicatedOnlyBit) != 0;
        }

        var allocCreateInfo = new AllocationCreateInfo
        {
            Usage = MemoryUsage.Auto,
        };

        ctx.Allocator.FindMemoryTypeIndexForBufferInfo(in bufCreateInfo, in allocCreateInfo, out var memTypeIndexObj);
        Assert.NotNull(memTypeIndexObj);
        var memTypeIndex = (uint)memTypeIndexObj.Value;

        var poolCreateInfo = new AllocationPoolCreateInfo
        {
            MemoryTypeIndex = (int)memTypeIndex,
            MemoryAllocateNext = &exportMemAllocInfo,
        };

        using var pool = ctx.Allocator.CreatePool(in poolCreateInfo);

        allocCreateInfo.Pool = pool;

        for (var test = 0; test < 2; test++)
        {
            if (test == 0 && requiresDedicated)
            {
                continue;
            }

            if (test == 1)
            {
                allocCreateInfo.Flags = AllocationCreateFlags.DedicatedMemory;
            }

            ctx.Allocator.CreateBuffer(in bufCreateInfo, in allocCreateInfo, out var buf, out var alloc);
            Assert.NotNull(alloc);

            var res = ctx.Allocator.GetMemoryWin32Handle(alloc, handleType, out var handle);
            Assert.Equal(Result.Success, res);
            Assert.NotEqual(0, handle);

            res = ctx.Allocator.GetMemoryWin32Handle(alloc, handleType, out var handle2);
            Assert.Equal(Result.Success, res);
            Assert.NotEqual(0, handle2);
            Assert.NotEqual(handle2, handle);

            alloc.Dispose();
            ctx.Vk.DestroyBuffer(ctx.Device, buf, ReadOnlySpan<AllocationCallbacks>.Empty);
            ctx.Allocator.CloseWin32Handle(handle);
            ctx.Allocator.CloseWin32Handle(handle2);
        }
    }

    [Fact]
    public void IsExternalMemoryWin32Supported_MatchesContextDetection()
    {
        using var ctx = new VulkanContext();
        Assert.Equal(ctx.ExternalMemoryWin32Supported, ctx.Allocator.IsExternalMemoryWin32Supported);
    }
}
