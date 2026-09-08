using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

using Silk.NET.Core;
using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;


using VulkanCube.TaskTypes;
using VmaCS;

namespace VulkanCube;

public abstract class AllocatorAndBuffersExample : CommandPoolCreationExample
{
    protected const Format DEPTHFORMAT = Format.D16Unorm;

    protected readonly VulkanMemoryAllocator Allocator;

    protected Buffer VertexBuffer;
    protected Buffer IndexBuffer;
    protected Buffer InstanceBuffer;
    protected Allocation VertexAllocation;
    protected Allocation IndexAllocation;
    protected Allocation InstanceAllocation;

    protected uint VertexCount;
    protected uint IndexCount;
    protected uint InstanceCount;

    protected CameraUniform Camera = new();

    protected Buffer UniformBuffer;
    protected Allocation UniformAllocation;

    protected DepthBufferObject DepthBuffer;

    protected Image TextureImage;
    protected Allocation TextureAllocation;
    protected ImageView TextureView;
    protected Sampler TextureSampler;

    private readonly WaitScheduler _scheduler;

    protected Task BufferCopyPromise;
    protected Task TextureCopyPromise;

    protected AllocatorAndBuffersExample() : base()
    {
        _scheduler = new WaitScheduler(Device);

        Allocator = CreateAllocator();

        CreateBuffers();

        CreateUniformBuffer();

        CreateDepthBuffer();

        CreateTexture();
    }

    public override unsafe void Dispose()
    {
        _scheduler.Dispose();

        VkApi.DestroyImageView(Device, DepthBuffer.View, null);
        VkApi.DestroyImage(Device, DepthBuffer.Image, null);
        DepthBuffer.Allocation.Dispose();

        VkApi.DestroySampler(Device, TextureSampler, null);
        VkApi.DestroyImageView(Device, TextureView, null);
        VkApi.DestroyImage(Device, TextureImage, null);
        TextureAllocation.Dispose();

        VkApi.DestroyBuffer(Device, UniformBuffer, null);
        UniformAllocation.Dispose();

        VkApi.DestroyBuffer(Device, VertexBuffer, null);
        VertexAllocation.Dispose();

        VkApi.DestroyBuffer(Device, IndexBuffer, null);
        IndexAllocation.Dispose();

        VkApi.DestroyBuffer(Device, InstanceBuffer, null);
        InstanceAllocation.Dispose();

        Allocator.Dispose();

        base.Dispose();
    }

    private unsafe VulkanMemoryAllocator CreateAllocator()
    {
        uint version;
        var res = VkApi.EnumerateInstanceVersion(&version);

        if (res != Result.Success)
        {
            throw new VulkanResultException("Unable to retrieve instance version", res);
        }

        var createInfo = new VulkanMemoryAllocatorCreateInfo
        {
            VulkanAPIVersion = (Version32)version,
            VulkanAPIObject = VkApi,
            Instance = Instance,
            PhysicalDevice = PhysicalDevice,
            LogicalDevice = Device,
            PreferredLargeHeapBlockSize = 64L * 1024 * 1024,
            ThrowOnError = true
        };

        return new VulkanMemoryAllocator(createInfo);
    }

    private unsafe void CreateBuffers()
    {
        var positionData = VertexData.IndexedCubeData;

        var indexData = VertexData.CubeIndexData;

        var instanceData = new InstanceData[]
        {
            new(new Vector3(0, 0, 0)),
            new(new Vector3(2, 0, 0)),
            new(new Vector3(-2, 0, 0))
        };

        CreateHostBufferWithContent(positionData, out var hostBuffer1, out var hostAlloc1);
        CreateHostBufferWithContent(indexData, out var hostBuffer2, out var hostAlloc2);
        CreateHostBufferWithContent(instanceData, out var hostBuffer3, out var hostAlloc3);

        CreateDeviceLocalBuffer(BufferUsageFlags.VertexBufferBit, GetByteLength(positionData), out VertexBuffer, out VertexAllocation);
        CreateDeviceLocalBuffer(BufferUsageFlags.IndexBufferBit, GetByteLength(indexData), out IndexBuffer, out IndexAllocation);
        CreateDeviceLocalBuffer(BufferUsageFlags.VertexBufferBit, GetByteLength(instanceData), out InstanceBuffer, out InstanceAllocation);

        var cbuffer = AllocateCommandBuffer(CommandBufferLevel.Primary);

        var copies = stackalloc BufferCopy[1];

        BeginCommandBuffer(cbuffer, CommandBufferUsageFlags.OneTimeSubmitBit);

        copies[0] = new BufferCopy(0, 0, (ulong)GetByteLength(positionData));
        VkApi.CmdCopyBuffer(cbuffer, hostBuffer1, VertexBuffer, 1, copies);

        copies[0] = new BufferCopy(0, 0, (ulong)GetByteLength(indexData));
        VkApi.CmdCopyBuffer(cbuffer, hostBuffer2, IndexBuffer, 1, copies);

        copies[0] = new BufferCopy(0, 0, (ulong)GetByteLength(instanceData));
        VkApi.CmdCopyBuffer(cbuffer, hostBuffer3, InstanceBuffer, 1, copies);

        EndCommandBuffer(cbuffer);

        var subInfo = new SubmitInfo(commandBufferCount: 1, pCommandBuffers: &cbuffer);

        var fence = CreateFence();

        var res = VkApi.QueueSubmit(GraphicsQueue, 1, &subInfo, fence);

        if (res != Result.Success)
        {
            throw new Exception("Unable to submit to queue. " + res);
        }

        var bufferTmp = cbuffer; //Allows the capture of this command buffer in a lambda

        BufferCopyPromise = _scheduler.WaitForFenceAsync(fence);

        BufferCopyPromise.GetAwaiter().OnCompleted(() =>
        {
            VkApi.DestroyFence(Device, fence, null);

            FreeCommandBuffer(bufferTmp);

            VkApi.DestroyBuffer(Device, hostBuffer1, null);
            VkApi.DestroyBuffer(Device, hostBuffer2, null);
            VkApi.DestroyBuffer(Device, hostBuffer3, null);

            hostAlloc1.Dispose();
            hostAlloc2.Dispose();
            hostAlloc3.Dispose();
        });

        VertexCount = (uint)positionData.Length;
        IndexCount = (uint)indexData.Length;
        InstanceCount = (uint)instanceData.Length;
    }

    private static uint GetByteLength<T>(T[] arr) where T : unmanaged => (uint)Unsafe.SizeOf<T>() * (uint)arr.Length;

    private unsafe void CreateHostBufferWithContent<T>(ReadOnlySpan<T> span, out Buffer buffer, out Allocation alloc) where T : unmanaged
    {
        BufferCreateInfo bufferInfo = new(
            usage: BufferUsageFlags.TransferSrcBit,
            size: (uint)Unsafe.SizeOf<T>() * (uint)span.Length);

        AllocationCreateInfo allocInfo = new()
        {
            Flags = AllocationCreateFlags.Mapped,
            Usage = MemoryUsage.CpuOnly
        };

        Allocator.CreateBuffer(in bufferInfo, in allocInfo, out buffer, out alloc);

        alloc.Map(out var mapped);

        var bufferSpan = new Span<T>((void*)mapped, span.Length);

        span.CopyTo(bufferSpan);

        alloc.Unmap();
    }

    private unsafe void CreateDeviceLocalBuffer(BufferUsageFlags usage, uint size, out Buffer buffer, out Allocation alloc)
    {
        BufferCreateInfo bufferInfo = new(
            usage: usage | BufferUsageFlags.TransferDstBit,
            size: size);

        AllocationCreateInfo allocInfo = new()
        {
            Usage = MemoryUsage.GpuOnly
        };

        Allocator.CreateBuffer(in bufferInfo, in allocInfo, out buffer, out alloc);
    }

    protected uint UniformBufferSize = (uint)Unsafe.SizeOf<Matrix4x4>() * 2;

    private unsafe void CreateUniformBuffer() //Simpler setup from the Vertex buffer because there is no staging or device copying
    {
        var bufferInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = UniformBufferSize,
            Usage = BufferUsageFlags.UniformBufferBit,
            SharingMode = SharingMode.Exclusive
        };

        // Allow this to be updated every frame
        var allocInfo = new AllocationCreateInfo
        {
            Usage = MemoryUsage.CpuToGpu,
            RequiredFlags = MemoryPropertyFlags.HostVisibleBit
        };

        // Binds buffer to allocation for you
        Allocator.CreateBuffer(in bufferInfo, in allocInfo, out var buffer, out var allocation);

        // Camera/MVP Matrix calculation
        Camera.LookAt(new Vector3(2f, 2f, -5f), new Vector3(0, 0, 0), new Vector3(0, 1, 0));

        var radFov = MathF.PI / 180f * 45f;
        var aspect = (float)SwapchainExtent.Width / SwapchainExtent.Height;

        Camera.Perspective(radFov, aspect, 0.5f, 100f);

        Camera.UpdateMVP();

        allocation.Map(out var data);

        var ptr = (Matrix4x4*)data;

        ptr[0] = Camera.MVPMatrix; // Camera Matrix
        ptr[1] = Matrix4x4.Identity;         // Model Matrix

        allocation.Unmap();

        UniformBuffer = buffer;
        UniformAllocation = allocation;
    }

    private unsafe void CreateDepthBuffer()
    {
        var depthInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = DEPTHFORMAT,
            Extent = new Extent3D(SwapchainExtent.Width, SwapchainExtent.Height, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            InitialLayout = ImageLayout.Undefined,
            Usage = ImageUsageFlags.DepthStencilAttachmentBit,
            SharingMode = SharingMode.Exclusive
        };

        var depthViewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Format = DEPTHFORMAT,
            Components = new ComponentMapping(ComponentSwizzle.R, ComponentSwizzle.G, ComponentSwizzle.B, ComponentSwizzle.A),
            SubresourceRange = new ImageSubresourceRange(aspectMask: ImageAspectFlags.DepthBit, levelCount: 1, layerCount: 1),
            ViewType = ImageViewType.Type2D
        };

        var allocInfo = new AllocationCreateInfo
        {
            Usage = MemoryUsage.GpuOnly
        };

        Allocator.CreateImage(depthInfo, allocInfo, out var image, out var alloc);

        depthViewInfo.Image = image;

        ImageView view;
        var res = VkApi.CreateImageView(Device, &depthViewInfo, null, &view);

        if (res != Result.Success)
        {
            throw new Exception("Unable to create depth image view!");
        }

        DepthBuffer.Image = image;
        DepthBuffer.View = view;
        DepthBuffer.Allocation = alloc;
    }

    private unsafe void CreateTexture()
    {
        const uint texWidth = 256;
        const uint texHeight = 256;
        const uint texChannels = 4;

        //Procedural checkerboard pattern
        var pixels = new byte[texWidth * texHeight * texChannels];

        for (var y = 0; y < texHeight; y++)
        {
            for (var x = 0; x < texWidth; x++)
            {
                var idx = (y * texWidth + x) * texChannels;
                var checker = ((x / 16) + (y / 16)) % 2 == 0;

                pixels[idx + 0] = checker ? (byte)220 : (byte)40;
                pixels[idx + 1] = checker ? (byte)80 : (byte)40;
                pixels[idx + 2] = checker ? (byte)40 : (byte)120;
                pixels[idx + 3] = 255;
            }
        }

        var imageSize = (ulong)pixels.Length;

        //Staging buffer (VMA, CPU_Only) to upload the pixel data
        var stagingBufferInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = imageSize,
            Usage = BufferUsageFlags.TransferSrcBit
        };

        var stagingAllocInfo = new AllocationCreateInfo
        {
            Flags = AllocationCreateFlags.Mapped,
            Usage = MemoryUsage.CpuOnly
        };

        Allocator.CreateBuffer(in stagingBufferInfo, in stagingAllocInfo, out var stagingBuffer, out var stagingAllocation);

        stagingAllocation.CopyMemoryToAllocation(pixels, 0);

        //Texture image (VMA, GPU_Only) sampled in the fragment shader
        var imageInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = Format.R8G8B8A8Unorm,
            Extent = new Extent3D(texWidth, texHeight, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            InitialLayout = ImageLayout.Undefined,
            Usage = ImageUsageFlags.SampledBit | ImageUsageFlags.TransferDstBit,
            SharingMode = SharingMode.Exclusive
        };

        var textureAllocInfo = new AllocationCreateInfo
        {
            Usage = MemoryUsage.GpuOnly
        };

        Allocator.CreateImage(in imageInfo, in textureAllocInfo, out TextureImage, out TextureAllocation);

        var viewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = TextureImage,
            ViewType = ImageViewType.Type2D,
            Format = Format.R8G8B8A8Unorm,
            Components = new ComponentMapping(ComponentSwizzle.R, ComponentSwizzle.G, ComponentSwizzle.B, ComponentSwizzle.A),
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1)
        };

        ImageView view;
        var res = VkApi.CreateImageView(Device, &viewInfo, null, &view);

        if (res != Result.Success)
        {
            throw new Exception("Unable to create texture image view!");
        }

        TextureView = view;

        var samplerInfo = new SamplerCreateInfo
        {
            SType = StructureType.SamplerCreateInfo,
            MagFilter = Filter.Linear,
            MinFilter = Filter.Linear,
            AddressModeU = SamplerAddressMode.Repeat,
            AddressModeV = SamplerAddressMode.Repeat,
            AddressModeW = SamplerAddressMode.Repeat,
            AnisotropyEnable = false,
            MaxAnisotropy = 1.0f,
            BorderColor = BorderColor.FloatTransparentBlack,
            UnnormalizedCoordinates = false,
            CompareEnable = false,
            CompareOp = CompareOp.Always,
            MipmapMode = SamplerMipmapMode.Nearest,
            MinLod = 0,
            MaxLod = 0,
            MipLodBias = 0
        };

        Sampler sampler;
        res = VkApi.CreateSampler(Device, &samplerInfo, null, &sampler);

        if (res != Result.Success)
        {
            throw new Exception("Unable to create texture sampler!");
        }

        TextureSampler = sampler;

        //Copy staging buffer into the image and transition its layout for sampling
        var cbuffer = AllocateCommandBuffer(CommandBufferLevel.Primary);

        BeginCommandBuffer(cbuffer, CommandBufferUsageFlags.OneTimeSubmitBit);

        var barrier1 = new ImageMemoryBarrier
        {
            SType = StructureType.ImageMemoryBarrier,
            OldLayout = ImageLayout.Undefined,
            NewLayout = ImageLayout.TransferDstOptimal,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = TextureImage,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1),
            SrcAccessMask = 0,
            DstAccessMask = AccessFlags.TransferWriteBit
        };

        VkApi.CmdPipelineBarrier(cbuffer, PipelineStageFlags.TopOfPipeBit, PipelineStageFlags.TransferBit, 0, 0, null, 0, null, 1, &barrier1);

        var region = new BufferImageCopy
        {
            BufferOffset = 0,
            BufferRowLength = 0,
            BufferImageHeight = 0,
            ImageSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1),
            ImageOffset = new Offset3D(0, 0, 0),
            ImageExtent = new Extent3D(texWidth, texHeight, 1)
        };

        VkApi.CmdCopyBufferToImage(cbuffer, stagingBuffer, TextureImage, ImageLayout.TransferDstOptimal, 1, &region);

        var barrier2 = new ImageMemoryBarrier
        {
            SType = StructureType.ImageMemoryBarrier,
            OldLayout = ImageLayout.TransferDstOptimal,
            NewLayout = ImageLayout.ShaderReadOnlyOptimal,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = TextureImage,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1),
            SrcAccessMask = AccessFlags.TransferWriteBit,
            DstAccessMask = AccessFlags.ShaderReadBit
        };

        VkApi.CmdPipelineBarrier(cbuffer, PipelineStageFlags.TransferBit, PipelineStageFlags.FragmentShaderBit, 0, 0, null, 0, null, 1, &barrier2);

        EndCommandBuffer(cbuffer);

        var subInfo = new SubmitInfo(commandBufferCount: 1, pCommandBuffers: &cbuffer);

        var fence = CreateFence();

        res = VkApi.QueueSubmit(GraphicsQueue, 1, &subInfo, fence);

        if (res != Result.Success)
        {
            throw new Exception("Unable to submit texture upload! " + res);
        }

        var bufferTmp = cbuffer;

        TextureCopyPromise = _scheduler.WaitForFenceAsync(fence);

        TextureCopyPromise.GetAwaiter().OnCompleted(() =>
        {
            VkApi.DestroyFence(Device, fence, null);

            FreeCommandBuffer(bufferTmp);

            VkApi.DestroyBuffer(Device, stagingBuffer, null);

            stagingAllocation.Dispose();
        });
    }

    //Helper methods

    protected unsafe Fence CreateFence(bool initialState = false)
    {
        var info = new FenceCreateInfo(flags: initialState ? FenceCreateFlags.SignaledBit : 0);

        Fence fence;
        var res = VkApi.CreateFence(Device, &info, null, &fence);

        if (res != Result.Success)
        {
            throw new VulkanResultException("Unable to create Fence!", res);
        }

        return fence;
    }

    protected struct DepthBufferObject
    {
        public Image Image;
        public Allocation Allocation;
        public ImageView View;
    }
}
