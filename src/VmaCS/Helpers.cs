using System.Diagnostics;
using System.Runtime.InteropServices;
using Silk.NET.Vulkan;

namespace VmaCS;

internal static class Helpers
{
    public const uint CORRUPTION_DETECTION_MAGIC_VALUE = 0x7F84E666;

    public const byte ALLOCATION_FILL_PATTERN_CREATED = 0xDC;
    public const byte ALLOCATION_FILL_PATTERN_DESTROYED = 0xEF;

    public const long DEBUG_ALIGNMENT = 1;
    public const long DEBUG_MIN_BUFFER_IMAGE_GRANULARITY = 1;

    public const AllocationStrategyFlags INTERNAL_ALLOCATION_STRATEGY_MIN_OFFSET = (AllocationStrategyFlags)0x10000000u;

    internal static unsafe Result CheckBlockCorruption(nuint blockDataPointer,
                                                       long blockSize,
                                                       long margin,
                                                       IEnumerable<IAllocation> allocations)
    {
        // C++ VmaBlockMetadata_*::CheckCorruption / ValidateMagicValueAfterAllocation:
        // only the trailing margin (VMA_DEBUG_MARGIN bytes after each allocation) is checked.
        if (blockSize <= 0 || margin <= 0)
        {
            return Result.Success;
        }

        var magicBytes = BitConverter.GetBytes(CORRUPTION_DETECTION_MAGIC_VALUE);
        var p = (byte*)blockDataPointer;

        foreach (var a in allocations)
        {
            var start = a.Offset + a.Size;
            var end = start + margin;
            if (end > blockSize)
            {
                end = blockSize;
            }

            for (var k = start; k < end; ++k)
            {
                if (p[k] != magicBytes[k & 3])
                {
                    return Result.ErrorUnknown;
                }
            }
        }

        return Result.Success;
    }

    public static bool IsPow2(long v) => BitOps.PopCount((ulong)v) == 1;

    public static int PrevPow(int v) => v == 0 ? 0 : 1 << (31 - BitOps.LeadingZeroCount((uint)v));
    public static long PrevPow(long v) => v == 0 ? 0L : 1L << (63 - BitOps.LeadingZeroCount((ulong)v));

    public static bool BlocksOnSamePage(long resourceAOffset, long resourceASize, long resourceBOffset, long pageSize)
    {
        Debug.Assert(resourceAOffset + resourceASize <= resourceBOffset && resourceASize > 0 && pageSize > 0);

        var resourceAEnd = resourceAOffset + resourceASize - 1;
        var resourceAEndPage = resourceAEnd & ~(pageSize - 1);
        var resourceBStart = resourceBOffset;
        var resourceBStartPage = resourceBStart & ~(pageSize - 1);
        return resourceAEndPage == resourceBStartPage;
    }

    public static bool IsBufferImageGranularityConflict(SuballocationType type1, SuballocationType type2)
    {
        if (type1 > type2)
        {
            (type2, type1) = (type1, type2);
        }

        switch (type1)
        {
            case SuballocationType.Free:
                return false;
            case SuballocationType.Unknown:
                return true;
            case SuballocationType.Buffer:
                return type2 == SuballocationType.ImageUnknown || type2 == SuballocationType.ImageOptimal;
            case SuballocationType.ImageUnknown:
                return type2 == SuballocationType.ImageUnknown || type2 == SuballocationType.ImageLinear || type2 == SuballocationType.ImageOptimal;
            case SuballocationType.ImageLinear:
                return type2 == SuballocationType.ImageOptimal;
            case SuballocationType.ImageOptimal:
                return false;
            default:
                Debug.Assert(false);
                return true;
        }
    }

    public static long AlignUp(long value, long alignment) => (value + alignment - 1) / alignment * alignment;
    public static long AlignDown(long value, long alignment) => (long)((ulong)value / (ulong)alignment * (ulong)alignment);

    public static int BinarySearch<T>(this List<T> list, T value, Comparison<T> comparer)
    {
        int begin = 0, end = list.Count - 1;

        while (begin <= end)
        {
            var mid = (begin + end) / 2;
            var comparison = comparer(list[mid], value);

            if (comparison == 0)
            {
                return mid;
            }

            if (comparison < 0)
            {
                begin = mid + 1;
            }
            else
            {
                end = mid - 1;
            }
        }

        return ~begin;
    }

    public static int InsertSorted<T>(this List<T> list, T value, Comparison<T> comparison)
    {
        var i = list.BinarySearch(value, comparison);

        if (i < 0)
        {
            i = ~i;
        }

        list.Insert(i, value);
        return i;
    }

    public static void Validate([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool assertion)
    {
        if (!assertion)
        {
            throw new ValidationFailedException();
        }
    }

    internal static unsafe bool HasDeviceExtension(Vk vk, PhysicalDevice physicalDevice, string extensionName)
    {
        uint extCount = 0;
        vk.EnumerateDeviceExtensionProperties(physicalDevice, (byte*)null, &extCount, null);
        if (extCount == 0)
        {
            return false;
        }

        var extProps = new ExtensionProperties[extCount];
        fixed (ExtensionProperties* p = extProps)
        {
            vk.EnumerateDeviceExtensionProperties(physicalDevice, (byte*)null, &extCount, p);
            for (uint i = 0; i < extCount; i++)
            {
                var name = Marshal.PtrToStringAnsi((nint)p[i].ExtensionName);
                if (name == extensionName)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
