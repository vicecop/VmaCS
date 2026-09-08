using Silk.NET.Vulkan;
using System.Text;
using System.Text.Json;

namespace VmaCS;

/// <summary>
/// Represents a virtual block.
/// Fill <see cref="VirtualBlockCreateInfo"/> and call the constructor to create it.
/// </summary>
public sealed class VirtualBlock : IDisposable
{
    private readonly VulkanMemoryAllocator _allocator;
    internal IBlockMetadata Metadata;
    private readonly object _syncLock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="VirtualBlock"/> class.
    /// </summary>
    /// <param name="allocator">The owning allocator.</param>
    /// <param name="metadata">The block metadata implementation.</param>
    internal VirtualBlock(VulkanMemoryAllocator allocator, IBlockMetadata metadata)
    {
        _allocator = allocator;
        Metadata = metadata;
    }

    /// <summary>
    /// Total size of the virtual block, in bytes.
    /// </summary>
    public long Size => Metadata.Size;

    /// <summary>
    /// Indicates whether the virtual block is empty - contains 0 virtual allocations and has all its space available for new allocations.
    /// </summary>
    public bool IsEmpty => Metadata.IsEmpty;

    /// <summary>
    /// Total number of active virtual allocations in this block.
    /// </summary>
    public int AllocationCount => Metadata.AllocationCount;

    private static AllocationStrategyFlags StrategyFromFlags(VirtualAllocationCreateFlags flags)
    {
        var strategy = flags & VirtualAllocationCreateFlags.StrategyMask;

        if (strategy == 0)
        {
            return AllocationStrategyFlags.BestFit;
        }

        if ((strategy & VirtualAllocationCreateFlags.StrategyMinMemory) != 0)
        {
            return AllocationStrategyFlags.BestFit;
        }

        if ((strategy & VirtualAllocationCreateFlags.StrategyMinTime) != 0)
        {
            return AllocationStrategyFlags.FirstFit;
        }

        if ((strategy & VirtualAllocationCreateFlags.StrategyMinOffset) != 0)
        {
            return Helpers.INTERNAL_ALLOCATION_STRATEGY_MIN_OFFSET;
        }

        return AllocationStrategyFlags.BestFit;
    }

    /// <summary>
    /// Allocates a new virtual allocation inside this block.
    /// </summary>
    /// <param name="createInfo">Parameters for the allocation.</param>
    /// <param name="allocation">Receives the created <see cref="VirtualAllocation"/> if successful.</param>
    /// <returns>The Vulkan Result of the allocation.</returns>
    public Result Allocate(in VirtualAllocationCreateInfo createInfo, out VirtualAllocation? allocation)
    {
        allocation = null;

        if (createInfo.Size <= 0)
        {
            return Result.ErrorOutOfDeviceMemory;
        }

        var alignment = createInfo.Alignment;

        if (alignment == 0)
        {
            alignment = 1;
        }

        var strategy = StrategyFromFlags(createInfo.Flags);

        var context = new AllocationContext
        {
            BufferImageGranularity = 1,
            AllocationSize = createInfo.Size,
            AllocationAlignment = alignment,
            Strategy = strategy,
            SuballocationType = SuballocationType.Unknown,
            UpperAddress = (createInfo.Flags & VirtualAllocationCreateFlags.UpperAddress) != 0
        };

        if (Metadata.TryCreateAllocationRequest(in context, out var request))
        {
            var alloc = new VirtualAllocation(_allocator)
            {
                Owner = this
            };

            Metadata.Alloc(in request, SuballocationType.Unknown, createInfo.Size, alloc);
            alloc.InitVirtualAllocation(request.Offset, createInfo.Size, alignment, SuballocationType.Unknown);
            alloc.UserData = createInfo.UserData;

            allocation = alloc;

            return Result.Success;
        }

        return Result.ErrorOutOfDeviceMemory;
    }

    /// <summary>
    /// Allocates a new virtual allocation inside this block and returns its offset.
    /// </summary>
    /// <param name="createInfo">Parameters for the allocation.</param>
    /// <param name="allocation">Receives the created <see cref="VirtualAllocation"/> if successful.</param>
    /// <param name="offset">Receives the offset of the allocation within the block.</param>
    /// <returns>The Vulkan Result of the allocation.</returns>
    public Result Allocate(in VirtualAllocationCreateInfo createInfo, out VirtualAllocation? allocation, out long offset)
    {
        allocation = null;
        offset = unchecked((long)ulong.MaxValue);

        var res = Allocate(in createInfo, out allocation);

        if (res.IsSuccess() && allocation != null)
        {
            offset = allocation.Offset;
        }

        return res;
    }

    /// <summary>
    /// Frees all virtual allocations inside this block.
    /// </summary>
    public void Clear() => Metadata.Clear();

    /// <summary>
    /// Calculates and returns statistics about virtual allocations and memory usage in this block.
    /// </summary>
    /// <returns>The statistics for this block.</returns>
    public StatInfo GetStatistics()
    {
        Metadata.CalcAllocationStatInfo(out var info);
        StatInfo.PostProcessCalcStatInfo(ref info);
        return info;
    }

    /// <summary>
    /// Builds and returns a string in JSON format with information about this virtual block.
    /// </summary>
    /// <param name="detailedMap">If true, include a detailed map of all suballocations.</param>
    /// <returns>A JSON string with statistics.</returns>
    public string BuildStatsString(bool detailedMap)
    {
        using var scope = new LockScope(_syncLock, _allocator.UseLock);

        Metadata.CalcAllocationStatInfo(out var info);

        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });

        writer.WriteStartObject();
        StatsString.WriteDetailedStatistics(writer, "Stats", info);

        if (detailedMap)
        {
            writer.WriteStartObject("Details");
            writer.WriteNumber("TotalBytes", (ulong)Metadata.Size);
            writer.WriteNumber("UnusedBytes", (ulong)Metadata.SumFreeSize);
            writer.WriteNumber("Allocations", Metadata.AllocationCount);
            writer.WriteNumber("UnusedRanges", info.UnusedRangeCount);

            writer.WriteStartArray("Suballocations");
            foreach (var alloc in Metadata.GetAllocations())
            {
                writer.WriteStartObject();
                writer.WriteNumber("Offset", (ulong)alloc.Offset);
                writer.WriteNumber("Size", (ulong)alloc.Size);
                writer.WriteString("Type", StatsString.SuballocTypeName(alloc.SuballocationType));

                if (alloc.UserData != null)
                {
                    writer.WriteString("CustomData", alloc.UserData.ToString() ?? "");
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();

            writer.WriteEndObject();
        }

        writer.WriteEndObject();
        writer.Flush();

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private bool _disposed;

    /// <summary>
    /// Destroys this virtual block. All virtual allocations must be freed before calling this.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (!Metadata.IsEmpty)
        {
            throw new InvalidOperationException("Some virtual allocations were not freed before destruction of this virtual block.");
        }

        Metadata = null!;
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
