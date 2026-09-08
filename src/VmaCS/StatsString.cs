using System.Text.Json;
using Silk.NET.Vulkan;

namespace VmaCS;

/// <summary>
/// Internal helpers for formatting statistics strings and JSON output.
/// </summary>
internal static class StatsString
{
    /// <summary>
    /// Formats a Vulkan API version number into a human-readable string.
    /// </summary>
    /// <param name="version">The Vulkan version number.</param>
    /// <returns>A string in the form "major.minor.patch".</returns>
    public static string FormatApiVersion(uint version)
    {
        var major = (version >> 22) & 0x3FF;
        var minor = (version >> 12) & 0x3FF;
        var patch = version & 0xFFF;

        return $"{major}.{minor}.{patch}";
    }

    /// <summary>
    /// Returns the string name for a suballocation type.
    /// </summary>
    /// <param name="type">The suballocation type.</param>
    /// <returns>A string representation of the type.</returns>
    public static string SuballocTypeName(SuballocationType type) => type switch
    {
        SuballocationType.Free => "FREE",
        SuballocationType.Unknown => "UNKNOWN",
        SuballocationType.Buffer => "BUFFER",
        SuballocationType.ImageUnknown => "IMAGE_UNKNOWN",
        SuballocationType.ImageLinear => "IMAGE_LINEAR",
        SuballocationType.ImageOptimal => "IMAGE_OPTIMAL",
        _ => "UNKNOWN"
    };

    /// <summary>
    /// Writes detailed statistics for a <see cref="StatInfo"/> to a JSON writer.
    /// </summary>
    /// <param name="w">The JSON writer to write to.</param>
    /// <param name="name">The JSON object name.</param>
    /// <param name="info">The statistics to write.</param>
    public static void WriteDetailedStatistics(Utf8JsonWriter w, string name, in StatInfo info)
    {
        w.WriteStartObject(name);
        w.WriteNumber("BlockCount", info.BlockCount);
        w.WriteNumber("BlockBytes", (ulong)(info.UsedBytes + info.UnusedBytes));
        w.WriteNumber("AllocationCount", info.AllocationCount);
        w.WriteNumber("AllocationBytes", (ulong)info.UsedBytes);
        w.WriteNumber("UnusedRangeCount", info.UnusedRangeCount);

        if (info.AllocationCount > 1)
        {
            w.WriteNumber("AllocationSizeMin", info.AllocationSizeMin);
            w.WriteNumber("AllocationSizeMax", info.AllocationSizeMax);
        }

        if (info.UnusedRangeCount > 1)
        {
            w.WriteNumber("UnusedRangeSizeMin", info.UnusedRangeSizeMin);
            w.WriteNumber("UnusedRangeSizeMax", info.UnusedRangeSizeMax);
        }

        w.WriteEndObject();
    }

    /// <summary>
    /// Writes memory heap flags to a JSON writer.
    /// </summary>
    /// <param name="w">The JSON writer to write to.</param>
    /// <param name="name">The JSON array name.</param>
    /// <param name="flags">The memory heap flags.</param>
    public static void WriteHeapFlags(Utf8JsonWriter w, string name, MemoryHeapFlags flags)
    {
        w.WriteStartArray(name);

        if ((flags & MemoryHeapFlags.DeviceLocalBit) != 0)
        {
            w.WriteStringValue("DEVICE_LOCAL");
        }

        if ((flags & MemoryHeapFlags.MultiInstanceBit) != 0)
        {
            w.WriteStringValue("MULTI_INSTANCE");
        }

        var leftover = flags & ~(MemoryHeapFlags.DeviceLocalBit | MemoryHeapFlags.MultiInstanceBit);

        if (leftover != 0)
        {
            w.WriteNumberValue((uint)leftover);
        }

        w.WriteEndArray();
    }

    /// <summary>
    /// Writes memory property flags to a JSON writer.
    /// </summary>
    /// <param name="w">The JSON writer to write to.</param>
    /// <param name="name">The JSON array name.</param>
    /// <param name="flags">The memory property flags.</param>
    public static void WriteTypeFlags(Utf8JsonWriter w, string name, MemoryPropertyFlags flags)
    {
        w.WriteStartArray(name);

        if ((flags & MemoryPropertyFlags.DeviceLocalBit) != 0)
        {
            w.WriteStringValue("DEVICE_LOCAL");
        }

        if ((flags & MemoryPropertyFlags.HostVisibleBit) != 0)
        {
            w.WriteStringValue("HOST_VISIBLE");
        }

        if ((flags & MemoryPropertyFlags.HostCoherentBit) != 0)
        {
            w.WriteStringValue("HOST_COHERENT");
        }

        if ((flags & MemoryPropertyFlags.HostCachedBit) != 0)
        {
            w.WriteStringValue("HOST_CACHED");
        }

        if ((flags & MemoryPropertyFlags.LazilyAllocatedBit) != 0)
        {
            w.WriteStringValue("LAZILY_ALLOCATED");
        }

        if ((flags & MemoryPropertyFlags.ProtectedBit) != 0)
        {
            w.WriteStringValue("PROTECTED");
        }

        if ((flags & MemoryPropertyFlags.DeviceCoherentBitAmd) != 0)
        {
            w.WriteStringValue("DEVICE_COHERENT_AMD");
        }

        if ((flags & MemoryPropertyFlags.DeviceUncachedBitAmd) != 0)
        {
            w.WriteStringValue("DEVICE_UNCACHED_AMD");
        }

        var leftover = flags & ~(MemoryPropertyFlags.DeviceLocalBit
            | MemoryPropertyFlags.HostVisibleBit
            | MemoryPropertyFlags.HostCoherentBit
            | MemoryPropertyFlags.HostCachedBit
            | MemoryPropertyFlags.LazilyAllocatedBit
            | MemoryPropertyFlags.ProtectedBit
            | MemoryPropertyFlags.DeviceCoherentBitAmd
            | MemoryPropertyFlags.DeviceUncachedBitAmd);

        if (leftover != 0)
        {
            w.WriteNumberValue((uint)leftover);
        }

        w.WriteEndArray();
    }
}
