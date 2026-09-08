using System.Diagnostics;
using Silk.NET.Vulkan;

namespace VmaCS;

/// <summary>
/// Allocation that owns its own dedicated VkDeviceMemory block.
/// </summary>
internal class DedicatedAllocation : Allocation
{
    private DeviceMemory _memory;
    private unsafe void* _mappedDataValue;

    /// <summary>
    /// Pool this dedicated allocation belongs to, if any.
    /// </summary>
    internal VulkanMemoryPool? Pool;

    /// <summary>
    /// Indicates whether this allocation was imported from an external memory handle.
    /// </summary>
    internal bool IsImported;

    /// <summary>
    /// Initializes a new instance of the <see cref="DedicatedAllocation"/> class.
    /// </summary>
    /// <param name="allocator">The allocator that owns this allocation.</param>
    /// <param name="memTypeIndex">Vulkan memory type index.</param>
    /// <param name="memory">The dedicated VkDeviceMemory object.</param>
    /// <param name="mappedData">Pointer to persistently mapped data, if any.</param>
    /// <param name="size">Size of the allocation, in bytes.</param>
    public unsafe DedicatedAllocation(VulkanMemoryAllocator allocator,
                                      int memTypeIndex,
                                      DeviceMemory memory,
                                      void* mappedData,
                                      long size)
        : base(allocator)
    {
        _memory = memory;
        _mappedDataValue = mappedData;
        MemoryTypeIndexValue = memTypeIndex;
        Size = size;
        MapCount = mappedData != default ? -1 : 0;
    }

    /// <summary>
    /// The VkDeviceMemory object backing this allocation.
    /// </summary>
    public override DeviceMemory DeviceMemory => _memory;

    /// <summary>
    /// Pointer to the mapped data, if the allocation is persistently mapped. Otherwise null.
    /// </summary>
    public unsafe override void* MappedData => MapCount != 0 ? _mappedDataValue : default;

    /// <summary>
    /// Maps the dedicated allocation, reusing the persistent map if available.
    /// </summary>
    /// <param name="pData">Receives the pointer to the mapped data.</param>
    /// <returns>The Vulkan Result of the mapping operation.</returns>
    internal unsafe Result DedicatedAllocMap(out void* pData)
    {
        if (IsPersistantMapped)
        {
            // Created-mapped (persistent) allocation: already mapped, must stay so.
            pData = _mappedDataValue;
            return Result.Success;
        }

        if (MapCount != 0)
        {
            if ((MapCount & int.MaxValue) < int.MaxValue)
            {
                Debug.Assert(_mappedDataValue != default);

                pData = _mappedDataValue;
                MapCount += 1;

                return Result.Success;
            }
            else
            {
                throw new InvalidOperationException("Dedicated allocation mapped too many times simultaneously");
            }
        }
        else
        {
            pData = default;

            void* tmp;
            var res = VkApi.MapMemory(Allocator.Device, _memory, 0, Vk.WholeSize, 0, &tmp);

            if (res.IsSuccess())
            {
                _mappedDataValue = tmp;
                MapCount = 1;
                pData = tmp;
            }

            return res;
        }
    }

    /// <summary>
    /// Calculates statistics for this dedicated allocation.
    /// </summary>
    /// <param name="stats">Receives the statistics.</param>
    public void CalcStatsInfo(out StatInfo stats)
    {
        StatInfo.Init(out stats);
        stats.BlockCount = 1;
        stats.AllocationCount = 1;
        stats.UsedBytes = Size;
        stats.AllocationSizeMin = stats.AllocationSizeMax = Size;
    }

    /// <summary>
    /// Maps the allocation and returns a pointer to the mapped data.
    /// </summary>
    /// <param name="data">Receives the pointer to the mapped data.</param>
    /// <returns>The Vulkan Result of the mapping operation.</returns>
    public override unsafe Result Map(out void* data)
    {
        var res = DedicatedAllocMap(out var pData);

        if (res.IsError())
        {
            data = default;
            return res.ThrowOrReturn<MapMemoryException>(Allocator.ThrowOnError, "Mapping a dedicated allocation encountered an issue");
        }

        data = pData;
        return Result.Success;
    }

    /// <summary>
    /// Unmaps the allocation previously mapped using <see cref="Map(out void*)"/>.
    /// </summary>
    public override unsafe void Unmap()
    {
        if (IsPersistantMapped)
        {
            // Created-mapped (persistent) allocation: already mapped, cannot be unmapped.
            return;
        }

        if ((MapCount & int.MaxValue) != 0)
        {
            MapCount -= 1;

            if (MapCount == 0)
            {
                _mappedDataValue = default;
                VkApi.UnmapMemory(Allocator.Device, _memory);
            }
        }
        else
        {
            throw new InvalidOperationException("Unmapping dedicated allocation not previously mapped");
        }
    }
}
