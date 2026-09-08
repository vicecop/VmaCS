using Silk.NET.Vulkan;

namespace VmaCS.Tests.Unit;

public class MetadataAlgorithmsTests
{
    private static MemoryRequirements Req(long size, long alignment = 1, uint typeBits = uint.MaxValue) => new()
    {
        Size = (ulong)size,
        Alignment = (ulong)alignment,
        MemoryTypeBits = typeBits,
    };

    private static void ValidatePool(VulkanMemoryPool pool)
    {
        for (var i = 0; i < pool.BlockList.BlockCount; ++i)
        {
            pool.BlockList[i].MetaData.Validate();
        }
    }

    private static int FindGpuMemoryType(VulkanMemoryAllocator allocator)
    {
        var bufferInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = 64,
            Usage = BufferUsageFlags.VertexBufferBit,
        };
        var baseAllocInfo = new AllocationCreateInfo { Usage = MemoryUsage.GpuOnly };

        allocator.FindMemoryTypeIndexForBufferInfo(in bufferInfo, in baseAllocInfo, out var memType);
        Assert.True(memType.HasValue, "No GPU memory type found");

        return memType.Value;
    }

    [Fact]
    public void DefaultAllocator_UsesTLSF_NotGeneric()
    {
        using var ctx = new VulkanContext();

        ctx.Allocator.AllocateMemory(Req(4096), new AllocationCreateInfo { Usage = MemoryUsage.GpuOnly }, out var alloc);
        Assert.NotNull(alloc);
        try
        {
            var foundTlsf = false;

            foreach (var blockList in ctx.Allocator.BlockLists)
            {
                if (blockList == null)
                {
                    continue;
                }

                for (var i = 0; i < blockList.BlockCount; ++i)
                {
                    if (blockList[i].MetaData is BlockMetadataTlsf)
                    {
                        foundTlsf = true;
                    }
                }
            }

            Assert.True(foundTlsf, "Default allocator should create a TLSF metadata block");
        }
        finally
        {
            alloc.Dispose();
        }
    }

    [Fact]
    public void LinearPool_UsesLinearMetadata()
    {
        using var ctx = new VulkanContext();

        var memType = FindGpuMemoryType(ctx.Allocator);

        using var pool = ctx.Allocator.CreatePool(new AllocationPoolCreateInfo
        {
            MemoryTypeIndex = memType,
            Flags = PoolCreateFlags.LinearAlgorithm,
            MinBlockCount = 1,
            MaxBlockCount = 4,
            BlockSize = 1 << 20,
        });

        ctx.Allocator.AllocateMemory(Req(1024), new AllocationCreateInfo { Usage = MemoryUsage.GpuOnly, Pool = pool }, out var alloc);
        Assert.NotNull(alloc);
        try
        {
            Assert.IsType<BlockMetadataLinear>(pool.BlockList[0].MetaData);
            ValidatePool(pool);
        }
        finally
        {
            alloc.Dispose();
        }
    }

    [Fact]
    public void LinearPool_TailIsReusedAfterFullFree()
    {
        using var ctx = new VulkanContext();

        var memType = FindGpuMemoryType(ctx.Allocator);

        using var pool = ctx.Allocator.CreatePool(new AllocationPoolCreateInfo
        {
            MemoryTypeIndex = memType,
            Flags = PoolCreateFlags.LinearAlgorithm,
            MinBlockCount = 1,
            MaxBlockCount = 1,
            BlockSize = 1 << 20,
        });

        var allocations = new System.Collections.Generic.List<Allocation>();
        for (var i = 0; i < 8; ++i)
        {
            ctx.Allocator.AllocateMemory(Req(4096), new AllocationCreateInfo { Usage = MemoryUsage.GpuOnly, Pool = pool }, out var a);
            Assert.NotNull(a);
            allocations.Add(a);
        }

        foreach (var a in allocations)
        {
            a.Dispose();
        }

        allocations.Clear();
        ValidatePool(pool);

        ctx.Allocator.AllocateMemory(Req(4096), new AllocationCreateInfo { Usage = MemoryUsage.GpuOnly, Pool = pool }, out var reused);
        Assert.NotNull(reused);
        try
        {
            Assert.Equal(0L, reused.Offset);
            ValidatePool(pool);
        }
        finally
        {
            reused.Dispose();
        }
    }

    [Theory]
    [InlineData((PoolCreateFlags)0)]
    [InlineData(PoolCreateFlags.LinearAlgorithm)]
    public void FragmentationStress_NoOverlap_Validates(PoolCreateFlags algorithm)
    {
        using var ctx = new VulkanContext();

        var memType = FindGpuMemoryType(ctx.Allocator);

        using var pool = ctx.Allocator.CreatePool(new AllocationPoolCreateInfo
        {
            MemoryTypeIndex = memType,
            Flags = algorithm,
            MinBlockCount = 1,
            MaxBlockCount = 16,
            BlockSize = 1 << 20,
        });

        var random = new Random(0x1234);
        var live = new System.Collections.Generic.List<Allocation>();
        var alignments = new[] { 1L, 64L, 256L, 4096L };

        for (var step = 0; step < 400; ++step)
        {
            if (live.Count > 0 && random.Next(2) == 0)
            {
                var idx = random.Next(live.Count);
                live[idx].Dispose();
                live.RemoveAt(idx);
            }
            else
            {
                var size = 256L + random.Next(65536 / 256) * 256L;
                var alignment = alignments[random.Next(alignments.Length)];

                var res = ctx.Allocator.AllocateMemory(Req(size, alignment), new AllocationCreateInfo { Usage = MemoryUsage.GpuOnly, Pool = pool }, out var allocation);
                if (res == Result.Success)
                {
                    Assert.NotNull(allocation);
                    live.Add(allocation);
                }
                else
                {
                    // Pool is full for this configuration; free something to make room next step.
                    if (live.Count > 0)
                    {
                        var idx = random.Next(live.Count);
                        live[idx].Dispose();
                        live.RemoveAt(idx);
                    }
                }
            }

            ValidatePool(pool);
        }

        foreach (var a in live)
        {
            a.Dispose();
        }

        ValidatePool(pool);
    }
}
