using Silk.NET.Vulkan;

namespace VmaCS.Tests.Unit;

public unsafe class AllocationStrategyTests : IDisposable
{
    private readonly VulkanContext _ctx = new();

    public void Dispose() => _ctx.Dispose();

    private static int PickMemoryTypeIndex(Vk vk, PhysicalDevice pd, uint memoryTypeBits)
    {
        vk.GetPhysicalDeviceMemoryProperties(pd, out var memProps);

        for (var i = 0; i < memProps.MemoryTypeCount; ++i)
        {
            if ((memoryTypeBits & (1u << i)) != 0)
            {
                return i;
            }
        }

        throw new InvalidOperationException("No compatible memory type for probe buffer.");
    }

    private int ProbeMemoryTypeIndex()
    {
        var probeInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = 64,
            Usage = BufferUsageFlags.TransferSrcBit | BufferUsageFlags.TransferDstBit,
        };

        _ctx.Vk.CreateBuffer(_ctx.Device, in probeInfo, null, out var probe);
        _ctx.Vk.GetBufferMemoryRequirements(_ctx.Device, probe, out var req);
        _ctx.Vk.DestroyBuffer(_ctx.Device, probe, ReadOnlySpan<AllocationCallbacks>.Empty);

        return PickMemoryTypeIndex(_ctx.Vk, _ctx.PhysicalDevice, req.MemoryTypeBits);
    }

    [Fact]
    public void DedicatedAllocation_SizeRecorded_AndBudgetDecays()
    {
        var bi = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = 1024,
            Usage = BufferUsageFlags.TransferSrcBit | BufferUsageFlags.TransferDstBit,
        };
        var ai = new AllocationCreateInfo { Flags = AllocationCreateFlags.DedicatedMemory };

        var statsBefore = _ctx.Allocator.CalculateStats();

        _ctx.Allocator.CreateBuffer(in bi, in ai, out var buf, out var alloc);

        Assert.NotNull(alloc);

        var bufDestroyed = false;
        var allocFreed = false;

        try
        {
            _ctx.Vk.GetBufferMemoryRequirements(_ctx.Device, buf, out var req);

            Assert.Equal((long)req.Size, alloc.Size);
            Assert.True(alloc.Size > 0);

            var statsAfterAlloc = _ctx.Allocator.CalculateStats();
            Assert.Equal(statsBefore.Total.UsedBytes + (long)req.Size, statsAfterAlloc.Total.UsedBytes);

            _ctx.Vk.DestroyBuffer(_ctx.Device, buf, ReadOnlySpan<AllocationCallbacks>.Empty);
            bufDestroyed = true;

            alloc.Dispose();
            allocFreed = true;
            alloc = null;

            var statsAfterFree = _ctx.Allocator.CalculateStats();
            Assert.Equal(statsBefore.Total.UsedBytes, statsAfterFree.Total.UsedBytes);
        }
        finally
        {
            if (!bufDestroyed)
            {
                _ctx.Vk.DestroyBuffer(_ctx.Device, buf, ReadOnlySpan<AllocationCallbacks>.Empty);
            }

            if (!allocFreed && alloc != null)
            {
                alloc.Dispose();
            }

        }
    }

    [Fact]
    public void FirstFitAllocation_Succeeds()
    {
        var bi = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = 1024,
            Usage = BufferUsageFlags.TransferSrcBit | BufferUsageFlags.TransferDstBit,
        };
        var ai = new AllocationCreateInfo { Strategy = AllocationStrategyFlags.FirstFit };

        _ctx.Allocator.CreateBuffer(in bi, in ai, out var buf, out var alloc);
        Assert.NotNull(alloc);
        var bufDestroyed = false;
        var allocFreed = false;

        try
        {
            Assert.True(alloc.Size > 0);

            _ctx.Vk.DestroyBuffer(_ctx.Device, buf, ReadOnlySpan<AllocationCallbacks>.Empty);
            bufDestroyed = true;
            alloc.Dispose();
            allocFreed = true;
        }
        finally
        {
            if (!bufDestroyed)
            {
                _ctx.Vk.DestroyBuffer(_ctx.Device, buf, ReadOnlySpan<AllocationCallbacks>.Empty);
            }

            if (!allocFreed)
            {
                alloc.Dispose();
            }

        }
    }

    [Fact]
    public void BestFitFragmentedAllocation_ReturnsNonSuccess()
    {
        var memTypeIndex = ProbeMemoryTypeIndex();

        var poolInfo = new AllocationPoolCreateInfo
        {
            MemoryTypeIndex = memTypeIndex,
            BlockSize = 8192,
            MaxBlockCount = 1
        };
        using var pool = _ctx.Allocator.CreatePool(in poolInfo);

        var bi = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = 2048,
            Usage = BufferUsageFlags.TransferSrcBit | BufferUsageFlags.TransferDstBit,
        };

        var ai = new AllocationCreateInfo { Strategy = AllocationStrategyFlags.BestFit, Pool = pool };

        _ctx.Allocator.CreateBuffer(in bi, in ai, out var bufA, out var allocA);
        Assert.NotNull(allocA);
        _ctx.Allocator.CreateBuffer(in bi, in ai, out var bufB, out var allocB);
        Assert.NotNull(allocB);
        _ctx.Allocator.CreateBuffer(in bi, in ai, out var bufC, out var allocC);
        Assert.NotNull(allocC);

        // Free A and C, keep B allocated -> fragmented free list (2 KB gap + 4 KB merged gap).
        _ctx.Vk.DestroyBuffer(_ctx.Device, bufA, ReadOnlySpan<AllocationCallbacks>.Empty);
        allocA.Dispose();
        _ctx.Vk.DestroyBuffer(_ctx.Device, bufC, ReadOnlySpan<AllocationCallbacks>.Empty);
        allocC.Dispose();

        // Request 5 KB: larger than any single free chunk (max 4 KB) but <= total free (6 KB).
        var biBig = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = 5120,
            Usage = BufferUsageFlags.TransferSrcBit | BufferUsageFlags.TransferDstBit,
        };
        var aiBig = new AllocationCreateInfo { Strategy = AllocationStrategyFlags.BestFit, Pool = pool };

        var resD = _ctx.Allocator.CreateBuffer(in biBig, in aiBig, out var bufD, out var allocD);

        try { }
        finally
        {
            // B is always allocated; clean it up regardless of the big-allocation outcome.
            try { _ctx.Vk.DestroyBuffer(_ctx.Device, bufB, ReadOnlySpan<AllocationCallbacks>.Empty); } catch { }
            try { allocB.Dispose(); } catch { }

            if (allocD != null)
            {
                try { _ctx.Vk.DestroyBuffer(_ctx.Device, bufD, ReadOnlySpan<AllocationCallbacks>.Empty); } catch { }
                try { allocD.Dispose(); } catch { }
            }
        }

        Assert.NotEqual(Result.Success, resD);
    }
}
