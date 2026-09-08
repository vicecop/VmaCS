using Silk.NET.Vulkan;

namespace VmaCS.Tests.Unit;

public class VirtualBlockTests : IDisposable
{
    private readonly VulkanContext _ctx = new();

    public void Dispose() => _ctx.Dispose();

    private VirtualBlock CreateBlock(long size, VirtualBlockCreateFlags flags = default)
    {
        var res = _ctx.Allocator.CreateVirtualBlock(new VirtualBlockCreateInfo { Size = size, Flags = flags }, out var block);

        Assert.Equal(Result.Success, res);
        Assert.NotNull(block);

        return block.NonNull();
    }

    private static VirtualAllocationCreateInfo Req(long size, long alignment = 1, object? userData = null) => new()
    {
        Size = size,
        Alignment = alignment,
        UserData = userData
    };

    [Fact]
    public void NewBlock_IsEmpty()
    {
        using var block = CreateBlock(1024 * 1024);

        Assert.True(block.IsEmpty);
        Assert.Equal(0, block.AllocationCount);
        Assert.Equal(1024 * 1024, block.Size);
    }

    [Fact]
    public void Allocate_ReturnsOffsetAndSize()
    {
        using var block = CreateBlock(1024 * 1024);
        var createInfo = Req(4096);

        var res = block.Allocate(in createInfo, out var alloc, out var offset);

        Assert.Equal(Result.Success, res);
        Assert.NotNull(alloc);
        Assert.Equal(4096, alloc.NonNull().Size);
        Assert.Equal(0, offset);
        Assert.False(block.IsEmpty);
        Assert.Equal(1, block.AllocationCount);

        alloc.Dispose();
        Assert.True(block.IsEmpty);
    }

    [Fact]
    public void TwoAllocations_DoNotOverlap()
    {
        using var block = CreateBlock(1024 * 1024);
        var a = Req(4096);
        var b = Req(4096);

        block.Allocate(in a, out var allocA, out var offA);
        block.Allocate(in b, out var allocB, out var offB);

        Assert.NotNull(allocA);
        Assert.NotNull(allocB);
        Assert.NotEqual(offA, offB);
        Assert.True(offA + 4096 <= block.Size);
        Assert.True(offB + 4096 <= block.Size);

        block.Clear();
    }

    [Fact]
    public void Free_EmptiesBlock()
    {
        using var block = CreateBlock(1024 * 1024);
        var createInfo = Req(4096);

        block.Allocate(in createInfo, out var alloc);
        Assert.False(block.IsEmpty);
        Assert.NotNull(alloc);

        alloc.Dispose();

        Assert.True(block.IsEmpty);
        Assert.Equal(0, block.AllocationCount);
    }

    [Fact]
    public void Allocate_OutOfSpace_ReturnsError()
    {
        using var block = CreateBlock(4096);
        var createInfo = Req(8192);

        var res = block.Allocate(in createInfo, out var alloc);

        Assert.Equal(Result.ErrorOutOfDeviceMemory, res);
        Assert.Null(alloc);
        Assert.True(block.IsEmpty);
    }

    [Fact]
    public void UserData_RoundTrips()
    {
        using var block = CreateBlock(1024 * 1024);
        var userData = new object();
        var createInfo = Req(4096, userData: userData);

        block.Allocate(in createInfo, out var alloc);

        Assert.NotNull(alloc);
        Assert.Same(userData, alloc.NonNull().UserData);

        var info = alloc.NonNull();
        Assert.Equal(4096, info.Size);
        Assert.Same(userData, info.UserData);

        alloc.Dispose();
    }

    [Fact]
    public void SetAllocationUserData_UpdatesInfo()
    {
        using var block = CreateBlock(1024 * 1024);
        var createInfo = Req(4096);

        block.Allocate(in createInfo, out var alloc);

        var newData = new object();
        alloc.NonNull().UserData = newData;

        var info = alloc.NonNull();
        Assert.Same(newData, info.UserData);
        Assert.Same(newData, alloc.NonNull().UserData);

        block.Clear();
    }

    [Fact]
    public void Clear_RemovesAllAllocations()
    {
        using var block = CreateBlock(1024 * 1024);
        var a = Req(4096);
        var b = Req(4096);

        block.Allocate(in a, out _);
        block.Allocate(in b, out _);

        Assert.Equal(2, block.AllocationCount);

        block.Clear();

        Assert.True(block.IsEmpty);
        Assert.Equal(0, block.AllocationCount);
    }

    [Fact]
    public void GetStatistics_ReflectsAllocationCount()
    {
        using var block = CreateBlock(1024 * 1024);
        var createInfo = Req(4096);

        block.Allocate(in createInfo, out _);

        var stats = block.GetStatistics();

        Assert.Equal(1, stats.AllocationCount);
        Assert.Equal(4096, stats.UsedBytes);

        block.Clear();
    }

    [Fact]
    public void Dispose_WithLeakedAllocation_Throws()
    {
        var block = CreateBlock(1024 * 1024);
        var createInfo = Req(4096);

        block.Allocate(in createInfo, out _);

        Assert.Throws<InvalidOperationException>(() => block.Dispose());
    }

    [Fact]
    public void LinearAlgorithm_Allocates()
    {
        using var block = CreateBlock(1024 * 1024, VirtualBlockCreateFlags.LinearAlgorithm);
        var createInfo = Req(2048);

        var res = block.Allocate(in createInfo, out var alloc, out var offset);

        Assert.Equal(Result.Success, res);
        Assert.NotNull(alloc);
        Assert.Equal(0, offset);

        alloc.Dispose();
    }

    [Fact]
    public void VirtualAllocation_IsNotRealAllocation()
    {
        using var block = CreateBlock(1024 * 1024);
        var createInfo = Req(4096);

        block.Allocate(in createInfo, out var alloc);

        // Virtual allocations implement IAllocation directly (no common base) and have no backing device
        // memory, so they do not expose binding or mapping members at all (compile-time guarantee).
        Assert.IsType<VirtualAllocation>(alloc);

        alloc.Dispose();
    }
}
