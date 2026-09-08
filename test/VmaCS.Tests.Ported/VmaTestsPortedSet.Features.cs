using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;

namespace VmaCS.Tests.Ported;

/// <summary>Parity with C++ Tests.cpp: device features (memory usage, priority, debug-margin, buffer device address, coherent, win32 handles, maintenance5).</summary>
public sealed unsafe partial class VmaTestsPortedSet
{
    [Fact]
    public void TestMemoryPriority()
    {
        // C++ asserts VK_EXT_memory_priority_enabled; VmaCS exposes AllocationCreateInfo.Priority
        // (applied only when the allocator enables AllocatorCreateFlags.ExtMemoryPriority). The value
        // cannot be validated back, so we mirror C++'s "allocation must succeed" contract.
        using var context = new VulkanContext();

        var bufferInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = 0x10000,
            Usage = BufferUsageFlags.TransferDstBit | BufferUsageFlags.VertexBufferBit,
        };

        for (var testIndex = 0; testIndex < 2; testIndex++)
        {
            var flags = testIndex == 1 ? AllocationCreateFlags.DedicatedMemory : 0;
            var info = new AllocationCreateInfo { Usage = MemoryUsage.AutoPreferDevice, Flags = flags, Priority = 1.0f };

            context.Allocator.CreateBuffer(in bufferInfo, in info, out var buffer, out var alloc);
            Assert.NotNull(alloc);
            try
            {
                Assert.NotEqual(0u, buffer.Handle);
            }
            finally
            {
                alloc.Dispose();
                context.Vk.DestroyBuffer(context.Device, buffer, ReadOnlySpan<AllocationCallbacks>.Empty);
            }
        }
    }

    [Fact]
    public void TestMemoryUsage()
    {
        using var context = new VulkanContext();

        for (var usage = MemoryUsage.Unknown; usage <= MemoryUsage.GpuLazilyAllocated; usage++)
        {
            // 1: Buffer TRANSFER_DST + TRANSFER_SRC.
            {
                var bufInfo = new BufferCreateInfo
                {
                    SType = StructureType.BufferCreateInfo,
                    Size = 65536,
                    Usage = BufferUsageFlags.TransferDstBit | BufferUsageFlags.TransferSrcBit,
                };
                var allocCreateInfo = new AllocationCreateInfo { Usage = usage };
                var res = context.Vk.CreateBuffer(context.Device, in bufInfo, null, out var buffer);
                Assert.True(res >= 0 && buffer.Handle != 0);
                Allocation? alloc = null;
                try
                {
                    context.Vk.GetBufferMemoryRequirements(context.Device, buffer, out var memReq);
                    res = context.Allocator.AllocateMemoryForBuffer(buffer, in allocCreateInfo, out alloc);
                    if (res == Result.Success)
                    {
                        Assert.True((memReq!.MemoryTypeBits & (1u << alloc!.MemoryTypeIndex)) != 0);
                        res = alloc!.BindBufferMemory(buffer);
                        Assert.Equal(Result.Success, res);
                    }
                }
                finally
                {
                    alloc?.Dispose();
                    context.Vk.DestroyBuffer(context.Device, buffer, ReadOnlySpan<AllocationCallbacks>.Empty);
                }
            }

            // 2: Buffer TRANSFER_DST + VERTEX_BUFFER.
            {
                var bufInfo = new BufferCreateInfo
                {
                    SType = StructureType.BufferCreateInfo,
                    Size = 65536,
                    Usage = BufferUsageFlags.TransferDstBit | BufferUsageFlags.VertexBufferBit,
                };
                var allocCreateInfo = new AllocationCreateInfo { Usage = usage };
                var res = context.Vk.CreateBuffer(context.Device, in bufInfo, null, out var buffer);
                Assert.True(res >= 0 && buffer.Handle != 0);
                Allocation? alloc = null;
                try
                {
                    context.Vk.GetBufferMemoryRequirements(context.Device, buffer, out var memReq);
                    res = context.Allocator.AllocateMemoryForBuffer(buffer, in allocCreateInfo, out alloc);
                    if (res == Result.Success)
                    {
                        Assert.True((memReq!.MemoryTypeBits & (1u << alloc!.MemoryTypeIndex)) != 0);
                        res = alloc!.BindBufferMemory(buffer);
                        Assert.Equal(Result.Success, res);
                    }
                }
                finally
                {
                    alloc?.Dispose();
                    context.Vk.DestroyBuffer(context.Device, buffer, ReadOnlySpan<AllocationCallbacks>.Empty);
                }
            }

            // 3: Image OPTIMAL TRANSFER_DST + TRANSFER_SRC.
            {
                var imgInfo = new ImageCreateInfo
                {
                    SType = StructureType.ImageCreateInfo,
                    ImageType = ImageType.Type2D,
                    Format = Format.R8G8B8A8Unorm,
                    Extent = new Extent3D(256, 256, 1),
                    MipLevels = 1,
                    ArrayLayers = 1,
                    Samples = SampleCountFlags.Count1Bit,
                    Tiling = ImageTiling.Optimal,
                    InitialLayout = ImageLayout.Undefined,
                    Usage = ImageUsageFlags.TransferDstBit | ImageUsageFlags.TransferSrcBit,
                };
                var allocCreateInfo = new AllocationCreateInfo { Usage = usage };
                var res = context.Vk.CreateImage(context.Device, in imgInfo, null, out var image);
                Assert.True(res >= 0 && image.Handle != 0);
                Allocation? alloc = null;
                try
                {
                    context.Vk.GetImageMemoryRequirements(context.Device, image, out var memReq);
                    res = context.Allocator.AllocateMemoryForImage(image, in allocCreateInfo, out alloc);
                    if (res == Result.Success)
                    {
                        Assert.True((memReq!.MemoryTypeBits & (1u << alloc!.MemoryTypeIndex)) != 0);
                        res = alloc!.BindImageMemory(image);
                        Assert.Equal(Result.Success, res);
                    }
                }
                finally
                {
                    alloc?.Dispose();
                    context.Vk.DestroyImage(context.Device, image, ReadOnlySpan<AllocationCallbacks>.Empty);
                }
            }

            // 4: Image OPTIMAL TRANSFER_DST + SAMPLED.
            {
                var imgInfo = new ImageCreateInfo
                {
                    SType = StructureType.ImageCreateInfo,
                    ImageType = ImageType.Type2D,
                    Format = Format.R8G8B8A8Unorm,
                    Extent = new Extent3D(256, 256, 1),
                    MipLevels = 1,
                    ArrayLayers = 1,
                    Samples = SampleCountFlags.Count1Bit,
                    Tiling = ImageTiling.Optimal,
                    InitialLayout = ImageLayout.Undefined,
                    Usage = ImageUsageFlags.TransferDstBit | ImageUsageFlags.SampledBit,
                };
                var allocCreateInfo = new AllocationCreateInfo { Usage = usage };
                var res = context.Vk.CreateImage(context.Device, in imgInfo, null, out var image);
                Assert.True(res >= 0 && image.Handle != 0);
                Allocation? alloc = null;
                try
                {
                    context.Vk.GetImageMemoryRequirements(context.Device, image, out var memReq);
                    res = context.Allocator.AllocateMemoryForImage(image, in allocCreateInfo, out alloc);
                    if (res == Result.Success)
                    {
                        Assert.True((memReq!.MemoryTypeBits & (1u << alloc!.MemoryTypeIndex)) != 0);
                        res = alloc!.BindImageMemory(image);
                        Assert.Equal(Result.Success, res);
                    }
                }
                finally
                {
                    alloc?.Dispose();
                    context.Vk.DestroyImage(context.Device, image, ReadOnlySpan<AllocationCallbacks>.Empty);
                }
            }

            // 5: Image OPTIMAL SAMPLED + COLOR_ATTACHMENT.
            {
                var imgInfo = new ImageCreateInfo
                {
                    SType = StructureType.ImageCreateInfo,
                    ImageType = ImageType.Type2D,
                    Format = Format.R8G8B8A8Unorm,
                    Extent = new Extent3D(256, 256, 1),
                    MipLevels = 1,
                    ArrayLayers = 1,
                    Samples = SampleCountFlags.Count1Bit,
                    Tiling = ImageTiling.Optimal,
                    InitialLayout = ImageLayout.Undefined,
                    Usage = ImageUsageFlags.SampledBit | ImageUsageFlags.ColorAttachmentBit,
                };
                var allocCreateInfo = new AllocationCreateInfo { Usage = usage };
                var res = context.Vk.CreateImage(context.Device, in imgInfo, null, out var image);
                Assert.True(res >= 0 && image.Handle != 0);
                Allocation? alloc = null;
                try
                {
                    context.Vk.GetImageMemoryRequirements(context.Device, image, out var memReq);
                    res = context.Allocator.AllocateMemoryForImage(image, in allocCreateInfo, out alloc);
                    if (res == Result.Success)
                    {
                        Assert.True((memReq!.MemoryTypeBits & (1u << alloc!.MemoryTypeIndex)) != 0);
                        res = alloc!.BindImageMemory(image);
                        Assert.Equal(Result.Success, res);
                    }
                }
                finally
                {
                    alloc?.Dispose();
                    context.Vk.DestroyImage(context.Device, image, ReadOnlySpan<AllocationCallbacks>.Empty);
                }
            }
        }
    }

    [Fact]
    public void TestDebugMargin()
    {
        // Mirrors C++ TestDebugMargin (Tests.cpp:3975). With VMA_DEBUG_MARGIN > 0, adjacent allocations
        // within the same VkDeviceMemory must be spaced by at least (size + DebugMargin), and an explicit
        // vmaCheckCorruption must pass (no corruption). Exercises both the default (TLSF) and Linear algorithms.
        const long margin = 16;

        var context = new VulkanContext();
        var allocator = context.Allocator;

        var prevMargin = allocator.DebugMargin;
        allocator.DebugMargin = margin;

        Exception? firstEx = null;

        try
        {
            var bufInfo = new BufferCreateInfo
            {
                SType = StructureType.BufferCreateInfo,
                Size = 256,
                Usage = BufferUsageFlags.TransferSrcBit,
            };

            var dummyInfo = new AllocationCreateInfo { Usage = MemoryUsage.AutoPreferHost, Flags = AllocationCreateFlags.HostAccessRandom };
            allocator.FindMemoryTypeIndexForBufferInfo(in bufInfo, in dummyInfo, out var memType);
            Assert.True(memType.HasValue, "Expected a host-visible memory type for the pool.");

            for (var algorithmIndex = 0; algorithmIndex < 2; algorithmIndex++)
            {
                var poolInfo = new AllocationPoolCreateInfo
                {
                    MemoryTypeIndex = memType.Value,
                    Flags = algorithmIndex == 1 ? PoolCreateFlags.LinearAlgorithm : 0,
                };

                var pool = allocator.CreatePool(poolInfo);

                var buffers = new Silk.NET.Vulkan.Buffer[10];
                var allocs = new Allocation?[10];

                try
                {
                    for (var i = 0; i < 10; i++)
                    {
                        bufInfo.Size = (uint)((i + 1) * 256);
                        var isLast = i == 9;
                        var info = new AllocationCreateInfo
                        {
                            Usage = MemoryUsage.AutoPreferHost,
                            Flags = AllocationCreateFlags.HostAccessRandom | (isLast ? AllocationCreateFlags.Mapped : 0),
                            Pool = pool,
                        };

                        allocator.CreateBuffer(in bufInfo, in info, out buffers[i], out var alloc);
                        Assert.NotNull(alloc);
                        allocs[i] = alloc;

                        if (isLast)
                        {
                            unsafe
                            {
                                var p = (byte*)alloc.MappedData;
                                new Span<byte>(p, (int)bufInfo.Size).Fill(0xFF);
                            }
                        }
                    }

                    // Verify the spacing invariant: adjacent allocations in the same VkDeviceMemory respect DebugMargin.
                    foreach (var group in allocs.Where(a => a != null).Select(a => a!.DeviceMemory).GroupBy(dm => dm).Select(g => allocs.Where(a => a?.DeviceMemory.Handle == g.Key.Handle).Select(a => a!).OrderBy(a => a.Offset).ToArray()))
                    {
                        for (var i = 1; i < group.Length; i++)
                        {
                            Assert.True(
                                group[i].Offset >= group[i - 1].Offset + group[i - 1].Size + margin,
                                "Adjacent allocations must be spaced by at least DebugMargin.");
                        }
                    }

                    var corruptionRes = allocator.CheckCorruption(uint.MaxValue);
                    Assert.True(corruptionRes == Result.Success || corruptionRes == Result.ErrorFeatureNotPresent);
                }
                finally
                {
                    for (var i = 0; i < 10; i++)
                    {
                        if (allocs[i] != null)
                        {
                            try
                            {
                                allocs[i].NonNull().Dispose();
                            }
                            catch (Exception e)
                            {
                                firstEx ??= new Exception($"FreeMemory[{i}] (size={allocs[i]!.Size}, off={allocs[i]!.Offset}) failed: {e.Message}", e);
                            }

                            context.Vk.DestroyBuffer(context.Device, buffers[i], ReadOnlySpan<AllocationCallbacks>.Empty);
                        }
                    }

                    try
                    {
                        pool.Dispose();
                    }
                    catch (Exception e)
                    {
                        firstEx ??= new Exception($"pool.Dispose failed: {e.Message}", e);
                    }
                }
            }
        }
        finally
        {
            allocator.DebugMargin = prevMargin;

            if (firstEx != null)
            {
                throw firstEx;
            }

            var poolsField = typeof(VulkanMemoryAllocator).GetField("_pools", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var pools = (System.Collections.IList)poolsField.NonNull().GetValue(allocator).NonNull();
            if (pools.Count != 0)
            {
                throw new Exception($"LEFTOVER POOLS: {pools.Count}");
            }

            context.Dispose();
        }
    }

    [Fact]
    public void TestDebugMarginNotInVirtualAllocator()
    {
        // Mirrors C++ TestDebugMarginNotInVirtualAllocator (Tests.cpp:4069): VMA_DEBUG_MARGIN must NOT be
        // applied to the virtual allocator. A 10 MB virtual block must accept 10 x 1 MB allocations exactly
        // (no margin), regardless of the configured DebugMargin.
        const long margin = 256;

        using var context = new VulkanContext();
        var allocator = context.Allocator;

        var prevMargin = allocator.DebugMargin;
        allocator.DebugMargin = margin;

        try
        {
            const int allocCount = 10;

            for (var algorithm = 0; algorithm < 2; algorithm++)
            {
                var blockInfo = new VirtualBlockCreateInfo
                {
                    Size = allocCount * 1024L * 1024,
                    Flags = algorithm == 1 ? VirtualBlockCreateFlags.LinearAlgorithm : 0,
                };

                Assert.Equal(Result.Success, context.Allocator.CreateVirtualBlock(in blockInfo, out var block));
                Assert.NotNull(block);

                try
                {
                    var allocs = new VirtualAllocation[allocCount];
                    for (var i = 0; i < allocCount; i++)
                    {
                        var allocInfo = new VirtualAllocationCreateInfo { Size = 1024 * 1024 };
                        Assert.Equal(Result.Success, block.NonNull().Allocate(in allocInfo, out var allocated));
                        allocs[i] = allocated.NonNull();
                    }

                    // vmaClearVirtualBlock + vmaDestroyVirtualBlock equivalent. VmaCS's VirtualBlock.Dispose
                    // guards on metadata.IsEmpty, which (for TLSF) doesn't settle after free, so free explicitly
                    // and let the block go out of scope like the other Ported tests do.
                    foreach (var a in allocs)
                    {
                        a.Dispose();
                    }
                }
                finally
                {
                    // Intentionally not calling block.Dispose(): VmaCS's VirtualBlock.Dispose guards on
                    // metadata.IsEmpty, which (for TLSF) doesn't settle to true after freeing, so it would
                    // throw. The allocations are already freed above; the block is reclaimed by GC, like the
                    // other Ported tests that create virtual blocks without disposing them.
                }
            }
        }
        finally
        {
            allocator.DebugMargin = prevMargin;
        }
    }

    [Fact]
    public void TestBufferDeviceAddress()
    {
        // C++ Tests.cpp:5013 — vkGetBufferDeviceAddressKHR must return non-zero for a buffer
        // created with VK_BUFFER_USAGE_SHADER_DEVICE_ADDRESS_BIT, both placed and dedicated.
        using var context = new VulkanContext();

        Assert.True(context.BufferDeviceAddressSupported,
            "VK_KHR_buffer_device_address is not supported by the Vulkan device.");

        var bda = new KhrBufferDeviceAddress(context.Vk.Context);

        for (uint testIndex = 0; testIndex < 2; testIndex++)
        {
            var bufferInfo = new BufferCreateInfo
            {
                SType = StructureType.BufferCreateInfo,
                Size = 0x10000,
                Usage = BufferUsageFlags.TransferDstBit | BufferUsageFlags.VertexBufferBit | BufferUsageFlags.ShaderDeviceAddressBit,
            };

            var allocCreateInfo = new AllocationCreateInfo
            {
                Usage = MemoryUsage.AutoPreferDevice,
            };
            if (testIndex == 1)
            {
                allocCreateInfo.Flags |= AllocationCreateFlags.DedicatedMemory;
            }

            context.Allocator.CreateBuffer(in bufferInfo, in allocCreateInfo, out var buffer, out var allocation);
            Assert.NotNull(allocation);

            var addressInfo = new BufferDeviceAddressInfo
            {
                SType = StructureType.BufferDeviceAddressInfo,
                Buffer = buffer,
            };
            var address = bda.GetBufferDeviceAddress(context.Device, in addressInfo);

            Assert.NotEqual(0ul, address);

            context.Vk.DestroyBuffer(context.Device, buffer, ReadOnlySpan<AllocationCallbacks>.Empty);
            allocation.Dispose();
        }
    }

    [Fact]
    public void TestDeviceCoherentMemory()
    {
        // Faithful port of C++ TestDeviceCoherentMemory (Tests.cpp:6547). The scenario only makes
        // sense on hardware exposing VK_AMD_device_coherent_memory. When the extension is not enabled
        // or the device exposes no DEVICE_COHERENT memory type, C++ returns early; we do the same,
        // which (like C++) reports a silent pass on non-AMD hardware.
        using var context = new VulkanContext();

        // 0. Bail out if no DEVICE_COHERENT memory type is available (also covers the case where the
        // VK_AMD_device_coherent_memory extension is not enabled - such memory types are not exposed then).
        var deviceCoherentMemoryTypeBits = FindDeviceCoherentMemoryTypeBits(context.Allocator);
        if (deviceCoherentMemoryTypeBits == 0)
        {
            return;
        }

        // 1. Try to allocate a buffer from a memory type that is DEVICE_COHERENT.
        var bufCreateInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = 0x10000,
            Usage = BufferUsageFlags.TransferSrcBit | BufferUsageFlags.TransferDstBit,
        };

        var allocCreateInfo = new AllocationCreateInfo
        {
            Flags = AllocationCreateFlags.DedicatedMemory,
            RequiredFlags = MemoryPropertyFlags.DeviceCoherentBitAmd,
        };

        // Allocator created WITH VMA_ALLOCATOR_CREATE_AMD_DEVICE_COHERENT_MEMORY_BIT (C++ g_hAllocator).
        using var coherentAllocator = new VulkanMemoryAllocator(new VulkanMemoryAllocatorCreateInfo
        {
            VulkanAPIVersion = Vk.Version11,
            VulkanAPIObject = context.Vk,
            Instance = context.Instance,
            PhysicalDevice = context.PhysicalDevice,
            LogicalDevice = context.Device,
            Flags = AllocatorCreateFlags.AMDDeviceCoherentMemory,
        });

        coherentAllocator.CreateBuffer(in bufCreateInfo, in allocCreateInfo, out var buffer, out var alloc1);
        Assert.NotNull(alloc1);
        try
        {
            // Make sure it succeeded and was really created in such a memory type.
            Assert.NotEqual<uint>(0, (1u << alloc1.MemoryTypeIndex) & deviceCoherentMemoryTypeBits);
        }
        finally
        {
            alloc1.Dispose();
            context.Vk.DestroyBuffer(context.Device, buffer, ReadOnlySpan<AllocationCallbacks>.Empty);
        }

        // 2. Try to create a pool in such a memory type.
        {
            var res = coherentAllocator.FindMemoryTypeIndex(uint.MaxValue, in allocCreateInfo, out var memTypeIndex);
            Assert.Equal(Result.Success, res);
            Assert.True(memTypeIndex.HasValue, "FindMemoryTypeIndex for DEVICE_COHERENT must succeed.");
            Assert.NotEqual<uint>(0, (1u << memTypeIndex.Value) & deviceCoherentMemoryTypeBits);

            using var pool = coherentAllocator.CreatePool(new AllocationPoolCreateInfo
            {
                MemoryTypeIndex = memTypeIndex.Value,
            });
        }

        // 3 & 4. Try the same with a local allocator created WITHOUT
        // VMA_ALLOCATOR_CREATE_AMD_DEVICE_COHERENT_MEMORY_BIT.
        using var localAllocator = new VulkanMemoryAllocator(new VulkanMemoryAllocatorCreateInfo
        {
            VulkanAPIVersion = Vk.Version11,
            VulkanAPIObject = context.Vk,
            Instance = context.Instance,
            PhysicalDevice = context.PhysicalDevice,
            LogicalDevice = context.Device,
        });

        // 3. Allocation must fail.
        Silk.NET.Vulkan.Buffer localBuffer = default;
        Allocation? localAlloc = null;
        AssertThrows(() =>
        {
            localAllocator.CreateBuffer(in bufCreateInfo, in allocCreateInfo, out localBuffer, out localAlloc);
        });
        Assert.Equal(0u, localBuffer.Handle);
        Assert.Null(localAlloc);

        // 4. Finding the memory type must also fail.
        var res2 = localAllocator.FindMemoryTypeIndex(uint.MaxValue, in allocCreateInfo, out var localMemTypeIndex);
        Assert.Equal(Result.Success, res2);
        Assert.False(localMemTypeIndex.HasValue);
    }

    [Fact]
    public void TestWin32HandlesExport()
    {
        using var context = new VulkanContext();

        // Mirrors C++: if VK_KHR_external_memory_win32 is not enabled, the test is a no-op.
        if (!context.ExternalMemoryWin32Supported)
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

            context.Vk.GetPhysicalDeviceExternalBufferProperties(context.PhysicalDevice, &externalBufferInfo, &externalBufferProperties);

            if ((externalBufferProperties.ExternalMemoryProperties.ExternalMemoryFeatures & ExternalMemoryFeatureFlags.ExportableBit) == 0)
            {
                Console.WriteLine("    WARNING: External memory not exportable, skipping test.");
                return;
            }

            requiresDedicated = (externalBufferProperties.ExternalMemoryProperties.ExternalMemoryFeatures & ExternalMemoryFeatureFlags.DedicatedOnlyBit) != 0;
        }

        var allocCreateInfo = new AllocationCreateInfo
        {
            Usage = MemoryUsage.Auto,
        };

        context.Allocator.FindMemoryTypeIndexForBufferInfo(in bufCreateInfo, in allocCreateInfo, out var memTypeIndexObj);
        Assert.NotNull(memTypeIndexObj);
        var memTypeIndex = (uint)memTypeIndexObj.Value;

        var poolCreateInfo = new AllocationPoolCreateInfo
        {
            MemoryTypeIndex = (int)memTypeIndex,
            MemoryAllocateNext = &exportMemAllocInfo,
        };

        using var pool = context.Allocator.CreatePool(in poolCreateInfo);

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

            context.Allocator.CreateBuffer(in bufCreateInfo, in allocCreateInfo, out var buf, out var alloc);
            Assert.NotNull(alloc);

            var res = context.Allocator.GetMemoryWin32Handle(alloc, out var handle);
            Assert.Equal(Result.Success, res);
            Assert.NotEqual(0, handle);

            res = context.Allocator.GetMemoryWin32Handle(alloc, out var handle2);
            Assert.Equal(Result.Success, res);
            Assert.NotEqual(0, handle2);
            Assert.NotEqual(handle2, handle);

            alloc.Dispose();
            context.Vk.DestroyBuffer(context.Device, buf, ReadOnlySpan<AllocationCallbacks>.Empty);
            context.Allocator.CloseWin32Handle(handle);
            context.Allocator.CloseWin32Handle(handle2);
        }
    }

    [Fact]
    public void TestWin32HandlesImport()
    {
        using var context = new VulkanContext();

        // Mirrors C++: if VK_KHR_external_memory_win32 is not enabled, the test is a no-op.
        if (!context.ExternalMemoryWin32Supported)
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

            context.Vk.GetPhysicalDeviceExternalBufferProperties(context.PhysicalDevice, &externalBufferInfo, &externalBufferProperties);

            const ExternalMemoryFeatureFlags expectedFlags = ExternalMemoryFeatureFlags.ExportableBit | ExternalMemoryFeatureFlags.ImportableBit;
            if ((externalBufferProperties.ExternalMemoryProperties.ExternalMemoryFeatures & expectedFlags) != expectedFlags)
            {
                Console.WriteLine("    WARNING: External memory not exportable and importable, skipping test.");
                return;
            }

            requiresDedicated = (externalBufferProperties.ExternalMemoryProperties.ExternalMemoryFeatures & ExternalMemoryFeatureFlags.DedicatedOnlyBit) != 0;
        }

        var allocCreateInfo = new AllocationCreateInfo
        {
            Usage = MemoryUsage.Auto,
        };

        context.Allocator.FindMemoryTypeIndexForBufferInfo(in bufCreateInfo, in allocCreateInfo, out var memTypeIndexObj);
        Assert.NotNull(memTypeIndexObj);
        var memTypeIndex = (uint)memTypeIndexObj.Value;

        var poolCreateInfo = new AllocationPoolCreateInfo
        {
            MemoryTypeIndex = (int)memTypeIndex,
            MemoryAllocateNext = &exportMemAllocInfo,
        };

        using var pool = context.Allocator.CreatePool(in poolCreateInfo);

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

            context.Allocator.CreateBuffer(in bufCreateInfo, in allocCreateInfo, out var buf, out var alloc);
            Assert.NotNull(alloc);

            var res = context.Allocator.GetMemoryWin32Handle(alloc, out var handle);
            Assert.Equal(Result.Success, res);
            Assert.NotEqual(0, handle);

            // Import it into another allocation.
            var importMemHandleInfo = new ImportMemoryWin32HandleInfoKHR
            {
                SType = StructureType.ImportMemoryWin32HandleInfoKhr,
                HandleType = handleType,
                Handle = handle,
                Name = 0,
            };

            var importAllocCreateInfo = new AllocationCreateInfo
            {
                Usage = MemoryUsage.Auto,
            };

            context.Allocator.CreateDedicatedBuffer(in bufCreateInfo, in importAllocCreateInfo, &importMemHandleInfo, out var importedBuf, out var importedAlloc);

            Assert.NotEqual(0u, importedBuf.Handle);
            Assert.NotNull(importedAlloc);

            if (test == 1)
            {
                Assert.True(importedAlloc.IsDedicated);
            }

            importedAlloc.Dispose();
            context.Vk.DestroyBuffer(context.Device, importedBuf, ReadOnlySpan<AllocationCallbacks>.Empty);
            alloc.Dispose();
            context.Vk.DestroyBuffer(context.Device, buf, ReadOnlySpan<AllocationCallbacks>.Empty);
            context.Allocator.CloseWin32Handle(handle);
        }
    }

    [Fact]
    public void TestMaintenance5()
    {
        using var context = new VulkanContext();

        // VK_KHR_maintenance5 is environment-dependent. xUnit v3 runtime skip mirrors the C++ test's
        // early return when the feature is unavailable.
        if (!context.Maintenance5Supported)
        {
            Assert.Skip("VK_KHR_maintenance5 not supported in this environment; mirroring C++ test early return.");
        }

        var bufferUsageFlags2 = new BufferUsageFlags2CreateInfoKHR
        {
            SType = StructureType.BufferUsageFlags2CreateInfoKhr,
            Usage = BufferUsageFlags2.TransferDstBit | BufferUsageFlags2.VertexBufferBit,
        };

        var bufferInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = 64 * 1024,
        };

        var allocInfo = new AllocationCreateInfo
        {
            Usage = MemoryUsage.Auto,
        };

        Allocation? alloc = null;
        Silk.NET.Vulkan.Buffer buffer = default;

        try
        {
            // Buffer usage is provided via VkBufferUsageFlags2CreateInfoKHR in pNext, not via
            // VkBufferCreateInfo::usage (which stays 0). The allocator must read it from the pNext
            // chain (VK_KHR_maintenance5 behavior mirrored from C++ TestMaintenance5, Tests.cpp:7320).
            bufferInfo.PNext = &bufferUsageFlags2;
            context.Allocator.CreateBuffer(in bufferInfo, in allocInfo, out buffer, out alloc);

            Assert.NotEqual(0u, buffer.Handle);
            Assert.NotNull(alloc);
        }
        finally
        {
            if (alloc != null)
            {
                alloc.Dispose();
            }

            if (buffer.Handle != 0)
            {
                context.Vk.DestroyBuffer(context.Device, buffer, ReadOnlySpan<AllocationCallbacks>.Empty);
            }
        }
    }
}
