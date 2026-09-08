using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Silk.NET.Core;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace VmaCS;

/// <summary>
/// Represents the main object of the Vulkan Memory Allocator library.
/// Fill <see cref="VulkanMemoryAllocatorCreateInfo"/> and pass it to the constructor to create it.
/// It is recommended to create just one object of this type per <c>VkDevice</c> object,
/// right after Vulkan is initialized and keep it alive until before Vulkan device is destroyed.
/// </summary>
public sealed unsafe class VulkanMemoryAllocator : IDisposable
{
    private const long SMALL_HEAP_MAX_SIZE = 1024L * 1024 * 1024;
    private const BufferUsageFlags UNKNOWN_BUFFER_USAGE = unchecked((BufferUsageFlags)uint.MaxValue);

    /// <summary>
    /// The Vulkan API interface used for dispatching Vulkan calls.
    /// </summary>
    public Vk VkApi { get; }

    /// <summary>
    /// The Vulkan logical device this allocator was created from.
    /// </summary>
    public Device Device { get; }

    private readonly Instance _instanceValue;
    private readonly KhrExternalMemoryWin32 _khrExternalMemoryWin32;

    private readonly Version32 _vulkanAPIVersion;

    private readonly bool _useExtMemoryBudget;
    private readonly bool _useAMDDeviceCoherentMemory;
    private readonly bool _useExtMemoryPriority;

    internal readonly bool UseKhrBufferDeviceAddress;

    internal readonly bool ThrowOnError;

    internal readonly bool UseLock;

    private readonly DeviceMemoryCallbacks? _deviceMemoryCallbacks;
    private readonly AllocationCallbacks? _allocationCallbacks;

    private readonly uint _heapSizeLimitMask;

    private PhysicalDeviceProperties _physicalDevicePropertiesValue;
    private PhysicalDeviceMemoryProperties _memoryPropertiesValue;

    internal readonly BlockList[] BlockLists = new BlockList[Vk.MaxMemoryTypes];
    internal DedicatedAllocationHandler[] DedicatedAllocations = new DedicatedAllocationHandler[Vk.MaxMemoryTypes];

    private readonly long _preferredLargeHeapBlockSize;
    private PhysicalDevice _physicalDevice;
    private uint _currentFrameIndex;

    private readonly object _allocatorLock = new();

    private readonly ReaderWriterLockSlim _poolsLock = new();
    private readonly List<VulkanMemoryPool> _pools = [];
    private bool _disposed;

    internal uint NextPoolId;
    internal CurrentBudgetData Budget = new();

    internal long BufferImageGranularity => (long)Math.Max(1, PhysicalDeviceProperties.Limits.BufferImageGranularity);

    internal int MemoryHeapCount => (int)MemoryProperties.MemoryHeapCount;

    internal int MemoryTypeCount => (int)MemoryProperties.MemoryTypeCount;

    internal bool IsIntegratedGPU => PhysicalDeviceProperties.DeviceType == PhysicalDeviceType.IntegratedGpu;

    internal uint GlobalMemoryTypeBits { get; private set; }

    /// <summary>
    /// Gets the physical device properties.
    /// </summary>
    public ref readonly PhysicalDeviceProperties PhysicalDeviceProperties => ref _physicalDevicePropertiesValue;

    /// <summary>
    /// Gets the physical device memory properties.
    /// </summary>
    public ref readonly PhysicalDeviceMemoryProperties MemoryProperties => ref _memoryPropertiesValue;

    /// <summary>
    /// Gets the Vulkan instance.
    /// </summary>
    public Instance Instance => _instanceValue;

    /// <summary>
    /// Gets the physical device.
    /// </summary>
    public PhysicalDevice PhysicalDevice => _physicalDevice;

    /// <summary>
    /// Gets or sets the current frame index, used for tracking allocations in flight.
    /// </summary>
    public uint CurrentFrameIndex
    {
        get => _currentFrameIndex;
        set
        {
            _currentFrameIndex = value;
            if (_useExtMemoryBudget)
            {
                UpdateVulkanBudget();
            }
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether allocations should be initialized with a debug pattern.
    /// </summary>
    public bool DebugInitializeAllocations { get; set; }

    /// <summary>
    /// Gets or sets the debug margin size in bytes, used to detect memory corruption.
    /// </summary>
    public long DebugMargin { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether corruption detection is enabled.
    /// </summary>
    public bool DebugDetectCorruption { get; set; }
    /// <summary>
    /// Gets whether <c>VK_KHR_maintenance4</c> is supported by the physical device used by this allocator.
    /// </summary>
    public bool IsMaintenance4Supported { get; }

    /// <summary>
    /// Gets whether <c>VK_KHR_maintenance5</c> is supported by the physical device used by this allocator.
    /// </summary>
    public bool IsMaintenance5Supported { get; }

    /// <summary>
    /// Gets whether <c>VK_KHR_external_memory_win32</c> is supported by the physical device used by this allocator.
    /// </summary>
    public bool IsExternalMemoryWin32Supported { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="VulkanMemoryAllocator"/> class.
    /// </summary>
    /// <param name="createInfo">The allocator creation information.</param>
    public VulkanMemoryAllocator(in VulkanMemoryAllocatorCreateInfo createInfo)
    {
        VkApi = createInfo.VulkanAPIObject;

        _khrExternalMemoryWin32 = new KhrExternalMemoryWin32(VkApi.Context);

        if (createInfo.Instance.Handle == default)
        {
            throw new ArgumentNullException("createInfo.Instance");
        }

        if (createInfo.LogicalDevice.Handle == default)
        {
            throw new ArgumentNullException("createInfo.LogicalDevice");
        }

        if (createInfo.PhysicalDevice.Handle == default)
        {
            throw new ArgumentNullException("createInfo.PhysicalDevice");
        }

        if (createInfo.VulkanAPIVersion < Vk.Version11)
        {
            throw new NotSupportedException("Vulkan API Version of less than 1.1 is not supported");
        }

        _instanceValue = createInfo.Instance;
        _physicalDevice = createInfo.PhysicalDevice;
        Device = createInfo.LogicalDevice;

        _vulkanAPIVersion = createInfo.VulkanAPIVersion;

        if (_vulkanAPIVersion == 0)
        {
            _vulkanAPIVersion = Vk.Version10;
        }

        _useExtMemoryBudget = (createInfo.Flags & AllocatorCreateFlags.ExtMemoryBudget) != 0;
        _useAMDDeviceCoherentMemory = (createInfo.Flags & AllocatorCreateFlags.AMDDeviceCoherentMemory) != 0;
        UseKhrBufferDeviceAddress = (createInfo.Flags & AllocatorCreateFlags.BufferDeviceAddress) != 0;
        _useExtMemoryPriority = (createInfo.Flags & AllocatorCreateFlags.ExtMemoryPriority) != 0;

        IsMaintenance4Supported = Helpers.HasDeviceExtension(VkApi, _physicalDevice, "VK_KHR_maintenance4");
        IsMaintenance5Supported = Helpers.HasDeviceExtension(VkApi, _physicalDevice, "VK_KHR_maintenance5");
        IsExternalMemoryWin32Supported = Helpers.HasDeviceExtension(VkApi, _physicalDevice, "VK_KHR_external_memory_win32");

        UseLock = (createInfo.Flags & AllocatorCreateFlags.ExternallySyncronized) == 0;

        ThrowOnError = createInfo.ThrowOnError;

        _deviceMemoryCallbacks = createInfo.DeviceMemoryCallbacks;
        _allocationCallbacks = createInfo.AllocationCallbacks;

        VkApi.GetPhysicalDeviceProperties(_physicalDevice, out _physicalDevicePropertiesValue);
        VkApi.GetPhysicalDeviceMemoryProperties(_physicalDevice, out _memoryPropertiesValue);

        Debug.Assert(Helpers.IsPow2(Helpers.DEBUG_ALIGNMENT));
        Debug.Assert(Helpers.IsPow2(Helpers.DEBUG_MIN_BUFFER_IMAGE_GRANULARITY));
        Debug.Assert(Helpers.IsPow2((long)PhysicalDeviceProperties.Limits.BufferImageGranularity));
        Debug.Assert(Helpers.IsPow2((long)PhysicalDeviceProperties.Limits.NonCoherentAtomSize));

        _preferredLargeHeapBlockSize = (createInfo.PreferredLargeHeapBlockSize != 0) ? createInfo.PreferredLargeHeapBlockSize : (256L * 1024 * 1024);

        GlobalMemoryTypeBits = CalculateGlobalMemoryTypeBits();

        if (createInfo.HeapSizeLimits != null)
        {
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP3_1_OR_GREATER
            var memoryHeaps = MemoryMarshal.CreateSpan(ref GetMemoryHeaps().Element0, MemoryHeapCount);
#else
            var memoryHeaps = new Span<MemoryHeap>(
            Unsafe.AsPointer(ref GetMemoryHeaps().Element0), MemoryHeapCount);
#endif

            var heapLimitLength = Math.Min(createInfo.HeapSizeLimits.Length, MemoryHeapCount);

            for (var heapIndex = 0; heapIndex < heapLimitLength; ++heapIndex)
            {
                var limit = createInfo.HeapSizeLimits[heapIndex];

                if (limit <= 0)
                {
                    continue;
                }

                _heapSizeLimitMask |= 1u << heapIndex;
                ref var heap = ref memoryHeaps[heapIndex];

                if ((ulong)limit < heap.Size)
                {
                    heap.Size = (ulong)limit;
                }
            }
        }

        for (var memTypeIndex = 0; memTypeIndex < MemoryTypeCount; ++memTypeIndex)
        {
            var preferredBlockSize = CalcPreferredBlockSize(memTypeIndex);

            BlockLists[memTypeIndex] =
                new BlockList(this, null, memTypeIndex, preferredBlockSize, 0, int.MaxValue, BufferImageGranularity, false, GetMemoryTypeMinAlignment(memTypeIndex), size => new BlockMetadataTlsf(size, DebugMargin));

            ref var alloc = ref DedicatedAllocations[memTypeIndex];

            alloc.Allocations = [];
            alloc.Lock = new ReaderWriterLockSlim();
        }

        if (_useExtMemoryBudget)
        {
            UpdateVulkanBudget();
        }
    }

    /// <summary>
    /// Disposes this allocator and frees all underlying Vulkan device memory.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

#if DEBUG
    /// <summary>
    /// Finalizes the allocator, warning if it was not disposed properly.
    /// </summary>
    ~VulkanMemoryAllocator()
    {
        if (!_disposed)
        {
            Debug.Fail("VulkanMemoryAllocator was not disposed. Vulkan device memory may be leaked.");
        }
    }
#endif

    private void Dispose(bool disposing)
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

        if (_pools.Count != 0)
        {
            throw new InvalidOperationException("Cannot dispose VulkanMemoryAllocator: not all VulkanMemoryPool instances were disposed.");
        }

        var i = MemoryTypeCount;

        while (i-- != 0)
        {
            if (DedicatedAllocations[i].Allocations.Count != 0)
            {
                throw new InvalidOperationException("Cannot dispose VulkanMemoryAllocator: unfreed dedicated allocations remain.");
            }

            BlockLists[i].Dispose();
        }

    }

    /// <summary>
    /// Returns the memory property flags of the Vulkan memory type at the given index.
    /// </summary>
    /// <param name="memoryTypeIndex">Index of the Vulkan memory type to query.</param>
    /// <returns>The memory property flags of the memory type.</returns>
    public MemoryPropertyFlags GetMemoryTypeProperties(int memoryTypeIndex) => GetMemoryTypes()[memoryTypeIndex].PropertyFlags;

    /// <summary>
    /// Helps to find memoryTypeIndex, given memoryTypeBits and <see cref="AllocationCreateInfo"/>.
    /// </summary>
    /// <param name="memoryTypeBits">Bit mask of allowed memory types.</param>
    /// <param name="allocInfo">Allocation creation info describing requirements.</param>
    /// <param name="memoryTypeIndex">Receives the index of the selected memory type, or null if none found.</param>
    /// <returns>The Vulkan Result of the operation.</returns>
    public Result FindMemoryTypeIndex(uint memoryTypeBits,
                                      in AllocationCreateInfo allocInfo,
                                      out int? memoryTypeIndex)
        => FindMemoryTypeIndex(memoryTypeBits, in allocInfo, in BufferImageUsageInfo.Unknown, out memoryTypeIndex);

    private Result FindMemoryTypeIndex(uint memoryTypeBits,
                                       in AllocationCreateInfo allocInfo,
                                       in BufferImageUsageInfo bufImgUsage,
                                       out int? memoryTypeIndex)
    {
        memoryTypeBits &= GlobalMemoryTypeBits;

        if (allocInfo.MemoryTypeBits != 0)
        {
            memoryTypeBits &= allocInfo.MemoryTypeBits;
        }

        if (!CalcMemoryPreferences(in allocInfo, in bufImgUsage, out var requiredFlags, out var preferredFlags, out var notPreferredFlags))
        {
            memoryTypeIndex = null;
            return Result.ErrorFeatureNotPresent.ThrowOrReturn<AllocationException>(ThrowOnError, "Unable to resolve memory preferences for allocation");
        }

        memoryTypeIndex = null;
        var minCost = int.MaxValue;
        uint memTypeBit = 1;

        for (var memTypeIndex = 0; memTypeIndex < MemoryTypeCount; ++memTypeIndex, memTypeBit <<= 1)
        {
            if ((memTypeBit & memoryTypeBits) == 0)
            {
                continue;
            }

            var currFlags = GetMemoryTypes()[memTypeIndex].PropertyFlags;

            if ((requiredFlags & ~currFlags) != 0)
            {
                continue;
            }

            var currCost = BitOps.PopCount((uint)(preferredFlags & ~currFlags));

            currCost += BitOps.PopCount((uint)(currFlags & notPreferredFlags));

            if (currCost < minCost)
            {
                if (currCost == 0)
                {
                    memoryTypeIndex = memTypeIndex;
                    return Result.Success;
                }
                memoryTypeIndex = memTypeIndex;
                minCost = currCost;
            }
        }

        if (memoryTypeIndex.HasValue)
        {
            return Result.Success;
        }

        return Result.ErrorFeatureNotPresent.ThrowOrReturn<AllocationException>(ThrowOnError, "Unable to find suitable memory type for allocation");
    }

    // VMA's FindMemoryPreferences: converts a MemoryUsage (together with the resource's
    // buffer/image usage and the host-access allocation flags) into required/preferred/not-preferred
    // MemoryPropertyFlags. Returns false when an AUTO* usage is used without resource information
    // (bufImgUsage.IsUnknown), which VMA treats as an error.
    private bool CalcMemoryPreferences(in AllocationCreateInfo allocInfo,
                                       in BufferImageUsageInfo bufImgUsage,
                                       out MemoryPropertyFlags requiredFlags,
                                       out MemoryPropertyFlags preferredFlags,
                                       out MemoryPropertyFlags notPreferredFlags)
    {
        requiredFlags = allocInfo.RequiredFlags;
        preferredFlags = allocInfo.PreferredFlags;
        notPreferredFlags = default;

        switch (allocInfo.Usage)
        {
            case MemoryUsage.Unknown:
                break;
            case MemoryUsage.GpuOnly:
                if (IsIntegratedGPU || (preferredFlags & MemoryPropertyFlags.HostVisibleBit) == 0)
                {
                    preferredFlags |= MemoryPropertyFlags.DeviceLocalBit;
                }
                break;
            case MemoryUsage.CpuOnly:
                requiredFlags |= MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit;
                break;
            case MemoryUsage.CpuToGpu:
                requiredFlags |= MemoryPropertyFlags.HostVisibleBit;
                if (!IsIntegratedGPU || (preferredFlags & MemoryPropertyFlags.HostVisibleBit) == 0)
                {
                    preferredFlags |= MemoryPropertyFlags.DeviceLocalBit;
                }
                break;
            case MemoryUsage.GpuToCpu:
                requiredFlags |= MemoryPropertyFlags.HostVisibleBit;
                preferredFlags |= MemoryPropertyFlags.HostCachedBit;
                break;
            case MemoryUsage.CpuCopy:
                notPreferredFlags |= MemoryPropertyFlags.DeviceLocalBit;
                break;
            case MemoryUsage.GpuLazilyAllocated:
                requiredFlags |= MemoryPropertyFlags.LazilyAllocatedBit;
                break;
            case MemoryUsage.Auto:
            case MemoryUsage.AutoPreferDevice:
            case MemoryUsage.AutoPreferHost:
                {
                    if (bufImgUsage.IsUnknown)
                    {
                        return false;
                    }

                    var deviceAccess = bufImgUsage.ContainsDeviceAccess();
                    var hostAccessSeqWrite = (allocInfo.Flags & AllocationCreateFlags.HostAccessSequentialWrite) != 0;
                    var hostAccessRandom = (allocInfo.Flags & AllocationCreateFlags.HostAccessRandom) != 0;
                    var hostAccessTransfer = (allocInfo.Flags & AllocationCreateFlags.HostAccessAllowTransferInstead) != 0;
                    var preferDevice = allocInfo.Usage == MemoryUsage.AutoPreferDevice;
                    var preferHost = allocInfo.Usage == MemoryUsage.AutoPreferHost;

                    if (hostAccessRandom)
                    {
                        preferredFlags |= MemoryPropertyFlags.HostCachedBit;

                        if (!IsIntegratedGPU && deviceAccess && hostAccessTransfer && !preferHost)
                        {
                            preferredFlags |= MemoryPropertyFlags.DeviceLocalBit;
                        }
                        else
                        {
                            if (hostAccessTransfer)
                            {
                                preferredFlags |= MemoryPropertyFlags.HostVisibleBit;
                            }
                            else
                            {
                                requiredFlags |= MemoryPropertyFlags.HostVisibleBit;
                            }
                        }
                    }
                    else if (hostAccessSeqWrite)
                    {
                        notPreferredFlags |= MemoryPropertyFlags.HostCachedBit;

                        if (!IsIntegratedGPU && deviceAccess && hostAccessTransfer && !preferHost)
                        {
                            preferredFlags |= MemoryPropertyFlags.DeviceLocalBit | MemoryPropertyFlags.HostVisibleBit;
                        }
                        else
                        {
                            if (hostAccessTransfer)
                            {
                                preferredFlags |= MemoryPropertyFlags.HostVisibleBit;
                            }
                            else
                            {
                                requiredFlags |= MemoryPropertyFlags.HostVisibleBit;
                            }

                            if (deviceAccess)
                            {
                                if (preferHost)
                                {
                                    notPreferredFlags |= MemoryPropertyFlags.DeviceLocalBit;
                                }
                                else
                                {
                                    preferredFlags |= MemoryPropertyFlags.DeviceLocalBit;
                                }
                            }
                            else
                            {
                                if (preferDevice)
                                {
                                    preferredFlags |= MemoryPropertyFlags.DeviceLocalBit;
                                }
                                else
                                {
                                    notPreferredFlags |= MemoryPropertyFlags.DeviceLocalBit;
                                }
                            }
                        }
                    }
                    else
                    {
                        if (preferHost)
                        {
                            notPreferredFlags |= MemoryPropertyFlags.DeviceLocalBit;
                        }
                        else
                        {
                            preferredFlags |= MemoryPropertyFlags.DeviceLocalBit;
                        }
                    }
                    break;
                }
            default:
                throw new ArgumentException("Invalid Usage Flags");
        }

        if (((allocInfo.RequiredFlags | allocInfo.PreferredFlags) & (MemoryPropertyFlags.DeviceCoherentBitAmd | MemoryPropertyFlags.DeviceUncachedBitAmd)) == 0)
        {
            notPreferredFlags |= MemoryPropertyFlags.DeviceCoherentBitAmd;
        }

        return true;
    }

    /// <summary>
    /// Helps to find memoryTypeIndex, given VkBufferCreateInfo and <see cref="AllocationCreateInfo"/>.
    /// </summary>
    /// <param name="bufferInfo">VkBufferCreateInfo describing the buffer to allocate memory for.</param>
    /// <param name="allocInfo">Allocation creation info describing requirements.</param>
    /// <param name="typeIndex">Receives the index of the selected memory type, or null if none found.</param>
    /// <returns>The Vulkan Result of the operation.</returns>
    public Result FindMemoryTypeIndexForBufferInfo(in BufferCreateInfo bufferInfo,
                                                   in AllocationCreateInfo allocInfo,
                                                   out int? typeIndex)
    {
        if (IsMaintenance4Supported)
        {
            DeviceBufferMemoryRequirements req;
            fixed (BufferCreateInfo* pBufferInfo = &bufferInfo)
            {
                req = new DeviceBufferMemoryRequirements
                {
                    SType = StructureType.DeviceBufferMemoryRequirements,
                    PCreateInfo = pBufferInfo
                };

                var memReq2 = new MemoryRequirements2 { SType = StructureType.MemoryRequirements2 };
                VkApi.GetDeviceBufferMemoryRequirements(Device, &req, &memReq2);

                var usage = BufferUsageResolver.Resolve(in bufferInfo);
                return FindMemoryTypeIndex(memReq2.MemoryRequirements.MemoryTypeBits, in allocInfo, in usage, out typeIndex);
            }
        }

        Buffer buffer;
        fixed (BufferCreateInfo* pBufferInfo = &bufferInfo)
        {
            var res = VkApi.CreateBuffer(Device, pBufferInfo, null, &buffer);

            if (res.IsError())
            {
                typeIndex = null;
                return res.ThrowOrReturn<AllocationException>(ThrowOnError, "Buffer creation failed");
            }
        }

        MemoryRequirements memReq;
        VkApi.GetBufferMemoryRequirements(Device, buffer, &memReq);

        var bufImgUsage = BufferUsageResolver.Resolve(in bufferInfo);
        var tmp = FindMemoryTypeIndex(memReq.MemoryTypeBits, in allocInfo, in bufImgUsage, out typeIndex);

        VkApi.DestroyBuffer(Device, buffer, null);

        return tmp;
    }

    /// <summary>
    /// Helps to find memoryTypeIndex, given VkImageCreateInfo and <see cref="AllocationCreateInfo"/>.
    /// </summary>
    /// <param name="imageInfo">VkImageCreateInfo describing the image to allocate memory for.</param>
    /// <param name="allocInfo">Allocation creation info describing requirements.</param>
    /// <param name="typeIndex">Receives the index of the selected memory type, or null if none found.</param>
    /// <returns>The Vulkan Result of the operation.</returns>
    public Result FindMemoryTypeIndexForImageInfo(in ImageCreateInfo imageInfo,
                                                  in AllocationCreateInfo allocInfo,
                                                  out int? typeIndex)
    {
        if (IsMaintenance4Supported)
        {
            DeviceImageMemoryRequirements req;
            fixed (ImageCreateInfo* pImageInfo = &imageInfo)
            {
                req = new DeviceImageMemoryRequirements
                {
                    SType = StructureType.DeviceImageMemoryRequirements,
                    PCreateInfo = pImageInfo
                };

                var memReq2 = new MemoryRequirements2 { SType = StructureType.MemoryRequirements2 };
                VkApi.GetDeviceImageMemoryRequirements(Device, &req, &memReq2);

                var usage = new BufferImageUsageInfo((uint)imageInfo.Usage);
                return FindMemoryTypeIndex(memReq2.MemoryRequirements.MemoryTypeBits, in allocInfo, in usage, out typeIndex);
            }
        }

        Image image;
        fixed (ImageCreateInfo* pImageInfo = &imageInfo)
        {
            var res = VkApi.CreateImage(Device, pImageInfo, null, &image);

            if (res.IsError())
            {
                typeIndex = null;
                return res.ThrowOrReturn<AllocationException>(ThrowOnError, "Image creation failed");
            }
        }

        MemoryRequirements memReq;
        VkApi.GetImageMemoryRequirements(Device, image, &memReq);

        var bufImgUsage = new BufferImageUsageInfo((uint)imageInfo.Usage);
        var tmp = FindMemoryTypeIndex(memReq.MemoryTypeBits, in allocInfo, in bufImgUsage, out typeIndex);

        VkApi.DestroyImage(Device, image, null);

        return tmp;
    }

    /// <summary>
    /// General purpose memory allocation.
    /// </summary>
    /// <param name="requirements">VkMemoryRequirements describing the size and alignment needed.</param>
    /// <param name="createInfo">Allocation creation info describing usage, flags, and other parameters.</param>
    /// <param name="allocation">Receives the created <see cref="Allocation"/> if successful.</param>
    /// <returns>The Vulkan Result of the allocation.</returns>
    public Result AllocateMemory(in MemoryRequirements requirements,
                                 in AllocationCreateInfo createInfo,
                                 out Allocation? allocation)
    {
        var dedicatedInfo = DedicatedAllocationInfo.Default;

        return AllocateMemory(in requirements, in dedicatedInfo, in createInfo, SuballocationType.Unknown, out allocation);
    }

    /// <summary>
    /// General purpose memory allocation for multiple allocation objects at once.
    /// </summary>
    /// <param name="requirements">VkMemoryRequirements describing the size and alignment needed.</param>
    /// <param name="createInfo">Allocation creation info describing usage, flags, and other parameters.</param>
    /// <param name="allocationCount">Number of allocations to create.</param>
    /// <param name="allocations">Receives the array of created <see cref="Allocation"/> objects if successful.</param>
    /// <returns>The Vulkan Result of the allocation.</returns>
    public Result AllocateMemoryPages(in MemoryRequirements requirements,
                                      in AllocationCreateInfo createInfo,
                                      int allocationCount,
                                      out Allocation[] allocations)
    {
        if (allocationCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(allocationCount));
        }

        if (allocationCount == 0)
        {
            allocations = [];
            return Result.Success;
        }

        allocations = new Allocation[allocationCount];

        for (var i = 0; i < allocationCount; ++i)
        {
            var res = AllocateMemory(in requirements, in createInfo, out var alloc);

            if (res.IsError())
            {
                for (var j = 0; j < i; ++j)
                {
                    FreeMemory(allocations[j]);
                }

                return res;
            }

            allocations[i] = alloc!;
        }

        return Result.Success;
    }

    /// <summary>
    /// Allocates memory suitable for given VkBuffer.
    /// </summary>
    /// <param name="buffer">The VkBuffer to allocate memory for.</param>
    /// <param name="createInfo">Allocation creation info describing usage, flags, and other parameters.</param>
    /// <param name="allocation">Receives the created <see cref="Allocation"/> if successful.</param>
    /// <param name="BindToBuffer">If true, bind the allocation to the buffer automatically.</param>
    /// <returns>The Vulkan Result of the allocation.</returns>
    public Result AllocateMemoryForBuffer(Buffer buffer,
                                          in AllocationCreateInfo createInfo,
                                          out Allocation? allocation,
                                          bool BindToBuffer = false)
    {
        var dedicatedInfo = DedicatedAllocationInfo.Default;

        dedicatedInfo.DedicatedBuffer = buffer;

        GetBufferMemoryRequirements(buffer,
                                    out var memReq,
                                    out dedicatedInfo.RequiresDedicatedAllocation,
                                    out dedicatedInfo.PrefersDedicatedAllocation);

        var res = AllocateMemory(in memReq,
                                 in dedicatedInfo,
                                 in createInfo,
                                 SuballocationType.Buffer,
                                 out allocation);

        if (res.IsError())
        {
            return res;
        }

        if (BindToBuffer)
        {
            res = allocation!.BindBufferMemory(buffer);

            if (res.IsError())
            {
                FreeMemory(allocation);
                allocation = null!;
                return res;
            }
        }

        return Result.Success;
    }

    /// <summary>
    /// Allocates memory suitable for given VkImage.
    /// </summary>
    /// <param name="image">The VkImage to allocate memory for.</param>
    /// <param name="createInfo">Allocation creation info describing usage, flags, and other parameters.</param>
    /// <param name="allocation">Receives the created <see cref="Allocation"/> if successful.</param>
    /// <param name="suballocType">Suballocation type derived from image tiling.</param>
    /// <param name="BindToImage">If true, bind the allocation to the image automatically.</param>
    /// <returns>The Vulkan Result of the allocation.</returns>
    public Result AllocateMemoryForImage(Image image,
                                         in AllocationCreateInfo createInfo,
                                         out Allocation? allocation,
                                         SuballocationType suballocType = SuballocationType.ImageUnknown,
                                         bool BindToImage = false)
            => AllocateMemoryForImage(image, in createInfo, in BufferImageUsageInfo.Unknown, out allocation, suballocType, BindToImage);

    private Result AllocateMemoryForImage(Image image,
                                          in AllocationCreateInfo createInfo,
                                          in BufferImageUsageInfo bufImgUsage,
                                          out Allocation? allocation,
                                          SuballocationType suballocType = SuballocationType.ImageUnknown,
                                          bool BindToImage = false)
    {
        var dedicatedInfo = DedicatedAllocationInfo.Default;

        dedicatedInfo.DedicatedImage = image;

        GetImageMemoryRequirements(image,
                                   out var memReq,
                                   out dedicatedInfo.RequiresDedicatedAllocation,
                                   out dedicatedInfo.PrefersDedicatedAllocation);

        var res = AllocateMemory(in memReq,
                                 in dedicatedInfo,
                                 in createInfo,
                                 suballocType,
                                 in bufImgUsage,
                                 out allocation);

        if (res.IsError())
        {
            return res;
        }

        if (BindToImage)
        {
            res = allocation!.BindImageMemory(image);

            if (res.IsError())
            {
                FreeMemory(allocation);
                allocation = null!;
                return res;
            }
        }

        return Result.Success;
    }

    /// <summary>
    /// Checks magic number in margins around all allocations in given memory types in search for corruptions.
    /// </summary>
    /// <param name="memoryTypeBits">Bit mask of memory types to check. 0 means all memory types.</param>
    /// <returns>The Vulkan Result of the corruption check.</returns>
    public Result CheckCorruption(uint memoryTypeBits)
    {
        if (!DebugDetectCorruption)
        {
            return Result.ErrorFeatureNotPresent;
        }

        if (memoryTypeBits == 0)
        {
            memoryTypeBits = uint.MaxValue;
        }

        var result = Result.Success;

        for (uint i = 0; i < BlockLists.Length; ++i)
        {
            if ((memoryTypeBits & (1u << (int)i)) != 0)
            {
                var r = BlockLists[i].CheckCorruption();

                if (r.IsError())
                {
                    result = r;
                }
            }
        }

        _poolsLock.EnterReadLock(UseLock);

        try
        {
            foreach (var pool in _pools)
            {
                var r = pool.BlockList.CheckCorruption();

                if (r.IsError())
                {
                    result = r;
                }
            }
        }
        finally
        {
            _poolsLock.ExitReadLock(UseLock);
        }

        return result;
    }

    /// <summary>
    /// Creates a new VkBuffer, allocates and binds memory for it.
    /// </summary>
    /// <param name="bufferInfo">VkBufferCreateInfo describing the buffer to create.</param>
    /// <param name="allocInfo">Allocation creation info describing usage, flags, and other parameters.</param>
    /// <param name="buffer">Receives the created <c>Buffer</c>.</param>
    /// <param name="allocation">Receives the created <see cref="Allocation"/> if successful.</param>
    /// <returns>The Vulkan Result of the operation.</returns>
    public Result CreateBuffer(in BufferCreateInfo bufferInfo,
                               in AllocationCreateInfo allocInfo,
                               out Buffer buffer,
                               out Allocation? allocation)
    {
        buffer = default;
        allocation = null!;

        Result res;
        Buffer localBuffer;

        fixed (BufferCreateInfo* pInfo = &bufferInfo)
        {
            res = VkApi.CreateBuffer(Device, pInfo, null, &localBuffer);

            if (res < 0)
            {
                return res.ThrowOrReturn<AllocationException>(ThrowOnError, "Buffer creation failed");
            }
        }

        DedicatedAllocationInfo dedicatedInfo = default;

        dedicatedInfo.DedicatedBuffer = localBuffer;
        var bufImgUsage = BufferUsageResolver.Resolve(in bufferInfo);
        dedicatedInfo.DedicatedBufferUsage = (BufferUsageFlags)bufImgUsage.Value;

        GetBufferMemoryRequirements(localBuffer,
                                    out var memReq,
                                    out dedicatedInfo.RequiresDedicatedAllocation,
                                    out dedicatedInfo.PrefersDedicatedAllocation);

        res = AllocateMemory(in memReq,
                             in dedicatedInfo,
                             in allocInfo,
                             SuballocationType.Buffer,
                             in bufImgUsage,
                             out var alloc);

        if (res.IsError())
        {
            VkApi.DestroyBuffer(Device, localBuffer, null);
            return res;
        }

        if ((allocInfo.Flags & AllocationCreateFlags.DontBind) == 0)
        {
            res = alloc!.BindBufferMemory(localBuffer);

            if (res.IsError())
            {
                VkApi.DestroyBuffer(Device, localBuffer, null);
                alloc.Dispose();

                return res.ThrowOrReturn<AllocationException>(ThrowOnError, "Unable to bind memory to buffer");
            }
        }

        allocation = alloc;

        if (alloc is BlockAllocation blockAlloc)
        {
            blockAlloc.Buffer = localBuffer;
        }

        buffer = localBuffer;
        return Result.Success;
    }

    /// <summary>
    /// Function similar to <c>vmaCreateBuffer</c> but for images. Creates a new VkImage, allocates and binds memory for it.
    /// </summary>
    /// <param name="imageInfo">VkImageCreateInfo describing the image to create.</param>
    /// <param name="allocInfo">Allocation creation info describing usage, flags, and other parameters.</param>
    /// <param name="image">Receives the created <c>Image</c>.</param>
    /// <param name="allocation">Receives the created <see cref="Allocation"/> if successful.</param>
    /// <returns>The Vulkan Result of the operation.</returns>
    public Result CreateImage(in ImageCreateInfo imageInfo,
                              in AllocationCreateInfo allocInfo,
                              out Image image,
                              out Allocation? allocation)
    {
        image = default;
        allocation = null!;

        if (imageInfo.Extent.Width == 0 ||
            imageInfo.Extent.Height == 0 ||
            imageInfo.Extent.Depth == 0 ||
            imageInfo.MipLevels == 0 ||
            imageInfo.ArrayLayers == 0)
        {
            throw new ArgumentException("Invalid Image Info");
        }

        Result res;
        Image localImage;

        fixed (ImageCreateInfo* pInfo = &imageInfo)
        {
            res = VkApi.CreateImage(Device, pInfo, null, &localImage);

            if (res < 0)
            {
                return res.ThrowOrReturn<AllocationException>(ThrowOnError, "Image creation failed");
            }
        }

        var suballocType = imageInfo.Tiling == ImageTiling.Optimal ? SuballocationType.ImageOptimal : SuballocationType.ImageLinear;
        var bufImgUsage = new BufferImageUsageInfo((uint)imageInfo.Usage);

        res = AllocateMemoryForImage(localImage, allocInfo, in bufImgUsage, out var alloc, suballocType);

        if (res.IsError())
        {
            VkApi.DestroyImage(Device, localImage, null);
            return res;
        }

        if ((allocInfo.Flags & AllocationCreateFlags.DontBind) == 0)
        {
            res = alloc!.BindImageMemory(localImage);

            if (res.IsError())
            {
                VkApi.DestroyImage(Device, localImage, null);
                alloc.Dispose();

                return res.ThrowOrReturn<AllocationException>(ThrowOnError, "Unable to Bind memory to image");
            }
        }

        allocation = alloc;
        image = localImage;
        return Result.Success;
    }

    /// <summary>
    /// Creates a new VkBuffer bound to an existing allocation at offset 0.
    /// </summary>
    /// <param name="allocation">The existing allocation to bind the buffer to.</param>
    /// <param name="bufferInfo">VkBufferCreateInfo describing the buffer to create.</param>
    /// <param name="buffer">Receives the created <c>Buffer</c>.</param>
    /// <returns>The Vulkan Result of the operation.</returns>
    public Result CreateAliasingBuffer(Allocation allocation,
                                       in BufferCreateInfo bufferInfo,
                                       out Buffer buffer)
        => CreateAliasingBuffer(allocation, 0, in bufferInfo, out buffer);

    /// <summary>
    /// Creates a new VkBuffer bound to an existing allocation at a local offset.
    /// </summary>
    /// <param name="allocation">The existing allocation to bind the buffer to.</param>
    /// <param name="allocationLocalOffset">Local offset within the allocation to bind the buffer at.</param>
    /// <param name="bufferInfo">VkBufferCreateInfo describing the buffer to create.</param>
    /// <param name="buffer">Receives the created <c>Buffer</c>.</param>
    /// <returns>The Vulkan Result of the operation.</returns>
    public Result CreateAliasingBuffer(Allocation allocation,
                                       long allocationLocalOffset,
                                       in BufferCreateInfo bufferInfo,
                                       out Buffer buffer)
    {
        buffer = default;

        if (bufferInfo.Size == 0 || allocationLocalOffset + (long)bufferInfo.Size > allocation.Size)
        {
            return Result.ErrorInitializationFailed;
        }

        fixed (BufferCreateInfo* pInfo = &bufferInfo)
        {
            var res = VkApi.CreateBuffer(Device, pInfo, null, out var localBuffer);

            if (res < 0)
            {
                return res;
            }

            buffer = localBuffer;
        }

        var bindRes = allocation.BindBufferMemory(buffer, allocationLocalOffset, null);

        if (bindRes.IsError())
        {
            VkApi.DestroyBuffer(Device, buffer, null);
            buffer = default;
        }

        return bindRes;
    }

    /// <summary>
    /// Creates a new VkImage bound to an existing allocation at offset 0.
    /// </summary>
    /// <param name="allocation">The existing allocation to bind the image to.</param>
    /// <param name="imageInfo">VkImageCreateInfo describing the image to create.</param>
    /// <param name="image">Receives the created <see cref="Image"/>.</param>
    /// <returns>The Vulkan Result of the operation.</returns>
    public Result CreateAliasingImage(Allocation allocation,
                                      in ImageCreateInfo imageInfo,
                                      out Image image)
        => CreateAliasingImage(allocation, 0, in imageInfo, out image);

    /// <summary>
    /// Creates a new VkImage bound to an existing allocation at a local offset.
    /// </summary>
    /// <param name="allocation">The existing allocation to bind the image to.</param>
    /// <param name="allocationLocalOffset">Local offset within the allocation to bind the image at.</param>
    /// <param name="imageInfo">VkImageCreateInfo describing the image to create.</param>
    /// <param name="image">Receives the created <see cref="Image"/>.</param>
    /// <returns>The Vulkan Result of the operation.</returns>
    public Result CreateAliasingImage(Allocation allocation,
                                      long allocationLocalOffset,
                                      in ImageCreateInfo imageInfo,
                                      out Image image)
    {
        image = default;

        if (imageInfo.Extent.Width == 0 ||
            imageInfo.Extent.Height == 0 ||
            imageInfo.Extent.Depth == 0 ||
            imageInfo.MipLevels == 0 ||
            imageInfo.ArrayLayers == 0)
        {
            return Result.ErrorInitializationFailed;
        }

        fixed (ImageCreateInfo* pInfo = &imageInfo)
        {
            var res = VkApi.CreateImage(Device, pInfo, null, out var localImage);

            if (res < 0)
            {
                return res;
            }

            image = localImage;
        }

        var bindRes = allocation.BindImageMemory(image, allocationLocalOffset, null);

        if (bindRes.IsError())
        {
            VkApi.DestroyImage(Device, image, null);
            image = default;
        }

        return bindRes;
    }

    private ref PhysicalDeviceMemoryProperties.MemoryTypesBuffer GetMemoryTypes()
        => ref _memoryPropertiesValue.MemoryTypes;

    private ref PhysicalDeviceMemoryProperties.MemoryHeapsBuffer GetMemoryHeaps()
        => ref _memoryPropertiesValue.MemoryHeaps;

    internal int MemoryTypeIndexToHeapIndex(int typeIndex)
    {
        Debug.Assert(typeIndex < MemoryProperties.MemoryTypeCount);
        return (int)GetMemoryTypes()[typeIndex].HeapIndex;
    }

    private bool IsMemoryTypeNonCoherent(int memTypeIndex) =>
        (GetMemoryTypes()[memTypeIndex].PropertyFlags & (MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit)) == MemoryPropertyFlags.HostVisibleBit;

    internal long GetMemoryTypeMinAlignment(int memTypeIndex) =>
        IsMemoryTypeNonCoherent(memTypeIndex) ? (long)Math.Max(1, PhysicalDeviceProperties.Limits.NonCoherentAtomSize) : 1;

    private void GetBufferMemoryRequirements(Buffer buffer,
                                             out MemoryRequirements memReq,
                                             out bool requiresDedicatedAllocation,
                                             out bool prefersDedicatedAllocation)
    {
        var req = new BufferMemoryRequirementsInfo2
        {
            SType = StructureType.BufferMemoryRequirementsInfo2,
            Buffer = buffer
        };

        var dedicatedRequirements = new MemoryDedicatedRequirements
        {
            SType = StructureType.MemoryDedicatedRequirements,
        };

        var memReq2 = new MemoryRequirements2
        {
            SType = StructureType.MemoryRequirements2,
            PNext = &dedicatedRequirements
        };

        VkApi.GetBufferMemoryRequirements2(Device, &req, &memReq2);

        memReq = memReq2.MemoryRequirements;
        requiresDedicatedAllocation = (bool)dedicatedRequirements.RequiresDedicatedAllocation;
        prefersDedicatedAllocation = (bool)dedicatedRequirements.PrefersDedicatedAllocation;
    }

    private void GetImageMemoryRequirements(Image image,
                                            out MemoryRequirements memReq,
                                            out bool requiresDedicatedAllocation,
                                            out bool prefersDedicatedAllocation)
    {
        var req = new ImageMemoryRequirementsInfo2
        {
            SType = StructureType.ImageMemoryRequirementsInfo2,
            Image = image
        };

        var dedicatedRequirements = new MemoryDedicatedRequirements
        {
            SType = StructureType.MemoryDedicatedRequirements,
        };

        var memReq2 = new MemoryRequirements2
        {
            SType = StructureType.MemoryRequirements2,
            PNext = &dedicatedRequirements
        };

        VkApi.GetImageMemoryRequirements2(Device, &req, &memReq2);

        memReq = memReq2.MemoryRequirements;
        requiresDedicatedAllocation = (bool)dedicatedRequirements.RequiresDedicatedAllocation;
        prefersDedicatedAllocation = (bool)dedicatedRequirements.PrefersDedicatedAllocation;
    }

    private Result AllocateMemory(in MemoryRequirements memReq,
                                  in DedicatedAllocationInfo dedicatedInfo,
                                  in AllocationCreateInfo createInfo,
                                  SuballocationType suballocType,
                                  out Allocation? allocation)
        => AllocateMemory(in memReq, in dedicatedInfo, in createInfo, suballocType, in BufferImageUsageInfo.Unknown, out allocation);

    private Result AllocateMemory(in MemoryRequirements memReq,
                                  in DedicatedAllocationInfo dedicatedInfo,
                                  in AllocationCreateInfo createInfo,
                                  SuballocationType suballocType,
                                  in BufferImageUsageInfo bufImgUsage,
                                  out Allocation? allocation)
    {
        var req = memReq;
        if (createInfo.MinAlignment > 0)
        {
            req.Alignment = Math.Max(req.Alignment, (ulong)createInfo.MinAlignment);
        }

        Debug.Assert(Helpers.IsPow2((long)req.Alignment));

        if (createInfo.Usage is MemoryUsage.Auto or MemoryUsage.AutoPreferDevice or MemoryUsage.AutoPreferHost
            && (createInfo.Flags & AllocationCreateFlags.Mapped) != 0
            && (createInfo.Flags & (AllocationCreateFlags.HostAccessSequentialWrite | AllocationCreateFlags.HostAccessRandom)) == 0)
        {
            throw new ArgumentException(
                "When using MemoryUsage.Auto* with AllocationCreateFlags.Mapped, you must also specify " +
                "AllocationCreateFlags.HostAccessSequentialWrite or AllocationCreateFlags.HostAccessRandom.");
        }

        if (req.Size == 0)
        {
            throw new ArgumentException("Allocation size cannot be 0");
        }

        const AllocationCreateFlags CheckFlags1 = AllocationCreateFlags.DedicatedMemory | AllocationCreateFlags.NeverAllocate;

        if ((createInfo.Flags & CheckFlags1) == CheckFlags1)
        {
            throw new ArgumentException("Specifying AllocationCreateFlags.DedicatedMemory with AllocationCreateFlags.NeverAllocate is invalid");
        }

        if (dedicatedInfo.RequiresDedicatedAllocation)
        {
            if ((createInfo.Flags & AllocationCreateFlags.NeverAllocate) != 0)
            {
                allocation = null!;
                return Result.ErrorOutOfDeviceMemory.ThrowOrReturn<AllocationException>(ThrowOnError, "AllocationCreateFlags.NeverAllocate specified while dedicated allocation required");
            }

            if (createInfo.Pool != null)
            {
                throw new ArgumentException("Pool specified while dedicated allocation required");
            }
        }

        // A dedicated allocation requested through a pool must become its own separate VkDeviceMemory
        // (mirroring VmaBlockVector::Allocate's dedicated path), not a suballocation of the pool block.
        // Routing it to AllocateMemoryOfType also lets the pool's MemoryAllocateNext (e.g. external-memory
        // export info) be applied to that dedicated memory.
        if (createInfo.Pool != null && (createInfo.Flags & AllocationCreateFlags.DedicatedMemory) == 0)
        {
            var memoryTypeIndex = createInfo.Pool.BlockList.MemoryTypeIndex;
            var alignmentForPool = Math.Max((long)req.Alignment, GetMemoryTypeMinAlignment(memoryTypeIndex));

            var infoForPool = createInfo;

            if ((createInfo.Flags & AllocationCreateFlags.Mapped) != 0 && (GetMemoryTypes()[memoryTypeIndex].PropertyFlags & MemoryPropertyFlags.HostVisibleBit) == 0)
            {
                infoForPool.Flags &= ~AllocationCreateFlags.Mapped;
            }

            var res = createInfo.Pool.BlockList.Allocate((long)req.Size, alignmentForPool, infoForPool, suballocType, out var alloc);

            if (res.IsError())
            {
                allocation = null!;
                return res;
            }

            FillAllocation(alloc!, Helpers.ALLOCATION_FILL_PATTERN_CREATED);
            allocation = alloc!;
            return Result.Success;
        }
        else
        {
            int typeIndex;
            if (createInfo.Pool != null)
            {
                typeIndex = createInfo.Pool.BlockList.MemoryTypeIndex;
            }
            else
            {
                var memoryTypeBits = req.MemoryTypeBits;
                var res = FindMemoryTypeIndex(memoryTypeBits, createInfo, bufImgUsage, out var memoryTypeIndex);

                if (res.IsError())
                {
                    allocation = null!;
                    return res.ThrowOrReturn<AllocationException>(ThrowOnError, "Unable to find suitable memory type for allocation");
                }

                typeIndex = memoryTypeIndex!.Value;
            }

            var alignmentForType = Math.Max((long)req.Alignment, GetMemoryTypeMinAlignment(typeIndex));

            return AllocateMemoryOfType((long)req.Size, alignmentForType, in dedicatedInfo, in createInfo, typeIndex, suballocType, out allocation);
        }
    }

    /// <summary>
    /// Frees memory previously allocated using <c>AllocateMemory</c>, <c>AllocateMemoryForBuffer</c>, or <c>AllocateMemoryForImage</c>.
    /// </summary>
    /// <param name="allocation">The allocation to free.</param>
    internal void FreeMemory(Allocation allocation)
    {
        using var lockScope = new LockScope(_allocatorLock, UseLock);

        if (allocation.IsDisposed)
        {
            return;
        }

        if (allocation is BlockAllocation { FreedByDefrag: true })
        {
            allocation.MarkDisposed();
            return;
        }

        if (allocation is BlockAllocation blockAlloc)
        {
            BlockList list;
            var pool = blockAlloc.Block.ParentPool;

            if (pool != null)
            {
                list = pool.BlockList;
            }
            else
            {
                list = BlockLists[allocation.MemoryTypeIndex];
            }

            FillAllocation(allocation, Helpers.ALLOCATION_FILL_PATTERN_DESTROYED);

            list.Free(allocation);
        }
        else
        {
            var dedicated = (DedicatedAllocation)allocation;

            FillAllocation(allocation, Helpers.ALLOCATION_FILL_PATTERN_DESTROYED);

            FreeDedicatedMemory(dedicated);
        }

        // Imported memory is owned by the exporter, so it was never added to the budget and must
        // not be subtracted from it here.
        if (allocation is not DedicatedAllocation { IsImported: true })
        {
            Budget.RemoveAllocation(MemoryTypeIndexToHeapIndex(allocation.MemoryTypeIndex), allocation.Size);
        }

        // The cached base export handle (set on first GetMemoryWin32Handle) is owned by this allocation,
        // so it must be released when the allocation is freed, not left open until GC.
        if (allocation.Win32BaseHandle != 0)
        {
            Win32Native.CloseHandle(allocation.Win32BaseHandle);
            allocation.Win32BaseHandle = 0;
        }

        allocation.MarkDisposed();
    }

    /// <summary>
    /// Calculates and returns numeric statistics about the allocator.
    /// </summary>
    /// <returns>The calculated <see cref="Stats"/> for this allocator.</returns>
    public Stats CalculateStats()
    {
        var newStats = new Stats();

        for (var i = 0; i < MemoryTypeCount; ++i)
        {
            var list = BlockLists[i];

            list.AddStats(newStats);
        }

        _poolsLock.EnterReadLock(UseLock);

        try
        {
            foreach (var pool in _pools)
            {
                pool.BlockList.AddStats(newStats);
            }
        }
        finally
        {
            _poolsLock.ExitReadLock(UseLock);
        }

        for (var typeIndex = 0; typeIndex < MemoryTypeCount; ++typeIndex)
        {
            var heapIndex = MemoryTypeIndexToHeapIndex(typeIndex);

            ref var handler = ref DedicatedAllocations[typeIndex];

            handler.Lock.EnterReadLock(UseLock);

            try
            {
                foreach (var alloc in handler.Allocations)
                {
                    alloc.CalcStatsInfo(out var stat);

                    StatInfo.Add(ref newStats.TotalRef, stat);
                    StatInfo.Add(ref newStats.MemoryTypeArray[typeIndex], stat);
                    StatInfo.Add(ref newStats.MemoryHeapArray[heapIndex], stat);
                }
            }
            finally
            {
                handler.Lock.ExitReadLock(UseLock);
            }
        }

        newStats.PostProcess();

        return newStats;
    }

    /// <summary>
    /// Builds and returns a null-terminated string in JSON format with information about this allocator.
    /// </summary>
    /// <param name="detailedMap">If true, include a detailed map of all allocations.</param>
    /// <returns>A JSON string with statistics.</returns>
    public string BuildStatsString(bool detailedMap)
    {
        var phys = PhysicalDeviceProperties;
        var heapCount = MemoryHeapCount;
        var typeCount = MemoryTypeCount;
        var stats = CalculateStats();

        var budgets = new AllocationBudget[heapCount];
        GetBudget(budgets);

        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });

        writer.WriteStartObject();

        writer.WriteStartObject("General");
        writer.WriteString("API", "Vulkan");
        writer.WriteString("apiVersion", StatsString.FormatApiVersion(phys.ApiVersion));
        writer.WriteString("GPU", Marshal.PtrToStringAnsi((nint)phys.DeviceName) ?? "");
        writer.WriteNumber("deviceType", (uint)phys.DeviceType);
        writer.WriteNumber("maxMemoryAllocationCount", (ulong)phys.Limits.MaxMemoryAllocationCount);
        writer.WriteNumber("bufferImageGranularity", phys.Limits.BufferImageGranularity);
        writer.WriteNumber("nonCoherentAtomSize", phys.Limits.NonCoherentAtomSize);
        writer.WriteNumber("memoryHeapCount", heapCount);
        writer.WriteNumber("memoryTypeCount", typeCount);
        writer.WriteEndObject();

        StatsString.WriteDetailedStatistics(writer, "Total", stats.Total);

        writer.WriteStartObject("MemoryInfo");
        for (var heapIndex = 0; heapIndex < heapCount; ++heapIndex)
        {
            writer.WriteStartObject($"Heap {heapIndex}");

            StatsString.WriteHeapFlags(writer, "Flags", GetMemoryHeaps()[heapIndex].Flags);
            writer.WriteNumber("Size", GetMemoryHeaps()[heapIndex].Size);

            writer.WriteStartObject("Budget");
            writer.WriteNumber("BudgetBytes", (ulong)budgets[heapIndex].Budget);
            writer.WriteNumber("UsageBytes", (ulong)budgets[heapIndex].Usage);
            writer.WriteEndObject();

            StatsString.WriteDetailedStatistics(writer, "Stats", stats.MemoryHeap[heapIndex]);

            writer.WriteStartObject("MemoryPools");
            for (var typeIndex = 0; typeIndex < typeCount; ++typeIndex)
            {
                if (MemoryTypeIndexToHeapIndex(typeIndex) != heapIndex)
                {
                    continue;
                }

                writer.WriteStartObject($"Type {typeIndex}");
                StatsString.WriteTypeFlags(writer, "Flags", GetMemoryTypes()[typeIndex].PropertyFlags);
                StatsString.WriteDetailedStatistics(writer, "Stats", stats.MemoryType[typeIndex]);
                writer.WriteEndObject();
            }

            writer.WriteEndObject();

            writer.WriteEndObject();
        }

        writer.WriteEndObject();

        if (detailedMap)
        {
            WriteDetailedMap(writer);
        }

        writer.WriteEndObject();
        writer.Flush();

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private void WriteDetailedMap(Utf8JsonWriter writer)
    {
        writer.WriteStartObject("DetailedMap");

        writer.WriteStartObject("DefaultPools");
        for (var typeIndex = 0; typeIndex < MemoryTypeCount; ++typeIndex)
        {
            var blockList = BlockLists[typeIndex];

            var allocations = blockList.EnumerateAllocations().ToList();
            var hasDedicated = DedicatedAllocations[typeIndex].Allocations.Count > 0;

            if (allocations.Count == 0 && !hasDedicated)
            {
                continue;
            }

            writer.WriteStartObject($"Type {typeIndex}");
            writer.WriteNumber("PreferredBlockSize", (ulong)blockList.PreferredBlockSize);
            WriteBlocks(writer, allocations);

            writer.WriteStartArray("DedicatedAllocations");
            ref var handler = ref DedicatedAllocations[typeIndex];
            handler.Lock.EnterReadLock(UseLock);
            try
            {
                foreach (var alloc in handler.Allocations)
                {
                    writer.WriteStartObject();
                    writer.WriteNumber("Offset", 0UL);
                    writer.WriteNumber("Size", (ulong)alloc.Size);
                    writer.WriteString("Type", "DEDICATED");

                    if (alloc.UserData != null)
                    {
                        writer.WriteString("UserData", alloc.UserData.ToString() ?? "");
                    }

                    writer.WriteEndObject();
                }
            }
            finally
            {
                handler.Lock.ExitReadLock(UseLock);
            }

            writer.WriteEndArray();

            writer.WriteEndObject();
        }

        writer.WriteEndObject();

        writer.WriteStartObject("CustomPools");
        _poolsLock.EnterReadLock(UseLock);

        try
        {
            foreach (var pool in _pools)
            {
                var allocations = pool.BlockList.EnumerateAllocations().ToList();

                if (allocations.Count == 0)
                {
                    continue;
                }

                writer.WriteStartObject($"{pool.Id}");
                writer.WriteNumber("PreferredBlockSize", (ulong)pool.BlockList.PreferredBlockSize);
                WriteBlocks(writer, allocations);
                writer.WriteStartArray("DedicatedAllocations");
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
        }
        finally
        {
            _poolsLock.ExitReadLock(UseLock);
        }

        writer.WriteEndObject();

        writer.WriteEndObject();
    }

    private static void WriteBlocks(Utf8JsonWriter writer, List<BlockAllocation> allocations)
    {
        writer.WriteStartArray("Blocks");

        foreach (var group in allocations.GroupBy(a => a.Block))
        {
            var block = group.Key;
            var meta = block.MetaData;

            writer.WriteStartObject();
            writer.WriteNumber("TotalBytes", (ulong)meta.Size);
            writer.WriteNumber("UnusedBytes", (ulong)meta.SumFreeSize);
            writer.WriteNumber("Allocations", (ulong)group.Count());
            meta.CalcAllocationStatInfo(out var sinfo);
            writer.WriteNumber("UnusedRanges", sinfo.UnusedRangeCount);

            writer.WriteStartArray("Suballocations");
            foreach (var alloc in group)
            {
                writer.WriteStartObject();
                writer.WriteNumber("Offset", (ulong)alloc.Offset);
                writer.WriteNumber("Size", (ulong)alloc.Size);
                writer.WriteString("Type", StatsString.SuballocTypeName(alloc.SuballocationType));
                writer.WriteEndObject();
            }

            writer.WriteEndArray();

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    /// <summary>
    /// Returns information about the memory budget for the given memory heap.
    /// </summary>
    /// <param name="heapIndex">Index of the Vulkan memory heap to query.</param>
    /// <returns>The <see cref="AllocationBudget"/> for the specified heap.</returns>
    public AllocationBudget GetBudget(int heapIndex)
    {
        if ((uint)heapIndex >= (uint)MemoryHeapCount)
        {
            throw new ArgumentOutOfRangeException(nameof(heapIndex));
        }

        if (_useExtMemoryBudget)
        {
            if (Budget.OperationsSinceBudgetFetch >= 30)
            {
                UpdateVulkanBudget();
            }

            Budget.Lock.EnterReadLock(UseLock);
            try
            {
                ref var heapBudget = ref Budget.BudgetData[heapIndex];

                var outBudget = new AllocationBudget
                {
                    BlockBytes = heapBudget.BlockBytes,
                    AllocationBytes = heapBudget.AllocationBytes,
                    BlockCount = heapBudget.BlockCount,
                    AllocationCount = heapBudget.AllocationCount,
                };

                if (heapBudget.VulkanUsage + outBudget.BlockBytes > heapBudget.BlockBytesAtBudgetFetch)
                {
                    outBudget.Usage = heapBudget.VulkanUsage + outBudget.BlockBytes - heapBudget.BlockBytesAtBudgetFetch;
                }
                else
                {
                    outBudget.Usage = 0;
                }

                outBudget.Budget = Math.Min(heapBudget.VulkanBudget, (long)GetMemoryHeaps()[heapIndex].Size);

                return outBudget;
            }
            finally
            {
                Budget.Lock.ExitReadLock(UseLock);
            }
        }
        else
        {
            ref var heapBudget = ref Budget.BudgetData[heapIndex];

            return new AllocationBudget
            {
                BlockBytes = heapBudget.BlockBytes,
                AllocationBytes = heapBudget.AllocationBytes,
                BlockCount = heapBudget.BlockCount,
                AllocationCount = heapBudget.AllocationCount,
                Usage = heapBudget.BlockBytes,
                Budget = (long)(GetMemoryHeaps()[heapIndex].Size * 8 / 10),
            };
        }
    }

    /// <summary>
    /// Returns information about the memory budget for all memory heaps.
    /// </summary>
    /// <param name="outBudgets">Receives the array of <see cref="AllocationBudget"/> for each memory heap. Array length must be at least the number of memory heaps.</param>
    public void GetBudget(AllocationBudget[] outBudgets)
    {
        if (outBudgets.Length < MemoryHeapCount)
        {
            throw new ArgumentException($"Array length must be at least the number of memory heaps ({MemoryHeapCount}).", nameof(outBudgets));
        }

        for (var i = 0; i < MemoryHeapCount; ++i)
        {
            outBudgets[i] = GetBudget(i);
        }
    }

    /// <summary>
    /// Begins the defragmentation process.
    /// </summary>
    /// <param name="info">Defragmentation parameters.</param>
    /// <returns>A <see cref="DefragmentationContext"/> representing the ongoing defragmentation.</returns>
    public DefragmentationContext DefragmentationBegin(in DefragmentationInfo info)
        => new(this, in info);

    /// <summary>
    /// Creates a new <see cref="VirtualBlock"/> object.
    /// </summary>
    /// <param name="createInfo">Parameters for creating the virtual block.</param>
    /// <param name="virtualBlock">Receives the created <see cref="VirtualBlock"/> if successful.</param>
    /// <returns>The Vulkan Result of the operation.</returns>
    public Result CreateVirtualBlock(in VirtualBlockCreateInfo createInfo, out VirtualBlock? virtualBlock)
    {
        virtualBlock = null;

        if (createInfo.Size <= 0)
        {
            return Result.ErrorOutOfDeviceMemory;
        }

        var linear = (createInfo.Flags & VirtualBlockCreateFlags.LinearAlgorithm) != 0;

        // C++ does not apply VMA_DEBUG_MARGIN to the virtual allocator, so the trailing margin is zero.
        IBlockMetadata metadata = linear
            ? new BlockMetadataLinear(createInfo.Size, 0)
            : new BlockMetadataTlsf(createInfo.Size, 0);

        metadata.BufferImageGranularity = 1;

        virtualBlock = new VirtualBlock(this, metadata);

        return Result.Success;
    }

    /// <summary>
    /// Creates a new custom memory pool.
    /// </summary>
    /// <param name="createInfo">Configuration for the pool.</param>
    /// <returns>The created <see cref="VulkanMemoryPool"/>.</returns>
    public VulkanMemoryPool CreatePool(in AllocationPoolCreateInfo createInfo)
    {
        var tmpCreateInfo = createInfo;

        if (tmpCreateInfo.MaxBlockCount == 0)
        {
            tmpCreateInfo.MaxBlockCount = int.MaxValue;
        }

        if (tmpCreateInfo.MinBlockCount > tmpCreateInfo.MaxBlockCount)
        {
            throw new ArgumentException("Min block count is higher than max block count");
        }

        Debug.Assert(tmpCreateInfo.MinAllocationAlignment == 0 || Helpers.IsPow2(tmpCreateInfo.MinAllocationAlignment));

        if (tmpCreateInfo.MemoryTypeIndex >= MemoryTypeCount || ((1u << tmpCreateInfo.MemoryTypeIndex) & GlobalMemoryTypeBits) == 0)
        {
            throw new ArgumentException("Invalid memory type index");
        }

        var preferredBlockSize = CalcPreferredBlockSize(tmpCreateInfo.MemoryTypeIndex);

        var pool = new VulkanMemoryPool(this, tmpCreateInfo, preferredBlockSize);


        _poolsLock.EnterWriteLock(UseLock);
        try
        {
            _pools.Add(pool);
        }
        finally
        {
            _poolsLock.ExitWriteLock(UseLock);
        }

        return pool;
    }

    /// <summary>
    /// Creates a dedicated buffer while offering extra parameter pMemoryAllocateNext.
    /// </summary>
    /// <param name="bufferInfo">VkBufferCreateInfo describing the buffer to create.</param>
    /// <param name="allocInfo">Allocation creation info describing usage, flags, and other parameters.</param>
    /// <param name="memoryAllocateNext">Additional pNext chain to be attached to VkMemoryAllocateInfo.</param>
    /// <param name="buffer">Receives the created <c>Buffer</c>.</param>
    /// <param name="allocation">Receives the created <see cref="Allocation"/> if successful.</param>
    /// <returns>The Vulkan Result of the operation.</returns>
    public Result CreateDedicatedBuffer(in BufferCreateInfo bufferInfo,
                                        in AllocationCreateInfo allocInfo,
                                        void* memoryAllocateNext,
                                        out Buffer buffer,
                                        out Allocation? allocation)
    {
        var dedicatedInfo = new AllocationCreateInfo
        {
            Flags = allocInfo.Flags | AllocationCreateFlags.DedicatedMemory,
            Strategy = allocInfo.Strategy,
            Usage = allocInfo.Usage,
            RequiredFlags = allocInfo.RequiredFlags,
            PreferredFlags = allocInfo.PreferredFlags,
            MemoryTypeBits = allocInfo.MemoryTypeBits,
            Pool = allocInfo.Pool,
            UserData = allocInfo.UserData,
            MinAlignment = allocInfo.MinAlignment,
            Priority = allocInfo.Priority,
            MemoryAllocateNext = memoryAllocateNext,
        };

        return CreateBuffer(in bufferInfo, in dedicatedInfo, out buffer, out allocation);
    }

    /// <summary>
    /// Function similar to <c>vmaCreateDedicatedBuffer</c> but for images. Creates a dedicated image while offering extra parameter pMemoryAllocateNext.
    /// </summary>
    /// <param name="imageInfo">VkImageCreateInfo describing the image to create.</param>
    /// <param name="allocInfo">Allocation creation info describing usage, flags, and other parameters.</param>
    /// <param name="memoryAllocateNext">Additional pNext chain to be attached to VkMemoryAllocateInfo.</param>
    /// <param name="image">Receives the created <c>Image</c>.</param>
    /// <param name="allocation">Receives the created <see cref="Allocation"/> if successful.</param>
    /// <returns>The Vulkan Result of the operation.</returns>
    public Result CreateDedicatedImage(in ImageCreateInfo imageInfo,
                                       in AllocationCreateInfo allocInfo,
                                       void* memoryAllocateNext,
                                       out Image image,
                                       out Allocation? allocation)
    {
        if ((imageInfo.Flags & ImageCreateFlags.CreateDisjointBit) != 0)
        {
            throw new InvalidOperationException(
                "vmaCreateDedicatedImage() doesn't support disjoint multi-planar images. " +
                "Please allocate memory for the planes using CreateImage with a normal allocation " +
                "and bind them using BindImageMemory2().");
        }

        var dedicatedInfo = new AllocationCreateInfo
        {
            Flags = allocInfo.Flags | AllocationCreateFlags.DedicatedMemory,
            Strategy = allocInfo.Strategy,
            Usage = allocInfo.Usage,
            RequiredFlags = allocInfo.RequiredFlags,
            PreferredFlags = allocInfo.PreferredFlags,
            MemoryTypeBits = allocInfo.MemoryTypeBits,
            Pool = allocInfo.Pool,
            UserData = allocInfo.UserData,
            MinAlignment = allocInfo.MinAlignment,
            Priority = allocInfo.Priority,
            MemoryAllocateNext = memoryAllocateNext,
        };

        return CreateImage(in imageInfo, in dedicatedInfo, out image, out allocation);
    }

    /// <summary>
    /// Given an allocation, returns a Win32 handle that may be imported by other processes or APIs.
    /// </summary>
    /// <param name="allocation">The allocation to get the handle for.</param>
    /// <param name="handle">Receives the Win32 handle if successful.</param>
    /// <returns>The Vulkan Result of the operation.</returns>
    public Result GetMemoryWin32Handle(Allocation allocation, out nint handle)
    {
        handle = 0;

        // Vulkan (VK_EXTERNAL_MEMORY_HANDLE_TYPE_OPAQUE_WIN32_BIT) returns a valid, caller-owned handle
        // only once per memory object; subsequent queries yield 0. Mirror VMA by caching the first
        // vkGetMemoryWin32HandleKHR result per allocation and returning a fresh DuplicateHandle duplicate
        // on each call, so the two handles are distinct and each is closed by the caller.
        if (allocation.Win32BaseHandle == 0)
        {
            var info = new MemoryGetWin32HandleInfoKHR
            {
                SType = StructureType.MemoryGetWin32HandleInfoKhr,
                Memory = allocation.DeviceMemory,
                HandleType = ExternalMemoryHandleTypeFlags.OpaqueWin32Bit,
            };

            nint baseHandle = 0;
            var res = _khrExternalMemoryWin32.GetMemoryWin32Handle(Device, &info, &baseHandle);
            if (res.IsError())
            {
                return res;
            }

            allocation.Win32BaseHandle = baseHandle;
        }

        if (!Win32Native.DuplicateHandle(Win32Native.GetCurrentProcess(), allocation.Win32BaseHandle,
                Win32Native.GetCurrentProcess(), out handle, 0, false, 2 /* DUPLICATE_SAME_ACCESS */))
        {
            handle = 0;
            return Result.ErrorUnknown;
        }

        return Result.Success;
    }

    /// <summary>
    /// Given an allocation, returns a Win32 handle that may be imported by other processes or APIs.
    /// Uses <c>ExternalMemoryHandleTypeFlags</c> to specify handle type.
    /// </summary>
    /// <param name="allocation">The allocation to get the handle for.</param>
    /// <param name="handleType">The type of external memory handle to retrieve.</param>
    /// <param name="handle">Receives the Win32 handle if successful.</param>
    /// <returns>The Vulkan Result of the operation.</returns>
    public Result GetMemoryWin32Handle(Allocation allocation, ExternalMemoryHandleTypeFlags handleType, out nint handle)
    {
        handle = 0;

        // Mirror VmaWin32Handle::GetHandle: fetch the base handle once (cached on the allocation) and
        // DuplicateHandle on each call, so every returned handle is a distinct, caller-owned duplicate.
        if (allocation.Win32BaseHandle == 0)
        {
            var info = new MemoryGetWin32HandleInfoKHR
            {
                SType = StructureType.MemoryGetWin32HandleInfoKhr,
                Memory = allocation.DeviceMemory,
                HandleType = handleType,
            };

            nint baseHandle = 0;
            var res = _khrExternalMemoryWin32.GetMemoryWin32Handle(Device, &info, &baseHandle);
            if (res.IsError())
            {
                return res;
            }

            allocation.Win32BaseHandle = baseHandle;
        }

        if (!Win32Native.DuplicateHandle(Win32Native.GetCurrentProcess(), allocation.Win32BaseHandle,
                Win32Native.GetCurrentProcess(), out handle, 0, false, 2 /* DUPLICATE_SAME_ACCESS */))
        {
            handle = 0;
            return Result.ErrorUnknown;
        }

        return Result.Success;
    }

    /// <summary>
    /// Closes a Win32 handle previously returned by <c>GetMemoryWin32Handle</c>.
    /// </summary>
    /// <param name="handle">The Win32 handle to close.</param>
    public void CloseWin32Handle(nint handle) => Win32Native.CloseHandle(handle);

    /// <summary>
    /// Creates a buffer with additional minimum alignment.
    /// </summary>
    /// <param name="bufferInfo">VkBufferCreateInfo describing the buffer to create.</param>
    /// <param name="allocInfo">Allocation creation info describing usage, flags, and other parameters.</param>
    /// <param name="alignment">Minimum alignment for the allocation, in bytes.</param>
    /// <param name="buffer">Receives the created <c>Buffer</c>.</param>
    /// <param name="allocation">Receives the created <see cref="Allocation"/> if successful.</param>
    /// <returns>The Vulkan Result of the operation.</returns>
    public Result CreateBufferWithAlignment(in BufferCreateInfo bufferInfo,
                                            in AllocationCreateInfo allocInfo,
                                            long alignment,
                                            out Buffer buffer,
                                            out Allocation? allocation)
    {
        var localInfo = allocInfo;

        if (alignment > localInfo.MinAlignment)
        {
            localInfo.MinAlignment = alignment;
        }

        var res = CreateBuffer(in bufferInfo, in localInfo, out buffer, out allocation);

        if (res.IsError())
        {
            return res;
        }

        return Result.Success;
    }

    /// <summary>
    /// Flushes memory of given set of allocations.
    /// </summary>
    /// <param name="allocations">Array of allocations to flush.</param>
    /// <returns>The Vulkan Result of the flush operation.</returns>
    public Result FlushAllocations(Allocation[] allocations)
    {
        if (allocations.Length == 0)
        {
            return Result.Success;
        }

        var ranges = new MappedMemoryRange[allocations.Length];
        var count = 0;

        for (var i = 0; i < allocations.Length; ++i)
        {
            var offset = 0;
            var size = long.MaxValue;

            if (TryGetFlushOrInvalidateRange(allocations[i], offset, size, out var memRange))
            {
                ranges[count++] = memRange;
            }
        }

        if (count == 0)
        {
            return Result.Success;
        }

        fixed (MappedMemoryRange* pRanges = ranges)
        {
            return VkApi.FlushMappedMemoryRanges(Device, (uint)count, pRanges);
        }
    }

    /// <summary>
    /// Flushes memory of given set of allocations with explicit offsets and sizes.
    /// </summary>
    /// <param name="allocations">Array of allocations to flush.</param>
    /// <param name="offsets">Array of offsets within each allocation to flush.</param>
    /// <param name="sizes">Array of sizes of the ranges to flush.</param>
    /// <returns>The Vulkan Result of the flush operation.</returns>
    public Result FlushAllocations(Allocation[] allocations, long[] offsets, long[] sizes)
    {
        if (allocations.Length == 0)
        {
            return Result.Success;
        }

        var ranges = new MappedMemoryRange[allocations.Length];
        var count = 0;

        for (var i = 0; i < allocations.Length; ++i)
        {
            var offset = offsets[i];
            var size = sizes[i];

            if (TryGetFlushOrInvalidateRange(allocations[i], offset, size, out var memRange))
            {
                ranges[count++] = memRange;
            }
        }

        if (count == 0)
        {
            return Result.Success;
        }

        fixed (MappedMemoryRange* pRanges = ranges)
        {
            return VkApi.FlushMappedMemoryRanges(Device, (uint)count, pRanges);
        }
    }

    /// <summary>
    /// Invalidates memory of given set of allocations.
    /// </summary>
    /// <param name="allocations">Array of allocations to invalidate.</param>
    /// <returns>The Vulkan Result of the invalidate operation.</returns>
    public Result InvalidateAllocations(Allocation[] allocations)
    {
        if (allocations.Length == 0)
        {
            return Result.Success;
        }

        var ranges = new MappedMemoryRange[allocations.Length];
        var count = 0;

        for (var i = 0; i < allocations.Length; ++i)
        {
            var offset = 0;
            var size = long.MaxValue;

            if (TryGetFlushOrInvalidateRange(allocations[i], offset, size, out var memRange))
            {
                ranges[count++] = memRange;
            }
        }

        if (count == 0)
        {
            return Result.Success;
        }

        fixed (MappedMemoryRange* pRanges = ranges)
        {
            return VkApi.InvalidateMappedMemoryRanges(Device, (uint)count, pRanges);
        }
    }

    /// <summary>
    /// Invalidates memory of given set of allocations with explicit offsets and sizes.
    /// </summary>
    /// <param name="allocations">Array of allocations to invalidate.</param>
    /// <param name="offsets">Array of offsets within each allocation to invalidate.</param>
    /// <param name="sizes">Array of sizes of the ranges to invalidate.</param>
    /// <returns>The Vulkan Result of the invalidate operation.</returns>
    public Result InvalidateAllocations(Allocation[] allocations, long[] offsets, long[] sizes)
    {
        if (allocations.Length == 0)
        {
            return Result.Success;
        }

        var ranges = new MappedMemoryRange[allocations.Length];
        var count = 0;

        for (var i = 0; i < allocations.Length; ++i)
        {
            var offset = offsets[i];
            var size = sizes[i];

            if (TryGetFlushOrInvalidateRange(allocations[i], offset, size, out var memRange))
            {
                ranges[count++] = memRange;
            }
        }

        if (count == 0)
        {
            return Result.Success;
        }

        fixed (MappedMemoryRange* pRanges = ranges)
        {
            return VkApi.InvalidateMappedMemoryRanges(Device, (uint)count, pRanges);
        }
    }

    internal Result AllocateVulkanMemory(in MemoryAllocateInfo allocInfo,
                                         out DeviceMemory memory,
                                         bool isImported = false)
    {
        var heapIndex = MemoryTypeIndexToHeapIndex((int)allocInfo.MemoryTypeIndex);
        ref var budgetData = ref Budget.BudgetData[heapIndex];

        if ((_heapSizeLimitMask & (1u << heapIndex)) != 0)
        {
            long heapSize, blockBytes, blockBytesAfterAlloc;

            heapSize = (long)GetMemoryHeaps()[heapIndex].Size;

            do
            {
                blockBytes = budgetData.BlockBytes;
                blockBytesAfterAlloc = blockBytes + (long)allocInfo.AllocationSize;

                if (blockBytesAfterAlloc > heapSize)
                {
                    memory = default;
                    return Result.ErrorOutOfDeviceMemory.ThrowOrReturn<AllocationException>(ThrowOnError, "Budget limit reached for heap index " + heapIndex);
                }
            }
            while (Interlocked.CompareExchange(ref budgetData.BlockBytes, blockBytesAfterAlloc, blockBytes) != blockBytes);
        }
        else
        {
            Interlocked.Add(ref budgetData.BlockBytes, (long)allocInfo.AllocationSize);
        }

        fixed (MemoryAllocateInfo* pInfo = &allocInfo)
        fixed (DeviceMemory* pMemory = &memory)
        {
            Result res;

            if (_allocationCallbacks.HasValue)
            {
                var cb = _allocationCallbacks.Value;

                res = VkApi.AllocateMemory(Device, pInfo, &cb, pMemory);
            }
            else
            {
                res = VkApi.AllocateMemory(Device, pInfo, null, pMemory);
            }

            if (res.IsSuccess())
            {
                if (!isImported)
                {
                    Interlocked.Increment(ref budgetData.BlockCount);
                    Interlocked.Increment(ref Budget.OperationsSinceBudgetFetch);
                }

                if (_deviceMemoryCallbacks is { } callbacks && callbacks.Allocate is { } allocate)
                {
                    allocate(this, allocInfo.MemoryTypeIndex, memory, allocInfo.AllocationSize, callbacks.UserData);
                }
            }
            else
            {
                Interlocked.Add(ref budgetData.BlockBytes, -(long)allocInfo.AllocationSize);
                Interlocked.Decrement(ref budgetData.BlockCount);
            }

            return res;
        }
    }

    internal void FreeVulkanMemory(int memoryType, long size, DeviceMemory memory)
    {
        if (_deviceMemoryCallbacks is { } callbacks && callbacks.Free is { } free)
        {
            free(this, (uint)memoryType, memory, (ulong)size, callbacks.UserData);
        }

        if (_allocationCallbacks.HasValue)
        {
            var cb = _allocationCallbacks.Value;

            VkApi.FreeMemory(Device, memory, &cb);
        }
        else
        {
            VkApi.FreeMemory(Device, memory, null);
        }

        ref var freedHeap = ref Budget.BudgetData[MemoryTypeIndexToHeapIndex(memoryType)];
        Interlocked.Add(ref freedHeap.BlockBytes, -size);
        Interlocked.Decrement(ref freedHeap.BlockCount);
    }

    internal void RemovePool(VulkanMemoryPool pool)
    {
        _poolsLock.EnterWriteLock(UseLock);
        try
        {
            var success = _pools.Remove(pool);
            if (!success)
            {
                throw new InvalidOperationException($"Pool ID={pool.Id} not found in allocator _pools (count={_pools.Count}).");
            }
        }
        finally
        {
            _poolsLock.ExitWriteLock(UseLock);
        }
    }


    internal Result BindVulkanBuffer(Buffer buffer, DeviceMemory memory, long offset, void* pNext)
    {
        if (pNext != null)
        {
            var info = new BindBufferMemoryInfo(pNext: pNext, buffer: buffer, memory: memory, memoryOffset: (ulong)offset);

            return VkApi.BindBufferMemory2(Device, 1, &info);
        }
        else
        {
            return VkApi.BindBufferMemory(Device, buffer, memory, (ulong)offset);
        }
    }

    internal Result BindVulkanImage(Image image, DeviceMemory memory, long offset, void* pNext)
    {
        if (pNext != default)
        {
            var info = new BindImageMemoryInfo
            {
                SType = StructureType.BindBufferMemoryInfo,
                PNext = pNext,
                Image = image,
                Memory = memory,
                MemoryOffset = (ulong)offset
            };

            return VkApi.BindImageMemory2(Device, 1, &info);
        }
        else
        {
            return VkApi.BindImageMemory(Device, image, memory, (ulong)offset);
        }
    }

    internal void FillAllocation(Allocation allocation, byte pattern)
    {
        if (!DebugInitializeAllocations)
        {
            return;
        }

        if ((GetMemoryTypes()[allocation.MemoryTypeIndex].PropertyFlags & MemoryPropertyFlags.HostVisibleBit) == 0)
        {
            return;
        }

        void* pData;
        bool unmap;

        if (allocation.MappedData != default)
        {
            // Already mapped (persistent or currently mapped): reuse the pointer without
            // remapping, so we never double-map a persistently mapped allocation.
            pData = allocation.MappedData;
            unmap = false;
        }
        else
        {
            // Debug-only fill: if mapping fails (e.g. when exceptions are disabled and the
            // mapping returns an error Result), skip the fill rather than propagate.
            if (allocation.Map(out pData).IsError())
            {
                return;
            }

            unmap = true;
        }

        unsafe
        {
            Unsafe.InitBlockUnaligned(ref *(byte*)pData, pattern, (uint)allocation.Size);
        }

        FlushOrInvalidateAllocation(allocation, 0, long.MaxValue, CacheOperation.Flush);

        if (unmap)
        {
            allocation.Unmap();
        }
    }

    internal Result FlushOrInvalidateAllocation(Allocation allocation,
                                                long offset,
                                                long size,
                                                CacheOperation op)
    {
        if (TryGetFlushOrInvalidateRange(allocation, offset, size, out var memRange))
        {
            switch (op)
            {
                case CacheOperation.Flush:
                    return VkApi.FlushMappedMemoryRanges(Device, 1, &memRange);
                case CacheOperation.Invalidate:
                    return VkApi.InvalidateMappedMemoryRanges(Device, 1, &memRange);
                default:
                    Debug.Assert(false);
                    throw new ArgumentException("Invalid Cache Operation value", nameof(op));
            }
        }

        return Result.Success;
    }

    private long CalcPreferredBlockSize(int memTypeIndex)
    {
        var heapIndex = MemoryTypeIndexToHeapIndex(memTypeIndex);

        Debug.Assert((uint)heapIndex < Vk.MaxMemoryHeaps);

        var heapSize = (long)GetMemoryHeaps()[heapIndex].Size;

        return Helpers.AlignUp(heapSize <= SMALL_HEAP_MAX_SIZE ? (heapSize / 8) : _preferredLargeHeapBlockSize, 32);
    }

    private static bool IsImportMemoryNext(void* pNext)
    {
        // Walks the VkMemoryAllocateInfo pNext chain to detect an external-memory import
        // struct (e.g. VkImportMemoryWin32HandleInfoKHR).
        while (pNext != null)
        {
            var sType = (StructureType)Marshal.ReadInt32((nint)pNext);
            if (sType == StructureType.ImportMemoryWin32HandleInfoKhr ||
                sType == StructureType.ImportMemoryHostPointerInfoExt)
            {
                return true;
            }

            pNext = (void*)Marshal.ReadIntPtr((nint)pNext, (int)Marshal.OffsetOf<MemoryAllocateInfo>("PNext").ToInt64());
        }

        return false;
    }

    private Result AllocateMemoryOfType(long size,
                                        long alignment,
                                        in DedicatedAllocationInfo dedicatedInfo,
                                        in AllocationCreateInfo createInfo,
                                        int memoryTypeIndex,
                                        SuballocationType suballocType,
                                        out Allocation allocation)
    {
        using var lockScope = new LockScope(_allocatorLock, UseLock);

        var finalCreateInfo = createInfo;

        // A custom pNext chain for VkMemoryAllocateInfo (e.g. external memory) requires a dedicated
        // allocation, mirroring C++: if(pMemoryAllocateNext != VMA_NULL) requiresDedicatedAllocation = true.
        if (finalCreateInfo.MemoryAllocateNext != null)
        {
            finalCreateInfo.Flags |= AllocationCreateFlags.DedicatedMemory;
        }

        if ((finalCreateInfo.Flags & AllocationCreateFlags.Mapped) != 0
            && (GetMemoryTypes()[memoryTypeIndex].PropertyFlags & MemoryPropertyFlags.HostVisibleBit) == 0)
        {
            finalCreateInfo.Flags &= ~AllocationCreateFlags.Mapped;
        }

        if (finalCreateInfo.Usage == MemoryUsage.GpuLazilyAllocated)
        {
            finalCreateInfo.Flags |= AllocationCreateFlags.DedicatedMemory;
        }

        var blockList = BlockLists[memoryTypeIndex];

        var preferredBlockSize = blockList.PreferredBlockSize;
        var preferDedicatedMemory = (dedicatedInfo.RequiresDedicatedAllocation | dedicatedInfo.PrefersDedicatedAllocation)
            || size > preferredBlockSize / 2;

        if (preferDedicatedMemory
            && (finalCreateInfo.Flags & AllocationCreateFlags.NeverAllocate) == 0
            && finalCreateInfo.Pool == null)
        {
            finalCreateInfo.Flags |= AllocationCreateFlags.DedicatedMemory;
        }

        var blockAllocResult = Result.Success;

        if ((finalCreateInfo.Flags & AllocationCreateFlags.DedicatedMemory) == 0)
        {
            var allocResult = blockList.Allocate(size, alignment, finalCreateInfo, suballocType, out var alloc);

            if (allocResult.IsSuccess())
            {
                FillAllocation(alloc!, Helpers.ALLOCATION_FILL_PATTERN_CREATED);
                allocation = alloc!;
                return Result.Success;
            }

            blockAllocResult = allocResult;
        }

        // A pool's pMemoryAllocateNext (e.g. VK_KHR_external_memory export info) must be applied to every
        // allocation made through that pool, including dedicated allocations created within it.
        var effectiveMemoryAllocateNext = finalCreateInfo.MemoryAllocateNext;
        if (effectiveMemoryAllocateNext == null && finalCreateInfo.Pool != null)
        {
            effectiveMemoryAllocateNext = finalCreateInfo.Pool.MemoryAllocateNext;
        }

        //Try a dedicated allocation if a block allocation failed, or if specified as a dedicated allocation
        if ((finalCreateInfo.Flags & AllocationCreateFlags.NeverAllocate) != 0)
        {
            allocation = null!;
            return blockAllocResult.ThrowOrReturn<AllocationException>(ThrowOnError, "Block List allocation failed, and `AllocationCreateFlags.NeverAllocate` specified");
        }

        var res = AllocateDedicatedMemory(size,
                                          memoryTypeIndex,
                                          (finalCreateInfo.Flags & AllocationCreateFlags.WithinBudget) != 0,
                                          (finalCreateInfo.Flags & AllocationCreateFlags.Mapped) != 0,
                                          (finalCreateInfo.Flags & AllocationCreateFlags.CanAlias) != 0,
                                          finalCreateInfo.UserData,
                                          out var dedicatedAlloc,
                                          in dedicatedInfo,
                                          finalCreateInfo.Priority,
                                          effectiveMemoryAllocateNext,
                                          finalCreateInfo.Pool);

        allocation = dedicatedAlloc;
        return res;
    }

    private Result AllocateDedicatedMemoryPage(long size,
                                               int memTypeIndex,
                                               in MemoryAllocateInfo allocInfo,
                                               bool map,
                                               object? userData,
                                               out DedicatedAllocation allocation,
                                               bool isImported = false)
    {
        var res = AllocateVulkanMemory(in allocInfo, out var memory, isImported);

        if (res.IsError())
        {
            allocation = null!;
            return res.ThrowOrReturn<AllocationException>(ThrowOnError, "Dedicated memory allocation Failed");
        }

        void* mappedData = default;
        if (map)
        {
            res = VkApi.MapMemory(Device, memory, 0, Vk.WholeSize, 0, &mappedData);

            if (res.IsError())
            {
                FreeVulkanMemory(memTypeIndex, size, memory);

                allocation = null!;
                return res.ThrowOrReturn<AllocationException>(ThrowOnError, "Unable to map dedicated allocation");
            }
        }

        var allocation2 = new DedicatedAllocation(this, memTypeIndex, memory, mappedData, size)
        {
            UserData = userData,
            IsImported = isImported
        };

        // Imported memory is owned by the exporter, so it must not be counted against the
        // allocator's budget (no release will ever balance it).
        if (!isImported)
        {
            Budget.AddAllocation(MemoryTypeIndexToHeapIndex(memTypeIndex), size);
        }

        FillAllocation(allocation2, Helpers.ALLOCATION_FILL_PATTERN_CREATED);

        allocation = allocation2;
        return Result.Success;
    }

    private Result AllocateDedicatedMemory(long size,
                                           int memTypeIndex,
                                           bool withinBudget,
                                           bool map,
                                           bool canAlias,
                                           object? userData,
                                           out DedicatedAllocation allocation,
                                           in DedicatedAllocationInfo dedicatedInfo,
                                           float priority = 0.5f,
                                           void* memoryAllocateNext = null,
                                           VulkanMemoryPool? pool = null,
                                           bool isImported = false)
    {
        isImported = isImported || IsImportMemoryNext(memoryAllocateNext);

        var heapIndex = MemoryTypeIndexToHeapIndex(memTypeIndex);

        if (withinBudget)
        {
            var budget = GetBudget(heapIndex);
            if (budget.Usage + size > budget.Budget)
            {
                allocation = null!;
                return Result.ErrorOutOfDeviceMemory.ThrowOrReturn<AllocationException>(ThrowOnError, "Memory Budget limit reached for heap index " + heapIndex);
            }
        }

        var allocInfo = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            MemoryTypeIndex = (uint)memTypeIndex,
            AllocationSize = (ulong)size
        };

        Debug.Assert(!(dedicatedInfo.DedicatedBuffer.Handle != default && dedicatedInfo.DedicatedImage.Handle != default), "dedicated buffer and dedicated image were both specified");

        allocInfo.PNext = memoryAllocateNext;

        var dedicatedAllocInfo = new MemoryDedicatedAllocateInfo(StructureType.MemoryDedicatedAllocateInfo);

        // CAN_ALIAS_BIT: do not supply VkMemoryDedicatedAllocateInfoKHR so the dedicated memory can be
        // aliased by other resources.
        if (!canAlias)
        {
            if (dedicatedInfo.DedicatedBuffer.Handle != default)
            {
                dedicatedAllocInfo.Buffer = dedicatedInfo.DedicatedBuffer;
                dedicatedAllocInfo.PNext = allocInfo.PNext;
                allocInfo.PNext = &dedicatedAllocInfo;
            }
            else if (dedicatedInfo.DedicatedImage.Handle != default)
            {
                dedicatedAllocInfo.Image = dedicatedInfo.DedicatedImage;
                dedicatedAllocInfo.PNext = allocInfo.PNext;
                allocInfo.PNext = &dedicatedAllocInfo;
            }
        }

        var allocFlagsInfo = new MemoryAllocateFlagsInfoKHR(StructureType.MemoryAllocateFlagsInfoKhr);
        if (UseKhrBufferDeviceAddress)
        {
            var canContainBufferWithDeviceAddress = true;

            if (dedicatedInfo.DedicatedBuffer.Handle != default)
            {
                canContainBufferWithDeviceAddress = dedicatedInfo.DedicatedBufferUsage == UNKNOWN_BUFFER_USAGE
                    || (dedicatedInfo.DedicatedBufferUsage & BufferUsageFlags.ShaderDeviceAddressBitKhr) != 0;
            }
            else if (dedicatedInfo.DedicatedImage.Handle != default)
            {
                canContainBufferWithDeviceAddress = false;
            }

            if (canContainBufferWithDeviceAddress)
            {
                allocFlagsInfo.Flags = MemoryAllocateFlags.DeviceAddressBit;
                allocFlagsInfo.PNext = allocInfo.PNext;
                allocInfo.PNext = &allocFlagsInfo;
            }
        }

        MemoryPriorityAllocateInfoEXT priorityInfo = default;
        if (_useExtMemoryPriority && priority >= 0F && priority <= 1F)
        {
            priorityInfo = new MemoryPriorityAllocateInfoEXT(StructureType.MemoryPriorityAllocateInfoExt)
            {
                Priority = priority,
                PNext = allocInfo.PNext
            };
            allocInfo.PNext = &priorityInfo;
        }

        var res = AllocateDedicatedMemoryPage(size,
                                              memTypeIndex,
                                              in allocInfo,
                                              map,
                                              userData,
                                              out var alloc,
                                              isImported);

        if (res.IsError())
        {
            allocation = null!;
            return res;
        }

        // A dedicated allocation created through a custom pool must be attributed to that pool so its
        // statistics (size, block count, allocation counts) are included in the pool, mirroring C++.
        alloc.Pool = pool;

        //Register made allocations
        ref var handler = ref DedicatedAllocations[memTypeIndex];

        handler.Lock.EnterWriteLock(UseLock);
        try
        {
            handler.Allocations.InsertSorted(alloc, (alloc1, alloc2) => alloc1.Offset.CompareTo(alloc2.Offset));
        }
        finally
        {
            handler.Lock.ExitWriteLock(UseLock);
        }

        allocation = alloc;
        return Result.Success;
    }

    private void FreeDedicatedMemory(DedicatedAllocation allocation)
    {
        ref var handler = ref DedicatedAllocations[allocation.MemoryTypeIndex];

        handler.Lock.EnterWriteLock(UseLock);

        try
        {
            var success = handler.Allocations.Remove(allocation);

            Debug.Assert(success);
        }
        finally
        {
            handler.Lock.ExitWriteLock(UseLock);
        }

        // Imported memory is owned by the exporter; freeing it here would release memory the
        // original allocation (and its pool) still reference.
        if (!allocation.IsImported)
        {
            FreeVulkanMemory(allocation.MemoryTypeIndex, allocation.Size, allocation.DeviceMemory);
        }
    }

    private uint CalculateGlobalMemoryTypeBits()
    {
        Debug.Assert(MemoryTypeCount > 0);

        var memoryTypeBits = uint.MaxValue;

        if (!_useAMDDeviceCoherentMemory)
        {
            // Exclude memory types that have VK_MEMORY_PROPERTY_DEVICE_COHERENT_BIT_AMD or
            // VK_MEMORY_PROPERTY_DEVICE_UNCACHED_BIT_AMD.
            const MemoryPropertyFlags amdDeviceCoherentMask =
                MemoryPropertyFlags.DeviceCoherentBitAmd | MemoryPropertyFlags.DeviceUncachedBitAmd;

            for (var index = 0; index < MemoryTypeCount; ++index)
            {
                if ((GetMemoryTypes()[index].PropertyFlags & amdDeviceCoherentMask) != 0)
                {
                    memoryTypeBits &= ~(1u << index);
                }
            }
        }

        return memoryTypeBits;
    }

    private void UpdateVulkanBudget()
    {
        Debug.Assert(_useExtMemoryBudget);

        var budgetProps = new PhysicalDeviceMemoryBudgetPropertiesEXT(StructureType.PhysicalDeviceMemoryBudgetPropertiesExt);

        var memProps = new PhysicalDeviceMemoryProperties2(StructureType.PhysicalDeviceMemoryProperties2, &budgetProps);

        VkApi.GetPhysicalDeviceMemoryProperties2(_physicalDevice, &memProps);

        Budget.Lock.EnterWriteLock(UseLock);

        try
        {
            for (var i = 0; i < MemoryHeapCount; ++i)
            {
                ref var data = ref Budget.BudgetData[i];

                data.VulkanUsage = (long)budgetProps.HeapUsage[i];
                data.VulkanBudget = (long)budgetProps.HeapBudget[i];

                data.BlockBytesAtBudgetFetch = data.BlockBytes;

                // Some bugged drivers return the budget incorrectly, e.g. 0 or much bigger than heap size.

                ref var heap = ref GetMemoryHeaps()[i];

                if (data.VulkanBudget == 0)
                {
                    data.VulkanBudget = (long)(heap.Size * 8 / 10);
                }
                else if ((ulong)data.VulkanBudget > heap.Size)
                {
                    data.VulkanBudget = (long)heap.Size;
                }

                if (data.VulkanUsage == 0 && data.BlockBytesAtBudgetFetch > 0)
                {
                    data.VulkanUsage = data.BlockBytesAtBudgetFetch;
                }
            }

            Budget.OperationsSinceBudgetFetch = 0;
        }
        finally
        {
            Budget.Lock.ExitWriteLock(UseLock);
        }
    }

    private bool TryGetFlushOrInvalidateRange(Allocation allocation,
                                              long offset,
                                              long size,
                                              out MappedMemoryRange range)
    {
        range = default;
        var memTypeIndex = allocation.MemoryTypeIndex;

        if (size > 0 && IsMemoryTypeNonCoherent(memTypeIndex))
        {
            var allocSize = allocation.Size;

            Debug.Assert((ulong)offset <= (ulong)allocSize);

            var nonCoherentAtomSize = (long)_physicalDevicePropertiesValue.Limits.NonCoherentAtomSize;

            range = new MappedMemoryRange(memory: allocation.DeviceMemory);

            if (allocation is BlockAllocation blockAlloc)
            {
                range.Offset = (ulong)Helpers.AlignDown(offset, nonCoherentAtomSize);

                if (size == long.MaxValue)
                {
                    size = allocSize - offset;
                }
                else
                {
                    Debug.Assert(offset + size <= allocSize);
                }

                range.Size = (ulong)Helpers.AlignUp(size + (offset - (long)range.Offset), nonCoherentAtomSize);

                var allocOffset = blockAlloc.Offset;

                Debug.Assert(allocOffset % nonCoherentAtomSize == 0);

                var blockSize = blockAlloc.Block.MetaData.Size;

                range.Offset += (ulong)allocOffset;
                range.Size = Math.Min(range.Size, (ulong)blockSize - range.Offset);
            }
            else if (allocation is DedicatedAllocation)
            {
                range.Offset = (ulong)Helpers.AlignDown(offset, nonCoherentAtomSize);

                if (size == long.MaxValue)
                {
                    range.Size = (ulong)allocSize - range.Offset;
                }
                else
                {
                    Debug.Assert(offset + size <= allocSize);

                    range.Size = (ulong)Helpers.AlignUp(size + (offset - (long)range.Offset), nonCoherentAtomSize);
                }
            }
            else
            {
                throw new ArgumentException("allocation type is not BlockAllocation or DedicatedAllocation");
            }

            return true;
        }

        return false;
    }

    internal struct DedicatedAllocationHandler
    {
        public List<DedicatedAllocation> Allocations;
        public ReaderWriterLockSlim Lock;
    }

    internal struct DedicatedAllocationInfo
    {
        public Buffer DedicatedBuffer;
        public Image DedicatedImage;
        public BufferUsageFlags DedicatedBufferUsage; //uint.MaxValue when unknown
        public bool RequiresDedicatedAllocation;
        public bool PrefersDedicatedAllocation;

        public static readonly DedicatedAllocationInfo Default = new()
        {
            DedicatedBufferUsage = unchecked((BufferUsageFlags)uint.MaxValue)
        };
    }
}

