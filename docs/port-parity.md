# VmaCS ↔ VMA Parity Table

Legend:
- ✅ Implemented — full functional parity with VMA
- ⚠️ Behavior differs — ported, but contract/defaults diverge from VMA
- ❓ Undecided — unclear if this should be implemented in VmaCS
- 🚫 Not planned — intentionally excluded from VmaCS

---

## 1. Allocator Lifecycle

| VMA C++ | VmaCS C# | Status |
|---|---|---|
| `vmaCreateAllocator` | `VulkanMemoryAllocator..ctor(in VulkanMemoryAllocatorCreateInfo)` | ✅ |
| `vmaDestroyAllocator` | `VulkanMemoryAllocator.Dispose()` | ✅ |
| `vmaGetAllocatorInfo` | `VulkanMemoryAllocator.Instance`, `VulkanMemoryAllocator.PhysicalDevice`, `VulkanMemoryAllocator.Device` | ✅ |
| `vmaGetPhysicalDeviceProperties` | `VulkanMemoryAllocator.PhysicalDeviceProperties` | ✅ |
| `vmaGetMemoryProperties` | `VulkanMemoryAllocator.MemoryProperties` | ✅ |
| `vmaGetMemoryTypeProperties` | `VulkanMemoryAllocator.GetMemoryTypeProperties(int)` | ✅ |
| `vmaSetCurrentFrameIndex` | `VulkanMemoryAllocator.CurrentFrameIndex` | ✅ |
| `vmaImportVulkanFunctionsFromVolk` | `VmaCS` uses `Silk.NET` | 🚫 |

---

## 2. Resource Allocation

| VMA C++ | VmaCS C# | Status |
|---|---|---|
| `vmaAllocateMemory` | `VulkanMemoryAllocator.AllocateMemory(in MemoryRequirements, in AllocationCreateInfo, out Allocation?)` | ✅ |
| `vmaAllocateDedicatedMemory` | `VulkanMemoryAllocator.AllocateMemory(in MemoryRequirements, in AllocationCreateInfo, out Allocation)` (via `DedicatedMemory` flag + `MemoryAllocateNext` in `AllocationCreateInfo`) | ✅ |
| `vmaAllocateMemoryPages` | `VulkanMemoryAllocator.AllocateMemoryPages(in MemoryRequirements, in AllocationCreateInfo, int, out Allocation[])` | ✅ |
| `vmaFreeMemory` | `Allocation.Dispose()` | ✅ |
| `vmaFreeMemoryPages` | `foreach (var a in allocations) a.Dispose();` | ✅ |
| `vmaAllocateMemoryForBuffer` | `VulkanMemoryAllocator.AllocateMemoryForBuffer(Buffer, in AllocationCreateInfo, out Allocation?, bool)` | ✅ |
| `vmaAllocateMemoryForImage` | `VulkanMemoryAllocator.AllocateMemoryForImage(Image, in AllocationCreateInfo, out Allocation?, bool)` | ✅ |
| `vmaGetAllocationInfo` | `Allocation.Size`, `Allocation.Offset`, `Allocation.MemoryTypeIndex` | ✅ |
| `vmaGetAllocationInfo2` | `Allocation.BlockSize`, `Allocation.IsDedicated` | ✅ |
| `vmaSetAllocationUserData` | `IAllocation.UserData` | ✅ |
| `vmaSetAllocationName` | `IAllocation.Name` | ✅ |
| `vmaGetAllocationMemoryProperties` | `Allocation.MemoryPropertyFlags` | ✅ |
| `vmaCreateBuffer` | `VulkanMemoryAllocator.CreateBuffer(in BufferCreateInfo, in AllocationCreateInfo, out Buffer, out Allocation?)` | ✅ |
| `vmaCreateBufferWithAlignment` | `VulkanMemoryAllocator.CreateBufferWithAlignment(in BufferCreateInfo, in AllocationCreateInfo, long, out Buffer, out Allocation?)` | ✅ |
| `vmaCreateDedicatedBuffer` | `VulkanMemoryAllocator.CreateDedicatedBuffer(in BufferCreateInfo, in AllocationCreateInfo, void*, out Buffer, out Allocation?)` | ✅ |
| `vmaDestroyBuffer` | use `VkApi.DestroyBuffer` + `Allocation.Dispose()` | ✅ |
| `vmaCreateImage` | `VulkanMemoryAllocator.CreateImage(in ImageCreateInfo, in AllocationCreateInfo, out Image, out Allocation?)` | ✅ |
| `vmaCreateDedicatedImage` | `VulkanMemoryAllocator.CreateDedicatedImage(in ImageCreateInfo, in AllocationCreateInfo, void*, out Image, out Allocation?)` | ✅ |
| `vmaDestroyImage` | use `VkApi.DestroyImage` + `Allocation.Dispose()` | ✅ |

---

## 3. Buffer/Image Binding

| VMA C++ | VmaCS C# | Status |
|---|---|---|
| `vmaBindBufferMemory` | `Allocation.BindBufferMemory(Buffer)` | ✅ |
| `vmaBindBufferMemory2` | `Allocation.BindBufferMemory(Buffer, long, void*)` | ✅ |
| `vmaBindImageMemory` | `Allocation.BindImageMemory(Image)` | ✅ |
| `vmaBindImageMemory2` | `Allocation.BindImageMemory(Image, long, void*)` | ✅ |

---

## 4. Memory Type Selection

| VMA C++ | VmaCS C# | Status |
|---|---|---|
| `vmaFindMemoryTypeIndex` | `VulkanMemoryAllocator.FindMemoryTypeIndex(uint, in AllocationCreateInfo, out int?)` | ✅ |
| `vmaFindMemoryTypeIndexForBufferInfo` | `VulkanMemoryAllocator.FindMemoryTypeIndexForBufferInfo(in BufferCreateInfo, in AllocationCreateInfo, out int?)` | ✅ |
| `vmaFindMemoryTypeIndexForImageInfo` | `VulkanMemoryAllocator.FindMemoryTypeIndexForImageInfo(in ImageCreateInfo, in AllocationCreateInfo, out int?)` | ✅ |

---

## 5. Memory Mapping

| VMA C++ | VmaCS C# | Status |
|---|---|---|
| `vmaMapMemory` | `Allocation.Map(out void*)` | ✅ |
| `vmaUnmapMemory` | `Allocation.Unmap()` | ✅ |
| `vmaFlushAllocation` | `Allocation.Flush(long, long)` | ✅ |
| `vmaInvalidateAllocation` | `Allocation.Invalidate(long, long)` | ✅ |
| `vmaFlushAllocations` | `VulkanMemoryAllocator.FlushAllocations(Allocation[])`, `VulkanMemoryAllocator.FlushAllocations(Allocation[], long[], long[])` | ✅ |
| `vmaInvalidateAllocations` | `VulkanMemoryAllocator.InvalidateAllocations(Allocation[])`, `VulkanMemoryAllocator.InvalidateAllocations(Allocation[], long[], long[])` | ✅ |
| `vmaCopyMemoryToAllocation` | `Allocation.CopyMemoryToAllocation(ReadOnlySpan<byte>, long)` | ✅ |
| `vmaCopyAllocationToMemory` | `Allocation.CopyAllocationToMemory(long, Span<byte>)` | ✅ |

---

## 6. Statistics & Budget

| VMA C++ | VmaCS C# | Status |
|---|---|---|
| `vmaCalculateStatistics` | `VulkanMemoryAllocator.CalculateStats()` | ✅ |
| `vmaGetHeapBudgets` | `VulkanMemoryAllocator.GetBudget(int)`, `GetBudget(AllocationBudget[])` | ✅ |
| `vmaBuildStatsString` | `VulkanMemoryAllocator.BuildStatsString(bool)` | ✅ |
| `vmaFreeStatsString` | managed string, freeing not needed | 🚫 |

---

## 7. Custom Memory Pools

| VMA C++ | VmaCS C# | Status |
|---|---|---|
| `vmaCreatePool` | `VulkanMemoryAllocator.CreatePool(in AllocationPoolCreateInfo)` | ✅ |
| `vmaDestroyPool` | `VulkanMemoryPool.Dispose()` | ✅ |
| `vmaCheckPoolCorruption` | `VulkanMemoryPool.CheckForCorruption()` | ✅ |
| `vmaGetPoolName` | `VulkanMemoryPool.Name` | ✅ |
| `vmaSetPoolName` | `VulkanMemoryPool.Name` | ✅ |
| `vmaGetPoolStatistics` | `VulkanMemoryPool.GetPoolStats()` | ✅ |
| `vmaCalculatePoolStatistics` | `VulkanMemoryPool.GetPoolStats()` | ✅ |

---

## 8. Defragmentation

| VMA C++ | VmaCS C# | Status |
|---|---|---|
| `vmaBeginDefragmentation` | `VulkanMemoryAllocator.DefragmentationBegin(in DefragmentationInfo)` | ✅ |
| `vmaEndDefragmentation` | `DefragmentationContext.End()` | ✅ |
| `vmaBeginDefragmentationPass` | `DefragmentationContext.PassBegin()` | ✅ |
| `vmaEndDefragmentationPass` | `DefragmentationContext.PassEnd()` | ✅ |

Defrag algorithms: Fast ✅ | Balanced ✅ | Full ✅ | Extensive ✅

---

## 9. Virtual Allocator

| VMA C++ | VmaCS C# | Status |
|---|---|---|
| `vmaCreateVirtualBlock` | `VulkanMemoryAllocator.CreateVirtualBlock(in VirtualBlockCreateInfo, out VirtualBlock?)` | ✅ |
| `vmaDestroyVirtualBlock` | `VirtualBlock.Dispose()` | ✅ |
| `vmaIsVirtualBlockEmpty` | `VirtualBlock.IsEmpty` | ✅ |
| `vmaGetVirtualAllocationInfo` | `VirtualAllocation.Offset`, `VirtualAllocation.Size`, `VirtualAllocation.UserData` | ✅ |
| `vmaVirtualAllocate` | `VirtualBlock.Allocate(in VirtualAllocationCreateInfo, out VirtualAllocation?)` | ✅ |
| `vmaVirtualFree` | `VirtualAllocation.Dispose()` | ✅ |
| `vmaClearVirtualBlock` | `VirtualBlock.Clear()` | ✅ |
| `vmaSetVirtualAllocationUserData` | `VirtualAllocation.UserData` | ✅ |
| `vmaGetVirtualBlockStatistics` | `VirtualBlock.GetStatistics()` | ✅ |
| `vmaCalculateVirtualBlockStatistics` | `VirtualBlock.GetStatistics()` | ✅ |
| `vmaBuildVirtualBlockStatsString` | `VirtualBlock.BuildStatsString(bool)` | ✅ |
| `vmaFreeVirtualBlockStatsString` | managed string, freeing not needed | 🚫 |

---

## 10. Resource Aliasing

| VMA C++ | VmaCS C# | Status |
|---|---|---|
| `vmaCreateAliasingBuffer` | `VulkanMemoryAllocator.CreateAliasingBuffer(Allocation, in BufferCreateInfo, out Buffer)` | ✅ |
| `vmaCreateAliasingBuffer2` | `VulkanMemoryAllocator.CreateAliasingBuffer(Allocation, long, in BufferCreateInfo, out Buffer)` | ✅ |
| `vmaCreateAliasingImage` | `VulkanMemoryAllocator.CreateAliasingImage(Allocation, in ImageCreateInfo, out Image)` | ✅ |
| `vmaCreateAliasingImage2` | `VulkanMemoryAllocator.CreateAliasingImage(Allocation, long, in ImageCreateInfo, out Image)` | ✅ |

---

## 11. API Interop

| VMA C++ | VmaCS C# | Status |
|---|---|---|
| `vmaGetMemoryWin32Handle` | `VulkanMemoryAllocator.GetMemoryWin32Handle(Allocation, out nint)` | ✅ |
| `vmaGetMemoryWin32Handle2` | `VulkanMemoryAllocator.GetMemoryWin32Handle(Allocation, ExternalMemoryHandleTypeFlags, out nint)` | ✅ |

---

## 12. Vulkan Extensions

| VMA C++ | VmaCS C# | Status |
|---|---|---|
| `VMA_ALLOCATOR_CREATE_KHR_MAINTENANCE4_BIT` | Automatic detection via `VulkanMemoryAllocator.IsMaintenance4Supported` | ⚠️ |
| `VMA_ALLOCATOR_CREATE_KHR_MAINTENANCE5_BIT` | Automatic detection via `VulkanMemoryAllocator.IsMaintenance5Supported`; `BufferUsageFlags2CreateInfoKHR` always read from buffer `pNext` | ⚠️ |
| `VMA_ALLOCATOR_CREATE_KHR_EXTERNAL_MEMORY_WIN32_BIT` | Automatic detection via `VulkanMemoryAllocator.IsExternalMemoryWin32Supported`; enable `VK_KHR_external_memory_win32` and `VK_KHR_external_memory` in `DeviceCreateInfo.EnabledExtensionNames` before creating the device — Silk.NET loads the extension functions automatically | ⚠️ |

---

## 13. Debug & Configuration

| VMA C++ | VmaCS C# | Status |
|---|---|---|
| `vmaCheckCorruption` | `VulkanMemoryAllocator.CheckCorruption(uint)` | ✅ |
| `VMA_DEBUG_INITIALIZE_ALLOCATIONS` | `VulkanMemoryAllocator.DebugInitializeAllocations` | ✅ |
| `VMA_DEBUG_MARGIN` | `VulkanMemoryAllocator.DebugMargin` | ✅ |
| `VMA_DEBUG_DETECT_CORRUPTION` | `VulkanMemoryAllocator.DebugDetectCorruption` | ✅ |
| `VMA_MAPPING_HYSTERESIS_ENABLED` | Always enabled | ⚠️ |
| `VMA_STATS_STRING_ENABLED` | Always enabled | ⚠️ |
| `VMA_DEBUG_LOG` | Not implemented | ❓ |