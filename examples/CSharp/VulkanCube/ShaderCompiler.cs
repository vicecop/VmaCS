using System;
using System.IO;
using System.Text;
using Silk.NET.Core.Native;
using Silk.NET.Shaderc;

namespace VulkanCube;

public static unsafe class ShaderCompiler
{
    public static byte[] CompileGlslFromFile(string path, ShaderKind kind)
    {
        var source = File.ReadAllText(path);
        return CompileGlsl(source, kind, Path.GetFileName(path));
    }

    public static byte[] CompileGlsl(string source, ShaderKind kind, string fileName)
    {
        var shaderc = Shaderc.GetApi();
        var compiler = shaderc.CompilerInitialize();
        var options = shaderc.CompileOptionsInitialize();

        try
        {
            var sourceBytes = Encoding.UTF8.GetBytes(source);
            var fileNameBytes = Encoding.UTF8.GetBytes(fileName);
            var entryPointBytes = Encoding.UTF8.GetBytes("main");

            fixed (byte* pSource = sourceBytes)
            fixed (byte* pFileName = fileNameBytes)
            fixed (byte* pEntry = entryPointBytes)
            {
                var result = shaderc.CompileIntoSpv
                (
                    compiler,
                    pSource,
                    (UIntPtr)sourceBytes.Length,
                    kind,
                    pFileName,
                    pEntry,
                    options
                );

                try
                {
                    var status = shaderc.ResultGetCompilationStatus(result);

                    if (status != CompilationStatus.Success)
                    {
                        var errorPtr = shaderc.ResultGetErrorMessage(result);
                        var error = SilkMarshal.PtrToString((nint)errorPtr) ?? "Unknown shader compilation error";
                        throw new InvalidOperationException($"Failed to compile shader '{fileName}': {error}");
                    }

                    var outputPtr = shaderc.ResultGetBytes(result);
                    var outputLength = (int)shaderc.ResultGetLength(result);
                    var output = new byte[outputLength];

                    if (outputLength > 0)
                    {
                        System.Runtime.InteropServices.Marshal.Copy((nint)outputPtr, output, 0, outputLength);
                    }

                    return output;
                }
                finally
                {
                    shaderc.ResultRelease(result);
                }
            }
        }
        finally
        {
            shaderc.CompileOptionsRelease(options);
            shaderc.CompilerRelease(compiler);
        }
    }
}
