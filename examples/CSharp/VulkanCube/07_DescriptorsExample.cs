using Silk.NET.Vulkan;

namespace VulkanCube;

public unsafe abstract class DescriptorSetExample : LayoutsExample
{
    protected readonly DescriptorPool DescriptorPool;
    protected readonly DescriptorSet[] DescriptorSets;

    protected DescriptorSetExample() : base()
    {
        DescriptorPool = CreateDescriptorPool();

        DescriptorSets = AllocateDescriptorSets();

        var bufferInfo = new DescriptorBufferInfo
        {
            Buffer = UniformBuffer,
            Offset = 0,
            Range = UniformBufferSize
        };

        var imageInfo = new DescriptorImageInfo
        {
            Sampler = TextureSampler,
            ImageView = TextureView,
            ImageLayout = ImageLayout.ShaderReadOnlyOptimal
        };

        var writes = stackalloc WriteDescriptorSet[2];

        writes[0] = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = DescriptorSets[0],
            DescriptorCount = 1,
            DescriptorType = DescriptorType.UniformBuffer,
            PBufferInfo = &bufferInfo,
            DstArrayElement = 0,
            DstBinding = 0
        };

        writes[1] = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = DescriptorSets[0],
            DescriptorCount = 1,
            DescriptorType = DescriptorType.CombinedImageSampler,
            PImageInfo = &imageInfo,
            DstArrayElement = 0,
            DstBinding = 1
        };

        VkApi.UpdateDescriptorSets(Device, 2, writes, 0, null);
    }

    public override void Dispose()
    {
        VkApi.FreeDescriptorSets(Device, DescriptorPool, (uint)DescriptorSets.Length, in DescriptorSets[0]);

        VkApi.DestroyDescriptorPool(Device, DescriptorPool, null);

        base.Dispose();
    }

    private DescriptorPool CreateDescriptorPool()
    {
        var uniformSize = new DescriptorPoolSize
        {
            Type = DescriptorType.UniformBuffer,
            DescriptorCount = 1
        };

        var samplerSize = new DescriptorPoolSize
        {
            Type = DescriptorType.CombinedImageSampler,
            DescriptorCount = 1
        };

        var poolSizes = stackalloc DescriptorPoolSize[2] { uniformSize, samplerSize };

        var createInfo = new DescriptorPoolCreateInfo
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            MaxSets = 1,
            PoolSizeCount = 2,
            PPoolSizes = poolSizes,
            Flags = DescriptorPoolCreateFlags.FreeDescriptorSetBit
        };

        DescriptorPool pool;

        var res = VkApi.CreateDescriptorPool(Device, &createInfo, null, &pool);

        if (res != Result.Success)
        {
            throw new VmaCS.VulkanResultException("Failed to create Descriptor Pool!", res);
        }

        return pool;
    }

    private DescriptorSet[] AllocateDescriptorSets()
    {
        fixed (DescriptorSetLayout* pLayouts = DescriptorSetLayouts)
        {
            var allocInfo = new DescriptorSetAllocateInfo
            {
                SType = StructureType.DescriptorSetAllocateInfo,
                DescriptorPool = DescriptorPool,
                DescriptorSetCount = (uint)DescriptorSetLayouts.Length,
                PSetLayouts = pLayouts
            };

            var arr = new DescriptorSet[DescriptorSetLayouts.Length];

            fixed (DescriptorSet* pSets = arr)
            {
                var res = VkApi.AllocateDescriptorSets(Device, &allocInfo, pSets);

                if (res != Result.Success)
                {
                    throw new VmaCS.VulkanResultException("Failed to allocate Descriptor Sets!", res);
                }

                return arr;
            }
        }
    }
}
