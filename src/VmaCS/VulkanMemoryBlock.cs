using System.Diagnostics;
using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace VmaCS;

internal class VulkanMemoryBlock(VulkanMemoryAllocator allocator,
                                 VulkanMemoryPool? pool,
                                 int memoryTypeIndex,
                                 DeviceMemory memory,
                                 uint id,
                                 IBlockMetadata metaObject) : IDisposable
{
    private readonly VulkanMemoryAllocator _allocator = allocator;
    internal readonly IBlockMetadata MetaData = metaObject;
    private readonly object _syncLock = new();
    private readonly MappingHysteresis _mappingHysteresis = new();
    private int _mapCount;

    public VulkanMemoryPool? ParentPool { get; } = pool;

    public DeviceMemory DeviceMemory { get; } = memory;

    public int MemoryTypeIndex { get; } = memoryTypeIndex;

    public uint ID { get; } = id;

    public unsafe void* MappedData { get; private set; }

    private bool _disposed;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

#if DEBUG
    ~VulkanMemoryBlock()
    {
        if (!_disposed)
        {
            Debug.Fail("VulkanMemoryBlock was not disposed. A Vulkan DeviceMemory allocation may be leaked.");
        }
    }
#endif

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (!disposing)
        {
            return;
        }

        if (!MetaData.IsEmpty)
        {
            throw new InvalidOperationException("Some allocations were not freed before destruction of this memory block!");
        }

        Debug.Assert(DeviceMemory.Handle != default);

        _allocator.FreeVulkanMemory(MemoryTypeIndex, MetaData.Size, DeviceMemory);
    }

    [Conditional("DEBUG")]
    public void Validate()
    {
        Helpers.Validate(DeviceMemory.Handle != default && MetaData.Size > 0);

        MetaData.Validate();
    }

    public unsafe Result CheckCorruption()
    {
        if (!_allocator.DebugDetectCorruption)
        {
            return Result.Success;
        }

        try
        {
            var res = Map(1, out var data);

            if (res.IsError())
            {
                // Non-host-visible memory cannot be mapped; corruption detection is skipped for it.
                return Result.Success;
            }

            try
            {
                return MetaData.CheckCorruption((nuint)(long)data);
            }
            finally
            {
                Unmap(1);
            }
        }
        catch (MapMemoryException)
        {
            // Non-host-visible memory cannot be mapped; corruption detection is skipped for it.
            return Result.Success;
        }
    }

    internal unsafe Result ValidateAllocationMagic(BlockAllocation alloc)
    {
        if (!_allocator.DebugDetectCorruption)
        {
            return Result.Success;
        }

        var res = Map(1, out var data);

        if (res.IsError())
        {
            // Non-host-visible memory cannot be mapped; corruption detection is skipped for it.
            return Result.Success;
        }

        var result = Helpers.CheckBlockCorruption((nuint)(long)data, MetaData.Size, _allocator.DebugMargin, new[] { alloc });
        Unmap(1);
        return result;
    }

    internal unsafe void FillWithMagic(long offset, long size)
    {
        if (!_allocator.DebugDetectCorruption || size <= 0)
        {
            return;
        }

        var res = Map(1, out var data);

        if (res.IsError())
        {
            return;
        }

        var p = (byte*)data;
        var magicBytes = BitConverter.GetBytes(Helpers.CORRUPTION_DETECTION_MAGIC_VALUE);

        for (long k = 0; k < size; ++k)
        {
            var abs = offset + k;
            p[abs] = magicBytes[abs & 3];
        }

        Unmap(1);
    }

    public unsafe Result Map(int count, out void* data)
    {
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        data = default;

        if (count == 0)
        {
            return Result.Success;
        }

        using var lockScope = new LockScope(_syncLock, _allocator.UseLock);

        Debug.Assert(_mapCount >= 0);

        var oldTotalMapCount = (uint)_mapCount + _mappingHysteresis.ExtraMapping;
        if (oldTotalMapCount != 0)
        {
            Debug.Assert(MappedData != default);

            _mappingHysteresis.PostMap();
            _mapCount += count;
            data = MappedData;
            return Result.Success;
        }

        void* pData;
        var res = _allocator.VkApi.MapMemory(_allocator.Device, DeviceMemory, 0, Vk.WholeSize, 0, &pData);

        if (res.IsError())
        {
            return res.ThrowOrReturn<MapMemoryException>(_allocator.ThrowOnError, "Mapping a Device Memory block encountered an issue");
        }

        _mapCount = count;
        MappedData = pData;
        _mappingHysteresis.PostMap();

        data = pData;
        return Result.Success;
    }

    public unsafe void Unmap(int count)
    {
        if (count == 0)
        {
            return;
        }

        using var lockScope = new LockScope(_syncLock, _allocator.UseLock);

        if (_mapCount < count)
        {
            throw new InvalidOperationException("Memory block is being unmapped while it was not previously mapped");
        }

        _mapCount -= count;

        var totalMapCount = (uint)_mapCount + _mappingHysteresis.ExtraMapping;
        if (totalMapCount == 0)
        {
            MappedData = default;
            _allocator.VkApi.UnmapMemory(_allocator.Device, DeviceMemory);
        }

        _mappingHysteresis.PostUnmap();
    }

    internal void NotifyAlloc() => _mappingHysteresis.PostAlloc();

    internal void NotifyFree()
    {
        if (_mappingHysteresis.PostFree())
        {
            Debug.Assert(_mappingHysteresis.ExtraMapping == 0);
        }
    }

    public unsafe Result BindBufferMemory(Allocation allocation, long allocationLocalOffset, Buffer buffer, void* pNext)
    {
        Debug.Assert(allocation is BlockAllocation blockAlloc && blockAlloc.Block == this);

        Debug.Assert((ulong)allocationLocalOffset < (ulong)allocation.Size, "Invalid allocationLocalOffset. Did you forget that this offset is relative to the beginning of the allocation, not the whole memory block?");

        var memoryOffset = allocationLocalOffset + allocation.Offset;

        using var lockScope = new LockScope(_syncLock, _allocator.UseLock);

        return _allocator.BindVulkanBuffer(buffer, DeviceMemory, memoryOffset, pNext);
    }

    public unsafe Result BindImageMemory(Allocation allocation, long allocationLocalOffset, Image image, void* pNext)
    {
        Debug.Assert(allocation is BlockAllocation blockAlloc && blockAlloc.Block == this);

        Debug.Assert((ulong)allocationLocalOffset < (ulong)allocation.Size, "Invalid allocationLocalOffset. Did you forget that this offset is relative to the beginning of the allocation, not the whole memory block?");

        var memoryOffset = allocationLocalOffset + allocation.Offset;

        using var lockScope = new LockScope(_syncLock, _allocator.UseLock);

        return _allocator.BindVulkanImage(image, DeviceMemory, memoryOffset, pNext);
    }
}
