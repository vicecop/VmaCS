using Silk.NET.Vulkan;
using System.Text.Json;

namespace VmaCS.Tests.Unit;

public class StatsStringTests : IDisposable
{
    private readonly VulkanContext _ctx = new();

    public void Dispose() => _ctx.Dispose();

    private static MemoryRequirements Req(long size) => new()
    {
        Size = (ulong)size,
        Alignment = 1,
        MemoryTypeBits = uint.MaxValue,
    };

    [Fact]
    public void BuildStatsString_NonDetailed_HasCoreSections()
    {
        var req = Req(65536);
        var ai = new AllocationCreateInfo { Usage = MemoryUsage.GpuOnly };
        _ctx.Allocator.AllocateMemory(in req, in ai, out var alloc);
        Assert.NotNull(alloc);

        try
        {
            var json = _ctx.Allocator.BuildStatsString(detailedMap: false);

            Assert.NotNull(json);
            using var doc = JsonDocument.Parse(json.NonNull());
            var root = doc.RootElement;

            Assert.True(root.TryGetProperty("General", out _));
            Assert.True(root.TryGetProperty("Total", out _));
            Assert.True(root.TryGetProperty("MemoryInfo", out _));
            Assert.False(root.TryGetProperty("DetailedMap", out _));

            var general = root.GetProperty("General");
            Assert.Equal("Vulkan", general.GetProperty("API").GetString());
            Assert.Contains(".", general.GetProperty("apiVersion").GetString().NonNull());
            Assert.True(general.GetProperty("memoryTypeCount").GetInt32() > 0);
        }
        finally
        {
            alloc.Dispose();
        }
    }

    [Fact]
    public void BuildStatsString_ReflectsAllocatedBytes()
    {
        const long size = 65536;
        var req = Req(size);
        var ai = new AllocationCreateInfo { Usage = MemoryUsage.GpuOnly };
        _ctx.Allocator.AllocateMemory(in req, in ai, out var a1);
        _ctx.Allocator.AllocateMemory(in req, in ai, out var a2);
        Assert.NotNull(a1);
        Assert.NotNull(a2);

        try
        {
            var json = _ctx.Allocator.BuildStatsString(detailedMap: false);

            using var doc = JsonDocument.Parse(json.NonNull());
            var total = doc.RootElement.GetProperty("Total");

            Assert.True(total.GetProperty("AllocationBytes").GetInt64() >= 2 * size);
            Assert.True(total.GetProperty("AllocationCount").GetInt64() >= 2);
        }
        finally
        {
            a1.Dispose();
            a2.Dispose();
        }
    }

    [Fact]
    public void BuildStatsString_Detailed_ContainsAllocation()
    {
        const long size = 65536;
        var req = Req(size);
        var ai = new AllocationCreateInfo { Usage = MemoryUsage.GpuOnly };
        _ctx.Allocator.AllocateMemory(in req, in ai, out var alloc);
        Assert.NotNull(alloc);
        var offset = alloc.Offset;

        try
        {
            var json = _ctx.Allocator.BuildStatsString(detailedMap: true);

            using var doc = JsonDocument.Parse(json.NonNull());
            var detail = doc.RootElement.GetProperty("DetailedMap");

            var found = false;

            foreach (var pool in detail.GetProperty("DefaultPools").EnumerateObject())
            {
                if (!pool.Value.TryGetProperty("Blocks", out var blocks))
                {
                    continue;
                }

                foreach (var block in blocks.EnumerateArray())
                {
                    if (!block.TryGetProperty("Suballocations", out var subs))
                    {
                        continue;
                    }

                    foreach (var sub in subs.EnumerateArray())
                    {
                        if (sub.GetProperty("Offset").GetInt64() == offset && sub.GetProperty("Size").GetInt64() == size)
                        {
                            found = true;
                        }
                    }
                }
            }

            Assert.True(found);
        }
        finally
        {
            alloc.Dispose();
        }
    }

    [Fact]
    public void VirtualBlock_BuildStatsString_ReflectsAllocations()
    {
        var res = _ctx.Allocator.CreateVirtualBlock(new VirtualBlockCreateInfo { Size = 1024 * 1024 }, out var block);

        Assert.Equal(Result.Success, res);

        using (var vb = block.NonNull())
        {
            var ci = new VirtualAllocationCreateInfo { Size = 4096 };
            Assert.Equal(Result.Success, vb.Allocate(in ci, out var alloc, out var offset));
            Assert.NotNull(alloc);

            var json = vb.BuildStatsString(detailedMap: true);

            Assert.NotNull(json);
            using var doc = JsonDocument.Parse(json.NonNull());
            var root = doc.RootElement;

            Assert.True(root.TryGetProperty("Stats", out var stats));
            Assert.True(stats.GetProperty("AllocationCount").GetInt64() >= 1);

            var details = root.GetProperty("Details");
            Assert.True(details.TryGetProperty("Suballocations", out var subs));

            var found = false;

            foreach (var sub in subs.EnumerateArray())
            {
                if (sub.GetProperty("Offset").GetInt64() == offset && sub.GetProperty("Size").GetInt64() == 4096)
                {
                    found = true;
                }
            }

            Assert.True(found);

            alloc.Dispose();
        }
    }
}
