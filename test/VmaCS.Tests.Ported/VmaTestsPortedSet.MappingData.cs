using System.Runtime.InteropServices;
using Silk.NET.Vulkan;

namespace VmaCS.Tests.Ported;

/// <summary>Parity with C++ Tests.cpp: mapping, data upload, aliasing, statistics, multithreading.</summary>
public sealed unsafe partial class VmaTestsPortedSet
{
    [Fact]
    public void TestDataUploadingWithStagingBuffer()
    {
        using var context = new VulkanContext();
        var allocator = context.Allocator;

        const long bufferSize = 65536;
        var bufferData = new byte[bufferSize];
        var rand = new Random();
        for (var i = 0; i < bufferData.Length; i++)
        {
            bufferData[i] = (byte)rand.Next(256);
        }

        var uniformBufferCI = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = bufferSize,
            Usage = BufferUsageFlags.UniformBufferBit | BufferUsageFlags.TransferDstBit,
        };
        var uniformBufferAllocCI = new AllocationCreateInfo { Usage = MemoryUsage.Auto };

        allocator.CreateBuffer(in uniformBufferCI, in uniformBufferAllocCI, out var uniformBuffer, out var uniformBufferAlloc);
        Assert.NotNull(uniformBufferAlloc);

        var stagingBufferCI = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = bufferSize,
            Usage = BufferUsageFlags.TransferSrcBit,
        };
        var stagingBufferAllocCI = new AllocationCreateInfo
        {
            Usage = MemoryUsage.Auto,
            Flags = AllocationCreateFlags.HostAccessSequentialWrite | AllocationCreateFlags.Mapped,
        };

        allocator.CreateBuffer(in stagingBufferCI, in stagingBufferAllocCI, out var stagingBuffer, out var stagingBufferAlloc);
        Assert.NotNull(stagingBufferAlloc);
        try
        {
            // The staging buffer must end up mapped.
            Assert.NotEqual(0, (nint)stagingBufferAlloc.MappedData);

            stagingBufferAlloc.CopyMemoryToAllocation(bufferData.AsSpan(), 0);

            context.SubmitAndWait(commandBuffer =>
            {
                var preBarrier = new BufferMemoryBarrier
                {
                    SType = StructureType.BufferMemoryBarrier,
                    SrcAccessMask = AccessFlags.HostWriteBit,
                    DstAccessMask = AccessFlags.TransferReadBit,
                    SrcQueueFamilyIndex = uint.MaxValue,
                    DstQueueFamilyIndex = uint.MaxValue,
                    Buffer = stagingBuffer,
                    Offset = 0,
                    Size = ulong.MaxValue,
                };
                context.Vk.CmdPipelineBarrier(commandBuffer, PipelineStageFlags.HostBit, PipelineStageFlags.TransferBit, 0, 0, null, 1, in preBarrier, 0, null);

                var copy = new BufferCopy { Size = bufferSize };
                context.Vk.CmdCopyBuffer(commandBuffer, stagingBuffer, uniformBuffer, 1, in copy);

                var postBarrier = new BufferMemoryBarrier
                {
                    SType = StructureType.BufferMemoryBarrier,
                    SrcAccessMask = AccessFlags.TransferWriteBit,
                    DstAccessMask = AccessFlags.UniformReadBit,
                    SrcQueueFamilyIndex = uint.MaxValue,
                    DstQueueFamilyIndex = uint.MaxValue,
                    Buffer = uniformBuffer,
                    Offset = 0,
                    Size = ulong.MaxValue,
                };
                context.Vk.CmdPipelineBarrier(commandBuffer, PipelineStageFlags.TransferBit, PipelineStageFlags.VertexShaderBit, 0, 0, null, 1, in postBarrier, 0, null);
            });
        }
        finally
        {
            uniformBufferAlloc.Dispose();
            stagingBufferAlloc.Dispose();
            context.Vk.DestroyBuffer(context.Device, uniformBuffer, ReadOnlySpan<AllocationCallbacks>.Empty);
            context.Vk.DestroyBuffer(context.Device, stagingBuffer, ReadOnlySpan<AllocationCallbacks>.Empty);
        }
    }

    [Fact]
    public void TestDataUploadingWithMappedMemory()
    {
        using var context = new VulkanContext();
        var allocator = context.Allocator;

        const long bufferSize = 65536;
        var bufferData = new byte[bufferSize];
        var rand = new Random();
        for (var i = 0; i < bufferData.Length; i++)
        {
            bufferData[i] = (byte)rand.Next(256);
        }

        var uniformBufferCI = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = bufferSize,
            Usage = BufferUsageFlags.UniformBufferBit,
        };
        var uniformBufferAllocCI = new AllocationCreateInfo
        {
            Usage = MemoryUsage.Auto,
            Flags = AllocationCreateFlags.HostAccessSequentialWrite | AllocationCreateFlags.Mapped,
        };

        allocator.CreateBuffer(in uniformBufferCI, in uniformBufferAllocCI, out var uniformBuffer, out var uniformBufferAlloc);
        Assert.NotNull(uniformBufferAlloc);
        try
        {
            // We need to check if the uniform buffer really ended up in mappable memory.
            var memPropFlags = allocator.GetMemoryTypeProperties(uniformBufferAlloc.MemoryTypeIndex);
            Assert.True((memPropFlags & MemoryPropertyFlags.HostVisibleBit) != 0);

            Assert.NotEqual(0, (nint)uniformBufferAlloc.MappedData);

            uniformBufferAlloc.CopyMemoryToAllocation(bufferData.AsSpan(), 0);

            context.SubmitAndWait(commandBuffer =>
            {
                var barrier = new BufferMemoryBarrier
                {
                    SType = StructureType.BufferMemoryBarrier,
                    SrcAccessMask = AccessFlags.HostWriteBit,
                    DstAccessMask = AccessFlags.UniformReadBit,
                    SrcQueueFamilyIndex = uint.MaxValue,
                    DstQueueFamilyIndex = uint.MaxValue,
                    Buffer = uniformBuffer,
                    Offset = 0,
                    Size = ulong.MaxValue,
                };
                context.Vk.CmdPipelineBarrier(commandBuffer, PipelineStageFlags.HostBit, PipelineStageFlags.VertexShaderBit, 0, 0, null, 1, in barrier, 0, null);
            });
        }
        finally
        {
            uniformBufferAlloc.Dispose();
            context.Vk.DestroyBuffer(context.Device, uniformBuffer, ReadOnlySpan<AllocationCallbacks>.Empty);
        }
    }

    [Fact]
    public void TestAdvancedDataUploading()
    {
        using var context = new VulkanContext();
        var allocator = context.Allocator;

        const long bufferSize = 65536;
        var bufferData = new byte[bufferSize];
        var rand = new Random();

        for (var i = 0; i < bufferData.Length; i++)
        {
            bufferData[i] = (byte)rand.Next(256);
        }

        var uniformBufferCI = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = bufferSize,
            Usage = BufferUsageFlags.UniformBufferBit | BufferUsageFlags.TransferDstBit,
        };

        var uniformBufferAllocCI = new AllocationCreateInfo
        {
            Usage = MemoryUsage.Auto,
            Flags = AllocationCreateFlags.HostAccessSequentialWrite | AllocationCreateFlags.HostAccessAllowTransferInstead | AllocationCreateFlags.Mapped,
        };

        allocator.CreateBuffer(in uniformBufferCI, in uniformBufferAllocCI, out var uniformBuffer, out var uniformBufferAlloc);
        Assert.NotNull(uniformBufferAlloc);
        try
        {
            var memProps = allocator.GetMemoryTypeProperties(uniformBufferAlloc.MemoryTypeIndex);

            if ((memProps & MemoryPropertyFlags.HostVisibleBit) != 0)
            {
                // The allocation ended up as mapped memory.
                uniformBufferAlloc.Map(out var p);
                for (var i = 0; i < bufferData.Length; i++)
                {
                    ((byte*)p)[i] = bufferData[i];
                }
                uniformBufferAlloc.Unmap();

                context.SubmitAndWait(cb =>
                {
                    var barrier = new BufferMemoryBarrier
                    {
                        SType = StructureType.BufferMemoryBarrier,
                        SrcAccessMask = AccessFlags.HostWriteBit,
                        DstAccessMask = AccessFlags.UniformReadBit,
                        SrcQueueFamilyIndex = uint.MaxValue,
                        DstQueueFamilyIndex = uint.MaxValue,
                        Buffer = uniformBuffer,
                        Offset = 0,
                        Size = ulong.MaxValue,
                    };

                    context.Vk.CmdPipelineBarrier(cb, PipelineStageFlags.HostBit, PipelineStageFlags.VertexShaderBit, 0, 0, null, 1, in barrier, 0, null);
                });
            }
            else
            {
                // The allocation did not end up in mapped memory, so we need a staging buffer and a copy operation.
                var stagingCI = new BufferCreateInfo
                {
                    SType = StructureType.BufferCreateInfo,
                    Size = bufferSize,
                    Usage = BufferUsageFlags.TransferSrcBit,
                };
                var stagingAllocCI = new AllocationCreateInfo
                {
                    Usage = MemoryUsage.Auto,
                    Flags = AllocationCreateFlags.HostAccessSequentialWrite | AllocationCreateFlags.Mapped,
                };
                allocator.CreateBuffer(in stagingCI, in stagingAllocCI, out var stagingBuffer, out var stagingAlloc);
                Assert.NotNull(stagingAlloc);
                try
                {
                    stagingAlloc.Map(out var sp);
                    for (var i = 0; i < bufferData.Length; i++)
                    {
                        ((byte*)sp)[i] = bufferData[i];
                    }
                    stagingAlloc.Unmap();

                    context.SubmitAndWait(cb =>
                    {
                        var preBarrier = new BufferMemoryBarrier
                        {
                            SType = StructureType.BufferMemoryBarrier,
                            SrcAccessMask = AccessFlags.HostWriteBit,
                            DstAccessMask = AccessFlags.TransferReadBit,
                            SrcQueueFamilyIndex = uint.MaxValue,
                            DstQueueFamilyIndex = uint.MaxValue,
                            Buffer = stagingBuffer,
                            Offset = 0,
                            Size = ulong.MaxValue,
                        };
                        context.Vk.CmdPipelineBarrier(cb, PipelineStageFlags.HostBit, PipelineStageFlags.TransferBit, 0, 0, null, 1, in preBarrier, 0, null);

                        var copy = new BufferCopy { Size = bufferSize };
                        context.Vk.CmdCopyBuffer(cb, stagingBuffer, uniformBuffer, 1, in copy);

                        var postBarrier = new BufferMemoryBarrier
                        {
                            SType = StructureType.BufferMemoryBarrier,
                            SrcAccessMask = AccessFlags.TransferWriteBit,
                            DstAccessMask = AccessFlags.UniformReadBit,
                            SrcQueueFamilyIndex = uint.MaxValue,
                            DstQueueFamilyIndex = uint.MaxValue,
                            Buffer = uniformBuffer,
                            Offset = 0,
                            Size = ulong.MaxValue,
                        };
                        context.Vk.CmdPipelineBarrier(cb, PipelineStageFlags.TransferBit, PipelineStageFlags.VertexShaderBit, 0, 0, null, 1, in postBarrier, 0, null);
                    });
                }
                finally
                {
                    stagingAlloc.Dispose();
                    context.Vk.DestroyBuffer(context.Device, stagingBuffer, ReadOnlySpan<AllocationCallbacks>.Empty);
                }
            }
        }
        finally
        {
            uniformBufferAlloc.Dispose();
            context.Vk.DestroyBuffer(context.Device, uniformBuffer, ReadOnlySpan<AllocationCallbacks>.Empty);
        }
    }

    [Fact]
    public void TestStatistics()
    {
        using var context = new VulkanContext();
        var allocator = context.Allocator;

        const long bufSize = 10L * 1024 * 1024;
        const uint bufCount = 4;
        const long preallocatedBlockSize = bufSize * (bufCount + 1);

        var heapCount = (int)allocator.MemoryProperties.MemoryHeapCount;
        var typeCount = (int)allocator.MemoryProperties.MemoryTypeCount;

        // Persisted across iterations: discovered during the normal (test 1) sub-test.
        var memTypeIndex = -1;

        for (uint testIndex = 0; testIndex < 5; ++testIndex)
        {
            var budgetBeg = new AllocationBudget[heapCount];
            allocator.GetBudget(budgetBeg);
            var statsBeg = allocator.CalculateStats();

            for (var i = 0; i < heapCount; i++)
            {
                Assert.True(budgetBeg[i].Budget > 0);
                Assert.True(budgetBeg[i].Budget <= (long)allocator.MemoryProperties.MemoryHeaps[i].Size);
                Assert.True(budgetBeg[i].AllocationBytes <= budgetBeg[i].BlockBytes);
            }

            var usePool = testIndex >= 2;
            var useDedicated = testIndex == 0 || testIndex == 3;
            var usePreallocated = testIndex == 4;

            VulkanMemoryPool? pool = null;
            if (usePool)
            {
                Assert.NotEqual(-1, memTypeIndex);
                var poolCI = new AllocationPoolCreateInfo { MemoryTypeIndex = memTypeIndex };
                if (usePreallocated)
                {
                    poolCI.BlockSize = preallocatedBlockSize;
                    poolCI.MinBlockCount = 1;
                    poolCI.MaxBlockCount = 1;
                }
                pool = allocator.CreatePool(poolCI);
            }

            PoolStats poolStatsBeg = default;
            if (usePool)
            {
                poolStatsBeg = pool.NonNull().GetPoolStats();
            }

            // CREATE BUFFERS
            var bufInfo = new BufferCreateInfo
            {
                SType = StructureType.BufferCreateInfo,
                Size = bufSize,
                Usage = BufferUsageFlags.TransferDstBit,
            };

            var allocCI = new AllocationCreateInfo();
            if (usePool)
            {
                allocCI.Pool = pool;
            }
            else
            {
                allocCI.Usage = MemoryUsage.AutoPreferDevice;
            }
            if (useDedicated)
            {
                allocCI.Flags |= AllocationCreateFlags.DedicatedMemory;
            }

            var buffers = new Silk.NET.Vulkan.Buffer[bufCount];
            var allocs = new Allocation?[bufCount];
            var heapIndex = -1;
            for (uint b = 0; b < bufCount; b++)
            {
                allocator.CreateBuffer(in bufInfo, in allocCI, out buffers[b], out var alloc);
                Assert.NotNull(alloc);
                allocs[b] = alloc;
                if (b == 0)
                {
                    if (testIndex == 1)
                    {
                        memTypeIndex = alloc.MemoryTypeIndex;
                    }
                    heapIndex = (int)allocator.MemoryProperties.MemoryTypes[alloc.MemoryTypeIndex].HeapIndex;
                }
                else
                {
                    Assert.Equal(heapIndex, (int)allocator.MemoryProperties.MemoryTypes[alloc.MemoryTypeIndex].HeapIndex);
                }
            }

            var budgetWith = new AllocationBudget[heapCount];
            allocator.GetBudget(budgetWith);
            var statsWith = allocator.CalculateStats();

            PoolStats poolStatsWith = default;
            if (usePool)
            {
                poolStatsWith = pool.NonNull().GetPoolStats();
            }

            // DESTROY BUFFERS
            for (var b = (int)bufCount - 1; b >= 0; b--)
            {
                allocs[b]!.Dispose();
                context.Vk.DestroyBuffer(context.Device, buffers[b], ReadOnlySpan<AllocationCallbacks>.Empty);
            }

            PoolStats poolStatsEnd = default;
            if (usePool)
            {
                poolStatsEnd = pool.NonNull().GetPoolStats();
            }

            pool?.Dispose();

            var budgetEnd = new AllocationBudget[heapCount];
            allocator.GetBudget(budgetEnd);
            var statsEnd = allocator.CalculateStats();

            // CHECK MEMORY HEAPS
            for (var i = 0; i < heapCount; i++)
            {
                Assert.True(budgetEnd[i].AllocationBytes <= budgetEnd[i].BlockBytes);

                if (i == heapIndex)
                {
                    Assert.True(budgetWith[i].Usage >= budgetBeg[i].Usage);
                    Assert.True(budgetEnd[i].Usage <= budgetWith[i].Usage);

                    Assert.Equal(budgetEnd[i].AllocationBytes, budgetBeg[i].AllocationBytes);
                    Assert.Equal(budgetWith[i].AllocationBytes, budgetBeg[i].AllocationBytes + bufSize * bufCount);

                    if (usePool)
                    {
                        Assert.Equal(budgetEnd[i].BlockBytes, budgetBeg[i].BlockBytes);
                        Assert.True(budgetWith[i].BlockBytes > budgetBeg[i].BlockBytes);
                    }
                    else
                    {
                        Assert.True(budgetWith[i].BlockBytes >= budgetBeg[i].BlockBytes);
                    }

                    Assert.Equal(budgetEnd[i].AllocationCount, budgetBeg[i].AllocationCount);
                    Assert.Equal(budgetWith[i].AllocationCount, budgetBeg[i].AllocationCount + (int)bufCount);

                    if (useDedicated)
                    {
                        Assert.Equal(budgetEnd[i].BlockCount, budgetBeg[i].BlockCount);
                        Assert.Equal(budgetWith[i].BlockCount, budgetBeg[i].BlockCount + (int)bufCount);
                    }
                    else if (usePool)
                    {
                        Assert.Equal(budgetEnd[i].BlockCount, budgetBeg[i].BlockCount);
                        if (usePreallocated)
                        {
                            Assert.Equal(budgetWith[i].BlockCount, budgetBeg[i].BlockCount + 1);
                        }
                        else
                        {
                            Assert.True(budgetWith[i].BlockCount > budgetBeg[i].BlockCount);
                        }
                    }
                }
                else
                {
                    Assert.Equal(budgetEnd[i].AllocationBytes, budgetBeg[i].AllocationBytes);
                    Assert.Equal(budgetWith[i].AllocationBytes, budgetBeg[i].AllocationBytes);
                    Assert.Equal(budgetEnd[i].BlockBytes, budgetBeg[i].BlockBytes);
                    Assert.Equal(budgetWith[i].BlockBytes, budgetBeg[i].BlockBytes);
                    Assert.Equal(budgetEnd[i].AllocationCount, budgetBeg[i].AllocationCount);
                    Assert.Equal(budgetWith[i].AllocationCount, budgetBeg[i].AllocationCount);
                    Assert.Equal(budgetEnd[i].BlockCount, budgetBeg[i].BlockCount);
                    Assert.Equal(budgetWith[i].BlockCount, budgetBeg[i].BlockCount);
                }

                // Validate that statistics per heap and per type sum up to total correctly.
                AssertTotalMatchesSum(statsBeg, heapCount, typeCount);
                AssertTotalMatchesSum(statsWith, heapCount, typeCount);
                AssertTotalMatchesSum(statsEnd, heapCount, typeCount);

                // Compare CalculateStats per heap with GetBudget.
                AssertStatInfoBudgetEqual(statsBeg.MemoryHeap[i], budgetBeg[i]);
                AssertStatInfoBudgetEqual(statsWith.MemoryHeap[i], budgetWith[i]);
                AssertStatInfoBudgetEqual(statsEnd.MemoryHeap[i], budgetEnd[i]);

                // The per-heap / per-type statistics are validated against the global Total via
                // AssertTotalMatchesSum, which now also checks the allocation size Min/Max bounds.
                // VmaCS computes Total.AllocationSizeMin/Max by aggregating every block and every
                // dedicated allocation (including those created through a custom pool) — matching C++.
                if (usePool)
                {
                    // VmaCS unifies vmaGetPoolStatistics and vmaCalculatePoolStatistics into a single
                    // GetPoolStats, so the two C++ APIs are trivially identical (no separate call to cross-check).
                    Assert.Equal(long.MaxValue, poolStatsBeg.AllocationSizeMin);
                    Assert.Equal(0, poolStatsBeg.AllocationSizeMax);
                    Assert.Equal(0, poolStatsBeg.AllocationCount);
                    Assert.Equal(0L, poolStatsBeg.AllocationBytes);
                    Assert.Equal(0, poolStatsEnd.AllocationCount);
                    Assert.Equal(0L, poolStatsEnd.AllocationBytes);
                    Assert.Equal(long.MaxValue, poolStatsEnd.AllocationSizeMin);
                    Assert.Equal(0, poolStatsEnd.AllocationSizeMax);

                    if (usePreallocated)
                    {
                        Assert.Equal(1, poolStatsBeg.BlockCount);
                        Assert.Equal(1, poolStatsEnd.BlockCount);
                        Assert.Equal(preallocatedBlockSize, poolStatsBeg.Size);
                        Assert.Equal(preallocatedBlockSize, poolStatsEnd.Size);
                    }
                    else
                    {
                        Assert.Equal(0, poolStatsBeg.BlockCount);
                        Assert.Equal(0L, poolStatsBeg.Size);
                    }

                    // Pool holding the buffers. VmaCS now aggregates DEDICATED allocations created
                    // through the pool into its statistics (each dedicated allocation is one pool block),
                    // so these assertions hold for both sub-allocated and dedicated pool usage.
                    Assert.Equal(bufSize, poolStatsWith.AllocationSizeMin);
                    Assert.Equal(bufSize, poolStatsWith.AllocationSizeMax);
                    Assert.Equal((int)bufCount, poolStatsWith.AllocationCount);
                    Assert.Equal(bufSize * bufCount, poolStatsWith.AllocationBytes);

                    if (usePreallocated)
                    {
                        Assert.Equal(1, poolStatsWith.BlockCount);
                        Assert.Equal(preallocatedBlockSize, poolStatsWith.Size);
                    }
                    else
                    {
                        Assert.True(poolStatsWith.BlockCount > 0);
                        Assert.True(poolStatsWith.Size >= poolStatsWith.AllocationBytes);
                    }
                }

            }
        }
    }

    [Fact]
    public void TestAliasing()
    {
        using var context = new VulkanContext();
        var allocator = context.Allocator;

        /*
        This is just a simple test, more like a code sample to demonstrate it's possible.
        */

        // A 512x512 texture to be sampled.
        var img1CI = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = Format.R8G8B8A8Srgb,
            Extent = new Extent3D(512, 512, 1),
            MipLevels = 10,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            InitialLayout = ImageLayout.Undefined,
            Usage = ImageUsageFlags.TransferDstBit | ImageUsageFlags.SampledBit,
        };

        // A full screen texture to be used as color attachment.
        var img2CI = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = Format.R8G8B8A8Unorm,
            Extent = new Extent3D(1920, 1080, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            InitialLayout = ImageLayout.Undefined,
            Usage = ImageUsageFlags.SampledBit | ImageUsageFlags.ColorAttachmentBit,
        };

        Assert.Equal(Result.Success, context.Vk.CreateImage(context.Device, in img1CI, null, out var img1));
        Assert.Equal(Result.Success, context.Vk.CreateImage(context.Device, in img2CI, null, out var img2));

        context.Vk.GetImageMemoryRequirements(context.Device, img1, out var img1Req);
        context.Vk.GetImageMemoryRequirements(context.Device, img2, out var img2Req);

        var finalReq = new MemoryRequirements
        {
            Size = Math.Max(img1Req.Size, img2Req.Size),
            Alignment = Math.Max(img1Req.Alignment, img2Req.Alignment),
            MemoryTypeBits = img1Req.MemoryTypeBits & img2Req.MemoryTypeBits,
        };

        if (finalReq.MemoryTypeBits != 0)
        {
            var allocCI = new AllocationCreateInfo { PreferredFlags = MemoryPropertyFlags.DeviceLocalBit };
            allocator.AllocateMemory(in finalReq, in allocCI, out var alloc);
            Assert.NotNull(alloc);

            Assert.Equal(Result.Success, alloc.BindImageMemory(img1));
            Assert.Equal(Result.Success, alloc.BindImageMemory(img2));

            // You can use img1, img2 here, but not at the same time.NonNull()

            alloc.Dispose();
        }

        context.Vk.DestroyImage(context.Device, img1, ReadOnlySpan<AllocationCallbacks>.Empty);
        context.Vk.DestroyImage(context.Device, img2, ReadOnlySpan<AllocationCallbacks>.Empty);
    }

    [Fact]
    public void TestAllocationAliasing()
    {
        // Mirrors C++ TestAllocationAliasing: a dedicated image (created with CAN_ALIAS_BIT) bound to two
        // images, and a large buffer promoted to a dedicated allocation by VMA's auto-dedicated heuristic
        // (CAN_ALIAS_BIT, no DEDICATED_MEMORY_BIT), then aliased by a smaller buffer created with
        // CreateAliasingBuffer (C++'s vmaCreateAliasingBuffer).
        using var context = new VulkanContext();

        // 1. Dedicated image bound to two images.
        var img1Info = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = Format.R8G8B8A8Unorm,
            Extent = new Extent3D(640, 480, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            InitialLayout = ImageLayout.Preinitialized,
            Usage = ImageUsageFlags.SampledBit,
        };

        // C++ uses vmaCreateImage with VMA_ALLOCATION_CREATE_DEDICATED_MEMORY_BIT |
        // VMA_MEMORY_USAGE_AUTO | VMA_ALLOCATION_CREATE_CAN_ALIAS_BIT. For an Auto dedicated image VMA
        // needs the image usage to pick a memory type, which vmaCreateImage supplies (a bare
        // vmaAllocateMemory with Auto and no resource info fails to resolve a type, exactly as in C++).
        // CAN_ALIAS_BIT makes VMA skip the dedicated allocate info so the memory can later be bound to a
        // second (aliasing) image without validation errors.
        var imgAllocCI = new AllocationCreateInfo
        {
            Usage = MemoryUsage.Auto,
            Flags = AllocationCreateFlags.DedicatedMemory | AllocationCreateFlags.CanAlias
        };

        Image img1 = default, img2 = default;
        Allocation? alloc = null;
        try
        {
            // Mirrors vmaCreateImage: creates img1 and allocates/binds the dedicated memory.
            context.Allocator.CreateImage(in img1Info, in imgAllocCI, out img1, out alloc);
            Assert.NotNull(alloc);

            var img2Info = img1Info;
            img2Info.Extent = new Extent3D(480, 256, 1);
            Assert.Equal(Result.Success, context.Vk.CreateImage(context.Device, in img2Info, null, out img2));
            // Mirrors vmaBindImageMemory: bind the second image to the same aliased allocation.
            Assert.Equal(Result.Success, alloc.BindImageMemory(img2));
        }
        finally
        {
            if (img2.Handle != 0)
            {
                context.Vk.DestroyImage(context.Device, img2, ReadOnlySpan<AllocationCallbacks>.Empty);
            }
            if (img1.Handle != 0)
            {
                context.Vk.DestroyImage(context.Device, img1, ReadOnlySpan<AllocationCallbacks>.Empty);
            }
            if (alloc != null)
            {
                alloc.Dispose();
            }
        }

        // 2. Large buffer aliased by a smaller buffer in the same memory.
        // C++ uses VMA_ALLOCATION_CREATE_CAN_ALIAS_BIT (no DEDICATED_MEMORY_BIT) with a 300 MiB buffer;
        // VMA auto-promotes it to a dedicated allocation (offset 0). The aliasing buffer is then created
        // with vmaCreateAliasingBuffer, which VmaCS mirrors with CreateAliasingBuffer.
        const long origSize = 300L * 1024 * 1024;
        const long aliasSize = 200L * 1024 * 1024;

        var bufInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = origSize,
            Usage = BufferUsageFlags.TransferDstBit | BufferUsageFlags.StorageTexelBufferBit,
        };
        context.Allocator.CreateBuffer(in bufInfo,
            new AllocationCreateInfo { Usage = MemoryUsage.Auto, Flags = AllocationCreateFlags.CanAlias },
            out var origBuffer, out var bufAlloc);
        Assert.NotNull(bufAlloc);
        Silk.NET.Vulkan.Buffer aliasBuffer = default;
        try
        {
            // Auto-dedicated promotion must have produced a dedicated, offset-0 allocation (as C++ asserts).
            Assert.Equal(0, bufAlloc.Offset);

            var aliasInfo = bufInfo;
            aliasInfo.Size = aliasSize;
            Assert.Equal(Result.Success, context.Allocator.CreateAliasingBuffer(bufAlloc, in aliasInfo, out aliasBuffer));
        }
        finally
        {
            if (aliasBuffer.Handle != 0)
            {
                context.Vk.DestroyBuffer(context.Device, aliasBuffer, ReadOnlySpan<AllocationCallbacks>.Empty);
            }
            bufAlloc.Dispose();
            context.Vk.DestroyBuffer(context.Device, origBuffer, ReadOnlySpan<AllocationCallbacks>.Empty);
        }
    }

    [Fact]
    public void TestMapping()
    {
        using var context = new VulkanContext();

        int? memTypeIndex = null;
        VulkanMemoryPool? pool = null;

        for (var testIndex = 0; testIndex < 3; testIndex++)
        {
            if (testIndex == 1)
            {
                Assert.True(memTypeIndex.HasValue);
                pool = context.Allocator.CreatePool(new AllocationPoolCreateInfo { MemoryTypeIndex = memTypeIndex.Value });
            }

            var bufInfo = new BufferCreateInfo
            {
                SType = StructureType.BufferCreateInfo,
                Size = 0x10000,
                Usage = BufferUsageFlags.TransferSrcBit,
            };

            var allocInfo = new AllocationCreateInfo
            {
                Usage = MemoryUsage.Auto,
                Flags = AllocationCreateFlags.HostAccessRandom | (testIndex == 2 ? AllocationCreateFlags.DedicatedMemory : 0),
            };
            if (pool != null)
            {
                allocInfo.Pool = pool;
            }

            var allocs = new Allocation?[3];
            var buffers = new Silk.NET.Vulkan.Buffer[3];

            for (var i = 0; i < 2; i++)
            {
                context.Allocator.CreateBuffer(in bufInfo, in allocInfo, out buffers[i], out var alloc);
                Assert.NotNull(alloc);
                allocs[i] = alloc;
                if (i == 0)
                {
                    memTypeIndex = alloc.MemoryTypeIndex;
                }
            }

            Assert.Equal(0, (nint)allocs[0].NonNull().MappedData);
            Assert.Equal(0, (nint)allocs[1].NonNull().MappedData);

            allocs[0].NonNull().Map(out var data00);
            Assert.NotEqual(0, (nint)data00);
            unsafe { ((byte*)data00)[0xFFFF] = ((byte*)data00)[0]; }

            allocs[0].NonNull().Map(out var data01);
            Assert.Equal((nint)data00, (nint)data01);

            allocs[1].NonNull().Map(out var data1);
            Assert.NotEqual(0, (nint)data1);
            Assert.True(!RegionsOverlap((nint)data00, (int)bufInfo.Size, (nint)data1, (int)bufInfo.Size));
            unsafe { ((byte*)data1)[0xFFFF] = ((byte*)data1)[0]; }

            allocs[0].NonNull().Unmap();
            allocs[0].NonNull().Unmap();
            Assert.Equal(0, (nint)allocs[0].NonNull().MappedData);

            allocs[1].NonNull().Unmap();
            Assert.Equal(0, (nint)allocs[1].NonNull().MappedData);

            allocInfo.Flags |= AllocationCreateFlags.Mapped;
            context.Allocator.CreateBuffer(in bufInfo, in allocInfo, out buffers[2], out var alloc2);
            Assert.NotNull(alloc2);
            allocs[2] = alloc2;

            allocs[2].NonNull().Map(out var data2);
            Assert.NotEqual(0, (nint)data2);
            unsafe { ((byte*)data2)[0xFFFF] = ((byte*)data2)[0]; }
            allocs[2].NonNull().Unmap();
            Assert.Equal((nint)data2, (nint)allocs[2].NonNull().MappedData);

            for (var i = 2; i >= 0; i--)
            {
                allocs[i].NonNull().Dispose();
                context.Vk.DestroyBuffer(context.Device, buffers[i], ReadOnlySpan<AllocationCallbacks>.Empty);
            }

            if (pool != null)
            {
                pool.Dispose();
                pool = null;
            }
        }
    }

    [Fact]
    public void TestAllocationMemoryCopy()
    {
        // Uses vmaCopyMemoryToAllocation / vmaCopyAllocationToMemory equivalents ported as
        // Allocation.CopyMemoryToAllocation / CopyAllocationToMemory.
        using var context = new VulkanContext();

        const int bufSize = 128 * 1024;
        const int fragmentSize = 1792;
        const int fragmentOffset = 14080;

        var origBuf = new byte[bufSize];
        for (var i = 0; i < bufSize; i++)
        {
            origBuf[i] = (byte)(i * 13 + 7);
        }

        var bufInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = bufSize,
            Usage = BufferUsageFlags.TransferSrcBit | BufferUsageFlags.TransferDstBit,
        };

        var variants = new[]
        {
            AllocationCreateFlags.HostAccessSequentialWrite,
            AllocationCreateFlags.HostAccessSequentialWrite | AllocationCreateFlags.Mapped,
            AllocationCreateFlags.HostAccessRandom,
            AllocationCreateFlags.HostAccessRandom | AllocationCreateFlags.Mapped,
        };

        foreach (var flags in variants)
        {
            var info = new AllocationCreateInfo { Usage = MemoryUsage.Auto, Flags = flags };
            context.Allocator.CreateBuffer(in bufInfo, in info, out var buffer, out var alloc);
            Assert.NotNull(alloc);
            var isRandom = (flags & AllocationCreateFlags.HostAccessRandom) != 0;

            try
            {
                void Write()
                {
                    alloc.CopyMemoryToAllocation(origBuf.AsSpan(), 0);
                }

                Write();

                if (isRandom)
                {
                    var readBack = new byte[bufSize];
                    alloc.CopyAllocationToMemory(0, readBack.AsSpan());
                    Assert.Equal(origBuf, readBack);
                }

                alloc.CopyMemoryToAllocation(origBuf.AsSpan(0, fragmentSize), fragmentOffset);

                if (isRandom)
                {
                    var readBack = new byte[fragmentSize];
                    alloc.CopyAllocationToMemory(fragmentOffset, readBack.AsSpan());
                    for (var i = 0; i < fragmentSize; i++)
                    {
                        Assert.Equal(origBuf[i], readBack[i]);
                    }
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
    public void TestDeviceLocalMapped()
    {
        using var context = new VulkanContext();

        for (var testIndex = 0; testIndex < 2; testIndex++)
        {
            var bufInfo = new BufferCreateInfo
            {
                SType = StructureType.BufferCreateInfo,
                Usage = BufferUsageFlags.TransferDstBit,
                Size = 4096,
            };

            VulkanMemoryPool? pool = null;
            var allocInfo = new AllocationCreateInfo
            {
                Usage = MemoryUsage.Unknown,
                RequiredFlags = MemoryPropertyFlags.DeviceLocalBit,
                Flags = AllocationCreateFlags.Mapped,
            };

            if (testIndex == 1)
            {
                context.Allocator.FindMemoryTypeIndexForBufferInfo(in bufInfo, in allocInfo, out var memType);
                if (memType is null)
                {
                    continue;
                }
                pool = context.Allocator.CreatePool(new AllocationPoolCreateInfo { MemoryTypeIndex = memType.Value });
                allocInfo.Pool = pool;
            }

            context.Allocator.CreateBuffer(in bufInfo, in allocInfo, out var buffer, out var alloc);
            Assert.NotNull(alloc);
            try
            {
                var memProps = context.Allocator.GetMemoryTypeProperties(alloc.MemoryTypeIndex);
                var shouldBeMapped = (memProps & MemoryPropertyFlags.HostVisibleBit) != 0;
                Assert.Equal(shouldBeMapped, alloc.MappedData != null);
            }
            finally
            {
                alloc.Dispose();
                context.Vk.DestroyBuffer(context.Device, buffer, ReadOnlySpan<AllocationCallbacks>.Empty);
                pool?.Dispose();
            }
        }
    }

    [Fact]
    public void TestMappingMultithreaded()
    {
        using var context = new VulkanContext();

        const int threadCount = 16;
        const int bufferCount = 1024;
        const int threadBufferCount = bufferCount / threadCount; // 64

        var bufCreateInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = 0x10000,
            Usage = BufferUsageFlags.TransferSrcBit,
        };

        // Discovered lazily during the NORMAL pass (C++ captures it from the first allocation).
        var memTypeIndex = -1;

        for (var testIndex = 0; testIndex < 3; testIndex++)
        {
            VulkanMemoryPool? pool = null;
            if (testIndex == 1) // TEST_POOL
            {
                Assert.True(memTypeIndex >= 0, "memory type must be discovered in the NORMAL pass first");
                pool = context.Allocator.CreatePool(new AllocationPoolCreateInfo
                {
                    MemoryTypeIndex = memTypeIndex,
                });
            }

            var allocCreateInfo = new AllocationCreateInfo
            {
                Usage = MemoryUsage.Auto,
                Flags = AllocationCreateFlags.HostAccessRandom,
                Pool = pool,
            };
            if (testIndex == 2) // TEST_DEDICATED
            {
                allocCreateInfo.Flags |= AllocationCreateFlags.DedicatedMemory;
            }

            var threads = new Thread[threadCount];
            for (var threadIndex = 0; threadIndex < threadCount; threadIndex++)
            {
                var localThreadIndex = threadIndex;
                threads[threadIndex] = new Thread(() =>
                {
                    var rand = new VulkanContext.PortedRandom((uint)localThreadIndex);

                    var bufInfos = new (Silk.NET.Vulkan.Buffer Buffer, Allocation Alloc, int Mode)[threadBufferCount];

                    for (var bufferIndex = 0; bufferIndex < threadBufferCount; bufferIndex++)
                    {
                        // MODE: 0 DONT_MAP, 1 MAP_FOR_MOMENT, 2 MAP_FOR_LONGER, 3 MAP_TWO_TIMES, 4 PERSISTENTLY_MAPPED
                        var mode = rand.Next(5);
                        bufInfos[bufferIndex].Mode = mode;

                        var localAllocCreateInfo = allocCreateInfo;
                        if (mode == 4)
                        {
                            localAllocCreateInfo.Flags |= AllocationCreateFlags.Mapped;
                        }

                        context.Allocator.CreateBuffer(in bufCreateInfo, in localAllocCreateInfo, out var buffer, out var alloc);
                        Assert.NotNull(alloc);
                        bufInfos[bufferIndex].Buffer = buffer;
                        bufInfos[bufferIndex].Alloc = alloc;

                        if (memTypeIndex < 0)
                        {
                            memTypeIndex = alloc.MemoryTypeIndex;
                        }

                        void* data = default;

                        if (mode == 4) // PERSISTENTLY_MAPPED
                        {
                            data = alloc.MappedData;
                            Assert.NotEqual(0, (nint)data);
                        }
                        else if (mode == 1 || mode == 2 || mode == 3) // MAP_* modes
                        {
                            Assert.Equal(0, (nint)alloc.MappedData);
                            alloc.Map(out data);
                            Assert.NotEqual(0, (nint)data);

                            if (mode == 3) // MAP_TWO_TIMES
                            {
                                alloc.Map(out var data2);
                                Assert.Equal((nint)data, (nint)data2);
                            }
                        }
                        else if (mode == 0) // DONT_MAP
                        {
                            Assert.Equal(0, (nint)alloc.MappedData);
                        }

                        // Test reading/writing the mapped range doesn't crash (valid only while mapped).
                        if (data != null)
                        {
                            Marshal.WriteByte((nint)data, 0xFFFF, Marshal.ReadByte((nint)data, 0));
                        }

                        if (mode == 1 || mode == 3) // MAP_FOR_MOMENT / MAP_TWO_TIMES: unmap now
                        {
                            alloc.Unmap();

                            if (mode == 1) // MAP_FOR_MOMENT: fully unmapped
                            {
                                Assert.Equal(0, (nint)alloc.MappedData);
                            }
                            else // MAP_TWO_TIMES: still mapped after one unmap
                            {
                                Assert.Equal((nint)data, (nint)alloc.MappedData);
                            }
                        }

                        // C++: rand % 3 -> 0: Sleep(0) yield, 1: Sleep(10), 2: no sleep.
                        switch (rand.Next(3))
                        {
                            case 0: Thread.Sleep(0); break;
                            case 1: Thread.Sleep(10); break;
                        }

                        // Second read/write after the sleep. C++ writes unconditionally, but for
                        // MAP_FOR_MOMENT the pointer is already unmapped (latent UB in the original);
                        // we write only while the allocation is still mapped to avoid an access violation.
                        var stillMapped = mode == 4 || mode == 2 || mode == 3;
                        if (data != null && stillMapped)
                        {
                            Marshal.WriteByte((nint)data, 0xFFFF, Marshal.ReadByte((nint)data, 0));
                        }
                    }

                    // Teardown: unmap the long-lived mappings, then free + destroy.
                    for (var bufferIndex = threadBufferCount - 1; bufferIndex >= 0; bufferIndex--)
                    {
                        var mode = bufInfos[bufferIndex].Mode;

                        if (mode == 2 || mode == 3) // MAP_FOR_LONGER / MAP_TWO_TIMES
                        {
                            bufInfos[bufferIndex].Alloc.Unmap();
                            Assert.Equal(0, (nint)bufInfos[bufferIndex].Alloc.MappedData);
                        }

                        bufInfos[bufferIndex].Alloc.Dispose();
                        context.Vk.DestroyBuffer(context.Device, bufInfos[bufferIndex].Buffer, ReadOnlySpan<AllocationCallbacks>.Empty);
                    }
                });
                threads[threadIndex].Start();
            }

            foreach (var thread in threads)
            {
                thread.Join();
            }

            if (pool != null)
            {
                pool.Dispose();
            }
        }
    }

    [Fact]
    public void TestMappingHysteresis()
    {
        // Mirrors C++: no observable assertion on hysteresis behaviour itself, only "must not crash/assert".
        // Scenario 5 (issue #407) additionally checks that mapping non-HOST_VISIBLE memory reliably fails.
        using var context = new VulkanContext();

        const int bufCount = 30;

        var bufCreateInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Usage = BufferUsageFlags.TransferSrcBit,
            Size = 0x10000,
        };

        var templateAllocInfo = new AllocationCreateInfo
        {
            Usage = MemoryUsage.Auto,
            Flags = AllocationCreateFlags.HostAccessSequentialWrite,
        };
        context.Allocator.FindMemoryTypeIndexForBufferInfo(in bufCreateInfo, in templateAllocInfo, out var memType);
        Assert.True(memType.HasValue);

        var poolCreateInfo = new AllocationPoolCreateInfo
        {
            MemoryTypeIndex = memType.Value,
            BlockSize = 10 * 1024 * 1024,
            MinBlockCount = 1,
            MaxBlockCount = 1,
        };

        for (var scenarioIndex = 0; scenarioIndex < 5; scenarioIndex++)
        {
            using var pool = context.Allocator.CreatePool(poolCreateInfo);
            var bufs = new List<(Silk.NET.Vulkan.Buffer Buffer, Allocation Alloc)>();

            try
            {
                if (scenarioIndex == 0)
                {
                    var info = new AllocationCreateInfo { Pool = pool };
                    for (var i = 0; i < bufCount; i++)
                    {
                        context.Allocator.CreateBuffer(in bufCreateInfo, in info, out var buffer, out var alloc);
                        Assert.NotNull(alloc);
                        Assert.Equal(0, (nint)alloc.MappedData);
                        alloc.Dispose();
                        context.Vk.DestroyBuffer(context.Device, buffer, ReadOnlySpan<AllocationCallbacks>.Empty);
                    }
                }
                else if (scenarioIndex == 1)
                {
                    var mappedInfo = new AllocationCreateInfo { Pool = pool, Flags = AllocationCreateFlags.Mapped };
                    context.Allocator.CreateBuffer(in bufCreateInfo, in mappedInfo, out var anchor, out var anchorAlloc);
                    Assert.NotNull(anchorAlloc);
                    bufs.Add((anchor, anchorAlloc));
                    Assert.NotEqual(0, (nint)anchorAlloc.MappedData);

                    for (var i = 0; i < bufCount; i++)
                    {
                        context.Allocator.CreateBuffer(in bufCreateInfo, in mappedInfo, out var buffer, out var alloc);
                        Assert.NotNull(alloc);
                        Assert.NotEqual(0, (nint)alloc.MappedData);
                        alloc.Dispose();
                        context.Vk.DestroyBuffer(context.Device, buffer, ReadOnlySpan<AllocationCallbacks>.Empty);
                    }
                }
                else if (scenarioIndex == 2)
                {
                    var mappedInfo = new AllocationCreateInfo { Pool = pool, Flags = AllocationCreateFlags.Mapped };
                    for (var i = 0; i < bufCount; i++)
                    {
                        context.Allocator.CreateBuffer(in bufCreateInfo, in mappedInfo, out var buffer, out var alloc);
                        Assert.NotNull(alloc);
                        Assert.NotEqual(0, (nint)alloc.MappedData);
                        alloc.Dispose();
                        context.Vk.DestroyBuffer(context.Device, buffer, ReadOnlySpan<AllocationCallbacks>.Empty);
                    }
                }
                else if (scenarioIndex == 3)
                {
                    var info = new AllocationCreateInfo { Pool = pool };
                    context.Allocator.CreateBuffer(in bufCreateInfo, in info, out var buffer, out var alloc);
                    Assert.NotNull(alloc);
                    bufs.Add((buffer, alloc));
                    for (var i = 0; i < bufCount; i++)
                    {
                        alloc.Map(out var ptr);
                        Assert.NotEqual(0, (nint)ptr);
                        alloc.Unmap();
                    }
                }
                else if (scenarioIndex == 4)
                {
                    var info = new AllocationCreateInfo { Pool = pool };
                    for (var i = 0; i < bufCount; i++)
                    {
                        context.Allocator.CreateBuffer(in bufCreateInfo, in info, out var buffer, out var alloc);
                        Assert.NotNull(alloc);
                        Assert.Equal(0, (nint)alloc.MappedData);
                        bufs.Add((buffer, alloc));
                    }

                    for (var i = 0; i < bufCount; i++)
                    {
                        bufs[^1].Alloc.Map(out var ptr);
                        Assert.NotEqual(0, (nint)ptr);
                        bufs[^1].Alloc.Unmap();
                    }
                }
            }
            finally
            {
                foreach (var (buffer, alloc) in bufs)
                {
                    alloc.Dispose();
                    context.Vk.DestroyBuffer(context.Device, buffer, ReadOnlySpan<AllocationCallbacks>.Empty);
                }
            }
        }

        // Issue #407: mapping must keep failing on non-HOST_VISIBLE memory.
        // (C++ tests the inverted condition; we follow the comment's stated intent: only a non-HOST_VISIBLE
        // allocation can fail to map, which is the behaviour that actually exercises the failure path.)
        var finalBufInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = 1 * 1024 * 1024,
            Usage = BufferUsageFlags.VertexBufferBit,
        };
        var finalAllocInfo = new AllocationCreateInfo { Usage = MemoryUsage.Auto };
        context.Allocator.CreateBuffer(in finalBufInfo, in finalAllocInfo, out var finalBuffer, out var finalAlloc);
        Assert.NotNull(finalAlloc);
        try
        {
            var props = context.Allocator.GetMemoryTypeProperties(finalAlloc.MemoryTypeIndex);
            if ((props & MemoryPropertyFlags.HostVisibleBit) == 0)
            {
                for (var i = 0; i < 10; i++)
                {
                    // Map now returns a Result instead of throwing MapMemoryException (mirrors C++ vmaMapMemory
                    // returning VkResult); mapping non-HOST_VISIBLE memory must fail with a non-success code.
                    var mapRes = finalAlloc.Map(out _);
                    Assert.NotEqual(Result.Success, mapRes);
                }
            }
        }
        finally
        {
            finalAlloc.Dispose();
            context.Vk.DestroyBuffer(context.Device, finalBuffer, ReadOnlySpan<AllocationCallbacks>.Empty);
        }
    }

    private static StatInfo SumStatInfo(IReadOnlyList<StatInfo> arr, int count)
    {
        var sum = new StatInfo { AllocationSizeMin = long.MaxValue, UnusedRangeSizeMin = long.MaxValue };
        for (var i = 0; i < count; i++)
        {
            var s = arr[i];
            sum.BlockCount += s.BlockCount;
            sum.AllocationCount += s.AllocationCount;
            sum.UnusedRangeCount += s.UnusedRangeCount;
            sum.UsedBytes += s.UsedBytes;
            sum.UnusedBytes += s.UnusedBytes;
            sum.AllocationSizeMin = Math.Min(sum.AllocationSizeMin, s.AllocationSizeMin);
            sum.AllocationSizeMax = Math.Max(sum.AllocationSizeMax, s.AllocationSizeMax);
            sum.UnusedRangeSizeMin = Math.Min(sum.UnusedRangeSizeMin, s.UnusedRangeSizeMin);
            sum.UnusedRangeSizeMax = Math.Max(sum.UnusedRangeSizeMax, s.UnusedRangeSizeMax);
        }
        return sum;
    }

    private static void AssertStatInfoEqual(in StatInfo sum, in StatInfo total)
    {
        Assert.Equal(sum.BlockCount, total.BlockCount);
        Assert.Equal(sum.AllocationCount, total.AllocationCount);
        Assert.Equal(sum.UnusedRangeCount, total.UnusedRangeCount);
        Assert.Equal(sum.UsedBytes, total.UsedBytes);
        Assert.Equal(sum.UnusedBytes, total.UnusedBytes);
        Assert.Equal(sum.AllocationSizeMin, total.AllocationSizeMin);
        Assert.Equal(sum.AllocationSizeMax, total.AllocationSizeMax);
        Assert.Equal(sum.UnusedRangeSizeMin, total.UnusedRangeSizeMin);
        Assert.Equal(sum.UnusedRangeSizeMax, total.UnusedRangeSizeMax);
    }

    private static void AssertStatInfoBudgetEqual(in StatInfo s, in AllocationBudget b)
    {
        Assert.Equal(s.UsedBytes, b.AllocationBytes);
        Assert.Equal(s.AllocationCount, b.AllocationCount);
        Assert.Equal(s.BlockCount, b.BlockCount);
    }

    private static void AssertTotalMatchesSum(Stats stats, int heapCount, int typeCount)
    {
        AssertStatInfoEqual(SumStatInfo(stats.MemoryHeap, heapCount), stats.Total);
        AssertStatInfoEqual(SumStatInfo(stats.MemoryType, typeCount), stats.Total);
    }

    private static bool RegionsOverlap(IntPtr a, long aLen, IntPtr b, long bLen)
        => a < b + bLen && b < a + aLen;
}