using Silk.NET.Vulkan;

namespace VulkanCube;

/// <summary>
/// 
/// </summary>
public unsafe abstract class LayoutsExample : AllocatorAndBuffersExample
{
    protected readonly DescriptorSetLayout[] DescriptorSetLayouts;
    protected readonly PipelineLayout GraphicsPipelineLayout;

    protected LayoutsExample() : base()
    {
        DescriptorSetLayouts = CreateDescriptorSetLayouts();

        GraphicsPipelineLayout = CreatePipelineLayout();

        //VkApi.descriptors
    }

    public override void Dispose()
    {
        VkApi.DestroyPipelineLayout(Device, GraphicsPipelineLayout, null);

        foreach (var layout in DescriptorSetLayouts)
        {
            VkApi.DestroyDescriptorSetLayout(Device, layout, null);
        }

        base.Dispose();
    }

    private DescriptorSetLayout[] CreateDescriptorSetLayouts()
    {
        var binding0 = new DescriptorSetLayoutBinding
        {
            Binding = 0,
            DescriptorType = DescriptorType.UniformBuffer,
            DescriptorCount = 1,
            StageFlags = ShaderStageFlags.VertexBit
        };

        var binding1 = new DescriptorSetLayoutBinding
        {
            Binding = 1,
            DescriptorType = DescriptorType.CombinedImageSampler,
            DescriptorCount = 1,
            StageFlags = ShaderStageFlags.FragmentBit
        };

        var bindings = stackalloc DescriptorSetLayoutBinding[2] { binding0, binding1 };

        var createInfo = new DescriptorSetLayoutCreateInfo
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = 2,
            PBindings = bindings
        };

        DescriptorSetLayout layout;
        var res = VkApi.CreateDescriptorSetLayout(Device, &createInfo, null, &layout);

        if (res != Result.Success)
        {
            throw new VmaCS.VulkanResultException("Failed to create Descriptor Set Layout!", res);
        }

        return new[] { layout };
    }

    private PipelineLayout CreatePipelineLayout()
    {
        var createInfo = new PipelineLayoutCreateInfo
        {
            SType = StructureType.PipelineLayoutCreateInfo
        };

        fixed (DescriptorSetLayout* pLayouts = DescriptorSetLayouts)
        {
            createInfo.SetLayoutCount = (uint)DescriptorSetLayouts.Length;
            createInfo.PSetLayouts = pLayouts;

            PipelineLayout pipelineLayout;
            var res = VkApi.CreatePipelineLayout(Device, &createInfo, null, &pipelineLayout);

            if (res != Result.Success)
            {
                throw new VmaCS.VulkanResultException("Failed to create Pipeline Layout!", res);
            }

            return pipelineLayout;
        }
    }
}
