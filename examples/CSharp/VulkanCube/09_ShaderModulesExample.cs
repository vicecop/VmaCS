using System;
using System.IO;

using Silk.NET.Vulkan;
using Silk.NET.Shaderc;

namespace VulkanCube;

public unsafe abstract class ShaderModulesExample : RenderPassExample
{
    protected ShaderModule VertexShader, FragmentShader;

    public ShaderModulesExample() : base()
    {
        var baseDir = AppContext.BaseDirectory;
        VertexShader = LoadShaderModule(ShaderCompiler.CompileGlslFromFile(Path.Combine(baseDir, "shader.vert"), ShaderKind.GlslVertexShader));
        FragmentShader = LoadShaderModule(ShaderCompiler.CompileGlslFromFile(Path.Combine(baseDir, "shader.frag"), ShaderKind.GlslFragmentShader));
    }

    public override void Dispose()
    {
        VkApi.DestroyShaderModule(Device, VertexShader, null);
        VkApi.DestroyShaderModule(Device, FragmentShader, null);

        base.Dispose();
    }

    private ShaderModule LoadShaderModule(byte[] code)
    {
        fixed (byte* pData = code)
        {
            var createInfo = new ShaderModuleCreateInfo
            {
                SType = StructureType.ShaderModuleCreateInfo,
                CodeSize = new UIntPtr((uint)code.Length),
                PCode = (uint*)pData
            };

            ShaderModule module;
            var res = VkApi.CreateShaderModule(Device, &createInfo, null, &module);

            if (res != Result.Success)
            {
                throw new VmaCS.VulkanResultException("Failed to create Shader Module!", res);
            }

            return module;
        }
    }
}
