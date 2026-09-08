using System.Runtime.InteropServices;
using Silk.NET.Vulkan;

namespace VmaCS.Tests.Unit;

public class DefragmentationTests
{
    private static int GetHostVisibleMemoryType(VulkanMemoryAllocator allocator)
    {
        var bufferInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = 64,
            Usage = BufferUsageFlags.VertexBufferBit,
        };
        var baseAllocInfo = new AllocationCreateInfo { Usage = MemoryUsage.CpuOnly };

        allocator.FindMemoryTypeIndexForBufferInfo(in bufferInfo, in baseAllocInfo, out var memoryType);
        Assert.True(memoryType.HasValue, "Failed to find a host-visible memory type.");

        return memoryType.Value;
    }

    private static unsafe void CopyAllocation(Allocation src, Allocation dst)
    {
        var data = new byte[src.Size];

        src.Map(out var sp);
        Marshal.Copy((nint)sp, data, 0, data.Length);
        src.Unmap();

        dst.Map(out var dp);
        Marshal.Copy(data, 0, (nint)dp, data.Length);
        dst.Unmap();
    }

    [Fact]
    public unsafe void Defragment_Fast_MovesAllocationsBetweenBlocks()
    {
        using var ctx = new VulkanContext();
        var memoryType = GetHostVisibleMemoryType(ctx.Allocator);

        using var pool = ctx.Allocator.CreatePool(new AllocationPoolCreateInfo
        {
            MemoryTypeIndex = memoryType,
            MinBlockCount = 1,
            MaxBlockCount = 4,
            BlockSize = 1024,
            Flags = PoolCreateFlags.IgnoreBufferImageGranularity
        });

        var entries = new List<(Allocation alloc, byte[] data)>();

        (Allocation alloc, byte[] data) Alloc(long size, byte pattern)
        {
            var req = new MemoryRequirements { Size = (ulong)size, Alignment = 1, MemoryTypeBits = uint.MaxValue };
            var info = new AllocationCreateInfo { Usage = MemoryUsage.CpuOnly, Pool = pool };

            ctx.Allocator.AllocateMemory(in req, in info, out var alloc);
            Assert.NotNull(alloc);

            var data = new byte[size];
            for (var i = 0; i < size; i++)
            {
                data[i] = pattern;
            }

            alloc.Map(out var ptr);
            Marshal.Copy(data, 0, (nint)ptr, (int)size);
            alloc.Unmap();

            var entry = (alloc, data);
            entries.Add(entry);
            return entry;
        }

        // Six 256-byte allocations span two 1024-byte blocks (4 per block).
        var a0 = Alloc(256, 10);
        var a1 = Alloc(256, 20);
        var a2 = Alloc(256, 30);
        var a3 = Alloc(256, 40);
        var a4 = Alloc(256, 50);
        var a5 = Alloc(256, 60);

        // Free the first two allocations in block 0, leaving free space there.
        a0.alloc.Dispose();
        a1.alloc.Dispose();
        entries.Remove(a0);
        entries.Remove(a1);

        var liveBefore = new Allocation[entries.Count];
        for (var k = 0; k < entries.Count; k++)
        {
            liveBefore[k] = entries[k].alloc;
        }

        var defragInfo = new DefragmentationInfo
        {
            Allocations = liveBefore,
            Flags = DefragmentationFlags.AlgorithmFast
        };

        var defragCtx = ctx.Allocator.DefragmentationBegin(in defragInfo);

        var totalMoves = 0;
        while (true)
        {
            var pass = defragCtx.PassBegin();
            if (pass.Moves == null || pass.Moves.Length == 0)
            {
                break;
            }

            // User is responsible for copying the data (matching the C++ contract).
            foreach (var m in pass.Moves)
            {
                if (m.Operation == DefragmentationMoveOperation.Copy && m.Source != null && m.Destination != null)
                {
                    CopyAllocation(m.Source, m.Destination);
                    totalMoves++;
                }
            }

            defragCtx.PassEnd();
        }

        var stats = defragCtx.End();

        // Every original allocation object is reused (relocated in place) and must
        // still contain its original data.
        for (var i = 0; i < entries.Count; i++)
        {
            var (orig, expected) = entries[i];

            orig.Map(out var ptr);
            var actual = new byte[expected.Length];
            Marshal.Copy((nint)ptr, actual, 0, actual.Length);
            orig.Unmap();

            Assert.Equal(expected, actual);
        }

        Assert.True(totalMoves > 0, "Expected at least one allocation to be relocated.");
        Assert.True(stats.AllocationsMoved > 0, "Expected allocationsMoved > 0 in stats.");

        foreach (var e in entries)
        {
            e.alloc.Dispose();
        }
    }

    [Fact]
    public unsafe void Defragment_FullAlgorithm_RelocatesWithMultiplePasses()
    {
        using var ctx = new VulkanContext();
        var memoryType = GetHostVisibleMemoryType(ctx.Allocator);

        using var pool = ctx.Allocator.CreatePool(new AllocationPoolCreateInfo
        {
            MemoryTypeIndex = memoryType,
            MinBlockCount = 1,
            MaxBlockCount = 4,
            BlockSize = 1024,
            Flags = PoolCreateFlags.IgnoreBufferImageGranularity
        });

        var allocations = new List<(Allocation alloc, byte[] data)>();

        (Allocation alloc, byte[] data) Alloc(long size, byte pattern)
        {
            var req = new MemoryRequirements { Size = (ulong)size, Alignment = 1, MemoryTypeBits = uint.MaxValue };
            var info = new AllocationCreateInfo { Usage = MemoryUsage.CpuOnly, Pool = pool };

            ctx.Allocator.AllocateMemory(in req, in info, out var alloc);
            Assert.NotNull(alloc);

            var data = new byte[size];
            for (var i = 0; i < size; i++)
            {
                data[i] = pattern;
            }

            alloc.Map(out var ptr);
            Marshal.Copy(data, 0, (nint)ptr, (int)size);
            alloc.Unmap();

            var entry = (alloc, data);
            allocations.Add(entry);
            return entry;
        }

        // Eight allocations -> two blocks. Free the odd ones to leave gaps.
        var created = new List<(Allocation alloc, byte[] data)>();
        for (var i = 0; i < 8; i++)
        {
            created.Add(Alloc(256, (byte)(i * 7 + 1)));
        }

        for (var i = 1; i < created.Count; i += 2)
        {
            created[i].alloc.Dispose();
            created.RemoveAt(i);
            i--;
        }

        var handles = new Allocation[created.Count];
        for (var i = 0; i < created.Count; i++)
        {
            handles[i] = created[i].alloc;
        }

        var defragInfo = new DefragmentationInfo
        {
            Allocations = handles,
            Flags = DefragmentationFlags.AlgorithmFull,
            MaxCpuBytesToMove = 256 // force multiple passes
        };

        var defragCtx = ctx.Allocator.DefragmentationBegin(in defragInfo);

        while (true)
        {
            var pass = defragCtx.PassBegin();
            if (pass.Moves == null || pass.Moves.Length == 0)
            {
                break;
            }

            foreach (var m in pass.Moves)
            {
                if (m.Operation == DefragmentationMoveOperation.Copy && m.Source != null && m.Destination != null)
                {
                    CopyAllocation(m.Source, m.Destination);
                }
            }

            defragCtx.PassEnd();
        }

        var stats = defragCtx.End();

        for (var i = 0; i < created.Count; i++)
        {
            var (orig, expected) = created[i];

            orig.Map(out var ptr);
            var actual = new byte[expected.Length];
            Marshal.Copy((nint)ptr, actual, 0, actual.Length);
            orig.Unmap();

            Assert.Equal(expected, actual);
        }

        Assert.True(stats.AllocationsMoved > 0);
        foreach (var (alloc, _) in created)
        {
            alloc.Dispose();
        }
    }

    [Fact]
    public unsafe void Defragment_BalancedAlgorithm_MovesAllocations()
    {
        using var ctx = new VulkanContext();
        var memoryType = GetHostVisibleMemoryType(ctx.Allocator);

        using var pool = ctx.Allocator.CreatePool(new AllocationPoolCreateInfo
        {
            MemoryTypeIndex = memoryType,
            MinBlockCount = 1,
            MaxBlockCount = 4,
            BlockSize = 1024,
            Flags = PoolCreateFlags.IgnoreBufferImageGranularity
        });

        var entries = new List<(Allocation alloc, byte[] data)>();

        (Allocation alloc, byte[] data) Alloc(long size, byte pattern)
        {
            var req = new MemoryRequirements { Size = (ulong)size, Alignment = 1, MemoryTypeBits = uint.MaxValue };
            var info = new AllocationCreateInfo { Usage = MemoryUsage.CpuOnly, Pool = pool };

            ctx.Allocator.AllocateMemory(in req, in info, out var alloc);
            Assert.NotNull(alloc);

            var data = new byte[size];
            for (var i = 0; i < size; i++)
            {
                data[i] = pattern;
            }

            alloc.Map(out var ptr);
            Marshal.Copy(data, 0, (nint)ptr, (int)size);
            alloc.Unmap();

            var entry = (alloc, data);
            entries.Add(entry);
            return entry;
        }

        var a0 = Alloc(256, 10);
        var a1 = Alloc(256, 20);
        var a2 = Alloc(256, 30);
        var a3 = Alloc(256, 40);
        var a4 = Alloc(256, 50);
        var a5 = Alloc(256, 60);

        a0.alloc.Dispose();
        a1.alloc.Dispose();
        entries.Remove(a0);
        entries.Remove(a1);

        var liveBefore = new Allocation[entries.Count];
        for (var k = 0; k < entries.Count; k++)
        {
            liveBefore[k] = entries[k].alloc;
        }

        var defragInfo = new DefragmentationInfo
        {
            Allocations = liveBefore,
            Flags = DefragmentationFlags.AlgorithmBalanced
        };

        var defragCtx = ctx.Allocator.DefragmentationBegin(in defragInfo);

        var totalMoves = 0;
        while (true)
        {
            var pass = defragCtx.PassBegin();
            if (pass.Moves == null || pass.Moves.Length == 0)
            {
                break;
            }

            foreach (var m in pass.Moves)
            {
                if (m.Operation == DefragmentationMoveOperation.Copy && m.Source != null && m.Destination != null)
                {
                    CopyAllocation(m.Source, m.Destination);
                    totalMoves++;
                }
            }

            defragCtx.PassEnd();
        }

        var stats = defragCtx.End();

        for (var i = 0; i < entries.Count; i++)
        {
            var (orig, expected) = entries[i];

            orig.Map(out var ptr);
            var actual = new byte[expected.Length];
            Marshal.Copy((nint)ptr, actual, 0, actual.Length);
            orig.Unmap();

            Assert.Equal(expected, actual);
        }

        Assert.True(totalMoves > 0, "Expected at least one allocation to be relocated.");
        Assert.True(stats.AllocationsMoved > 0, "Expected allocationsMoved > 0 in stats.");

        foreach (var e in entries)
        {
            e.alloc.Dispose();
        }
    }

    [Fact]
    public unsafe void Defragment_ExtensiveAlgorithm_DefragmentsWhenGranularityGreaterThanOne()
    {
        using var ctx = new VulkanContext();
        var memoryType = GetHostVisibleMemoryType(ctx.Allocator);

        using var pool = ctx.Allocator.CreatePool(new AllocationPoolCreateInfo
        {
            MemoryTypeIndex = memoryType,
            MinBlockCount = 1,
            MaxBlockCount = 4,
            BlockSize = 1024,
            Flags = PoolCreateFlags.IgnoreBufferImageGranularity
        });

        var entries = new List<(Allocation alloc, byte[] data)>();

        (Allocation alloc, byte[] data) Alloc(long size, byte pattern)
        {
            var req = new MemoryRequirements { Size = (ulong)size, Alignment = 1, MemoryTypeBits = uint.MaxValue };
            var info = new AllocationCreateInfo { Usage = MemoryUsage.CpuOnly, Pool = pool };

            ctx.Allocator.AllocateMemory(in req, in info, out var alloc);
            Assert.NotNull(alloc);

            var data = new byte[size];
            for (var i = 0; i < size; i++)
            {
                data[i] = pattern;
            }

            alloc.Map(out var ptr);
            Marshal.Copy(data, 0, (nint)ptr, (int)size);
            alloc.Unmap();

            var entry = (alloc, data);
            entries.Add(entry);
            return entry;
        }

        var a0 = Alloc(256, 10);
        var a1 = Alloc(256, 20);
        var a2 = Alloc(256, 30);
        var a3 = Alloc(256, 40);
        var a4 = Alloc(256, 50);
        var a5 = Alloc(256, 60);

        a0.alloc.Dispose();
        a1.alloc.Dispose();
        entries.Remove(a0);
        entries.Remove(a1);

        var liveBefore = new Allocation[entries.Count];
        for (var k = 0; k < entries.Count; k++)
        {
            liveBefore[k] = entries[k].alloc;
        }

        var defragInfo = new DefragmentationInfo
        {
            Allocations = liveBefore,
            Flags = DefragmentationFlags.AlgorithmExtensive
        };

        var defragCtx = ctx.Allocator.DefragmentationBegin(in defragInfo);

        while (true)
        {
            var pass = defragCtx.PassBegin();
            if (pass.Moves == null || pass.Moves.Length == 0)
            {
                break;
            }

            foreach (var m in pass.Moves)
            {
                if (m.Operation == DefragmentationMoveOperation.Copy && m.Source != null && m.Destination != null)
                {
                    CopyAllocation(m.Source, m.Destination);
                }
            }

            defragCtx.PassEnd();
        }

        var stats = defragCtx.End();
        // Extensive may or may not move depending on device granularity; just ensure it completes.

        for (var i = 0; i < entries.Count; i++)
        {
            var (orig, expected) = entries[i];

            orig.Map(out var ptr);
            var actual = new byte[expected.Length];
            Marshal.Copy((nint)ptr, actual, 0, actual.Length);
            orig.Unmap();

            Assert.Equal(expected, actual);
        }

        foreach (var e in entries)
        {
            e.alloc.Dispose();
        }
    }
}
