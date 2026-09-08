using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace VmaCS.Tests.Ported;

/// <summary>Parity with C++ Tests.cpp: virtual blocks, linear allocators, allocation algorithms (TLSF/AllocatePages).</summary>
public sealed partial class VmaTestsPortedSet
{
    private static long AlignUp(long value, long align) => (value + align - 1) / align * align;

    private static bool TryCreateBuffer(VulkanContext ctx, in BufferCreateInfo bci, in AllocationCreateInfo aci, out Buffer buffer, out Allocation? alloc)
    {
        var res = ctx.Allocator.CreateBuffer(in bci, in aci, out buffer, out alloc);
        if (res == Result.Success)
        {
            return true;
        }

        buffer = default;
        alloc = null;
        return false;
    }

    [Fact]
    public void TestVirtualBlocks()
    {
        using var context = new VulkanContext();

        // Tests.cpp:3234 TestVirtualBlocks

        const long blockSize = 16 * 1024 * 1024;
        const long alignment = 256;

        Assert.Equal(Result.Success, context.Allocator.CreateVirtualBlock(new VirtualBlockCreateInfo { Size = blockSize }, out var block));
        var b = block.NonNull();

        // Allocate 8 MB with UserData = 1.
        var allocCreateInfo = new VirtualAllocationCreateInfo { Alignment = alignment, UserData = 1L, Size = 8 * 1024 * 1024 };
        Assert.Equal(Result.Success, b.Allocate(in allocCreateInfo, out var allocation0, out var offset));
        Assert.NotNull(allocation0);

        var allocInfo0 = allocation0.NonNull();
        Assert.True(allocInfo0.Offset < blockSize);
        Assert.Equal(allocInfo0.Offset, offset);
        Assert.Equal(allocCreateInfo.Size, allocInfo0.Size);
        Assert.Equal(1L, Convert.ToInt64(allocInfo0.UserData));

        // SetUserData to 2.
        allocation0.NonNull().UserData = 2L;
        allocInfo0 = allocation0.NonNull();
        Assert.Equal(2L, Convert.ToInt64(allocInfo0.UserData));

        // Allocate 4 MB.
        allocCreateInfo.Size = 4 * 1024 * 1024;
        Assert.Equal(Result.Success, b.Allocate(in allocCreateInfo, out var allocation1));
        Assert.NotNull(allocation1);
        var allocInfo1 = allocation1.NonNull();
        Assert.True(allocInfo1.Offset < blockSize);
        Assert.True(allocInfo1.Offset + 4 * 1024 * 1024 <= allocInfo0.Offset || allocInfo0.Offset + 8 * 1024 * 1024 <= allocInfo1.Offset);

        // Allocate another 8 MB - should fail, offset == max.
        allocCreateInfo.Size = 8 * 1024 * 1024;
        Assert.Equal(Result.ErrorOutOfDeviceMemory, b.Allocate(in allocCreateInfo, out var allocation2, out var offset2));
        Assert.Null(allocation2);
        Assert.Equal(-1, offset2);

        // Free the 4 MB block; allocating 8 MB should now succeed.
        allocation1.NonNull().Dispose();
        allocCreateInfo.UserData = 1L;
        Assert.Equal(Result.Success, b.Allocate(in allocCreateInfo, out var allocation2b));
        Assert.NotNull(allocation2b);
        var allocInfo2 = allocation2b.NonNull();
        Assert.True(allocInfo2.Offset < blockSize);
        Assert.True(allocInfo2.Offset + 4 * 1024 * 1024 <= allocInfo0.Offset || allocInfo0.Offset + 8 * 1024 * 1024 <= allocInfo2.Offset);

        // Statistics.
        var statInfo = b.GetStatistics();
        Assert.Equal(2, statInfo.AllocationCount);
        Assert.Equal(1, statInfo.BlockCount);
        Assert.Equal(blockSize, statInfo.UsedBytes);
        Assert.Equal(blockSize, b.Size);

        // JSON dump contains CustomData matching the live user data (1 and 2); mirrors
        // Tests.cpp:3320-3321 (VmaCS renders UserData via ToString, so values are "1"/"2").
        var json = b.BuildStatsString(detailedMap: true);
        Assert.Contains("\"CustomData\": \"1\"", json.NonNull());
        Assert.Contains("\"CustomData\": \"2\"", json.NonNull());

        // Free alloc0, leave alloc2.
        allocation0.NonNull().Dispose();

        // Alignment test.
        const int allocCount = 10;
        var allocations = new VirtualAllocation?[allocCount];
        for (var i = 0; i < allocCount; ++i)
        {
            var alignment0 = i == allocCount - 1;
            allocCreateInfo.Size = i * 3 + 15;
            allocCreateInfo.Alignment = alignment0 ? 0 : 8;
            Assert.Equal(Result.Success, b.Allocate(in allocCreateInfo, out allocations[i]));
            Assert.NotNull(allocations[i]);
            if (!alignment0)
            {
                var info = allocations[i].NonNull();
                Assert.Equal(0, info.Offset % allocCreateInfo.Alignment);
            }
        }

        for (var i = allocCount - 1; i >= 0; --i)
        {
            allocations[i].NonNull().Dispose();
        }

        // Another block, using Clear this time (reuses the original 16 MB blockCreateInfo).
        Assert.Equal(Result.Success, context.Allocator.CreateVirtualBlock(new VirtualBlockCreateInfo { Size = 16 * 1024 * 1024 }, out var block2));
        var b2 = block2.NonNull();
        allocCreateInfo = new VirtualAllocationCreateInfo { Size = 1024 * 1024 };
        for (var i = 0; i < 8; ++i)
        {
            Assert.Equal(Result.Success, b2.Allocate(in allocCreateInfo, out var a));
            Assert.NotNull(a);
        }

        b2.Clear();
        b2.Dispose();
        allocation2b.NonNull().Dispose();
        b.Dispose();
    }

    [Fact]
    public void TestVirtualBlocksAlgorithms()
    {
        using var context = new VulkanContext();

        // Tests.cpp:3382 TestVirtualBlocksAlgorithms
        var rand = new VulkanContext.PortedRandom(3454335u);
        long CalcRandomAllocSize() => (long)(rand.Generate() % 20) + 5;

        for (var algorithmIndex = 0; algorithmIndex < 2; ++algorithmIndex)
        {
            Assert.Equal(Result.Success, context.Allocator.CreateVirtualBlock(new VirtualBlockCreateInfo
            {
                Size = 10000,
                Flags = algorithmIndex == 1 ? VirtualBlockCreateFlags.LinearAlgorithm : 0,
            }, out var block));
            var b = block.NonNull();

            // Test too large allocation.
            {
                var tooLarge = new VirtualAllocationCreateInfo { Size = 20000 };
                Assert.Equal(Result.ErrorOutOfDeviceMemory, b.Allocate(in tooLarge, out var tl));
                Assert.Null(tl);
            }

            var allocations = new List<(VirtualAllocation Alloc, long Offset, long RequestedSize, long AllocationSize)>();

            // Make some allocations.
            for (var i = 0; i < 20; ++i)
            {
                var flags = (VirtualAllocationCreateFlags)0;
                if (i >= 10 && i < 12)
                {
                    flags = VirtualAllocationCreateFlags.StrategyMinMemory;
                }
                else if (i >= 12 && i < 14)
                {
                    flags = VirtualAllocationCreateFlags.StrategyMinTime;
                }
                else if (i >= 14 && i < 16)
                {
                    flags = VirtualAllocationCreateFlags.StrategyMinMemory;
                }
                else if (i >= 16 && i < 18 && algorithmIndex == 1)
                {
                    flags = VirtualAllocationCreateFlags.UpperAddress;
                }

                var info = new VirtualAllocationCreateInfo { Size = CalcRandomAllocSize(), Flags = flags, UserData = (long)0 };
                info.UserData = info.Size * 10;

                Assert.Equal(Result.Success, b.Allocate(in info, out var a));
                Assert.NotNull(a);
                var ai = a.NonNull();
                Assert.True(ai.Size >= info.Size);
                allocations.Add((a.NonNull(), ai.Offset, info.Size, ai.Size));
            }

            // Free some of the allocations.
            for (var i = 0; i < 5; ++i)
            {
                var index = (int)(rand.Generate() % (uint)allocations.Count);
                allocations[index].Alloc.Dispose();
                allocations.RemoveAt(index);
            }

            // Allocate some more.
            for (var i = 0; i < 6; ++i)
            {
                var info = new VirtualAllocationCreateInfo { Size = CalcRandomAllocSize(), UserData = (long)0 };
                info.UserData = info.Size * 10;

                Assert.Equal(Result.Success, b.Allocate(in info, out var a));
                Assert.NotNull(a);
                var ai = a.NonNull();
                Assert.True(ai.Size >= info.Size);
                allocations.Add((a.NonNull(), ai.Offset, info.Size, ai.Size));
            }

            // Allocate some with extra alignment.
            for (var i = 0; i < 3; ++i)
            {
                var info = new VirtualAllocationCreateInfo { Size = CalcRandomAllocSize(), Alignment = 16, UserData = (long)0 };
                info.UserData = info.Size * 10;

                Assert.Equal(Result.Success, b.Allocate(in info, out var a));
                Assert.NotNull(a);
                var ai = a.NonNull();
                Assert.Equal(0, ai.Offset % 16);
                Assert.True(ai.Size >= info.Size);
                allocations.Add((a.NonNull(), ai.Offset, info.Size, ai.Size));
            }

            // Check if the allocations don't overlap.
            allocations.Sort((x, y) => x.Offset.CompareTo(y.Offset));
            for (var i = 0; i < allocations.Count - 1; ++i)
            {
                Assert.True(allocations[i + 1].Offset >= allocations[i].Offset + allocations[i].AllocationSize);
            }

            // Check pUserData.
            {
                var last = allocations[^1];
                var ai = last.Alloc;
                Assert.Equal(last.RequestedSize * 10, Convert.ToInt64(ai.UserData));

                last.Alloc.UserData = 666L;
                ai = last.Alloc;
                Assert.Equal(666L, Convert.ToInt64(ai.UserData));
            }

            // Calculate statistics.
            long min = long.MaxValue, max = 0, sum = 0;
            foreach (var a in allocations)
            {
                min = Math.Min(min, a.AllocationSize);
                max = Math.Max(max, a.AllocationSize);
                sum += a.AllocationSize;
            }

            var stats = b.GetStatistics();
            Assert.Equal(allocations.Count, stats.AllocationCount);
            Assert.Equal(1, stats.BlockCount);
            Assert.Equal(10000L, b.Size);
            Assert.Equal(max, stats.AllocationSizeMax);
            Assert.Equal(min, stats.AllocationSizeMin);
            Assert.True(stats.UsedBytes >= sum);

            // Final cleanup.
            b.Clear();
            b.Dispose();
        }
    }

    [Fact]
    public void TestLinearAllocator()
    {
        using var context = new VulkanContext();

        // Tests.cpp:4097 TestLinearAllocator
        var rand = new VulkanContext.PortedRandom(645332u);

        var sampleBufCreateInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = 1024,
            Usage = BufferUsageFlags.TransferDstBit | BufferUsageFlags.VertexBufferBit,
        };
        var sampleAllocCreateInfo = new AllocationCreateInfo { Usage = MemoryUsage.AutoPreferDevice };
        context.Allocator.FindMemoryTypeIndexForBufferInfo(in sampleBufCreateInfo, in sampleAllocCreateInfo, out var memTypeIndex);
        Assert.True(memTypeIndex.HasValue);

        var pool = context.Allocator.CreatePool(new AllocationPoolCreateInfo
        {
            MemoryTypeIndex = memTypeIndex.NonNull(),
            BlockSize = 1024 * 300,
            Flags = PoolCreateFlags.LinearAlgorithm,
            MinBlockCount = 1,
            MaxBlockCount = 1,
        });

        var bufInfo = new List<(Buffer Buffer, Allocation Allocation)>();

        try
        {
            var bufCreateInfo = sampleBufCreateInfo;
            var allocCreateInfo = new AllocationCreateInfo { Pool = pool };

            const int maxBufCount = 100;
            const long bufSizeMin = 64;
            const long bufSizeMax = 1024;

            // Test one-time free.
            for (var outer = 0; outer < 2; ++outer)
            {
                long bufSumSize = 0;
                long prevOffset = 0;
                for (var i = 0; i < maxBufCount; ++i)
                {
                    bufCreateInfo.Size = (ulong)AlignUp(bufSizeMin + (long)(rand.Generate() % (ulong)(bufSizeMax - bufSizeMin)), 64);
                    Assert.True(TryCreateBuffer(context, in bufCreateInfo, in allocCreateInfo, out var buffer, out var alloc));
                    Assert.NotNull(alloc);
                    Assert.True(i == 0 || alloc.NonNull().Offset > prevOffset);
                    bufInfo.Add((buffer, alloc.NonNull()));
                    prevOffset = alloc.NonNull().Offset;
                    Assert.True(alloc.Size >= (long)bufCreateInfo.Size);
                    bufSumSize += alloc.Size;
                }

                var poolStats = pool.GetPoolStats();
                Assert.Equal(1024 * 300, poolStats.Size);
                Assert.Equal(1024 * 300 - bufSumSize, poolStats.UnusedSize);
                Assert.Equal(bufInfo.Count, poolStats.AllocationCount);

                while (bufInfo.Count > 0)
                {
                    var indexToDestroy = (int)(rand.Generate() % (uint)bufInfo.Count);
                    var curr = bufInfo[indexToDestroy];
                    context.Vk.DestroyBuffer(context.Device, curr.Buffer, ReadOnlySpan<AllocationCallbacks>.Empty); curr.Allocation.Dispose();
                    bufInfo.RemoveAt(indexToDestroy);
                }
            }

            // Test stack.
            {
                long prevOffset = 0;
                for (var i = 0; i < maxBufCount; ++i)
                {
                    bufCreateInfo.Size = (ulong)AlignUp(bufSizeMin + (long)(rand.Generate() % (ulong)(bufSizeMax - bufSizeMin)), 64);
                    Assert.True(TryCreateBuffer(context, in bufCreateInfo, in allocCreateInfo, out var buffer, out var alloc));
                    Assert.NotNull(alloc);
                    Assert.True(i == 0 || alloc.NonNull().Offset > prevOffset);
                    bufInfo.Add((buffer, alloc.NonNull()));
                    prevOffset = alloc.NonNull().Offset;
                }

                for (var i = 0; i < maxBufCount / 5; ++i)
                {
                    var curr = bufInfo[^1];
                    context.Vk.DestroyBuffer(context.Device, curr.Buffer, ReadOnlySpan<AllocationCallbacks>.Empty); curr.Allocation.Dispose();
                    bufInfo.RemoveAt(bufInfo.Count - 1);
                }

                for (var i = 0; i < maxBufCount / 5; ++i)
                {
                    bufCreateInfo.Size = (ulong)AlignUp(bufSizeMin + (long)(rand.Generate() % (ulong)(bufSizeMax - bufSizeMin)), 64);
                    Assert.True(TryCreateBuffer(context, in bufCreateInfo, in allocCreateInfo, out var buffer, out var alloc));
                    Assert.NotNull(alloc);
                    Assert.True(i == 0 || alloc.NonNull().Offset > prevOffset);
                    bufInfo.Add((buffer, alloc.NonNull()));
                    prevOffset = alloc.NonNull().Offset;
                }

                while (bufInfo.Count > 0)
                {
                    var curr = bufInfo[^1];
                    context.Vk.DestroyBuffer(context.Device, curr.Buffer, ReadOnlySpan<AllocationCallbacks>.Empty); curr.Allocation.Dispose();
                    bufInfo.RemoveAt(bufInfo.Count - 1);
                }
            }

            // Test ring buffer.
            {
                bufCreateInfo.Size = bufSizeMax;
                long prevOffset = 0;
                for (var i = 0; i < maxBufCount; ++i)
                {
                    Assert.True(TryCreateBuffer(context, in bufCreateInfo, in allocCreateInfo, out var buffer, out var alloc));
                    Assert.NotNull(alloc);
                    Assert.True(i == 0 || alloc.NonNull().Offset > prevOffset);
                    bufInfo.Add((buffer, alloc.NonNull()));
                    prevOffset = alloc.NonNull().Offset;
                }

                var buffersPerIter = maxBufCount / 10 - 1;
                var iterCount = (int)(1024L * 300 / bufSizeMax / buffersPerIter * 2);
                for (var iter = 0; iter < iterCount; ++iter)
                {
                    for (var k = 0; k < buffersPerIter; ++k)
                    {
                        var curr = bufInfo[0];
                        context.Vk.DestroyBuffer(context.Device, curr.Buffer, ReadOnlySpan<AllocationCallbacks>.Empty); curr.Allocation.Dispose();
                        bufInfo.RemoveAt(0);
                    }

                    for (var k = 0; k < buffersPerIter; ++k)
                    {
                        Assert.True(TryCreateBuffer(context, in bufCreateInfo, in allocCreateInfo, out var buffer, out var alloc) && alloc != null);
                        bufInfo.Add((buffer, alloc.NonNull()));
                    }
                }

                // Allocate until out of memory.
                while (TryCreateBuffer(context, in bufCreateInfo, in allocCreateInfo, out var buffer, out var alloc) && alloc != null)
                {
                    Assert.NotNull(alloc);
                    bufInfo.Add((buffer, alloc.NonNull()));
                }

                while (bufInfo.Count > 0)
                {
                    var indexToDestroy = (int)(rand.Generate() % (uint)bufInfo.Count);
                    var curr = bufInfo[indexToDestroy];
                    context.Vk.DestroyBuffer(context.Device, curr.Buffer, ReadOnlySpan<AllocationCallbacks>.Empty); curr.Allocation.Dispose();
                    bufInfo.RemoveAt(indexToDestroy);
                }
            }

            // Test double stack.
            {
                long prevOffsetLower = 0;
                long prevOffsetUpper = 1024 * 300;
                for (var i = 0; i < maxBufCount; ++i)
                {
                    var upperAddress = (i % 2) != 0;
                    if (upperAddress)
                    {
                        allocCreateInfo.Flags |= AllocationCreateFlags.UpperAddress;
                    }
                    else
                    {
                        allocCreateInfo.Flags &= ~AllocationCreateFlags.UpperAddress;
                    }

                    bufCreateInfo.Size = (ulong)AlignUp(bufSizeMin + (long)(rand.Generate() % (ulong)(bufSizeMax - bufSizeMin)), 64);
                    Assert.True(TryCreateBuffer(context, in bufCreateInfo, in allocCreateInfo, out var buffer, out var alloc));
                    Assert.NotNull(alloc);
                    if (upperAddress)
                    {
                        Assert.True(alloc.NonNull().Offset < prevOffsetUpper);
                        prevOffsetUpper = alloc.Offset;
                    }
                    else
                    {
                        Assert.True(alloc.NonNull().Offset >= prevOffsetLower);
                        prevOffsetLower = alloc.Offset;
                    }

                    Assert.True(prevOffsetLower < prevOffsetUpper);
                    bufInfo.Add((buffer, alloc.NonNull()));
                }

                for (var i = 0; i < maxBufCount / 5; ++i)
                {
                    var curr = bufInfo[^1];
                    context.Vk.DestroyBuffer(context.Device, curr.Buffer, ReadOnlySpan<AllocationCallbacks>.Empty); curr.Allocation.Dispose();
                    bufInfo.RemoveAt(bufInfo.Count - 1);
                }

                for (var i = 0; i < maxBufCount / 5; ++i)
                {
                    var upperAddress = (i % 2) != 0;
                    if (upperAddress)
                    {
                        allocCreateInfo.Flags |= AllocationCreateFlags.UpperAddress;
                    }
                    else
                    {
                        allocCreateInfo.Flags &= ~AllocationCreateFlags.UpperAddress;
                    }

                    bufCreateInfo.Size = (ulong)AlignUp(bufSizeMin + (long)(rand.Generate() % (ulong)(bufSizeMax - bufSizeMin)), 64);
                    Assert.True(TryCreateBuffer(context, in bufCreateInfo, in allocCreateInfo, out var buffer, out var alloc));
                    Assert.NotNull(alloc);
                    bufInfo.Add((buffer, alloc.NonNull()));
                }

                while (bufInfo.Count > 0)
                {
                    var curr = bufInfo[^1];
                    context.Vk.DestroyBuffer(context.Device, curr.Buffer, ReadOnlySpan<AllocationCallbacks>.Empty); curr.Allocation.Dispose();
                    bufInfo.RemoveAt(bufInfo.Count - 1);
                }

                // Create buffers on both sides until out of memory.
                prevOffsetLower = 0;
                prevOffsetUpper = 1024 * 300;
                for (var i = 0; ; ++i)
                {
                    var upperAddress = (i % 2) != 0;
                    if (upperAddress)
                    {
                        allocCreateInfo.Flags |= AllocationCreateFlags.UpperAddress;
                    }
                    else
                    {
                        allocCreateInfo.Flags &= ~AllocationCreateFlags.UpperAddress;
                    }

                    bufCreateInfo.Size = (ulong)AlignUp(bufSizeMin + (long)(rand.Generate() % (ulong)(bufSizeMax - bufSizeMin)), 64);
                    if (!TryCreateBuffer(context, in bufCreateInfo, in allocCreateInfo, out var buffer, out var alloc) || alloc == null)
                    {
                        break;
                    }

                    if (upperAddress)
                    {
                        Assert.True(alloc.NonNull().Offset < prevOffsetUpper);
                        prevOffsetUpper = alloc.Offset;
                    }
                    else
                    {
                        Assert.True(alloc.NonNull().Offset >= prevOffsetLower);
                        prevOffsetLower = alloc.Offset;
                    }

                    Assert.True(prevOffsetLower < prevOffsetUpper);
                    bufInfo.Add((buffer, alloc.NonNull()));
                }

                while (bufInfo.Count > 0)
                {
                    var indexToDestroy = (int)(rand.Generate() % (uint)bufInfo.Count);
                    var curr = bufInfo[indexToDestroy];
                    context.Vk.DestroyBuffer(context.Device, curr.Buffer, ReadOnlySpan<AllocationCallbacks>.Empty); curr.Allocation.Dispose();
                    bufInfo.RemoveAt(indexToDestroy);
                }

                // Create buffers on upper side only, constant size, until out of memory.
                prevOffsetUpper = 1024 * 300;
                allocCreateInfo.Flags |= AllocationCreateFlags.UpperAddress;
                bufCreateInfo.Size = bufSizeMax;
                for (var i = 0; ; ++i)
                {
                    if (!TryCreateBuffer(context, in bufCreateInfo, in allocCreateInfo, out var buffer, out var alloc) || alloc == null)
                    {
                        break;
                    }

                    Assert.True(alloc.NonNull().Offset < prevOffsetUpper);
                    prevOffsetUpper = alloc.Offset;
                    bufInfo.Add((buffer, alloc.NonNull()));
                }

                while (bufInfo.Count > 0)
                {
                    var curr = bufInfo[^1];
                    context.Vk.DestroyBuffer(context.Device, curr.Buffer, ReadOnlySpan<AllocationCallbacks>.Empty); curr.Allocation.Dispose();
                    bufInfo.RemoveAt(bufInfo.Count - 1);
                }
            }
        }

        finally
        {
            foreach (var b in bufInfo)
            {
                context.Vk.DestroyBuffer(context.Device, b.Buffer, ReadOnlySpan<AllocationCallbacks>.Empty); b.Allocation.Dispose();
            }

            pool.Dispose();
        }
    }

    [Fact]
    public void TestLinearAllocatorMultiBlock()
    {
        using var context = new VulkanContext();

        // Tests.cpp:4414 TestLinearAllocatorMultiBlock
        var rand = new VulkanContext.PortedRandom(345673u);

        var sampleBufCreateInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = 1024 * 1024,
            Usage = BufferUsageFlags.TransferSrcBit,
        };
        var sampleAllocCreateInfo = new AllocationCreateInfo { Usage = MemoryUsage.AutoPreferHost };
        context.Allocator.FindMemoryTypeIndexForBufferInfo(in sampleBufCreateInfo, in sampleAllocCreateInfo, out var memTypeIndex);
        Assert.True(memTypeIndex.HasValue);

        using var pool = context.Allocator.CreatePool(new AllocationPoolCreateInfo
        {
            MemoryTypeIndex = memTypeIndex.NonNull(),
            Flags = PoolCreateFlags.LinearAlgorithm,
        });

        var bufCreateInfo = sampleBufCreateInfo;
        var allocCreateInfo = new AllocationCreateInfo { Pool = pool };
        var bufInfo = new List<(Buffer Buffer, Allocation Allocation)>();

        // Test one-time free.
        {
            DeviceMemory lastMem = default;
            for (var i = 0; ; ++i)
            {
                Assert.True(TryCreateBuffer(context, in bufCreateInfo, in allocCreateInfo, out var buffer, out var alloc));
                Assert.NotNull(alloc);
                bufInfo.Add((buffer, alloc.NonNull()));
                if (!lastMem.Equals(default(DeviceMemory)) && !alloc.NonNull().DeviceMemory.Equals(lastMem))
                {
                    break;
                }

                lastMem = alloc.NonNull().DeviceMemory;
            }

            Assert.True(bufInfo.Count > 2);

            var poolStats = pool.GetPoolStats();
            Assert.Equal(2, poolStats.BlockCount);

            while (bufInfo.Count > 0)
            {
                var indexToDestroy = (int)(rand.Generate() % (uint)bufInfo.Count);
                var curr = bufInfo[indexToDestroy];
                context.Vk.DestroyBuffer(context.Device, curr.Buffer, ReadOnlySpan<AllocationCallbacks>.Empty); curr.Allocation.Dispose();
                bufInfo.RemoveAt(indexToDestroy);
            }

            poolStats = pool.GetPoolStats();
            Assert.True(poolStats.BlockCount <= 1);
        }

        // Test stack.
        {
            DeviceMemory lastMem = default;
            for (var i = 0; ; ++i)
            {
                Assert.True(TryCreateBuffer(context, in bufCreateInfo, in allocCreateInfo, out var buffer, out var alloc));
                Assert.NotNull(alloc);
                bufInfo.Add((buffer, alloc.NonNull()));
                if (!lastMem.Equals(default(DeviceMemory)) && !alloc.NonNull().DeviceMemory.Equals(lastMem))
                {
                    break;
                }

                lastMem = alloc.NonNull().DeviceMemory;
            }

            Assert.True(bufInfo.Count > 2);

            for (var i = 0; i < 5; ++i)
            {
                Assert.True(TryCreateBuffer(context, in bufCreateInfo, in allocCreateInfo, out var buffer, out var alloc));
                Assert.NotNull(alloc);
                bufInfo.Add((buffer, alloc.NonNull()));
            }

            var poolStats = pool.GetPoolStats();
            Assert.Equal(2, poolStats.BlockCount);

            var countToDelete = bufInfo.Count / 2;
            for (var i = 0; i < countToDelete; ++i)
            {
                var curr = bufInfo[^1];
                context.Vk.DestroyBuffer(context.Device, curr.Buffer, ReadOnlySpan<AllocationCallbacks>.Empty); curr.Allocation.Dispose();
                bufInfo.RemoveAt(bufInfo.Count - 1);
            }

            Assert.True(TryCreateBuffer(context, in bufCreateInfo, in allocCreateInfo, out var extraBuffer, out var extraAlloc));
            Assert.NotNull(extraAlloc);
            bufInfo.Add((extraBuffer, extraAlloc.NonNull()));

            poolStats = pool.GetPoolStats();
            Assert.Equal(1, poolStats.BlockCount);

            while (bufInfo.Count > 0)
            {
                var curr = bufInfo[^1];
                context.Vk.DestroyBuffer(context.Device, curr.Buffer, ReadOnlySpan<AllocationCallbacks>.Empty); curr.Allocation.Dispose();
                bufInfo.RemoveAt(bufInfo.Count - 1);
            }
        }
    }

    [Fact]
    public void TestAllocationAlgorithmsCorrectness()
    {
        using var context = new VulkanContext();

        // Tests.cpp:4549 TestAllocationAlgorithmsCorrectness
        const uint levelCount = 12;
        var rand = new VulkanContext.PortedRandom(2342435u);

        for (uint isVirtual = 0; isVirtual < 3; ++isVirtual)
        {
            // isVirtual == 0: VmaPool (unit 64 KB). 1: VmaVirtualBlock (unit 64 KB). 2: VmaVirtualBlock (unit 1 B).
            var sizeUnit = isVirtual == 2 ? 1L : 0x10000L;
            var blockSize = (1L << (int)(levelCount - 1)) * sizeUnit;

            VirtualBlock? virtualBlock = null;
            VulkanMemoryPool? pool = null;

            if (isVirtual != 0)
            {
                Assert.Equal(Result.Success, context.Allocator.CreateVirtualBlock(new VirtualBlockCreateInfo { Size = blockSize }, out var vb));
                virtualBlock = vb.NonNull();
            }
            else
            {
                var bufCreateInfo = new BufferCreateInfo
                {
                    SType = StructureType.BufferCreateInfo,
                    Size = 0x10000,
                    Usage = BufferUsageFlags.TransferDstBit,
                };
                var allocCreateInfo = new AllocationCreateInfo();
                context.Allocator.FindMemoryTypeIndexForBufferInfo(in bufCreateInfo, in allocCreateInfo, out var poolMemType);
                Assert.True(poolMemType.HasValue);

                pool = context.Allocator.CreatePool(new AllocationPoolCreateInfo
                {
                    MemoryTypeIndex = poolMemType.NonNull(),
                    BlockSize = blockSize,
                    MinBlockCount = 1,
                    MaxBlockCount = 1,
                });
            }

            for (uint strategyIndex = 0; strategyIndex < 3; ++strategyIndex)
            {
                var allocationsPerLevel = new List<(VirtualAllocation? VAlloc, Buffer Buffer, Allocation? Alloc)>[(int)levelCount];
                for (var l = 0; l < levelCount; ++l)
                {
                    allocationsPerLevel[l] = new List<(VirtualAllocation?, Buffer, Allocation?)>();
                }

                void CreateAllocation(uint level)
                {
                    var allocSize = (1L << (int)level) * sizeUnit;

                    if (isVirtual != 0)
                    {
                        var vaci = new VirtualAllocationCreateInfo { Size = allocSize };
                        switch (strategyIndex)
                        {
                            case 1: vaci.Flags = VirtualAllocationCreateFlags.StrategyMinTime; break;
                            case 2: vaci.Flags = VirtualAllocationCreateFlags.StrategyMinMemory; break;
                        }

                        Assert.Equal(Result.Success, virtualBlock.NonNull().Allocate(in vaci, out var valloc));
                        allocationsPerLevel[level].Add((valloc.NonNull(), default, null));
                    }
                    else
                    {
                        var aci = new AllocationCreateInfo { Pool = pool };
                        switch (strategyIndex)
                        {
                            case 1: aci.Strategy = AllocationStrategyFlags.MinTime; break;
                            case 2: aci.Strategy = AllocationStrategyFlags.MinMemory; break;
                        }

                        var bci = new BufferCreateInfo
                        {
                            SType = StructureType.BufferCreateInfo,
                            Size = (ulong)allocSize,
                            Usage = BufferUsageFlags.TransferDstBit,
                        };
                        Assert.True(TryCreateBuffer(context, in bci, in aci, out var buffer, out var alloc));
                        Assert.NotNull(alloc);
                        allocationsPerLevel[level].Add((null, buffer, alloc.NonNull()));
                    }
                }

                void DestroyAllocation(uint level, int index)
                {
                    var data = allocationsPerLevel[level][index];
                    if (isVirtual != 0)
                    {
                        data.VAlloc.NonNull().Dispose();
                    }
                    else
                    {
                        context.Vk.DestroyBuffer(context.Device, data.Buffer, ReadOnlySpan<AllocationCallbacks>.Empty); data.Alloc.NonNull().Dispose();
                    }

                    allocationsPerLevel[level].RemoveAt(index);
                }

                // Fill entire block with one big allocation.
                CreateAllocation(levelCount - 1);

                // For each level, remove one allocation and refill it with 2 at lower level.
                for (var level = levelCount; level-- > 1;)
                {
                    var indexToDestroy = (int)(rand.Generate() % (uint)allocationsPerLevel[level].Count);
                    DestroyAllocation(level, indexToDestroy);
                    CreateAllocation(level - 1);
                    CreateAllocation(level - 1);
                }

                // Test statistics.
                {
                    uint actualAllocCount = 0;
                    long actualAllocSize = 0;
                    for (uint level = 0; level < levelCount; ++level)
                    {
                        for (var index = allocationsPerLevel[level].Count - 1; index >= 0; --index)
                        {
                            if (isVirtual != 0)
                            {
                                var ai = allocationsPerLevel[level][index].VAlloc.NonNull();
                                actualAllocSize += ai.Size;
                            }
                            else
                            {
                                actualAllocSize += allocationsPerLevel[level][index].Alloc.NonNull().Size;
                            }
                        }

                        actualAllocCount += (uint)allocationsPerLevel[level].Count;
                    }

                    if (isVirtual != 0)
                    {
                        var vb = virtualBlock.NonNull();
                        var info = vb.GetStatistics();
                        Assert.Equal(actualAllocCount, (uint)info.AllocationCount);
                        Assert.Equal(actualAllocSize, info.UsedBytes);
                        Assert.Equal(1, info.BlockCount);
                        Assert.Equal(blockSize, vb.Size);
                    }
                    else
                    {
                        var stats = pool.NonNull().GetPoolStats();
                        Assert.Equal(actualAllocCount, (uint)stats.AllocationCount);
                        Assert.Equal(actualAllocSize, stats.Size - stats.UnusedSize);
                        Assert.Equal(1, stats.BlockCount);
                        Assert.Equal(blockSize, stats.Size);
                    }
                }

                // Free all remaining allocations.
                for (uint level = 0; level < levelCount; ++level)
                {
                    for (var index = allocationsPerLevel[level].Count - 1; index >= 0; --index)
                    {
                        DestroyAllocation(level, index);
                    }
                }
            }

            virtualBlock?.Dispose();
            pool?.Dispose();
        }
    }

    [Fact]
    public void ManuallyTestLinearAllocator()
    {
        using var context = new VulkanContext();

        // Tests.cpp:4744 ManuallyTestLinearAllocator
        var sampleBufCreateInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = 1024,
            Usage = BufferUsageFlags.TransferDstBit | BufferUsageFlags.VertexBufferBit,
        };
        var sampleAllocCreateInfo = new AllocationCreateInfo { Usage = MemoryUsage.AutoPreferDevice };
        context.Allocator.FindMemoryTypeIndexForBufferInfo(in sampleBufCreateInfo, in sampleAllocCreateInfo, out var memTypeIndex);
        Assert.True(memTypeIndex.HasValue);

        using var pool = context.Allocator.CreatePool(new AllocationPoolCreateInfo
        {
            MemoryTypeIndex = memTypeIndex.NonNull(),
            BlockSize = 10 * 1024,
            Flags = PoolCreateFlags.LinearAlgorithm,
            MinBlockCount = 1,
            MaxBlockCount = 1,
        });

        var bufCreateInfo = sampleBufCreateInfo;
        var allocCreateInfo = new AllocationCreateInfo { Pool = pool };
        var bufInfo = new List<(Buffer Buffer, Allocation Allocation)>();

        // Double stack: Lower 32/1024/32, Upper 128/1024/16.
        bufCreateInfo.Size = 32;
        Assert.True(TryCreateBuffer(context, in bufCreateInfo, in allocCreateInfo, out var b0, out var a0));
        Assert.NotNull(a0);
        bufInfo.Add((b0, a0.NonNull()));

        bufCreateInfo.Size = 1024;
        Assert.True(TryCreateBuffer(context, in bufCreateInfo, in allocCreateInfo, out var b1, out var a1));
        Assert.NotNull(a1);
        bufInfo.Add((b1, a1.NonNull()));

        bufCreateInfo.Size = 32;
        Assert.True(TryCreateBuffer(context, in bufCreateInfo, in allocCreateInfo, out var b2, out var a2));
        Assert.NotNull(a2);
        bufInfo.Add((b2, a2.NonNull()));

        allocCreateInfo.Flags |= AllocationCreateFlags.UpperAddress;

        bufCreateInfo.Size = 128;
        Assert.True(TryCreateBuffer(context, in bufCreateInfo, in allocCreateInfo, out var b3, out var a3));
        Assert.NotNull(a3);
        bufInfo.Add((b3, a3.NonNull()));

        bufCreateInfo.Size = 1024;
        Assert.True(TryCreateBuffer(context, in bufCreateInfo, in allocCreateInfo, out var b4, out var a4));
        Assert.NotNull(a4);
        bufInfo.Add((b4, a4.NonNull()));

        bufCreateInfo.Size = 16;
        Assert.True(TryCreateBuffer(context, in bufCreateInfo, in allocCreateInfo, out var b5, out var a5));
        Assert.NotNull(a5);
        bufInfo.Add((b5, a5.NonNull()));

        var poolStats = pool.GetPoolStats();
        Assert.Equal(1, poolStats.BlockCount);
        Assert.Equal(6, poolStats.AllocationCount);

        while (bufInfo.Count > 0)
        {
            var curr = bufInfo[^1];
            context.Vk.DestroyBuffer(context.Device, curr.Buffer, ReadOnlySpan<AllocationCallbacks>.Empty); curr.Allocation.Dispose();
            bufInfo.RemoveAt(bufInfo.Count - 1);
        }
    }

    [Fact]
    public void BasicTestTLSF()
    {
        using var context = new VulkanContext();

        // Tests.cpp:8175 BasicTestTLSF
        Assert.Equal(Result.Success, context.Allocator.CreateVirtualBlock(new VirtualBlockCreateInfo { Size = 50331648 }, out var block));
        var b = block.NonNull();

        var info = new VirtualAllocationCreateInfo { Alignment = 2, Size = 576 };

        Assert.Equal(Result.Success, b.Allocate(in info, out var a0));
        info.Size = 648;
        Assert.Equal(Result.Success, b.Allocate(in info, out var a1));
        a0.NonNull().Dispose();
        info.Size = 720;
        Assert.Equal(Result.Success, b.Allocate(in info, out var a2));
        a1.NonNull().Dispose();
        a2.NonNull().Dispose();

        b.Dispose();
    }

    [Fact]
    public unsafe void BasicTestAllocatePages()
    {
        using var context = new VulkanContext();

        // Tests.cpp:8207 BasicTestAllocatePages
        var sampleBufCreateInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = 1024,
            Usage = BufferUsageFlags.TransferSrcBit,
        };
        var sampleAllocCreateInfo = new AllocationCreateInfo
        {
            Usage = MemoryUsage.Auto,
            Flags = AllocationCreateFlags.HostAccessSequentialWrite,
        };
        context.Allocator.FindMemoryTypeIndexForBufferInfo(in sampleBufCreateInfo, in sampleAllocCreateInfo, out var memTypeIndex);
        Assert.True(memTypeIndex.HasValue);

        using var pool = context.Allocator.CreatePool(new AllocationPoolCreateInfo
        {
            MemoryTypeIndex = memTypeIndex.NonNull(),
            BlockSize = 1024 * 1024,
            MinBlockCount = 1,
            MaxBlockCount = 1,
        });

        var memReq = new MemoryRequirements
        {
            MemoryTypeBits = uint.MaxValue,
            Alignment = 4 * 1024,
            Size = 4 * 1024,
        };
        var allocCreateInfo = new AllocationCreateInfo
        {
            Flags = AllocationCreateFlags.Mapped,
            Pool = pool,
        };

        const uint allocCount = 100;

        // 100 allocations of 4 KB should fit into the 1 MB pool.
        context.Allocator.AllocateMemoryPages(in memReq, in allocCreateInfo, (int)allocCount, out var allocs);
        for (var i = 0; i < allocCount; i++)
        {
            Assert.NotNull(allocs[i]);
            Assert.NotEqual(0, (nint)allocs[i].MappedData);
            Assert.Equal(allocs[0].DeviceMemory, allocs[i].DeviceMemory);
            Assert.Equal(allocs[0].MemoryTypeIndex, allocs[i].MemoryTypeIndex);
        }

        foreach (var a in allocs)
        {
            a.Dispose();
        }

        // 100 allocations of 100 KB should fail: pool limited to 1 block / 1 MB.
        memReq.Size = 100 * 1024;
        var pagesRes = context.Allocator.AllocateMemoryPages(in memReq, in allocCreateInfo, (int)allocCount, out _);
        Assert.NotEqual(Result.Success, pagesRes);

        // 100 allocations of 4 KB with 128 KB alignment should also fail.
        memReq.Size = 4 * 1024;
        memReq.Alignment = 128 * 1024;
        var pagesRes2 = context.Allocator.AllocateMemoryPages(in memReq, in allocCreateInfo, (int)allocCount, out _);
        Assert.NotEqual(Result.Success, pagesRes2);

        // 100 dedicated allocations of 4 KB.
        memReq.Alignment = 4 * 1024;
        memReq.Size = 4 * 1024;
        var dedicatedInfo = new AllocationCreateInfo
        {
            RequiredFlags = MemoryPropertyFlags.HostVisibleBit,
            Flags = AllocationCreateFlags.Mapped | AllocationCreateFlags.DedicatedMemory,
        };
        context.Allocator.AllocateMemoryPages(in memReq, in dedicatedInfo, (int)allocCount, out var dedicated);
        for (var i = 0; i < allocCount; i++)
        {
            Assert.NotNull(dedicated[i]);
            Assert.NotEqual(0, (nint)dedicated[i].MappedData);
            Assert.Equal(dedicated[0].MemoryTypeIndex, dedicated[i].MemoryTypeIndex);
            Assert.Equal(0, dedicated[i].Offset);
            if (i > 0)
            {
                Assert.NotEqual(dedicated[0].DeviceMemory, dedicated[i].DeviceMemory);
            }
        }

        foreach (var a in dedicated)
        {
            a.Dispose();
        }
    }
}
