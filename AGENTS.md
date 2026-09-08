The directory where this file exists is VmaCS — a C# port of AMD Vulkan Memory Allocator (VMA) 3.4.0 for .NET. It is distributed under the MIT license.

# Requirements

Using VmaCS requires .NET 10 SDK or later. The library targets multiple frameworks: .NET 8.0+, .NET 9.0+, .NET 10.0+, and .NET Standard 2.0+.

It supports any platform that has Vulkan API available via Silk.NET (including but not limited to: Windows, Linux, Android) and any Vulkan-compatible GPU.

# Scope

Using this library is not required for using Vulkan, but it is helpful and recommended, as it simplifies some aspects of the Vulkan API: allocating device memory, creating buffers and images. Specifically, what the library does is:

1. It automatically selects the correct and optimal memory type among the ones available on the current GPU, based on the intended usage flags of the buffer/image, memory requirements that Vulkan returns for it, and parameters of the allocation to be created.
2. It allocates large blocks of `VkDeviceMemory` and implements an allocation algorithm to manage parts of them, to be assigned to individual buffers/images.
3. It provides a simple API (with the most important functions being `CreateBuffer`, `CreateImage`) that internally does the following things (so you don't need to directly call these `vk*` functions mentioned below):
    - Creates the new resource (buffer or image) using functions like `vk.CreateBuffer`, `vk.CreateImage`.
    - Queries for its memory requirements using a function like `vk.GetBufferMemoryRequirements`, `vk.GetImageMemoryRequirements` or similar.
    - Allocates a new `VkDeviceMemory` block using `vk.AllocateMemory` function or assigns a region of an existing one, suitable for the resource, while fulfilling the requirements of alignment and size.
    - Binds the resource to the memory using a function like `vk.BindBufferMemory`, `vk.BindImageMemory`.

# How to use

VmaCS is distributed as source code within this repository. The main library code lives in `src/VmaCS/`. The project is a standard .NET project with the following structure:

- `src/VmaCS/` — main library code
- `test/VmaCS.Tests.Unit/` — unit tests
- `test/VmaCS.Tests.Ported/` — ported tests from VMA C++ reference tests
- `test/VmaCS.Benchmarks/` — BDN benchmarks
- `examples/CSharp/` — example applications
- `docs/` — documentation, including `port-parity.md` for API mapping

The library uses Silk.NET as the Vulkan API provider. All Vulkan calls are dispatched through a `Vk` object obtained from `Silk.NET.Vulkan.Vk.GetApi()`.

# Library API

The API of VmaCS is a C# port of VMA's C API. The naming and behavior closely follow the original VMA, adapted to C# conventions.

## Error handling

Most functions return a `Silk.NET.Vulkan.Result` value. Check the result using `result.IsSuccess()` or `result.IsError()`:

```csharp
var res = allocator.CreateBuffer(in bufferInfo, in allocInfo, out var buffer, out var allocation);
if (res.IsError())
{
    // handle error
}
```

Alternatively, set `ThrowOnError = true` in `VulkanMemoryAllocatorCreateInfo` to throw exceptions on failure:

```csharp
var createInfo = new VulkanMemoryAllocatorCreateInfo
{
    ThrowOnError = true,
    // ...
};
```

## Types

Types that represent objects are C# classes or structs. Most important types of objects are:

- `VulkanMemoryAllocator` - represents the main allocator object. Creating only one per application and keeping it alive for the whole time when using Vulkan is recommended.
- `Allocation` - represents a specific region of allocated Vulkan memory. It may or may not be bound to a specific buffer/image. That memory block is managed internally by VmaCS.

## Pattern for creating allocations

The pattern for creating an allocation object, as well as other library objects, follows standard C# disposal pattern:

1. Fill `VulkanMemoryAllocatorCreateInfo` structure with required parameters.
2. Create `VulkanMemoryAllocator` instance using `new VulkanMemoryAllocator(in createInfo)`.
3. Use the allocator to create buffers/images and allocate memory.
4. When no longer needed, destroy objects using `Dispose()` or corresponding Vulkan destroy functions.

Example code:

```csharp
using Silk.NET.Vulkan;
using VmaCS;

// 1. Create allocator
var createInfo = new VulkanMemoryAllocatorCreateInfo
{
    VulkanAPIObject = vk,
    VulkanAPIVersion = Version32.MakeVersion(1, 3, 0),
    Instance = instance,
    PhysicalDevice = physicalDevice,
    LogicalDevice = device,
    ThrowOnError = false
};

using var allocator = new VulkanMemoryAllocator(in createInfo);

// 2. Allocate buffer
var bufferInfo = new BufferCreateInfo
{
    SType = StructureType.BufferCreateInfo,
    Size = 65536,
    Usage = BufferUsageFlags.VertexBufferBit | BufferUsageFlags.TransferDstBit,
};

var allocInfo = new AllocationCreateInfo { Usage = MemoryUsage.AutoPreferDevice };

var res = allocator.CreateBuffer(in bufferInfo, in allocInfo, out var buffer, out var allocation);
if (res.IsError())
{
    // handle error
}

// 3. Use allocation here
allocation!.Map(out var ptr);
// ... write data ...
allocation!.Unmap();

// 4. Cleanup
allocation!.Dispose();
vk.DestroyBuffer(device, buffer, ReadOnlySpan<AllocationCallbacks>.Empty);
```

Working with images follows the same workflow as in the example shown above for buffers, just use `CreateImage`, `DestroyImage`, and other image-related structures and functions.

# Allocation parameters

When filling `AllocationCreateInfo` members, using `MemoryUsage.Auto`, `MemoryUsage.AutoPreferDevice`, or `MemoryUsage.AutoPreferHost` is recommended in most cases, as it makes VmaCS automatically choose the best memory type among those available on the current GPU and compatible with the buffer/image created.

When using `MemoryUsage.Auto*`, if the memory needs to be mapped, you must also set appropriate allocation flags: `AllocationCreateFlags.HostAccessSequentialWrite` or `AllocationCreateFlags.HostAccessRandom`.

# Code conventions

- The library uses C# 13 features with .NET 10 target.
- Nullable reference types are enabled (`<Nullable>enable</Nullable>`).
- Unsafe code is used where necessary for Vulkan interop (`<AllowUnsafeBlocks>true</AllowUnsafeBlocks>`).
- The codebase follows the original VMA structure and naming conventions, adapted to C#.
- Internal implementation details are marked as `internal` or `private`. Public API is documented with XML documentation comments.
- For public API parity documentation, see `docs/port-parity.md`.

# Testing

- Unit tests are in `test/VmaCS.Tests.Unit/`
- Ported reference tests from VMA C++ are in `test/VmaCS.Tests.Ported/`
- Benchmarks using BenchmarkDotNet are in `test/VmaCS.Benchmarks/`

When making changes, ensure all tests pass:
```bash
dotnet test test/VmaCS.Tests.Unit/VmaCS.Tests.Unit.csproj
dotnet test test/VmaCS.Tests.Ported/VmaCS.Tests.Ported.csproj
```

# Vulkan extensions

VmaCS automatically detects support for certain Vulkan extensions on the physical device:

- `VK_KHR_maintenance4` — checked via `VulkanMemoryAllocator.IsMaintenance4Supported`
- `VK_KHR_maintenance5` — checked via `VulkanMemoryAllocator.IsMaintenance5Supported`
- `VK_KHR_external_memory_win32` — checked via `VulkanMemoryAllocator.IsExternalMemoryWin32Supported`

These properties are set during allocator initialization and do not require manual extension loading or flag setting.
