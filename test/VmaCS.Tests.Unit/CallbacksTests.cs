using Silk.NET.Vulkan;
using System.Runtime.InteropServices;

namespace VmaCS.Tests.Unit;

public unsafe class CallbacksTests : IDisposable
{
    private static readonly IntPtr _callbackUserData = 0x1234;

    private readonly VulkanContext _ctx = new();

    private readonly List<(uint MemoryType, ulong Size)> _allocates = new();
    private readonly List<(uint MemoryType, ulong Size)> _frees = new();

    private readonly VulkanMemoryAllocator _callbackAllocator;

    public CallbacksTests()
    {
        _callbackAllocator = new VulkanMemoryAllocator(new VulkanMemoryAllocatorCreateInfo
        {
            VulkanAPIVersion = Vk.Version11,
            VulkanAPIObject = _ctx.Vk,
            Instance = _ctx.Instance,
            PhysicalDevice = _ctx.PhysicalDevice,
            LogicalDevice = _ctx.Device,
            DeviceMemoryCallbacks = new DeviceMemoryCallbacks
            {
                Allocate = (_, memType, _, size, ud) =>
                {
                    if (ud == _callbackUserData)
                    {
                        _allocates.Add((memType, size));
                    }
                },
                Free = (_, memType, _, size, ud) =>
                {
                    if (ud == _callbackUserData)
                    {
                        _frees.Add((memType, size));
                    }
                },
                UserData = _callbackUserData
            }
        });
    }

    public void Dispose()
    {
        _callbackAllocator?.Dispose();
        _ctx.Dispose();
    }

    private static MemoryRequirements Req(long size) => new()
    {
        Size = (ulong)size,
        Alignment = 1,
        MemoryTypeBits = uint.MaxValue,
    };

    // Valid Vulkan system allocation callbacks. Vulkan requires all three function pointers to be
    // non-null when pAllocationCallbacks is provided; passing a struct with only UserData set makes
    // vkAllocateMemory dereference null function pointers and abort the native host. These forward to
    // the process heap and track the originally allocated base pointer so realloc/free stay consistent.
    private static readonly object _allocLock = new();
    private static readonly Dictionary<IntPtr, IntPtr> _allocBases = new();

    private static void* VkAllocate(void* userData, nuint size, nuint alignment, SystemAllocationScope scope)
    {
        var align = alignment < 1 ? 1 : alignment;
        var extra = align + (nuint)sizeof(nint);
        var raw = Marshal.AllocHGlobal((nint)(size + extra));
        var addr = (nuint)(void*)raw;
        var aligned = align <= 1 ? addr : (addr + align - 1) & ~(align - 1);
        lock (_allocLock)
        {
            _allocBases[(IntPtr)aligned] = raw;
        }
        return (void*)aligned;
    }

    private static void VkFree(void* userData, void* memory)
    {
        lock (_allocLock)
        {
            if (_allocBases.TryGetValue((IntPtr)memory, out var raw))
            {
                Marshal.FreeHGlobal(raw);
                _allocBases.Remove((IntPtr)memory);
            }
        }
    }

    private static void* VkReallocate(void* userData, void* original, nuint size, nuint alignment, SystemAllocationScope scope)
    {
        if (original == null)
        {
            return VkAllocate(userData, size, alignment, scope);
        }

        VkFree(userData, original);
        return VkAllocate(userData, size, alignment, scope);
    }

    // VmaDeviceMemoryCallbacks.pfnAllocate fires right after a successful vkAllocateMemory.
    [Fact]
    public void DeviceMemoryCallbacks_AllocateFiresWithCorrectArgs()
    {
        var ai = new AllocationCreateInfo { Usage = MemoryUsage.GpuOnly };
        var req = Req(4096);

        _callbackAllocator.AllocateMemory(in req, in ai, out var alloc);
        Assert.NotNull(alloc);

        try
        {
            Assert.Single(_allocates);
            Assert.True(_allocates[0].Size > 0);
            Assert.True(_allocates[0].MemoryType < Vk.MaxMemoryTypes);
        }
        finally
        {
            alloc.Dispose();
        }
    }

    // VmaDeviceMemoryCallbacks.pfnFree fires before vkFreeMemory when the block is released.
    [Fact]
    public void DeviceMemoryCallbacks_FreeFiresWhenBlockReleased()
    {
        var ai = new AllocationCreateInfo { Usage = MemoryUsage.GpuOnly, Flags = AllocationCreateFlags.DedicatedMemory };
        var req = Req(8192);

        _callbackAllocator.AllocateMemory(in req, in ai, out var alloc);
        Assert.NotNull(alloc);

        Assert.Single(_allocates);
        Assert.Empty(_frees);

        alloc.Dispose();

        Assert.Single(_frees);
        Assert.Equal((ulong)8192, _frees[0].Size);
        Assert.Equal(_allocates[0].MemoryType, _frees[0].MemoryType);
    }

    // pAllocationCallbacks is accepted by the create info and does not disturb normal operation.
    [Fact]
    public void AllocationCallbacks_FieldRoundTrips()
    {
        var createInfo = new VulkanMemoryAllocatorCreateInfo
        {
            VulkanAPIVersion = Vk.Version11,
            VulkanAPIObject = _ctx.Vk,
            Instance = _ctx.Instance,
            PhysicalDevice = _ctx.PhysicalDevice,
            LogicalDevice = _ctx.Device,
            AllocationCallbacks = new AllocationCallbacks
            {
                PUserData = (void*)_callbackUserData,
                PfnAllocation = new PfnAllocationFunction(VkAllocate),
                PfnReallocation = new PfnReallocationFunction(VkReallocate),
                PfnFree = new PfnFreeFunction(VkFree),
            }
        };

        using var allocator = new VulkanMemoryAllocator(createInfo);

        var ai = new AllocationCreateInfo { Usage = MemoryUsage.GpuOnly };
        var req = Req(4096);
        allocator.AllocateMemory(in req, in ai, out var alloc);
        Assert.NotNull(alloc);

        try
        {
        }
        finally
        {
            alloc.Dispose();
        }
    }
}
