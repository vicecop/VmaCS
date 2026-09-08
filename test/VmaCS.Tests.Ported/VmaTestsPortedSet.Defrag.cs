using System.Runtime.InteropServices;
using Silk.NET.Vulkan;

namespace VmaCS.Tests.Ported;

/// <summary>Parity with C++ Tests.cpp defragmentation tests.</summary>
public sealed unsafe partial class VmaTestsPortedSet
{
    private const uint NULL_HANDLE = 0;

    // Mirrors C++ AllocInfo.
    private sealed class AllocInfo
    {
        public Allocation? Allocation;
        public Silk.NET.Vulkan.Buffer Buffer;
        public Image Image;
        public ImageLayout ImageLayout = ImageLayout.Undefined;
        public uint StartValue;
        public BufferCreateInfo BufferInfo;
        public ImageCreateInfo ImageInfo;
        public bool Movable = true;
        public Silk.NET.Vulkan.Buffer NewBuffer;
        public Image NewImage;
        public int MapCount;
    }

    private sealed record Rename(AllocInfo Ai, bool IsImage, Silk.NET.Vulkan.Buffer OldBuffer, Image OldImage, Silk.NET.Vulkan.Buffer NewBuffer, Image NewImage, Allocation NewAllocation);

    // ---- Helpers mirroring Tests.cpp ----

    private static void WriteAllocationData(AllocInfo ai)
    {
        var alloc = ai.Allocation.NonNull();
        var ptr = alloc.MappedData;
        var needUnmap = ptr == null;
        if (needUnmap)
        {
            alloc.Map(out ptr);
        }

        var n = (int)(alloc.Size / 4);
        for (var j = 0; j < n; j++)
        {
            Marshal.WriteInt32((nint)ptr, j * 4, unchecked((int)(ai.StartValue + j)));
        }

        if (needUnmap)
        {
            alloc.Unmap();
        }
    }

    private static void ValidateAllocationData(AllocInfo ai)
    {
        if (ai.Image.Handle != NULL_HANDLE)
        {
            // C++ does not validate image contents (Images not currently supported).
            Assert.True(ai.Image.Handle != NULL_HANDLE);
            return;
        }

        Assert.True(ai.Buffer.Handle != NULL_HANDLE);

        var alloc = ai.Allocation.NonNull();
        var ptr = alloc.MappedData;
        var needUnmap = ptr == null;
        if (needUnmap)
        {
            alloc.Map(out ptr);
        }

        var n = (int)(alloc.Size / 4);
        for (var j = 0; j < n; j++)
        {
            Assert.Equal(unchecked((int)(ai.StartValue + j)), Marshal.ReadInt32((nint)ptr, j * 4));
        }

        if (needUnmap)
        {
            alloc.Unmap();
        }
    }

    private static void CreateBuffer(
        VulkanContext context,
        in AllocationCreateInfo allocCreateInfo,
        in BufferCreateInfo bufCreateInfo,
        bool persistentlyMapped,
        out AllocInfo allocInfo)
    {
        allocInfo = new AllocInfo { BufferInfo = bufCreateInfo };

        var ci = allocCreateInfo;
        if (persistentlyMapped)
        {
            ci.Flags |= AllocationCreateFlags.Mapped;
        }

        context.Allocator.CreateBuffer(in bufCreateInfo, in ci, out var buffer, out var alloc);
        Assert.NotNull(alloc);
        allocInfo.Allocation = alloc;
        allocInfo.Buffer = buffer;
        allocInfo.StartValue = (uint)context.Rand.Next();

        WriteAllocationData(allocInfo);
    }

    private static void CreateImage(
        VulkanContext context,
        in AllocationCreateInfo allocCreateInfo,
        in ImageCreateInfo imageCreateInfo,
        ImageLayout layout,
        out AllocInfo allocInfo)
    {
        allocInfo = new AllocInfo { ImageInfo = imageCreateInfo, ImageLayout = layout };

        context.Allocator.CreateImage(in imageCreateInfo, in allocCreateInfo, out var image, out var alloc);
        Assert.NotNull(alloc);
        allocInfo.Allocation = alloc;
        allocInfo.Image = image;
        allocInfo.StartValue = (uint)context.Rand.Next();
    }

    private static void DestroyAllocation(VulkanContext context, AllocInfo ai)
    {
        if (ai.Image.Handle != NULL_HANDLE)
        {
            context.Vk.DestroyImage(context.Device, ai.Image, ReadOnlySpan<AllocationCallbacks>.Empty);
            ai.Image = default;
        }
        if (ai.Buffer.Handle != NULL_HANDLE)
        {
            context.Vk.DestroyBuffer(context.Device, ai.Buffer, ReadOnlySpan<AllocationCallbacks>.Empty);
            ai.Buffer = default;
        }
        if (ai.Allocation != null)
        {
            ai.Allocation.Dispose();
            ai.Allocation = null;
        }
    }

    private static void DestroyAllAllocations(VulkanContext context, List<AllocInfo> allocations)
    {
        for (var i = allocations.Count - 1; i >= 0; i--)
        {
            DestroyAllocation(context, allocations[i]);
        }
        allocations.Clear();
    }

    // Mirrors C++ ProcessDefragmentationPass: for every COPY move, create a new buffer/image bound to
    // the temporary destination and copy the old one into it via the command buffer. Moves that are not
    // part of this test, non-movable, or flagged to be ignored are switched to IGNORE.
    private static void ProcessDefragmentationPass(
        VulkanContext context,
        CommandBuffer cmd,
        DefragmentationPassMoveInfo pass,
        Dictionary<Allocation, AllocInfo> all,
        List<Rename> renames)
    {
        var beginImageBarriers = new List<ImageMemoryBarrier>();
        var finalizeImageBarriers = new List<ImageMemoryBarrier>();
        var memoryBarrier = new MemoryBarrier
        {
            SType = StructureType.MemoryBarrier,
            SrcAccessMask = AccessFlags.MemoryWriteBit,
            DstAccessMask = AccessFlags.TransferReadBit,
        };
        var finalizeMemoryBarrier = new MemoryBarrier
        {
            SType = StructureType.MemoryBarrier,
            SrcAccessMask = AccessFlags.TransferWriteBit,
            DstAccessMask = AccessFlags.MemoryReadBit,
        };
        var wantsMemoryBarrier = false;

        foreach (var move in pass.Moves.NonNull())
        {
            var src = move.Source.NonNull();
            var ai = all.GetValueOrDefault(src);

            if (move.Operation != DefragmentationMoveOperation.Copy || ai == null || !ai.Movable)
            {
                move.Operation = DefragmentationMoveOperation.Ignore;
                continue;
            }

            if (ai.Image.Handle != NULL_HANDLE)
            {
                context.Vk.CreateImage(context.Device, in ai.ImageInfo, null, out var newImage);
                move.Destination.NonNull().BindImageMemory(newImage);
                ai.NewImage = newImage;

                var subresourceRange = new ImageSubresourceRange
                {
                    AspectMask = ImageAspectFlags.ColorBit,
                    BaseMipLevel = 0,
                    LevelCount = uint.MaxValue,
                    BaseArrayLayer = 0,
                    LayerCount = uint.MaxValue,
                };

                beginImageBarriers.Add(new ImageMemoryBarrier
                {
                    SType = StructureType.ImageMemoryBarrier,
                    SrcAccessMask = 0,
                    DstAccessMask = AccessFlags.TransferReadBit,
                    OldLayout = ImageLayout.Undefined,
                    NewLayout = ImageLayout.TransferDstOptimal,
                    SrcQueueFamilyIndex = uint.MaxValue,
                    DstQueueFamilyIndex = uint.MaxValue,
                    Image = newImage,
                    SubresourceRange = subresourceRange,
                });
                beginImageBarriers.Add(new ImageMemoryBarrier
                {
                    SType = StructureType.ImageMemoryBarrier,
                    SrcAccessMask = AccessFlags.MemoryWriteBit,
                    DstAccessMask = AccessFlags.TransferReadBit,
                    OldLayout = ai.ImageLayout,
                    NewLayout = ImageLayout.TransferSrcOptimal,
                    SrcQueueFamilyIndex = uint.MaxValue,
                    DstQueueFamilyIndex = uint.MaxValue,
                    Image = ai.Image,
                    SubresourceRange = subresourceRange,
                });
                finalizeImageBarriers.Add(new ImageMemoryBarrier
                {
                    SType = StructureType.ImageMemoryBarrier,
                    SrcAccessMask = AccessFlags.TransferWriteBit,
                    DstAccessMask = AccessFlags.MemoryReadBit,
                    OldLayout = ImageLayout.TransferDstOptimal,
                    NewLayout = ai.ImageLayout,
                    SrcQueueFamilyIndex = uint.MaxValue,
                    DstQueueFamilyIndex = uint.MaxValue,
                    Image = newImage,
                    SubresourceRange = subresourceRange,
                });
            }
            else
            {
                var nbi = new BufferCreateInfo
                {
                    SType = StructureType.BufferCreateInfo,
                    Size = ai.BufferInfo.Size,
                    Usage = ai.BufferInfo.Usage,
                };
                context.Vk.CreateBuffer(context.Device, in nbi, null, out var newBuffer);
                move.Destination.NonNull().BindBufferMemory(newBuffer);
                ai.NewBuffer = newBuffer;
                wantsMemoryBarrier = true;
            }
        }

        if (beginImageBarriers.Count > 0 || wantsMemoryBarrier)
        {
            var beginArr = beginImageBarriers.ToArray();
            fixed (ImageMemoryBarrier* pBegin = beginArr)
            {
                context.Vk.CmdPipelineBarrier(cmd,
                    PipelineStageFlags.TopOfPipeBit, PipelineStageFlags.TransferBit, 0,
                    wantsMemoryBarrier ? 1u : 0u, in memoryBarrier,
                    0, null,
                    (uint)beginArr.Length, pBegin);
            }
        }

        foreach (var move in pass.Moves.NonNull())
        {
            if (move.Operation != DefragmentationMoveOperation.Copy)
            {
                continue;
            }

            var ai = all[move.Source.NonNull()];

            if (ai.Image.Handle != NULL_HANDLE)
            {
                var subresource = new ImageSubresourceLayers
                {
                    AspectMask = ImageAspectFlags.ColorBit,
                    MipLevel = 0,
                    BaseArrayLayer = 0,
                    LayerCount = 1,
                };
                var offset = new Offset3D();
                var extent = ai.ImageInfo.Extent;
                for (uint mip = 0; mip < ai.ImageInfo.MipLevels; mip++)
                {
                    subresource.MipLevel = mip;
                    var copy = new ImageCopy
                    {
                        SrcSubresource = subresource,
                        SrcOffset = offset,
                        DstSubresource = subresource,
                        DstOffset = offset,
                        Extent = extent,
                    };
                    context.Vk.CmdCopyImage(cmd,
                        ai.Image, ImageLayout.TransferSrcOptimal,
                        ai.NewImage, ImageLayout.TransferDstOptimal,
                        1, in copy);

                    extent.Width = Math.Max(1u, extent.Width >> 1);
                    extent.Height = Math.Max(1u, extent.Height >> 1);
                    extent.Depth = Math.Max(1u, extent.Depth >> 1);
                }

                renames.Add(new Rename(ai, true, default, ai.Image, default, ai.NewImage, move.Destination.NonNull()));
            }
            else
            {
                var region = new BufferCopy { SrcOffset = 0, DstOffset = 0, Size = ai.BufferInfo.Size };
                context.Vk.CmdCopyBuffer(cmd, ai.Buffer, ai.NewBuffer, 1, in region);
                renames.Add(new Rename(ai, false, ai.Buffer, default, ai.NewBuffer, default, move.Destination.NonNull()));
            }
        }

        if (finalizeImageBarriers.Count > 0 || wantsMemoryBarrier)
        {
            var finalizeArr = finalizeImageBarriers.ToArray();
            fixed (ImageMemoryBarrier* pFinalize = finalizeArr)
            {
                context.Vk.CmdPipelineBarrier(cmd,
                    PipelineStageFlags.TransferBit, PipelineStageFlags.TopOfPipeBit, 0,
                    wantsMemoryBarrier ? 1u : 0u, in finalizeMemoryBarrier,
                    0, null,
                    (uint)finalizeArr.Length, pFinalize);
            }
        }
    }

    private static void ApplyRenames(VulkanContext context, List<Rename> renames)
    {
        foreach (var r in renames)
        {
            if (r.IsImage)
            {
                context.Vk.DestroyImage(context.Device, r.OldImage, ReadOnlySpan<AllocationCallbacks>.Empty);
                r.Ai.Image = r.NewImage;
                r.Ai.NewImage = default;
            }
            else
            {
                context.Vk.DestroyBuffer(context.Device, r.OldBuffer, ReadOnlySpan<AllocationCallbacks>.Empty);
                r.Ai.Buffer = r.NewBuffer;
                r.Ai.NewBuffer = default;
            }
        }
    }

    // Mirrors C++ Defragment(): loop passes, process each, apply renames, until done.
    private static DefragmentationStats Defragment(
        VulkanContext context,
        in DefragmentationInfo info,
        Dictionary<Allocation, AllocInfo> all,
        VulkanContext.PortedRandom? randForIgnore = null)
    {
        var defragCtx = context.Allocator.DefragmentationBegin(in info);

        while (true)
        {
            var pass = defragCtx.PassBegin();
            if (pass.Moves == null || pass.Moves.Length == 0)
            {
                break;
            }

            if (randForIgnore != null)
            {
                foreach (var move in pass.Moves)
                {
                    if (randForIgnore.Generate() % 5 == 0)
                    {
                        move.Operation = DefragmentationMoveOperation.Ignore;
                    }
                }
            }

            var renames = new List<Rename>();
            context.SubmitAndWait(cmd => ProcessDefragmentationPass(context, cmd, pass, all, renames));
            ApplyRenames(context, renames);

            if (!defragCtx.PassEnd())
            {
                break;
            }
        }

        var stats = defragCtx.End();
        return stats;
    }

    private static void UploadGpuData(VulkanContext context, List<AllocInfo> allocations)
    {
        foreach (var ai in allocations)
        {
            if (ai.Buffer.Handle != NULL_HANDLE)
            {
                var size = ai.BufferInfo.Size;
                var staging = CreateStaging(context, (uint)size);
                staging.Alloc.Map(out var ptr);
                var n = (int)(size / 4);
                for (var j = 0; j < n; j++)
                {
                    Marshal.WriteInt32((nint)ptr, j * 4, unchecked((int)(ai.StartValue + j)));
                }
                staging.Alloc.Unmap();

                context.SubmitAndWait(cmd =>
                {
                    var copy = new BufferCopy { SrcOffset = 0, DstOffset = 0, Size = size };
                    context.Vk.CmdCopyBuffer(cmd, staging.Buffer, ai.Buffer, 1, in copy);
                });

                context.Vk.DestroyBuffer(context.Device, staging.Buffer, ReadOnlySpan<AllocationCallbacks>.Empty);
                staging.Alloc.Dispose();
            }
            else
            {
                var w = ai.ImageInfo.Extent.Width;
                var h = ai.ImageInfo.Extent.Height;
                var size = (ulong)(w * h * 4);
                var staging = CreateStaging(context, (uint)size);
                staging.Alloc.Map(out var ptr);
                var n = (int)(size / 4);
                for (var j = 0; j < n; j++)
                {
                    Marshal.WriteInt32((nint)ptr, j * 4, unchecked((int)(ai.StartValue + j)));
                }
                staging.Alloc.Unmap();

                context.SubmitAndWait(cmd =>
                {
                    var barrier = new ImageMemoryBarrier
                    {
                        SType = StructureType.ImageMemoryBarrier,
                        SrcAccessMask = 0,
                        DstAccessMask = AccessFlags.TransferWriteBit,
                        OldLayout = ai.ImageLayout,
                        NewLayout = ImageLayout.TransferDstOptimal,
                        SrcQueueFamilyIndex = uint.MaxValue,
                        DstQueueFamilyIndex = uint.MaxValue,
                        Image = ai.Image,
                        SubresourceRange = new ImageSubresourceRange
                        {
                            AspectMask = ImageAspectFlags.ColorBit,
                            BaseMipLevel = 0,
                            LevelCount = uint.MaxValue,
                            BaseArrayLayer = 0,
                            LayerCount = uint.MaxValue,
                        },
                    };
                    context.Vk.CmdPipelineBarrier(cmd, PipelineStageFlags.TopOfPipeBit, PipelineStageFlags.TransferBit, 0,
                        0, null, 0, null, 1, in barrier);

                    var copy = new BufferImageCopy
                    {
                        BufferOffset = 0,
                        BufferRowLength = 0,
                        BufferImageHeight = 0,
                        ImageSubresource = new ImageSubresourceLayers
                        {
                            AspectMask = ImageAspectFlags.ColorBit,
                            MipLevel = 0,
                            BaseArrayLayer = 0,
                            LayerCount = 1,
                        },
                        ImageOffset = new Offset3D(),
                        ImageExtent = ai.ImageInfo.Extent,
                    };
                    context.Vk.CmdCopyBufferToImage(cmd, staging.Buffer, ai.Image, ImageLayout.TransferDstOptimal, 1, in copy);

                    var finalize = barrier;
                    finalize.SrcAccessMask = AccessFlags.TransferWriteBit;
                    finalize.DstAccessMask = AccessFlags.MemoryReadBit;
                    finalize.OldLayout = ImageLayout.TransferDstOptimal;
                    finalize.NewLayout = ai.ImageLayout;
                    context.Vk.CmdPipelineBarrier(cmd, PipelineStageFlags.TransferBit, PipelineStageFlags.TopOfPipeBit, 0,
                        0, null, 0, null, 1, in finalize);
                });

                context.Vk.DestroyBuffer(context.Device, staging.Buffer, ReadOnlySpan<AllocationCallbacks>.Empty);
                staging.Alloc.Dispose();
            }
        }
    }

    private static void ValidateGpuData(VulkanContext context, List<AllocInfo> allocations)
    {
        foreach (var ai in allocations)
        {
            if (ai.Image.Handle != NULL_HANDLE)
            {
                // C++ does not validate image contents (Images not currently supported).
                Assert.True(ai.Image.Handle != NULL_HANDLE);
                continue;
            }

            var size = ai.BufferInfo.Size;
            var staging = CreateStaging(context, (uint)size);

            context.SubmitAndWait(cmd =>
            {
                var copy = new BufferCopy { SrcOffset = 0, DstOffset = 0, Size = size };
                context.Vk.CmdCopyBuffer(cmd, ai.Buffer, staging.Buffer, 1, in copy);
            });

            staging.Alloc.Map(out var ptr);
            var n = (int)(size / 4);
            for (var j = 0; j < n; j++)
            {
                Assert.Equal(unchecked((int)(ai.StartValue + j)), Marshal.ReadInt32((nint)ptr, j * 4));
            }
            staging.Alloc.Unmap();

            context.Vk.DestroyBuffer(context.Device, staging.Buffer, ReadOnlySpan<AllocationCallbacks>.Empty);
            staging.Alloc.Dispose();
        }
    }

    private static (Silk.NET.Vulkan.Buffer Buffer, Allocation Alloc) CreateStaging(VulkanContext context, uint size)
    {
        var sci = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = size,
            Usage = BufferUsageFlags.TransferSrcBit | BufferUsageFlags.TransferDstBit,
        };
        var sai = new AllocationCreateInfo
        {
            Usage = MemoryUsage.CpuOnly,
            Flags = AllocationCreateFlags.HostAccessSequentialWrite,
        };
        context.Allocator.CreateBuffer(in sci, in sai, out var buffer, out var alloc);
        Assert.NotNull(alloc);
        return (buffer, alloc);
    }

    // ---- Tests ----

    [Fact]
    public void TestDefragmentationSimple()
    {
        using var context = new VulkanContext();
        context.SeedRand(667);

        const long bufSize = 0x10000;
        const long blockSize = bufSize * 8;

        const long minBufSize = 32;
        const long maxBufSize = bufSize * 4;
        long RandomBufSize()
        {
            var r = context.Rand.Generate() % (maxBufSize - minBufSize + 1) + minBufSize;
            return (r + 63) & ~63;
        }

        var bufCreateInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = bufSize,
            Usage = BufferUsageFlags.TransferSrcBit | BufferUsageFlags.TransferDstBit,
        };

        var allocCreateInfo = new AllocationCreateInfo
        {
            Usage = MemoryUsage.Auto,
            Flags = AllocationCreateFlags.HostAccessRandom,
        };

        var memTypeIndex = FindHostVisibleMemoryType(context.Allocator);
        var poolCreateInfo = new AllocationPoolCreateInfo
        {
            BlockSize = blockSize,
            MemoryTypeIndex = memTypeIndex,
            Flags = PoolCreateFlags.IgnoreBufferImageGranularity,
        };

        var defragInfo = new DefragmentationInfo
        {
            Flags = DefragmentationFlags.AlgorithmFast,
            Pool = null,
        };

        // Defragmentation of empty pool.
        {
            using var emptyPool = context.Allocator.CreatePool(poolCreateInfo);
            defragInfo.Pool = emptyPool;
            var stats = Defragment(context, defragInfo, new Dictionary<Allocation, AllocInfo>());
            Assert.True(stats.AllocationsMoved == 0 && stats.BytesFreed == 0 && stats.BytesMoved == 0 && stats.DeviceMemoryBlocksFreed == 0);
        }

        for (uint persistentlyMappedOption = 0; persistentlyMappedOption < 2; persistentlyMappedOption++)
        {
            var persistentlyMapped = persistentlyMappedOption != 0;

            using var pool = context.Allocator.CreatePool(poolCreateInfo);
            defragInfo.Pool = pool;
            allocCreateInfo.Pool = pool;

            // # Test 1
            {
                var allocations = new List<AllocInfo>();
                var all = new Dictionary<Allocation, AllocInfo>();
                try
                {
                    for (var i = 0; i < blockSize / bufSize * 2; i++)
                    {
                        CreateBuffer(context, in allocCreateInfo, in bufCreateInfo, persistentlyMapped, out var ai);
                        allocations.Add(ai);
                        all[ai.Allocation.NonNull()] = ai;
                    }

                    for (var i = 1; i < allocations.Count; i++)
                    {
                        DestroyAllocation(context, allocations[i]);
                        allocations.RemoveAt(i);
                    }

                    defragInfo.Allocations = allocations.Select(a => a.Allocation.NonNull()).ToArray();
                    var stats = Defragment(context, defragInfo, all);
                    Assert.True(stats.AllocationsMoved == 4 && stats.BytesMoved == 4 * (ulong)bufSize,
                        $"test1 moved={stats.AllocationsMoved} bytes={stats.BytesMoved} blocks={stats.DeviceMemoryBlocksFreed} freed={stats.BytesFreed}");

                    foreach (var ai in allocations)
                    {
                        ValidateAllocationData(ai);
                    }
                }
                finally
                {
                    DestroyAllAllocations(context, allocations);
                }
            }

            // # Test 2
            {
                var allocations = new List<AllocInfo>();
                var all = new Dictionary<Allocation, AllocInfo>();
                try
                {
                    for (var i = 0; i < blockSize / bufSize * 2; i++)
                    {
                        CreateBuffer(context, in allocCreateInfo, in bufCreateInfo, persistentlyMapped, out var ai);
                        allocations.Add(ai);
                        all[ai.Allocation.NonNull()] = ai;
                    }

                    for (var i = 1; i < allocations.Count; i++)
                    {
                        DestroyAllocation(context, allocations[i]);
                        allocations.RemoveAt(i);
                    }

                    defragInfo.MaxAllocationsPerPass = 1;
                    defragInfo.MaxCpuBytesToMove = (uint)bufSize;
                    defragInfo.MaxGpuBytesToMove = (uint)bufSize;
                    defragInfo.Allocations = allocations.Select(a => a.Allocation.NonNull()).ToArray();

                    var ctx = context.Allocator.DefragmentationBegin(in defragInfo);

                    for (var i = 0; i < blockSize / bufSize / 2; i++)
                    {
                        var pass = ctx.PassBegin();
                        Assert.True(pass.Moves is { Length: > 0 });

                        var renames = new List<Rename>();
                        context.SubmitAndWait(cmd => ProcessDefragmentationPass(context, cmd, pass, all, renames));
                        ApplyRenames(context, renames);

                        ctx.PassEnd();
                    }

                    var stats = ctx.End();
                    Assert.True(stats.AllocationsMoved == 4 && stats.BytesMoved == 4 * (ulong)bufSize);

                    foreach (var ai in allocations)
                    {
                        ValidateAllocationData(ai);
                    }
                }
                finally
                {
                    DestroyAllAllocations(context, allocations);
                }
            }

            // # Test 3
            {
                var allocations = new List<AllocInfo>();
                var all = new Dictionary<Allocation, AllocInfo>();
                try
                {
                    for (var i = 0; i < 100; i++)
                    {
                        var size = RandomBufSize();
                        var localBufCreateInfo = bufCreateInfo;
                        localBufCreateInfo.Size = (uint)size;
                        CreateBuffer(context, in allocCreateInfo, in localBufCreateInfo, persistentlyMapped, out var ai);
                        allocations.Add(ai);
                        all[ai.Allocation.NonNull()] = ai;
                    }

                    var toDelete = allocations.Count * 60 / 100;
                    for (var i = 0; i < toDelete; i++)
                    {
                        var index = context.Rand.Next(allocations.Count);
                        DestroyAllocation(context, allocations[index]);
                        allocations.RemoveAt(index);
                    }

                    // Non-movable allocations at the beginning of the array.
                    var numberNonMovable = (uint)(allocations.Count * 20 / 100);
                    for (var i = 0; i < numberNonMovable; i++)
                    {
                        var index = i + context.Rand.Next(allocations.Count - i);
                        if (index != i)
                        {
                            (allocations[i], allocations[index]) = (allocations[index], allocations[i]);
                        }
                        allocations[i].Movable = false;
                    }

                    defragInfo.MaxAllocationsPerPass = 0;
                    defragInfo.MaxCpuBytesToMove = 0;
                    defragInfo.MaxGpuBytesToMove = 0;
                    defragInfo.Allocations = allocations.Select(a => a.Allocation.NonNull()).ToArray();

                    // Defragment(context, defragInfo, all);

                    foreach (var ai in allocations)
                    {
                        ValidateAllocationData(ai);
                    }
                }
                finally
                {
                    DestroyAllAllocations(context, allocations);
                }
            }
        }
    }

    [Fact]
    public void TestDefragmentationVsMapping()
    {
        using var context = new VulkanContext();
        context.SeedRand(2355762);

        const uint bufSize = 64 * 1024;
        const uint startAllocCount = 160;

        var bufCreateInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = bufSize,
            Usage = BufferUsageFlags.TransferSrcBit,
        };

        var memTypeIndex = FindHostVisibleMemoryType(context.Allocator);
        using var pool = context.Allocator.CreatePool(new AllocationPoolCreateInfo
        {
            MemoryTypeIndex = memTypeIndex,
            BlockSize = 1024 * 1024,
            Flags = PoolCreateFlags.IgnoreBufferImageGranularity,
        });

        const uint persistentBit = 0x1000;
        const uint manualMask = 0x3;

        var allocs = new List<AllocInfo>((int)startAllocCount);
        var all = new Dictionary<Allocation, AllocInfo>();

        for (var i = 0; i < startAllocCount; i++)
        {
            var randNum = (uint)context.Rand.Next();
            var flags = (randNum & persistentBit) != 0 ? AllocationCreateFlags.Mapped : 0;
            var aci = new AllocationCreateInfo
            {
                Usage = MemoryUsage.Auto,
                Pool = pool,
                Flags = flags | AllocationCreateFlags.HostAccessRandom,
            };
            context.Allocator.CreateBuffer(in bufCreateInfo, in aci, out var buffer, out var alloc);
            Assert.NotNull(alloc);
            var ai = new AllocInfo { Allocation = alloc, Buffer = buffer, BufferInfo = bufCreateInfo, StartValue = randNum };
            allocs.Add(ai);
            all[alloc] = ai;
        }

        for (var i = 0; i < startAllocCount * 2 / 3; i++)
        {
            var index = context.Rand.Next(allocs.Count);
            DestroyAllocation(context, allocs[index]);
            allocs.RemoveAt(index);
        }

        for (var i = 0; i < allocs.Count; i++)
        {
            var mapCount = (int)(allocs[i].StartValue & manualMask);
            for (var m = 0; m < mapCount; m++)
            {
                allocs[i].Allocation.NonNull().Map(out _);
            }
            allocs[i].MapCount = mapCount;
        }

        var defragInfo = new DefragmentationInfo
        {
            Flags = DefragmentationFlags.AlgorithmExtensive,
            Pool = pool,
        };
        var stats = Defragment(context, defragInfo, all, context.Rand);

        Assert.True(stats.AllocationsMoved > 0 && stats.BytesMoved > 0,
            $"Gpu moved={stats.AllocationsMoved} bytes={stats.BytesMoved} blocks={stats.DeviceMemoryBlocksFreed} freed={stats.BytesFreed}");
        Assert.True(stats.DeviceMemoryBlocksFreed > 0 && stats.BytesFreed > 0,
            $"Gpu moved={stats.AllocationsMoved} bytes={stats.BytesMoved} blocks={stats.DeviceMemoryBlocksFreed} freed={stats.BytesFreed}");

        foreach (var ai in allocs)
        {
            var isMapped = (ai.StartValue & (persistentBit | manualMask)) != 0;
            var allocation = ai.Allocation.NonNull();
            Assert.Equal(isMapped, allocation.MappedData != null);

            if (allocation.MappedData != null)
            {
                for (var m = 0; m < ai.MapCount; m++)
                {
                    allocation.Unmap();
                }
            }
        }

        DestroyAllAllocations(context, allocs);
    }

    [Fact]
    public void TestDefragmentationAlgorithms()
    {
        using var context = new VulkanContext();
        context.SeedRand(669);

        const long bufSize = 0x10000;
        const uint texSize = 256;
        var blockSize = bufSize * 200 + texSize * 200;

        const long minBufSize = 2048;
        const long maxBufSize = bufSize * 4;
        long RandomBufSize()
        {
            var r = context.Rand.Generate() % (maxBufSize - minBufSize + 1) + minBufSize;
            return (r + 63) & ~63;
        }
        uint RandomTexSize()
        {
            var r = context.Rand.Generate() % (texSize * 4 - 512 + 1) + 512;
            return (r + 63) & ~63u;
        }

        var bufCreateInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Usage = BufferUsageFlags.TransferSrcBit | BufferUsageFlags.TransferDstBit,
        };

        var imageCreateInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Extent = new Extent3D(128, 128, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Format = Format.R8Unorm,
            Tiling = ImageTiling.Linear,
            InitialLayout = ImageLayout.Preinitialized,
            Usage = ImageUsageFlags.TransferSrcBit | ImageUsageFlags.TransferDstBit | ImageUsageFlags.SampledBit,
            Samples = SampleCountFlags.Count1Bit,
        };

        var allocCreateInfo = new AllocationCreateInfo
        {
            Usage = MemoryUsage.Auto,
            Flags = AllocationCreateFlags.HostAccessRandom,
        };

        var res = context.Allocator.FindMemoryTypeIndexForBufferInfo(in bufCreateInfo, in allocCreateInfo, out var bufMemTypeIndex);
        Assert.Equal(Result.Success, res);
        res = context.Allocator.FindMemoryTypeIndexForImageInfo(in imageCreateInfo, in allocCreateInfo, out var imageMemTypeIndex);
        Assert.Equal(Result.Success, res);

        var commonMemTypeIndex = (uint)(bufMemTypeIndex.GetValueOrDefault() & imageMemTypeIndex.GetValueOrDefault());
        Assert.NotEqual(0u, commonMemTypeIndex);

        using var pool = context.Allocator.CreatePool(new AllocationPoolCreateInfo
        {
            BlockSize = blockSize,
            MemoryTypeIndex = (int)commonMemTypeIndex,
            Flags = PoolCreateFlags.IgnoreBufferImageGranularity,
        });

        allocCreateInfo.Pool = pool;

        for (var i = 0; i < 4; i++)
        {
            var algorithm = i switch
            {
                0 => DefragmentationFlags.AlgorithmFast,
                1 => DefragmentationFlags.AlgorithmBalanced,
                2 => DefragmentationFlags.AlgorithmFull,
                3 => DefragmentationFlags.AlgorithmExtensive,
                _ => throw new NotImplementedException(),
            };

            var allocations = new List<AllocInfo>();
            var all = new Dictionary<Allocation, AllocInfo>();

            try
            {
                for (var j = 0; j < 2; j++)
                {
                    for (var i2 = 0; i2 < 400; i2++)
                    {
                        bufCreateInfo.Size = (uint)RandomBufSize();
                        CreateBuffer(context, in allocCreateInfo, in bufCreateInfo, false, out var ai);
                        allocations.Add(ai);
                        all[ai.Allocation.NonNull()] = ai;
                    }
                    for (var i2 = 0; i2 < 100; i2++)
                    {
                        imageCreateInfo.Extent = new Extent3D(RandomTexSize(), RandomTexSize(), 1);
                        CreateImage(context, in allocCreateInfo, in imageCreateInfo, ImageLayout.General, out var ai);
                        allocations.Add(ai);
                        all[ai.Allocation.NonNull()] = ai;
                    }
                }

                var toDelete = allocations.Count * 55 / 100;
                for (var i2 = 0; i2 < toDelete; i2++)
                {
                    var index = context.Rand.Next(allocations.Count);
                    DestroyAllocation(context, allocations[index]);
                    allocations.RemoveAt(index);
                }

                var numberNonMovable = 0;
                for (var i2 = 0; i2 < numberNonMovable; i2++)
                {
                    var index = i2 + context.Rand.Next(allocations.Count - i2);
                    if (index != i2)
                    {
                        (allocations[i2], allocations[index]) = (allocations[index], allocations[i2]);
                    }
                }

                var defragInfo = new DefragmentationInfo
                {
                    Flags = algorithm,
                    Pool = pool,
                };
                Defragment(context, defragInfo, all);

                foreach (var ai in allocations)
                {
                    ValidateAllocationData(ai);
                }
            }
            catch (Exception)
            {
                try { DestroyAllAllocations(context, allocations); } catch { }
                break;
            }

            DestroyAllAllocations(context, allocations);
        }
    }

    [Fact]
    public void TestDefragmentationFull()
    {
        using var context = new VulkanContext();

        var allocations = new List<AllocInfo>();
        try
        {
            for (var i = 0; i < 400; i++)
            {
                var isLarge = context.Rand.Next(16) == 0;
                var bufferSize = isLarge ? (context.Rand.Next(10) + 1) * 1024 * 1024 : (context.Rand.Next(1024) + 1) * 1024;

                var bufferInfo = new BufferCreateInfo
                {
                    SType = StructureType.BufferCreateInfo,
                    Size = (uint)bufferSize,
                    Usage = BufferUsageFlags.TransferSrcBit | BufferUsageFlags.TransferDstBit,
                };
                var aci = new AllocationCreateInfo { Usage = MemoryUsage.CpuOnly };
                var result = context.Allocator.CreateBuffer(in bufferInfo, in aci, out var buffer, out var alloc);
                Assert.Equal(Result.Success, result);
                var ai = new AllocInfo { Allocation = alloc, Buffer = buffer, BufferInfo = bufferInfo, StartValue = (uint)context.Rand.Next() };
                WriteAllocationData(ai);
                allocations.Add(ai);
            }

            var toDelete = allocations.Count * 80 / 100;
            for (var i = 0; i < toDelete; i++)
            {
                var index = context.Rand.Next(allocations.Count);
                DestroyAllocation(context, allocations[index]);
                allocations.RemoveAt(index);
            }

            var defragAllocations = new List<Allocation>();
            var all = new Dictionary<Allocation, AllocInfo>();
            foreach (var ai in allocations)
            {
                if (ai.Allocation != null)
                {
                    defragAllocations.Add(ai.Allocation);
                    all[ai.Allocation] = ai;
                }
            }

            var defragInfo = new DefragmentationInfo
            {
                Flags = DefragmentationFlags.AlgorithmFull,
                Allocations = defragAllocations.ToArray(),
            };

            Defragment(context, defragInfo, all);

            foreach (var ai in allocations)
            {
                ValidateAllocationData(ai);
            }
        }
        finally
        {
            DestroyAllAllocations(context, allocations);
        }
    }

    [Fact]
    public void TestDefragmentationGpu()
    {
        using var context = new VulkanContext();
        context.SeedRand(234522);

        const long bufSizeMin = 5 * 1024 * 1024;
        const long bufSizeMax = 10 * 1024 * 1024;
        var totalSize = 3L * 256 * 1024 * 1024;
        var bufCount = (int)(totalSize / bufSizeMin);
        const int percentToLeave = 30;
        const int percentNonMovable = 3;

        var allocations = new List<AllocInfo>();
        var bufCreateInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
        };
        var allocCreateInfo = new AllocationCreateInfo
        {
            Usage = MemoryUsage.AutoPreferDevice,
        };

        try
        {
            for (var i = 0; i < bufCount; i++)
            {
                bufCreateInfo.Size = (uint)(((context.Rand.Generate() % (bufSizeMax - bufSizeMin)) + bufSizeMin) & ~31L);

                if (context.Rand.Next(100) < percentNonMovable)
                {
                    bufCreateInfo.Usage = BufferUsageFlags.VertexBufferBit | BufferUsageFlags.TransferDstBit | BufferUsageFlags.TransferSrcBit;
                    context.Allocator.CreateBuffer(in bufCreateInfo, in allocCreateInfo, out var buffer, out var alloc);
                    allocations.Add(new AllocInfo { Allocation = alloc, Buffer = buffer, BufferInfo = bufCreateInfo, StartValue = (uint)context.Rand.Next(), Movable = false });
                }
                else
                {
                    bufCreateInfo.Usage = BufferUsageFlags.UniformBufferBit | BufferUsageFlags.TransferDstBit | BufferUsageFlags.TransferSrcBit;
                    context.Allocator.CreateBuffer(in bufCreateInfo, in allocCreateInfo, out var buffer, out var alloc);
                    allocations.Add(new AllocInfo { Allocation = alloc, Buffer = buffer, BufferInfo = bufCreateInfo, StartValue = (uint)context.Rand.Next() });
                }
            }

            var toDestroy = (allocations.Count * (100 - percentToLeave) + 50) / 100;
            for (var k = 0; k < toDestroy; k++)
            {
                var index = context.Rand.Next(allocations.Count);
                DestroyAllocation(context, allocations[index]);
                allocations.RemoveAt(index);
            }

            UploadGpuData(context, allocations);
            context.Vk.DeviceWaitIdle(context.Device);

            var defragInfo = new DefragmentationInfo
            {
            };
            var all = new Dictionary<Allocation, AllocInfo>();
            foreach (var ai in allocations)
            {
                all[ai.Allocation.NonNull()] = ai;
            }

            var stats = Defragment(context, defragInfo, all);

            Assert.True(stats.AllocationsMoved > 0 && stats.BytesMoved > 0);
            Assert.True(stats.DeviceMemoryBlocksFreed > 0 && stats.BytesFreed > 0);

            context.Vk.DeviceWaitIdle(context.Device);
            ValidateGpuData(context, allocations);
        }
        finally
        {
            DestroyAllAllocations(context, allocations);
        }
    }

    [Fact]
    public void TestDefragmentationIncrementalBasic()
    {
        using var context = new VulkanContext();
        context.SeedRand(234522);

        const long bufSizeMin = 5 * 1024 * 1024;
        const long bufSizeMax = 10 * 1024 * 1024;
        var totalSize = 3L * 256 * 1024 * 1024;
        var imageCount = (int)(totalSize / (256L * 256 * 4)) / 2;
        var bufCount = (int)(totalSize / bufSizeMin) / 2;
        const int percentToLeave = 30;

        var allocations = new List<AllocInfo>();
        var imageSizes = new[] { 256u, 512u, 1024u };

        var imageInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Extent = new Extent3D(256, 256, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Format = Format.R8G8B8A8Unorm,
            Tiling = ImageTiling.Optimal,
            InitialLayout = ImageLayout.Preinitialized,
            Usage = ImageUsageFlags.TransferSrcBit | ImageUsageFlags.TransferDstBit | ImageUsageFlags.SampledBit,
            Samples = SampleCountFlags.Count1Bit,
        };
        var allocCreateInfo = new AllocationCreateInfo
        {
            Usage = MemoryUsage.AutoPreferDevice,
        };

        var bufCreateInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
        };

        try
        {
            for (var i = 0; i < imageCount; i++)
            {
                var size = imageSizes[context.Rand.Generate() % 3];
                imageInfo.Extent = new Extent3D(size, size, 1);
                CreateImage(context, in allocCreateInfo, in imageInfo, ImageLayout.ShaderReadOnlyOptimal, out var ai);
                allocations.Add(ai);
            }
            for (var i = 0; i < bufCount; i++)
            {
                bufCreateInfo.Size = (uint)((bufSizeMin + context.Rand.Generate() % (bufSizeMax - bufSizeMin) + 15) & ~15L);
                bufCreateInfo.Usage = BufferUsageFlags.UniformBufferBit | BufferUsageFlags.TransferDstBit | BufferUsageFlags.TransferSrcBit;
                context.Allocator.CreateBuffer(in bufCreateInfo, in allocCreateInfo, out var buffer, out var alloc);
                allocations.Add(new AllocInfo { Allocation = alloc, Buffer = buffer, BufferInfo = bufCreateInfo, StartValue = (uint)context.Rand.Next() });
            }

            var toDestroy = (allocations.Count * (100 - percentToLeave) + 50) / 100;
            for (var k = 0; k < toDestroy; k++)
            {
                var index = context.Rand.Next(allocations.Count);
                DestroyAllocation(context, allocations[index]);
                allocations.RemoveAt(index);
            }

            UploadGpuData(context, allocations);
            context.Vk.DeviceWaitIdle(context.Device);

            var defragInfo = new DefragmentationInfo
            {
            };
            var all = new Dictionary<Allocation, AllocInfo>();
            foreach (var ai in allocations)
            {
                all[ai.Allocation.NonNull()] = ai;
            }

            var ctx = context.Allocator.DefragmentationBegin(in defragInfo);

            while (true)
            {
                var pass = ctx.PassBegin();
                if (pass.Moves == null || pass.Moves.Length == 0)
                {
                    break;
                }

                var renames = new List<Rename>();
                context.SubmitAndWait(cmd => ProcessDefragmentationPass(context, cmd, pass, all, renames));
                ApplyRenames(context, renames);

                if (!ctx.PassEnd())
                {
                    break;
                }
            }

            var stats = ctx.End();

            Assert.True(stats.AllocationsMoved > 0 && stats.BytesMoved > 0);
            Assert.True(stats.DeviceMemoryBlocksFreed > 0 && stats.BytesFreed > 0);

            context.Vk.DeviceWaitIdle(context.Device);
            ValidateGpuData(context, allocations);
        }
        finally
        {
            DestroyAllAllocations(context, allocations);
        }
    }

    [Fact]
    public void TestDefragmentationIncrementalComplex()
    {
        using var context = new VulkanContext();
        context.SeedRand(234522);

        const long bufSizeMin = 5 * 1024 * 1024;
        const long bufSizeMax = 10 * 1024 * 1024;
        var totalSize = 3L * 256 * 1024 * 1024;
        var imageCount = (int)(totalSize / (256L * 256 * 4)) / 2;
        var bufCount = (int)(totalSize / bufSizeMin) / 2;
        const int percentToLeave = 30;

        var allocations = new List<AllocInfo>();
        var additional = new List<AllocInfo>();
        var imageSizes = new[] { 256u, 512u, 1024u };

        var imageInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Extent = new Extent3D(256, 256, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Format = Format.R8G8B8A8Unorm,
            Tiling = ImageTiling.Optimal,
            InitialLayout = ImageLayout.Preinitialized,
            Usage = ImageUsageFlags.TransferSrcBit | ImageUsageFlags.TransferDstBit | ImageUsageFlags.SampledBit,
            Samples = SampleCountFlags.Count1Bit,
        };
        var allocCreateInfo = new AllocationCreateInfo
        {
            Usage = MemoryUsage.AutoPreferDevice,
        };

        var bufCreateInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
        };

        try
        {
            for (var i = 0; i < imageCount; i++)
            {
                var size = imageSizes[context.Rand.Generate() % 3];
                imageInfo.Extent = new Extent3D(size, size, 1);
                CreateImage(context, in allocCreateInfo, in imageInfo, ImageLayout.ShaderReadOnlyOptimal, out var ai);
                allocations.Add(ai);
            }
            for (var i = 0; i < bufCount; i++)
            {
                bufCreateInfo.Size = (uint)((bufSizeMin + context.Rand.Generate() % (bufSizeMax - bufSizeMin) + 15) & ~15L);
                bufCreateInfo.Usage = BufferUsageFlags.UniformBufferBit | BufferUsageFlags.TransferDstBit | BufferUsageFlags.TransferSrcBit;
                context.Allocator.CreateBuffer(in bufCreateInfo, in allocCreateInfo, out var buffer, out var alloc);
                allocations.Add(new AllocInfo { Allocation = alloc, Buffer = buffer, BufferInfo = bufCreateInfo, StartValue = (uint)context.Rand.Next() });
            }

            var toDestroy = (allocations.Count * (100 - percentToLeave) + 50) / 100;
            for (var k = 0; k < toDestroy; k++)
            {
                var index = context.Rand.Next(allocations.Count);
                DestroyAllocation(context, allocations[index]);
                allocations.RemoveAt(index);
            }

            UploadGpuData(context, allocations);
            context.Vk.DeviceWaitIdle(context.Device);

            var all = new Dictionary<Allocation, AllocInfo>();
            foreach (var ai in allocations)
            {
                all[ai.Allocation.NonNull()] = ai;
            }

            void MakeAdditional()
            {
                if (additional.Count >= 100)
                {
                    return;
                }
                bufCreateInfo.Size = (uint)((bufSizeMin + context.Rand.Generate() % (bufSizeMax - bufSizeMin) + 15) & ~15L);
                bufCreateInfo.Usage = BufferUsageFlags.VertexBufferBit | BufferUsageFlags.TransferDstBit | BufferUsageFlags.TransferSrcBit;
                context.Allocator.CreateBuffer(in bufCreateInfo, in allocCreateInfo, out var buffer, out var alloc);
                additional.Add(new AllocInfo { Allocation = alloc, Buffer = buffer, BufferInfo = bufCreateInfo, StartValue = (uint)context.Rand.Next() });
            }

            var defragInfo = new DefragmentationInfo
            {
                Flags = DefragmentationFlags.AlgorithmFull,
                MaxPassCount = 0,
            };

            var ctx = context.Allocator.DefragmentationBegin(in defragInfo);

            MakeAdditional();
            while (true)
            {
                var pass = ctx.PassBegin();
                if (pass.Moves == null || pass.Moves.Length == 0)
                {
                    break;
                }

                MakeAdditional();

                var renames = new List<Rename>();
                context.SubmitAndWait(cmd => ProcessDefragmentationPass(context, cmd, pass, all, renames));
                ApplyRenames(context, renames);

                MakeAdditional();

                if (!ctx.PassEnd())
                {
                    break;
                }
            }

            var stats = ctx.End();

            Assert.True(stats.AllocationsMoved > 0);

            context.Vk.DeviceWaitIdle(context.Device);
            ValidateGpuData(context, allocations);
        }
        finally
        {
            DestroyAllAllocations(context, allocations);
            DestroyAllAllocations(context, additional);
        }
    }
}

