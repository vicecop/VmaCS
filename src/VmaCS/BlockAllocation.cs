using System.Diagnostics;
using Silk.NET.Vulkan;

namespace VmaCS;

/// <summary>
/// Represents a block-based allocation backed by a Vulkan <c>VkDeviceMemory</c>.
/// </summary>
public class BlockAllocation : Allocation
{
    internal VulkanMemoryBlock Block = null!;

    internal Silk.NET.Vulkan.Buffer Buffer;

    internal bool FreedByDefrag;

    internal BlockAllocation(VulkanMemoryAllocator allocator)
        : base(allocator)
    {
    }

    /// <summary>
    /// Gets the <c>VkDeviceMemory</c> associated with this allocation.
    /// </summary>
    public override DeviceMemory DeviceMemory => Block.DeviceMemory;

    /// <summary>
    /// Gets a pointer to the mapped data for this allocation, if mapped.
    /// </summary>
    public unsafe override void* MappedData
    {
        get
        {
            if (MapCount != 0)
            {
                var mapdata = Block.MappedData;

                Debug.Assert(mapdata != default);

                return (byte*)mapdata + Offset;
            }
            else
            {
                return default;
            }
        }
    }

    internal void InitBlockAllocation(VulkanMemoryBlock? block, long offset, long alignment, long size, int memoryTypeIndex, SuballocationType subType, bool mapped)
    {
        Block = block!;
        Offset = offset;
        Alignment = alignment;
        Size = size;
        MemoryTypeIndexValue = memoryTypeIndex;
        MapCount = mapped ? int.MinValue : 0;
        SuballocationType = subType;
    }

    internal unsafe void ChangeAllocation(VulkanMemoryBlock block, long offset)
    {
        Debug.Assert(offset >= 0);

        if (!ReferenceEquals(block, Block))
        {
            var mapRefCount = MapCount & int.MaxValue;

            if (IsPersistantMapped)
            {
                mapRefCount += 1;
            }

            Block.Unmap(mapRefCount);
            block.Map(mapRefCount, out var _);

            Block = block;
        }

        Offset = offset;
    }

    private void BlockAllocMap()
    {
        if (IsPersistantMapped)
        {
            // Created-mapped (persistent) allocation: it is already mapped and must stay so.
            return;
        }

        if ((MapCount & int.MaxValue) < int.MaxValue)
        {
            MapCount += 1;
        }
        else
        {
            throw new InvalidOperationException("Allocation mapped too many times simultaniously");
        }
    }

    private void BlockAllocUnmap()
    {
        if (IsPersistantMapped)
        {
            // Created-mapped (persistent) allocation: it is already mapped and cannot be unmapped.
            return;
        }

        if ((MapCount & int.MaxValue) > 0)
        {
            MapCount -= 1;
        }
        else
        {
            throw new InvalidOperationException("Unmapping allocation not previously mapped");
        }
    }

    /// <summary>
    /// Maps the memory represented by this block allocation and returns a pointer to it.
    /// </summary>
    /// <param name="data">Receives the pointer to the mapped memory.</param>
    /// <returns>The Vulkan Result of the map operation.</returns>
    public override unsafe Result Map(out void* data)
    {
        if (IsPersistantMapped)
        {
            data = MappedData;
            return Result.Success;
        }

        var res = Block.Map(1, out var blockData);

        if (res.IsError())
        {
            data = default;
            return res;
        }

        data = (byte*)blockData + Offset;

        BlockAllocMap();

        return Result.Success;
    }

    /// <summary>
    /// Unmaps the memory represented by this block allocation, previously mapped using <see cref="Map(out void*)"/>.
    /// </summary>
    public override void Unmap()
    {
        if (IsPersistantMapped)
        {
            return;
        }

        BlockAllocUnmap();
        Block.Unmap(1);
    }
}

