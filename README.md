# VmaCS

VmaCS is a C# port of [AMD Vulkan Memory Allocator (VMA)](https://github.com/GPUOpen-LibrariesAndSDKs/VulkanMemoryAllocator) 3.4.0 for .NET.
VmaCS uses [Silk.NET](https://github.com/dotnet/Silk.NET) as Vulkan API provider.

## Features

- [x] TLSF allocator
- [x] Linear allocator
- [x] Defragmentation
- [x] Virtual blocks
- [x] Custom pools
- [x] Buffer and image allocation
- [x] Memory mapping (Map/Unmap)
- [x] Dedicated allocation
- [x] Automatic `VK_KHR_maintenance4`/`maintenance5` detection

See [docs/port-parity.md](docs/port-parity.md) for full feature parity.

## Documentation

This README provides a quick start. For full API details, refer to the [original VMA documentation](https://gpuopen-librariesandsdks.github.io/VulkanMemoryAllocator/html/) and [docs/port-parity.md](docs/port-parity.md) for C#-specific mapping.

## Quick start

### Target frameworks

- .NET 8.0+
- .NET 9.0+
- .NET 10.0+
- .NET Standard 2.0
- .NET Standard 2.1

### Prerequisites

- A Vulkan-capable GPU with up-to-date drivers

### Installation

```bash
dotnet add package VmaCS
```

### Allocations

```csharp
using Silk.NET.Vulkan;
using VmaCS;

var createInfo = new VulkanMemoryAllocatorCreateInfo
{
    VulkanAPIObject = vk,
    VulkanAPIVersion = Version32.MakeVersion(1, 3, 0),
    Instance = instance,
    PhysicalDevice = physicalDevice,
    LogicalDevice = device,
    ThrowOnError = false //set true if you want exceptions for methods that returns Result
};

using var allocator = new VulkanMemoryAllocator(in createInfo);

var bufferInfo = new BufferCreateInfo
{
    SType = StructureType.BufferCreateInfo,
    Size = 65536,
    Usage = BufferUsageFlags.VertexBufferBit | BufferUsageFlags.TransferDstBit,
};

var allocInfo = new AllocationCreateInfo { Usage = MemoryUsage.GPU_Only };

var res = allocator.CreateBuffer(in bufferInfo, in allocInfo, out var buffer, out var allocation);
if (res.IsError())
{
    // handle error
}

var imageInfo = new ImageCreateInfo
{
    SType = StructureType.ImageCreateInfo,
    ImageType = ImageType.Type2D,
    Format = Format.R8G8B8A8Unorm,
    Extent = new Extent3D { Width = 256, Height = 256, Depth = 1 },
    MipLevels = 1,
    ArrayLayers = 1,
    Samples = SampleCountFlags.Count1Bit,
    Tiling = ImageTiling.Optimal,
    Usage = ImageUsageFlags.SampledBit | ImageUsageFlags.TransferDstBit,
    InitialLayout = ImageLayout.Undefined,
};

res = allocator.CreateImage(in imageInfo, in allocInfo, out var image, out var imageAllocation);
if (res.IsError())
{
    // handle error
}

allocation!.Map(out var ptr);
Unsafe.WriteUnaligned(ptr, 42);
allocation!.Unmap();

allocation!.Dispose();
vk.DestroyBuffer(device, buffer, ReadOnlySpan<AllocationCallbacks>.Empty);
```

### Custom pool

```csharp
var memType = allocator.FindMemoryTypeIndexForBufferInfo(in bufferInfo, in allocInfo, out var idx)
    .IsSuccess() ? idx!.Value : 0;

using var pool = allocator.CreatePool(new AllocationPoolCreateInfo
{
    MemoryTypeIndex = memType,
    BlockSize = 4 * 1024 * 1024,
    MinBlockCount = 1,
    MaxBlockCount = 16,
});

var pooledAllocInfo = new AllocationCreateInfo
{
    Usage = MemoryUsage.GPU_Only,
    Pool = pool,
};

allocator.CreateBuffer(in bufferInfo, in pooledAllocInfo, out var pooledBuffer, out var pooledAllocation);
```

### Defragmentation

```csharp
// Collect allocations to defragment
var handles = new[] { alloc1, alloc2, alloc3 };

var defragInfo = new DefragmentationInfo
{
    Allocations = handles,
    Flags = DefragmentationFlags.AlgorithmFast,
};

using var defragCtx = allocator.DefragmentationBegin(in defragInfo);

while (true)
{
    var pass = defragCtx.PassBegin();
    if (pass.Moves is not { Length: > 0 })
    {
        break;
    }

    foreach (var move in pass.Moves)
    {
        if (move.Operation == DefragmentationMoveOperation.Copy
            && move.Source is { } src
            && move.Destination is { } dst)
        {
            // Copy data from src to dst
            src.Map(out var srcPtr);
            dst.Map(out var dstPtr);
            Buffer.MemoryCopy(srcPtr, dstPtr, dst.Size, src.Size);
            dst.Unmap();
            src.Unmap();
        }
    }

    defragCtx.PassEnd();
}

var stats = defragCtx.End();
Console.WriteLine($"Moved: {stats.AllocationsMoved}");
```

### Virtual allocator

```csharp
var vbRes = allocator.CreateVirtualBlock(new VirtualBlockCreateInfo
{
    Size = 256 * 1024 * 1024,
}, out var vb);
if (vbRes.IsError())
{
    // handle error
}

vb.Allocate(new VirtualAllocationCreateInfo { Size = 4096, Alignment = 256 }, out var valloc);

// valloc.Offset, valloc.Size available here
// No GPU memory is actually allocated
```

## Examples

- [VmaCS.Examples.Allocations](examples/CSharp/VmaCS.Examples.Allocations) — minimal console app demonstrating allocator setup, buffer/image allocation, and cleanup
- [VulkanCube](examples/CSharp/VulkanCube) — adapted from the [VMASharp](https://github.com/sunkin351/VMASharp) example

## Planned tasks

- [ ] Increase unit test coverage to 80%+
- [ ] Add benchmark coverage for more allocator paths
- [ ] Add more usage examples (dedicated allocation, aliasing, budget updates)
- [ ] Write API reference documentation based on VMA docs
- [ ] Add conditional compilation flags for optimized builds

## Attribution
- Ported from https://github.com/GPUOpen-LibrariesAndSDKs/VulkanMemoryAllocator (c) 2017-2026 Advanced Micro Devices, Inc., MIT License.
- Inspired by https://github.com/sunkin351/VMASharp (c) 2020 sunkin351, MIT License.
