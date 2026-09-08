using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace VmaCS.Tests.Ported;

/// <summary>Parity with C++ Tests.cpp: basic scenarios (json, basics, pools, init, alignment, heap limit).</summary>
public sealed unsafe partial class VmaTestsPortedSet
{
    private static int FindHostVisibleMemoryType(VulkanMemoryAllocator allocator)
    {
        for (var index = 0; index < allocator.MemoryProperties.MemoryTypeCount; index++)
        {
            if ((allocator.GetMemoryTypeProperties(index) & MemoryPropertyFlags.HostVisibleBit) != 0)
            {
                return index;
            }
        }

        throw new InvalidOperationException("No host-visible Vulkan memory type found.");
    }

    private static uint FindDeviceCoherentMemoryTypeBits(VulkanMemoryAllocator allocator)
    {
        uint memTypeBits = 0;
        var memProps = allocator.MemoryProperties;
        for (uint index = 0; index < memProps.MemoryTypeCount; index++)
        {
            if ((memProps.MemoryTypes[(int)index].PropertyFlags & MemoryPropertyFlags.DeviceCoherentBitAmd) != 0)
            {
                memTypeBits |= 1u << (int)index;
            }
        }

        return memTypeBits;
    }

    private static void AssertThrows(Action action)
    {
        var threw = false;
        try
        {
            action();
        }
        catch (Exception)
        {
            threw = true;
        }

        Assert.True(threw, "Expected the invalid allocation call to throw.");
    }

    private static MemoryRequirements GetBufferMemoryRequirements(VulkanContext context, in BufferCreateInfo bufferInfo)
    {
        context.Vk.CreateBuffer(context.Device, in bufferInfo, null, out var buffer);
        try
        {
            context.Vk.GetBufferMemoryRequirements(context.Device, buffer, out var req);
            return req;
        }
        finally
        {
            context.Vk.DestroyBuffer(context.Device, buffer, ReadOnlySpan<AllocationCallbacks>.Empty);
        }
    }

    [Fact]
    public void TestJson()
    {
        using var context = new VulkanContext();

        var allocs = new List<Allocation>();
        var pools = new List<VulkanMemoryPool>();

        var bufferInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = 1024,
            Usage = BufferUsageFlags.TransferSrcBit | BufferUsageFlags.TransferDstBit,
        };

        var imageInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = Format.R8G8B8A8Unorm,
            Extent = new Extent3D(16, 16, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.TransferSrcBit | ImageUsageFlags.TransferDstBit | ImageUsageFlags.SampledBit,
            InitialLayout = ImageLayout.Preinitialized,
        };

        var memReq = GetBufferMemoryRequirements(context, in bufferInfo);

        try
        {
            // poolType: 0 = default allocator, 1 = custom pool.
            for (uint poolType = 0; poolType < 2; ++poolType)
            {
                // memType: 0 = device-preferring, 1 = host-preferring.
                for (uint memType = 0; memType < 2; ++memType)
                {
                    var allocCreateInfo = new AllocationCreateInfo();

                    switch (memType)
                    {
                        case 0:
                            allocCreateInfo.Usage = MemoryUsage.AutoPreferDevice;
                            allocCreateInfo.Flags = AllocationCreateFlags.DontBind;
                            break;
                        case 1:
                            allocCreateInfo.Usage = MemoryUsage.AutoPreferHost;
                            allocCreateInfo.Flags = AllocationCreateFlags.DontBind | AllocationCreateFlags.HostAccessRandom;
                            break;
                    }

                    switch (poolType)
                    {
                        case 0:
                            allocCreateInfo.Pool = null;
                            break;
                        case 1:
                            {
                                context.Allocator.FindMemoryTypeIndexForBufferInfo(in bufferInfo, in allocCreateInfo, out var memTypeIndex);
                                Assert.True(memTypeIndex.HasValue, "Expected a memory type for the pool.");

                                var poolCreateInfo = new AllocationPoolCreateInfo
                                {
                                    MemoryTypeIndex = memTypeIndex.Value
                                };
                                var pool = context.Allocator.CreatePool(in poolCreateInfo);
                                pools.Add(pool);
                                allocCreateInfo.Pool = pool;
                                break;
                            }
                    }

                    // allocFlag: 0 = none, 1 = dedicated.
                    for (uint allocFlag = 0; allocFlag < 2; ++allocFlag)
                    {
                        if (allocFlag == 1)
                        {
                            allocCreateInfo.Flags |= AllocationCreateFlags.DedicatedMemory;
                        }

                        // allocType: 0 = raw memory, 1 = buffer, 2 = linear image, 3 = optimal image.
                        for (uint allocType = 0; allocType < 4; ++allocType)
                        {
                            // Optimal tiling images (allocType 3) cannot be placed in host-visible memory
                            // (their memoryTypeBits only include DEVICE_LOCAL types), so the host-preferring
                            // memType (1) combination is impossible on most GPUs.
                            if (allocType == 3 && memType == 1)
                            {
                                continue;
                            }
                            // data: 0 = none, 1 = user data int, 2 = name, 3 = user data int + name.
                            for (uint data = 0; data < 4; ++data)
                            {
                                Allocation? alloc = null;

                                switch (allocType)
                                {
                                    case 0:
                                        {
                                            var localCreateInfo = allocCreateInfo;
                                            localCreateInfo.Usage = memType == 0 ? MemoryUsage.GpuOnly : MemoryUsage.CpuOnly;
                                            context.Allocator.AllocateMemory(in memReq, in localCreateInfo, out alloc);
                                            Assert.NotNull(alloc);
                                            break;
                                        }
                                    case 1:
                                        {
                                            context.Allocator.CreateBuffer(in bufferInfo, in allocCreateInfo, out var buffer, out alloc);
                                            Assert.NotNull(alloc);
                                            context.Vk.DestroyBuffer(context.Device, buffer, ReadOnlySpan<AllocationCallbacks>.Empty);
                                            break;
                                        }
                                    case 2:
                                        {
                                            var imgCreate = imageInfo;
                                            imgCreate.Tiling = ImageTiling.Linear;
                                            imgCreate.Extent = new Extent3D(512, 1, 1);
                                            context.Allocator.CreateImage(in imgCreate, in allocCreateInfo, out var image, out alloc);
                                            Assert.NotNull(alloc);
                                            context.Vk.DestroyImage(context.Device, image, ReadOnlySpan<AllocationCallbacks>.Empty);
                                            break;
                                        }
                                    case 3:
                                        {
                                            var imgCreate = imageInfo;
                                            imgCreate.Tiling = ImageTiling.Optimal;
                                            imgCreate.Extent = new Extent3D(1024, 512, 1);
                                            var res = context.Allocator.CreateImage(in imgCreate, in allocCreateInfo, out var image, out alloc);
                                            Assert.NotNull(alloc);
                                            context.Vk.DestroyImage(context.Device, image, ReadOnlySpan<AllocationCallbacks>.Empty);
                                            break;
                                        }
                                }
                                if (alloc != null)
                                {
                                    // Track the allocation for cleanup BEFORE any further operations (e.g.
                                    // SetAllocationName), so the finally block frees it even if one throws.
                                    allocs.Add(alloc);

                                    switch (data)
                                    {
                                        case 1:
                                            alloc.UserData = 16112007;
                                            break;
                                        case 2:
                                            alloc.Name = "SHEPURD";
                                            break;
                                        case 3:
                                            alloc.UserData = 26012010;
                                            alloc.Name = "JOKER";
                                            break;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            // Mirror C++ TestJson: dump the allocator stats to a file.
            File.WriteAllText("JSON_VULKAN.json", context.Allocator.BuildStatsString(detailedMap: true));
        }
        finally
        {
            foreach (var alloc in allocs)
            {
                alloc.Dispose();
            }

            foreach (var pool in pools)
            {
                pool.Dispose();
            }
        }
    }

    private static void TestGetAllocatorInfo(VulkanContext context)
    {
        Assert.Equal(context.Instance.Handle, context.Allocator.Instance.Handle);
        Assert.Equal(context.PhysicalDevice.Handle, context.Allocator.PhysicalDevice.Handle);
        Assert.Equal(context.Device.Handle, context.Allocator.Device.Handle);
    }

    [Fact]
    public void TestBasics()
    {
        using var context = new VulkanContext();

        TestGetAllocatorInfo(context);

        TestMemoryRequirements(context);

        // Allocation that is MAPPED and not necessarily HOST_VISIBLE.
        {
            var bufInfo = new BufferCreateInfo
            {
                SType = StructureType.BufferCreateInfo,
                Size = 128,
                Usage = BufferUsageFlags.IndexBufferBit,
            };

            var allocCreateInfo = new AllocationCreateInfo
            {
                Usage = MemoryUsage.AutoPreferDevice,
                Flags = AllocationCreateFlags.HostAccessSequentialWrite | AllocationCreateFlags.Mapped,
            };

            context.Allocator.CreateBuffer(in bufInfo, in allocCreateInfo, out var buffer, out var bufAlloc);
            Assert.NotNull(bufAlloc);
            bufAlloc.Dispose();
            context.Vk.DestroyBuffer(context.Device, buffer, ReadOnlySpan<AllocationCallbacks>.Empty);

            // Same with DEDICATED_MEMORY.
            allocCreateInfo.Flags |= AllocationCreateFlags.DedicatedMemory;

            context.Allocator.CreateBuffer(in bufInfo, in allocCreateInfo, out var buffer2, out var bufAlloc2);
            Assert.NotNull(bufAlloc2);
            bufAlloc2.Dispose();
            context.Vk.DestroyBuffer(context.Device, buffer2, ReadOnlySpan<AllocationCallbacks>.Empty);
        }

        TestUserData(context);

        TestInvalidAllocations(context);
    }

    private static void TestMemoryRequirements(VulkanContext context)
    {
        var bufInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Usage = BufferUsageFlags.TransferSrcBit,
            Size = 128,
        };

        // No requirements.
        {
            var allocCreateInfo = new AllocationCreateInfo();
            context.Allocator.CreateBuffer(in bufInfo, in allocCreateInfo, out var buffer, out var alloc);
            Assert.NotNull(alloc);
            alloc.Dispose();
            context.Vk.DestroyBuffer(context.Device, buffer, ReadOnlySpan<AllocationCallbacks>.Empty);
        }

        // Usage = auto + host access.
        {
            var allocCreateInfo = new AllocationCreateInfo
            {
                Usage = MemoryUsage.Auto,
                Flags = AllocationCreateFlags.HostAccessRandom,
                RequiredFlags = 0,
                PreferredFlags = 0,
                MemoryTypeBits = uint.MaxValue,
            };
            context.Allocator.CreateBuffer(in bufInfo, in allocCreateInfo, out var buffer, out var alloc);
            Assert.NotNull(alloc);
            try
            {
                Assert.NotEqual(0u, buffer.Handle);
                var props = context.Allocator.GetMemoryTypeProperties(alloc.MemoryTypeIndex);
                Assert.True((props & MemoryPropertyFlags.HostVisibleBit) != 0);
            }
            finally
            {
                alloc.Dispose();
                context.Vk.DestroyBuffer(context.Device, buffer, ReadOnlySpan<AllocationCallbacks>.Empty);
            }
        }

        // Required flags, preferred flags.
        {
            var allocCreateInfo = new AllocationCreateInfo
            {
                Usage = MemoryUsage.Unknown,
                Flags = 0,
                RequiredFlags = MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit,
                PreferredFlags = MemoryPropertyFlags.DeviceLocalBit | MemoryPropertyFlags.HostCachedBit,
                MemoryTypeBits = 0,
            };
            context.Allocator.CreateBuffer(in bufInfo, in allocCreateInfo, out var buffer, out var alloc);
            Assert.NotNull(alloc);
            try
            {
                var props = context.Allocator.GetMemoryTypeProperties(alloc.MemoryTypeIndex);
                Assert.True((props & MemoryPropertyFlags.HostVisibleBit) != 0);
                Assert.True((props & MemoryPropertyFlags.HostCoherentBit) != 0);
            }
            finally
            {
                alloc.Dispose();
                context.Vk.DestroyBuffer(context.Device, buffer, ReadOnlySpan<AllocationCallbacks>.Empty);
            }
        }

        // memoryTypeBits.
        {
            var allocCreateInfo = new AllocationCreateInfo
            {
                Usage = MemoryUsage.Unknown,
                Flags = 0,
                RequiredFlags = 0,
                PreferredFlags = 0,
            };
            context.Allocator.CreateBuffer(in bufInfo, in allocCreateInfo, out var buffer, out var alloc);
            Assert.NotNull(alloc);
            var memType = alloc.MemoryTypeIndex;
            alloc.Dispose();
            context.Vk.DestroyBuffer(context.Device, buffer, ReadOnlySpan<AllocationCallbacks>.Empty);

            allocCreateInfo.MemoryTypeBits = 1u << memType;
            context.Allocator.CreateBuffer(in bufInfo, in allocCreateInfo, out var buffer2, out var alloc2);
            Assert.NotNull(alloc2);
            try
            {
                Assert.Equal(memType, alloc2.MemoryTypeIndex);
            }
            finally
            {
                alloc2.Dispose();
                context.Vk.DestroyBuffer(context.Device, buffer2, ReadOnlySpan<AllocationCallbacks>.Empty);
            }
        }
    }

    private static void TestUserData(VulkanContext context)
    {
        var bufInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Usage = BufferUsageFlags.IndexBufferBit,
            Size = 0x10000,
        };

        for (uint testIndex = 0; testIndex < 2; ++testIndex)
        {
            // Opaque pointer.
            {
                var numberAsPointer = unchecked((long)0xC2501FF3u);
                var pointerToSomething = new object();

                var allocCreateInfo = new AllocationCreateInfo
                {
                    Usage = MemoryUsage.Auto,
                    UserData = numberAsPointer,
                };
                if (testIndex == 1)
                {
                    allocCreateInfo.Flags |= AllocationCreateFlags.DedicatedMemory;
                }

                context.Allocator.CreateBuffer(in bufInfo, in allocCreateInfo, out var buffer, out var alloc);
                Assert.NotNull(alloc);
                try
                {
                    Assert.Equal(numberAsPointer, (long)alloc.UserData.NonNull());

                    alloc.UserData = pointerToSomething;
                    Assert.Same(pointerToSomething, alloc.UserData);
                }
                finally
                {
                    alloc.Dispose();
                    context.Vk.DestroyBuffer(context.Device, buffer, ReadOnlySpan<AllocationCallbacks>.Empty);
                }
            }

            {
                const string name1 = "Buffer name \\\"\'<>&% \nSecond line .,;=";
                const string name2 = "2";

                var allocCreateInfo = new AllocationCreateInfo
                {
                    Usage = MemoryUsage.Auto,
                    Flags = 0,
                    UserData = name1,
                };
                if (testIndex == 1)
                {
                    allocCreateInfo.Flags |= AllocationCreateFlags.DedicatedMemory;
                }

                context.Allocator.CreateBuffer(in bufInfo, in allocCreateInfo, out var buffer, out var alloc);
                Assert.NotNull(alloc);
                try
                {
                    alloc.Name = name1;
                    Assert.Equal(name1, alloc.Name);

                    alloc.Name = name2;
                    Assert.Equal(name2, alloc.Name);

                    alloc.Name = null;
                    Assert.Null(alloc.Name);
                }
                finally
                {
                    alloc.Dispose();
                    context.Vk.DestroyBuffer(context.Device, buffer, ReadOnlySpan<AllocationCallbacks>.Empty);
                }
            }
        }
    }

    private static void TestInvalidAllocations(VulkanContext context)
    {
        // Try to allocate 0 bytes. C++ expects VK_ERROR_INITIALIZATION_FAILED; VmaCS throws
        // ArgumentException("Allocation size cannot be 0").
        {
            var req0 = new MemoryRequirements { Size = 0, Alignment = 4, MemoryTypeBits = uint.MaxValue };
            var zeroInfo = new AllocationCreateInfo();
            Assert.Throws<ArgumentException>(() => context.Allocator.AllocateMemory(in req0, in zeroInfo, out _));
        }

        // Try to create buffer with size = 0.
        {
            var zeroBuf = new BufferCreateInfo { SType = StructureType.BufferCreateInfo, Size = 0, Usage = BufferUsageFlags.TransferSrcBit };
            var zeroInfo = new AllocationCreateInfo();
            AssertThrows(() => context.Allocator.CreateBuffer(in zeroBuf, in zeroInfo, out _, out _));
        }

        // Try to create image with one dimension = 0.
        {
            var zeroImg = new ImageCreateInfo
            {
                SType = StructureType.ImageCreateInfo,
                ImageType = ImageType.Type2D,
                Format = Format.B8G8R8A8Unorm,
                Extent = new Extent3D(128, 0, 1),
                MipLevels = 1,
                ArrayLayers = 1,
                Samples = SampleCountFlags.Count1Bit,
                Tiling = ImageTiling.Linear,
                Usage = ImageUsageFlags.TransferSrcBit,
                InitialLayout = ImageLayout.Preinitialized,
            };
            var zeroInfo = new AllocationCreateInfo();
            AssertThrows(() => context.Allocator.CreateImage(in zeroImg, in zeroInfo, out _, out _));
        }
    }

    [Fact]
    public void TestAllocationVersusResourceSize()
    {
        // Mirror C++ TestAllocationVersusResourceSize (Tests.cpp:3551): for a non-dedicated and a dedicated
        // allocation, the allocation size must equal the resource size (dedicated) or be smaller than the
        // block size (sub-allocated). Uses Allocation.IsDedicated / Allocation.BlockSize.
        using var context = new VulkanContext();

        var bufCreateInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = 22921, // Prime number
            Usage = BufferUsageFlags.TransferSrcBit | BufferUsageFlags.TransferDstBit,
        };

        var allocCreateInfo = new AllocationCreateInfo
        {
            Usage = MemoryUsage.AutoPreferHost,
            Flags = AllocationCreateFlags.HostAccessSequentialWrite,
        };

        for (uint i = 0; i < 2; ++i)
        {
            var isDedicated = i == 1;
            if (isDedicated)
            {
                allocCreateInfo.Flags |= AllocationCreateFlags.DedicatedMemory;
            }
            else
            {
                allocCreateInfo.Flags &= ~AllocationCreateFlags.DedicatedMemory;
            }

            context.Allocator.CreateBuffer(in bufCreateInfo, in allocCreateInfo, out var buffer, out var alloc);
            Assert.NotNull(alloc);
            try
            {
                // Map and write the entire allocation (not only the buffer) with 0xCC.
                alloc.Map(out var mapped);
                try
                {
                    new Span<byte>(mapped, (int)alloc.Size).Fill(0xCC);
                }
                finally
                {
                    alloc.Unmap();
                }

                if (isDedicated)
                {
                    Assert.True(alloc.IsDedicated);
                    Assert.Equal(alloc.BlockSize, alloc.Size);
                }
                else
                {
                    Assert.False(alloc.IsDedicated);
                    Assert.True(alloc.BlockSize > alloc.Size);
                }
            }
            finally
            {
                alloc.Dispose();
                context.Vk.DestroyBuffer(context.Device, buffer, ReadOnlySpan<AllocationCallbacks>.Empty);
            }
        }
    }

    [Fact]
    public void TestPool_MinBlockCount()
    {
        // Mirror C++ TestPool_MinBlockCount (Tests.cpp:3603): block hysteresis as allocations are added/freed.
        using var context = new VulkanContext();

        const long allocSize = 512L * 1024;
        const long blockSize = allocSize * 2; // Each block fits 2 allocations.

        var allocCreateInfo = new AllocationCreateInfo
        {
            Usage = MemoryUsage.AutoPreferHost,
        };

        var bufCreateInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Usage = BufferUsageFlags.TransferDstBit | BufferUsageFlags.TransferSrcBit,
            Size = allocSize,
        };

        context.Allocator.FindMemoryTypeIndexForBufferInfo(in bufCreateInfo, in allocCreateInfo, out var memTypeIndex);
        Assert.True(memTypeIndex.HasValue, "Expected a memory type for the pool.");

        var poolCreateInfo = new AllocationPoolCreateInfo
        {
            MemoryTypeIndex = memTypeIndex.Value,
            BlockSize = blockSize,
            MinBlockCount = 2,
        };

        using var pool = context.Allocator.CreatePool(in poolCreateInfo);

        // 2 blocks preallocated.
        var begStats = pool.GetPoolStats();
        Assert.Equal(2, begStats.BlockCount);
        Assert.Equal(0, begStats.AllocationCount);
        Assert.Equal(blockSize * 2, begStats.Size);

        const int bufCount = 5;
        allocCreateInfo.Pool = pool;
        var allocs = new Allocation[bufCount];
        for (uint i = 0; i < bufCount; ++i)
        {
            context.Allocator.CreateBuffer(in bufCreateInfo, in allocCreateInfo, out var buffer, out var tmp);
            Assert.NotNull(tmp);
            allocs[i] = tmp;
            context.Vk.DestroyBuffer(context.Device, buffer, ReadOnlySpan<AllocationCallbacks>.Empty);
        }

        // 3 blocks now.
        var stats2 = pool.GetPoolStats();
        Assert.Equal(3, stats2.BlockCount);
        Assert.Equal(bufCount, stats2.AllocationCount);
        Assert.Equal(blockSize * 3, stats2.Size);

        // Free first two (one full block empty) -> still 3 due to hysteresis.
        allocs[0].Dispose();
        allocs[1].Dispose();
        var stats3 = pool.GetPoolStats();
        Assert.Equal(3, stats3.BlockCount);
        Assert.Equal(bufCount - 2, stats3.AllocationCount);
        Assert.Equal(blockSize * 3, stats3.Size);

        // Free the last allocation -> second block empty -> 2 blocks.
        allocs[bufCount - 1].Dispose();
        var stats4 = pool.GetPoolStats();
        Assert.Equal(2, stats4.BlockCount);
        Assert.Equal(bufCount - 3, stats4.AllocationCount);
        Assert.Equal(blockSize * 2, stats4.Size);

        for (uint i = 2; i < bufCount - 1; ++i)
        {
            allocs[i].Dispose();
        }
    }

    [Fact]
    public void TestPool_MinAllocationAlignment()
    {
        // Mirror C++ TestPool_MinAllocationAlignment (Tests.cpp:3685): a pool created with a non-zero
        // minAllocationAlignment must align every allocation to it.
        using var context = new VulkanContext();

        const long allocSize = 32;
        const long blockSize = 1024L * 1024;
        const long minAllocationAlignment = 64L * 1024;

        var allocCreateInfo = new AllocationCreateInfo
        {
            Usage = MemoryUsage.AutoPreferHost,
        };

        var bufCreateInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Usage = BufferUsageFlags.TransferDstBit | BufferUsageFlags.TransferSrcBit,
            Size = allocSize,
        };

        context.Allocator.FindMemoryTypeIndexForBufferInfo(in bufCreateInfo, in allocCreateInfo, out var memTypeIndex);
        Assert.True(memTypeIndex.HasValue, "Expected a memory type for the pool.");

        var poolCreateInfo = new AllocationPoolCreateInfo
        {
            MemoryTypeIndex = memTypeIndex.Value,
            BlockSize = blockSize,
            MinAllocationAlignment = minAllocationAlignment,
        };

        using var pool = context.Allocator.CreatePool(in poolCreateInfo);

        // Mirror C++: allocations must target the pool (allocCreateInfo.pool = pool).
        allocCreateInfo.Pool = pool;

        const int bufCount = 4;
        var createdBuffers = new Buffer[bufCount];
        var allocs = new Allocation[bufCount];

        try
        {
            for (var i = 0; i < bufCount; ++i)
            {
                context.Allocator.CreateBuffer(in bufCreateInfo, in allocCreateInfo, out createdBuffers[i], out var alloc);
                Assert.NotNull(alloc);
                Assert.True(createdBuffers[i].Handle != default, $"Buffer {i} should be created.");
                allocs[i] = alloc;
                Assert.Equal(0L, alloc.Offset % minAllocationAlignment);
            }
        }
        finally
        {
            for (var i = bufCount - 1; i >= 0; --i)
            {
                if (allocs[i] != null)
                {
                    allocs[i].Dispose();
                    context.Vk.DestroyBuffer(context.Device, createdBuffers[i], ReadOnlySpan<AllocationCallbacks>.Empty);
                }
            }
        }
    }

    [Fact]
    public void TestPoolsAndAllocationParameters()
    {
        // Mirror C++ TestPoolsAndAllocationParameters (Tests.cpp:3731): poolTypeI 0/1/2 (default / custom
        // flexible / custom fixed) with default, dedicated, never-allocate allocation variants, then stats.
        using var context = new VulkanContext();

        var bufCreateInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = 1 * 1024 * 1024,
            Usage = BufferUsageFlags.TransferDstBit | BufferUsageFlags.TransferSrcBit,
        };

        var allocCreateInfo = new AllocationCreateInfo();

        int memTypeIndex;
        {
            context.Allocator.FindMemoryTypeIndexForBufferInfo(in bufCreateInfo, in allocCreateInfo, out var mt);
            Assert.True(mt.HasValue, "Expected a memory type for the pool.");
            memTypeIndex = mt.Value;
        }

        VulkanMemoryPool? pool1 = null, pool2 = null;
        var bufs = new List<(Buffer Buffer, Allocation Alloc)>();

        uint totalNewAllocCount = 0, totalNewBlockCount = 0;
        var statsBeg = context.Allocator.CalculateStats();

        for (uint poolTypeI = 0; poolTypeI < 3; ++poolTypeI)
        {
            if (poolTypeI == 0)
            {
                allocCreateInfo.Pool = null;
            }
            else if (poolTypeI == 1)
            {
                var poolCreateInfo = new AllocationPoolCreateInfo
                {
                    MemoryTypeIndex = memTypeIndex
                };
                pool1 = context.Allocator.CreatePool(in poolCreateInfo);
                allocCreateInfo.Pool = pool1;
            }
            else if (poolTypeI == 2)
            {
                var poolCreateInfo = new AllocationPoolCreateInfo
                {
                    MemoryTypeIndex = memTypeIndex,
                    MaxBlockCount = 1,
                    BlockSize = 2 * 1024 * 1024 + 1024 * 1024 / 2, // 2.5 MB
                };
                pool2 = context.Allocator.CreatePool(in poolCreateInfo);
                allocCreateInfo.Pool = pool2;
            }

            uint poolAllocCount = 0;
            var allocInfo = new Allocation?[4];

            // Default parameters.
            allocCreateInfo.Flags = 0;
            {
                context.Allocator.CreateBuffer(in bufCreateInfo, in allocCreateInfo, out var buffer, out var alloc0);
                Assert.NotNull(alloc0);
                allocInfo[0] = alloc0;
                bufs.Add((buffer, alloc0));
                ++poolAllocCount;
            }

            // DEDICATED. Should not try pool2 as it asserts on invalid call.
            if (poolTypeI != 2)
            {
                allocCreateInfo.Flags = AllocationCreateFlags.DedicatedMemory;
                context.Allocator.CreateBuffer(in bufCreateInfo, in allocCreateInfo, out var buffer, out var alloc1);
                Assert.NotNull(alloc1);
                try
                {
                    Assert.Equal(0L, alloc1.Offset); // Dedicated
                    Assert.NotEqual(allocInfo[0].NonNull().DeviceMemory.Handle, alloc1.DeviceMemory.Handle); // Dedicated
                }
                finally
                {
                    bufs.Add((buffer, alloc1));
                    ++poolAllocCount;
                }
            }

            // NEVER_ALLOCATE #1 (must reuse an existing block).
            allocCreateInfo.Flags = AllocationCreateFlags.NeverAllocate;
            {
                context.Allocator.CreateBuffer(in bufCreateInfo, in allocCreateInfo, out var buffer, out var alloc2);
                Assert.NotNull(alloc2);
                Assert.Equal(allocInfo[0].NonNull().DeviceMemory.Handle, alloc2.DeviceMemory.Handle); // Reused block
                Assert.NotEqual(allocInfo[0].NonNull().Offset, alloc2.Offset); // Different offset
                bufs.Add((buffer, alloc2));
                ++poolAllocCount;
            }

            // NEVER_ALLOCATE #2. Should fail in pool2 (no space).
            allocCreateInfo.Flags = AllocationCreateFlags.NeverAllocate;
            if (poolTypeI == 2)
            {
                // NEVER_ALLOCATE #2. Should fail in pool2 (no space). CreateBuffer now returns a
                // Result instead of throwing (mirrors C++ TEST(res != VK_SUCCESS)).
                var resNoAlloc = context.Allocator.CreateBuffer(in bufCreateInfo, in allocCreateInfo, out _, out _);
                Assert.NotEqual(Result.Success, resNoAlloc);
            }
            else
            {
                context.Allocator.CreateBuffer(in bufCreateInfo, in allocCreateInfo, out var buffer, out var alloc3);
                Assert.NotNull(alloc3);
                bufs.Add((buffer, alloc3));
                ++poolAllocCount;
            }

            uint poolBlockCount = poolTypeI switch
            {
                0 => 1, // At least 1 added for dedicated allocation.
                1 => 2, // 1 for custom pool block and 1 for dedicated allocation.
                _ => 1, // Only custom pool, no dedicated allocation.
            };

            if (poolTypeI > 0)
            {
                var pool = poolTypeI == 2 ? pool2.NonNull() : pool1.NonNull();
                var poolStats = pool.GetPoolStats();
                Assert.Equal((int)poolAllocCount, poolStats.AllocationCount);
                Assert.Equal((long)poolAllocCount * 1024 * 1024, poolStats.AllocationBytes);
                Assert.Equal((int)poolBlockCount, poolStats.BlockCount);
            }

            totalNewAllocCount += poolAllocCount;
            totalNewBlockCount += poolBlockCount;
        }

        var statsEnd = context.Allocator.CalculateStats();
        Assert.Equal(statsEnd.Total.AllocationCount, statsBeg.Total.AllocationCount + (int)totalNewAllocCount);
        Assert.True(statsEnd.Total.BlockCount >= statsBeg.Total.BlockCount + (int)totalNewBlockCount);
        Assert.Equal(statsEnd.Total.UsedBytes, statsBeg.Total.UsedBytes + (long)totalNewAllocCount * 1024 * 1024);

        foreach (var (buffer, alloc) in bufs)
        {
            alloc.Dispose();
            context.Vk.DestroyBuffer(context.Device, buffer, ReadOnlySpan<AllocationCallbacks>.Empty);
        }

        pool2?.Dispose();
        pool1?.Dispose();
    }

    [Fact]
    public void TestPool_SameSize()
    {
        // Mirror C++ TestPool_SameSize (Tests.cpp:5153): fill the pool, verify OOM, random free/alloc,
        // refetch to capacity, validate stats, then defragment. All exercised entry points are implemented
        // in VmaCS (pool name, CurrentFrameIndex, BuildStatsString, defragmentation). With ThrowOnError
        // off (the default, mirroring C++ VMA which returns VkResult), the pool-full OOM is reported as a
        // non-Success Result; the oversized-allocation OOM asserts Result == ErrorOutOfDeviceMemory.
        using var context = new VulkanContext();
        context.SeedRand(123);

        const long bufSize = 1024L * 1024;
        const int bufCount = 100;

        var bufferInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = bufSize,
            Usage = BufferUsageFlags.VertexBufferBit | BufferUsageFlags.IndexBufferBit | BufferUsageFlags.TransferDstBit,
        };

        var memoryTypeBits = GetBufferMemoryRequirements(context, in bufferInfo).MemoryTypeBits;

        var poolAllocInfo = new AllocationCreateInfo
        {
            RequiredFlags = MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit | MemoryPropertyFlags.HostCachedBit,
        };
        var res = context.Allocator.FindMemoryTypeIndex(memoryTypeBits, in poolAllocInfo, out var memTypeIndex);
        Assert.Equal(Result.Success, res);
        if (memTypeIndex is null)
        {
            return;
        }

        var poolCreateInfo = new AllocationPoolCreateInfo
        {
            MemoryTypeIndex = memTypeIndex.Value,
            BlockSize = bufSize * bufCount / 4,
            MinBlockCount = 1,
            MaxBlockCount = 4,
        };

        using var pool = context.Allocator.CreatePool(in poolCreateInfo);

        // Test pool name.
        {
            const string poolName = "Pool name";
            pool.Name = poolName;

            var fetchedPoolName = pool.Name;
            Assert.Equal(poolName, fetchedPoolName);

            // Generate JSON dump (mirrors C++ vmaBuildStatsString).
            var json = context.Allocator.BuildStatsString(detailedMap: true);
            Assert.False(string.IsNullOrWhiteSpace(json));

            pool.Name = null;
        }

        context.Allocator.CurrentFrameIndex = 1;

        var allocInfo = new AllocationCreateInfo { Pool = pool };

        var items = new List<(Buffer Buffer, Allocation Alloc)>();
        try
        {
            // Fill entire pool.
            for (var i = 0; i < bufCount; i++)
            {
                context.Allocator.CreateBuffer(in bufferInfo, in allocInfo, out var buffer, out var alloc);
                Assert.NotNull(alloc);
                items.Add((buffer, alloc));
            }

            // Make sure that another allocation would fail. With ThrowOnError off (default, mirroring C++),
            // the full-pool allocation is reported as a non-Success Result rather than throwing.
            {
                var oomRes = context.Allocator.CreateBuffer(in bufferInfo, in allocInfo, out _, out _);
                Assert.NotEqual(Result.Success, oomRes);
            }

            // Validate allocations.
            foreach (var (buffer, alloc) in items)
            {
                Assert.NotNull(alloc);
                Assert.NotEqual(0UL, alloc.DeviceMemory.Handle);
                Assert.Equal(0, (nint)alloc.MappedData);
            }

            // Free ~10% at random indices.
            var percentToFree = bufCount * 10 / 100;
            for (var i = 0; i < percentToFree; i++)
            {
                var index = (int)(context.Rand.Generate() % (uint)items.Count);
                var (buffer, alloc) = items[index];
                alloc.Dispose();
                context.Vk.DestroyBuffer(context.Device, buffer, ReadOnlySpan<AllocationCallbacks>.Empty);
                items.RemoveAt(index);
            }

            // Randomly allocate and free up to bufCount.
            for (var i = 0; i < bufCount; i++)
            {
                var allocate = context.Rand.Generate() % 2 != 0;
                if (allocate)
                {
                    if (items.Count < bufCount)
                    {
                        var rndRes = context.Allocator.CreateBuffer(in bufferInfo, in allocInfo, out var buffer, out var alloc);
                        if (rndRes == Result.Success)
                        {
                            Assert.NotNull(alloc);
                            items.Add((buffer, alloc));
                        }
                    }
                }
                else if (items.Count > 0)
                {
                    var index = (int)(context.Rand.Generate() % (uint)items.Count);
                    var (buffer, alloc) = items[index];
                    alloc.Dispose();
                    context.Vk.DestroyBuffer(context.Device, buffer, ReadOnlySpan<AllocationCallbacks>.Empty);
                    items.RemoveAt(index);
                }
            }

            // Allocate up to maximum.
            while (items.Count < bufCount)
            {
                context.Allocator.CreateBuffer(in bufferInfo, in allocInfo, out var buffer, out var alloc);
                Assert.NotNull(alloc);
                items.Add((buffer, alloc));
            }

            // Free one item.
            {
                var (buffer, alloc) = items[^1];
                alloc.Dispose();
                context.Vk.DestroyBuffer(context.Device, buffer, ReadOnlySpan<AllocationCallbacks>.Empty);
                items.RemoveAt(items.Count - 1);
            }

            // Validate statistics.
            var poolStats = pool.GetPoolStats();
            Assert.Equal(items.Count, poolStats.AllocationCount);
            Assert.Equal(bufCount * bufSize, poolStats.Size);
            Assert.True(poolStats.UnusedRangeCount == 1, $"Expected exactly one free range in the pool. Actual={poolStats.UnusedRangeCount}, Size={poolStats.Size}, UnusedSize={poolStats.UnusedSize}, AllocCount={poolStats.AllocationCount}");
            Assert.Equal(bufSize, poolStats.Size - poolStats.AllocationBytes);

            // Free all remaining items.
            foreach (var (buffer, alloc) in items)
            {
                alloc.Dispose();
                context.Vk.DestroyBuffer(context.Device, buffer, ReadOnlySpan<AllocationCallbacks>.Empty);
            }

            items.Clear();

            // Allocate maximum items again.
            for (var i = 0; i < bufCount; i++)
            {
                context.Allocator.CreateBuffer(in bufferInfo, in allocInfo, out var buffer, out var alloc);
                Assert.NotNull(alloc);
                items.Add((buffer, alloc));
            }

            // Delete every other item.
            for (var i = 0; i < bufCount / 2; i++)
            {
                var (buffer, alloc) = items[i];
                alloc.Dispose();
                context.Vk.DestroyBuffer(context.Device, buffer, ReadOnlySpan<AllocationCallbacks>.Empty);
                items.RemoveAt(i);
            }

            // Defragment.NonNull()
            {
                var defragInfo = new DefragmentationInfo
                {
                    Flags = DefragmentationFlags.AlgorithmFull,
                    Pool = pool,
                };

                var defragCtx = context.Allocator.DefragmentationBegin(in defragInfo);

                while (true)
                {
                    var pass = defragCtx.PassBegin();
                    if (pass.Moves == null || pass.Moves.Length == 0)
                    {
                        break;
                    }

                    if (!defragCtx.PassEnd())
                    {
                        break;
                    }
                }

                var defragStats = defragCtx.End();
                Assert.True(defragStats.AllocationsMoved == 24, "Defragmentation should move exactly 24 allocations, matching C++.");

            }

            // Free all remaining items (mirrors C++ Tests.cpp:5361).
            foreach (var (buffer, alloc) in items)
            {
                alloc.Dispose();
                context.Vk.DestroyBuffer(context.Device, buffer, ReadOnlySpan<AllocationCallbacks>.Empty);
            }
            items.Clear();

            // Test for allocation too large for pool (mirrors C++ Tests.cpp:5366).
            {
                var oversizedReq = new MemoryRequirements
                {
                    MemoryTypeBits = uint.MaxValue,
                    Alignment = 1,
                    Size = (ulong)(poolCreateInfo.BlockSize + 4),
                };
                var oversizedInfo = new AllocationCreateInfo { Pool = pool };
                var oomRes2 = context.Allocator.AllocateMemory(in oversizedReq, in oversizedInfo, out _);
                Assert.Equal(Result.ErrorOutOfDeviceMemory, oomRes2);
            }
        }
        finally
        {
            foreach (var (buffer, alloc) in items)
            {
                alloc.Dispose();
                context.Vk.DestroyBuffer(context.Device, buffer, ReadOnlySpan<AllocationCallbacks>.Empty);
            }
        }
    }

    [Fact]
    public void TestAllocationsInitialization()
    {
        // Mirror C++ TestAllocationsInitialization (Tests.cpp:5399): with DebugInitializeAllocations, fresh
        // allocations are filled with 0xDC and freed memory with 0xEF.
        using var context = new VulkanContext();
        var allocator = context.Allocator;
        var prev = allocator.DebugInitializeAllocations;
        allocator.DebugInitializeAllocations = true;
        try
        {
            const int bufSize = 1024;
            const byte Created = 0xDC;
            const byte Destroyed = 0xEF;

            var bufInfo = new BufferCreateInfo
            {
                SType = StructureType.BufferCreateInfo,
                Size = bufSize,
                Usage = BufferUsageFlags.TransferSrcBit,
            };

            var dummyInfo = new AllocationCreateInfo
            {
                Usage = MemoryUsage.Auto,
                Flags = AllocationCreateFlags.HostAccessRandom,
            };
            context.Allocator.FindMemoryTypeIndexForBufferInfo(in bufInfo, in dummyInfo, out var memType);
            Assert.True(memType.HasValue, "Expected a host-visible memory type for the pool.");
            var poolInfo = new AllocationPoolCreateInfo
            {
                MemoryTypeIndex = memType.Value,
                BlockSize = bufSize * 10,
                MinBlockCount = 1,
                MaxBlockCount = 1,
            };
            var pool = context.Allocator.CreatePool(in poolInfo);

            // Create one persistently mapped buffer to keep the pool block's memory mapped.
            context.Allocator.CreateBuffer(in bufInfo,
                new AllocationCreateInfo { Usage = MemoryUsage.Unknown, Flags = AllocationCreateFlags.Mapped, Pool = pool },
                out var firstBuffer, out var firstAlloc);
            Assert.NotNull(firstAlloc);
            try
            {
                for (var i = 0; i < 2; i++)
                {
                    var persistentlyMapped = i == 0;

                    var info = new AllocationCreateInfo
                    {
                        Usage = MemoryUsage.Unknown,
                        Flags = persistentlyMapped ? AllocationCreateFlags.Mapped : 0,
                        Pool = pool,
                    };

                    context.Allocator.CreateBuffer(in bufInfo, in info, out var buffer, out var alloc);
                    Assert.NotNull(alloc);
                    void* pMappedData;
                    if (!persistentlyMapped)
                    {
                        alloc.Map(out pMappedData);
                    }
                    else
                    {
                        pMappedData = alloc.MappedData;
                    }

                    // Validate initialized (0xDC) content.
                    unsafe
                    {
                        var span = new Span<byte>(pMappedData, bufSize);
                        for (var b = 0; b < bufSize; b++)
                        {
                            Assert.Equal(Created, span[b]);
                        }
                    }

                    if (!persistentlyMapped)
                    {
                        alloc.Unmap();
                    }

                    alloc.Dispose();
                    context.Vk.DestroyBuffer(context.Device, buffer, ReadOnlySpan<AllocationCallbacks>.Empty);

                    // Validate freed (0xEF) content.
                    unsafe
                    {
                        var span = new Span<byte>(pMappedData, bufSize);
                        for (var b = 0; b < bufSize; b++)
                        {
                            Assert.Equal(Destroyed, span[b]);
                        }
                    }
                }
            }
            finally
            {
                firstAlloc.Dispose();
                context.Vk.DestroyBuffer(context.Device, firstBuffer, ReadOnlySpan<AllocationCallbacks>.Empty);
                pool.Dispose();
            }
        }
        finally
        {
            allocator.DebugInitializeAllocations = prev;
        }
    }

    [Fact]
    public void TestAllocationWithAlignment()
    {
        // Mirror C++ TestAllocationWithAlignment (Tests.cpp:6143): 4 scenarios for minAlignment.
        using var context = new VulkanContext();

        const long bufferSize = 4L * 1024;
        const long minAlignment = 64L * 1024;

        var bufCreateInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = bufferSize,
            Usage = BufferUsageFlags.TransferDstBit | BufferUsageFlags.TransferSrcBit,
        };

        // 1. vmaAllocateMemory with AllocationCreateInfo::minAlignment.
        {
            var memReq = new MemoryRequirements[2];
            var buffers = new Buffer[2];
            for (uint i = 0; i < 2; ++i)
            {
                context.Vk.CreateBuffer(context.Device, in bufCreateInfo, null, out buffers[i]);
                context.Vk.GetBufferMemoryRequirements(context.Device, buffers[i], out memReq[i]);
            }

            var allocCreateInfo = new AllocationCreateInfo { MinAlignment = minAlignment };

            var allocations = new Allocation?[2];
            for (uint i = 0; i < 2; ++i)
            {
                context.Allocator.AllocateMemory(in memReq[i], in allocCreateInfo, out var alloc);
                Assert.NotNull(alloc);
                allocations[i] = alloc;
                Assert.Equal(0L, alloc.Offset % minAlignment);
            }

            for (uint i = 0; i < 2; ++i)
            {
                allocations[i].NonNull().BindBufferMemory(buffers[i]);
            }

            for (uint i = 0; i < 2; ++i)
            {
                context.Vk.DestroyBuffer(context.Device, buffers[i], ReadOnlySpan<AllocationCallbacks>.Empty);
                allocations[i].NonNull().Dispose();
            }
        }

        // 2. vmaCreateBuffer with AllocationCreateInfo::minAlignment.
        {
            var buffers = new Buffer[2];
            var allocations = new Allocation?[2];

            var allocCreateInfo = new AllocationCreateInfo { MinAlignment = minAlignment };

            for (uint i = 0; i < 2; ++i)
            {
                context.Allocator.CreateBuffer(in bufCreateInfo, in allocCreateInfo, out buffers[i], out var alloc);
                Assert.NotNull(alloc);
                allocations[i] = alloc;
                Assert.Equal(0L, alloc.Offset % minAlignment);
            }

            for (uint i = 0; i < 2; ++i)
            {
                allocations[i].NonNull().Dispose();
                context.Vk.DestroyBuffer(context.Device, buffers[i], ReadOnlySpan<AllocationCallbacks>.Empty);
            }
        }

        // 3. vmaCreateBuffer in a custom pool with VmaPoolCreateInfo::minAllocationAlignment.
        {
            var sampleAllocCreateInfo = new AllocationCreateInfo
            {
                Usage = MemoryUsage.AutoPreferHost,
            };
            context.Allocator.FindMemoryTypeIndexForBufferInfo(in bufCreateInfo, in sampleAllocCreateInfo, out var memTypeIndex);
            Assert.True(memTypeIndex.HasValue, "Expected a memory type for the pool.");

            var poolCreateInfo = new AllocationPoolCreateInfo
            {
                MemoryTypeIndex = memTypeIndex.Value,
                BlockSize = 4 * minAlignment,
                MinBlockCount = 1,
                MaxBlockCount = 1,
                MinAllocationAlignment = minAlignment,
            };

            using var pool = context.Allocator.CreatePool(in poolCreateInfo);

            var allocCreateInfo = new AllocationCreateInfo
            {
                Pool = pool,
            };

            var buffers = new Buffer[2];
            var allocations = new Allocation?[2];

            try
            {
                for (uint i = 0; i < 2; ++i)
                {
                    context.Allocator.CreateBuffer(in bufCreateInfo, in allocCreateInfo, out buffers[i], out var alloc);
                    Assert.NotNull(alloc);
                    allocations[i] = alloc;
                    Assert.True(buffers[i].Handle != default, $"Buffer {i} should be created.");
                    Assert.True(alloc != null, $"Allocation {i} should be created.");
                    Assert.Equal(0L, alloc.Offset % minAlignment);
                }
            }
            finally
            {
                for (uint i = 0; i < 2; ++i)
                {
                    if (allocations[i] != null)
                    {
                        allocations[i].NonNull().Dispose();
                        context.Vk.DestroyBuffer(context.Device, buffers[i], ReadOnlySpan<AllocationCallbacks>.Empty);
                    }
                }
            }
        }

        // 4. vmaCreateBufferWithAlignment (implemented in VmaCS as CreateBufferWithAlignment, mirrors C++).
        {
            var allocCreateInfo = new AllocationCreateInfo();
            var buffers = new Buffer[2];
            var allocations = new Allocation?[2];
            for (uint i = 0; i < 2; ++i)
            {
                context.Allocator.CreateBufferWithAlignment(in bufCreateInfo, in allocCreateInfo, minAlignment, out buffers[i], out var alloc);
                Assert.NotNull(alloc);
                allocations[i] = alloc;
                Assert.Equal(0L, alloc.Offset % minAlignment);
            }

            for (uint i = 0; i < 2; ++i)
            {
                allocations[i].NonNull().Dispose();
                context.Vk.DestroyBuffer(context.Device, buffers[i], ReadOnlySpan<AllocationCallbacks>.Empty);
            }
        }
    }

    [Fact]
    public void TestHeapSizeLimit()
    {
        // Mirror C++ TestHeapSizeLimit (Tests.cpp:3859): build a separate VmaAllocator with
        // pHeapSizeLimit = 100 MB on every heap. Allocate two dedicated 5 MB buffers, then fill a
        // 10 MB-block pool from the same memory type until the heap budget is exhausted; the final
        // (even tiny) allocation must fail with VK_ERROR_OUT_OF_DEVICE_MEMORY.
        using var context = new VulkanContext();

        const long heapSizeLimit = 100L * 1024 * 1024; // 100 MB
        const long blockSize = 10L * 1024 * 1024;       // 10 MB

        var heapSizeLimits = new long[Vk.MaxMemoryHeaps];
        for (var i = 0; i < heapSizeLimits.Length; i++)
        {
            heapSizeLimits[i] = heapSizeLimit;
        }

        using var allocator = new VulkanMemoryAllocator(new VulkanMemoryAllocatorCreateInfo
        {
            VulkanAPIVersion = Vk.Version11,
            VulkanAPIObject = context.Vk,
            Instance = context.Instance,
            PhysicalDevice = context.PhysicalDevice,
            LogicalDevice = context.Device,
            HeapSizeLimits = heapSizeLimits,
        });

        var items = new List<(Buffer Buffer, Allocation Alloc)>();

        var bufCreateInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Usage = BufferUsageFlags.VertexBufferBit,
        };

        // 1. Two dedicated buffers, each half a block.
        var dedicatedMemoryType = 0;
        {
            var allocCreateInfo = new AllocationCreateInfo
            {
                Usage = MemoryUsage.AutoPreferDevice,
                Flags = AllocationCreateFlags.DedicatedMemory,
            };
            bufCreateInfo.Size = blockSize / 2;

            for (var i = 0; i < 2; i++)
            {
                allocator.CreateBuffer(in bufCreateInfo, in allocCreateInfo, out var buffer, out var alloc);
                Assert.NotNull(alloc);
                dedicatedMemoryType = alloc.MemoryTypeIndex;
                items.Add((buffer, alloc));
            }
        }

        // Pool that must draw memory from the dedicated allocation's type.
        using var pool = allocator.CreatePool(new AllocationPoolCreateInfo
        {
            MemoryTypeIndex = dedicatedMemoryType,
            BlockSize = blockSize,
        });

        // 2. Fill the remaining budget through the pool.
        {
            var allocCreateInfo = new AllocationCreateInfo { Pool = pool };
            bufCreateInfo.Size = blockSize / 2;

            var bufCount = (int)(((heapSizeLimit / blockSize) - 1) * 2);
            for (var i = 0; i < bufCount; i++)
            {
                allocator.CreateBuffer(in bufCreateInfo, in allocCreateInfo, out var buffer, out var alloc);
                Assert.NotNull(alloc);
                items.Add((buffer, alloc));
            }
        }

        // 3. One more allocation (even a tiny one) must fail with out-of-device-memory.
        // With ThrowOnError off (default, mirroring C++ vmaCreateBuffer), the failure is reported
        // as a non-Success Result rather than throwing.
        {
            var allocCreateInfo = new AllocationCreateInfo { Pool = pool };
            bufCreateInfo.Size = 128;

            var oomRes = allocator.CreateBuffer(in bufCreateInfo, in allocCreateInfo, out _, out _);
            Assert.Equal(Result.ErrorOutOfDeviceMemory, oomRes);
        }

        // Cleanup.
        foreach (var (buffer, alloc) in items)
        {
            alloc.Dispose();
            context.Vk.DestroyBuffer(context.Device, buffer, ReadOnlySpan<AllocationCallbacks>.Empty);
        }
    }
}

