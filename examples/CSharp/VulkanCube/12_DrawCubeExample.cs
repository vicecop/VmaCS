using Silk.NET.Windowing;
using Silk.NET.Vulkan;

namespace VulkanCube;

public sealed unsafe class DrawCubeExample : GraphicsPipelineExample
{
    public const int MAXFRAMESINFLIGHT = 2;

    private CommandBuffer[] _secondaryCommandBuffers;

    private readonly FrameCommandContext[] _frameContexts = new FrameCommandContext[MAXFRAMESINFLIGHT];

    private int _currentFrame = 0;

    public DrawCubeExample()
        : base()
    {
        RecordSecondaryCommandBuffers();

        InitializeFrameContexts();

        if (!BufferCopyPromise.IsCompleted)
        {
            BufferCopyPromise.Wait();
        }

        if (!TextureCopyPromise.IsCompleted)
        {
            TextureCopyPromise.Wait();
        }
    }

    public override void Run()
    {
        DisplayWindow.Render += DrawFrame;

        DisplayWindow.Run();

        VkApi.DeviceWaitIdle(Device);
    }

    public override void Dispose()
    {
        var primarys = stackalloc CommandBuffer[MAXFRAMESINFLIGHT];

        for (var i = 0; i < MAXFRAMESINFLIGHT; ++i)
        {
            ref var ctx = ref _frameContexts[i];

            primarys[i] = ctx.CmdBuffer;

            VkApi.DestroyFence(Device, ctx.Fence, null);

            VkApi.DestroySemaphore(Device, ctx.ImageAvailable, null);
            VkApi.DestroySemaphore(Device, ctx.RenderFinished, null);
        }

        VkApi.FreeCommandBuffers(Device, CommandPool, MAXFRAMESINFLIGHT, primarys);

        fixed (CommandBuffer* cbuffers = _secondaryCommandBuffers)
        {
            VkApi.FreeCommandBuffers(Device, CommandPool, (uint)_frameContexts.Length, cbuffers);
        }

        base.Dispose();
    }

    private void DrawFrame(double dTime)
    {
        ref var ctx = ref _frameContexts[_currentFrame];

        //Wait for a previous render operation to finish
        VkApi.WaitForFences(Device, 1, in ctx.Fence, true, ulong.MaxValue);

        //Acquire the next image index to render to, synchronize when its available
        uint nextImage = 0;
        var res = VkSwapchain.AcquireNextImage(Device, Swapchain, ulong.MaxValue, ctx.ImageAvailable, default, &nextImage);

        switch (res)
        {
            case Result.Success:
                break;
            case Result.ErrorOutOfDateKhr: //Window surface changed size, handling that is outside the scope of this example
                DisplayWindow.Close();
                return;
            default:
                throw new VmaCS.VulkanResultException("Failed to acquire next swapchain image!", res);
        }

        //Push semaphores, command buffer, and Pipeline Stage Flags to the stack to allow "fixed-less" addressing

        var waitSemaphore = ctx.ImageAvailable;
        var signalSemaphore = ctx.RenderFinished; //This semaphore will be used to synchronize presentation of the rendered image.

        var waitStages = PipelineStageFlags.ColorAttachmentOutputBit;

        var buffer = RecordPrimaryCommandBuffer(ctx.CmdBuffer, (int)nextImage); //Records primary command buffer on the fly

        //Fill out queue submit info
        var submitInfo = new SubmitInfo(
            waitSemaphoreCount: 1, pWaitSemaphores: &waitSemaphore, pWaitDstStageMask: &waitStages,
            commandBufferCount: 1, pCommandBuffers: &buffer,
            signalSemaphoreCount: 1, pSignalSemaphores: &signalSemaphore);

        //Reset Fence to unsignaled
        VkApi.ResetFences(Device, 1, in ctx.Fence);

        //Submit to Graphics queue
        res = VkApi.QueueSubmit(GraphicsQueue, 1, &submitInfo, ctx.Fence);
        if (res != Result.Success)
        {
            throw new VmaCS.VulkanResultException("Failed to submit draw command buffer!", res);
        }

        fixed (SwapchainKHR* swapchain = &Swapchain)
        {
            var presentInfo = new PresentInfoKHR(
                waitSemaphoreCount: 1,
                pWaitSemaphores: &signalSemaphore,
                swapchainCount: 1,
                pSwapchains: swapchain,
                pImageIndices: &nextImage);

            VkSwapchain.QueuePresent(PresentQueue, &presentInfo);
        }

        _currentFrame = (_currentFrame + 1) % MAXFRAMESINFLIGHT;
        Allocator.CurrentFrameIndex = (uint)_currentFrame;
    }

    private void RecordSecondaryCommandBuffers()
    {
        const uint secondaryCommandBufferCount = 1;

        _secondaryCommandBuffers = new CommandBuffer[secondaryCommandBufferCount];

        var allocInfo = new CommandBufferAllocateInfo
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = CommandPool,
            Level = CommandBufferLevel.Secondary,
            CommandBufferCount = secondaryCommandBufferCount
        };

        fixed (CommandBuffer* commandBuffers = _secondaryCommandBuffers)
        {
            var res = VkApi.AllocateCommandBuffers(Device, &allocInfo, commandBuffers);

            if (res != Result.Success)
            {
                throw new VmaCS.VulkanResultException("Failed to allocate command buffers!", res);
            }
        }

        var viewport = new Viewport
        {
            X = 0.0f,
            Y = 0.0f,
            Width = SwapchainExtent.Width,
            Height = SwapchainExtent.Height,
            MinDepth = 0.0f,
            MaxDepth = 1.0f
        };

        var scissor = new Rect2D(default, SwapchainExtent);

        var inherit = new CommandBufferInheritanceInfo(renderPass: RenderPass, subpass: 0);

        const CommandBufferUsageFlags usageFlags = CommandBufferUsageFlags.RenderPassContinueBit | CommandBufferUsageFlags.SimultaneousUseBit;

        var DrawCommandBuffer = _secondaryCommandBuffers[0];

        BeginCommandBuffer(DrawCommandBuffer, usageFlags, &inherit);

        VkApi.CmdBindPipeline(DrawCommandBuffer, PipelineBindPoint.Graphics, GraphicsPipeline);

        VkApi.CmdSetViewport(DrawCommandBuffer, 0, 1, &viewport);
        VkApi.CmdSetScissor(DrawCommandBuffer, 0, 1, &scissor);

        fixed (DescriptorSet* pDescriptorSets = DescriptorSets)
        {
            var setCount = (uint)DescriptorSets.Length;

            VkApi.CmdBindDescriptorSets(DrawCommandBuffer, PipelineBindPoint.Graphics, GraphicsPipelineLayout, 0, setCount, pDescriptorSets, 0, null);
        }

        var vertexBuffer = VertexBuffer;
        ulong offset = 0;

        VkApi.CmdBindVertexBuffers(DrawCommandBuffer, 0, 1, &vertexBuffer, &offset);

        vertexBuffer = InstanceBuffer;

        VkApi.CmdBindVertexBuffers(DrawCommandBuffer, 1, 1, &vertexBuffer, &offset);

        VkApi.CmdBindIndexBuffer(DrawCommandBuffer, IndexBuffer, 0, IndexType.Uint16);

        VkApi.CmdDrawIndexed(DrawCommandBuffer, IndexCount, InstanceCount, 0, 0, 0);
        EndCommandBuffer(DrawCommandBuffer);
    }

    private void InitializeFrameContexts()
    {
        var buffers = AllocateCommandBuffers(MAXFRAMESINFLIGHT, CommandBufferLevel.Primary);

        for (var i = 0; i < MAXFRAMESINFLIGHT; ++i)
        {
            ref var ctx = ref _frameContexts[i];

            ctx.CmdBuffer = buffers[i];

            ctx.Fence = CreateFence(true);

            ctx.ImageAvailable = CreateSemaphore();

            ctx.RenderFinished = CreateSemaphore();
        }
    }

    private CommandBuffer RecordPrimaryCommandBuffer(CommandBuffer primary, int framebufferIndex)
    {
        var res = VkApi.ResetCommandBuffer(primary, 0);

        if (res != Result.Success)
        {

        }

        var clearValues = stackalloc ClearValue[2]
        {
            new ClearValue(new ClearColorValue
            {
                Float32_0 = 0,
                Float32_1 = 0,
                Float32_2 = 0,
                Float32_3 = 1
            }),
            new ClearValue(depthStencil: new ClearDepthStencilValue(1.0f))
        };

        var renderPassInfo = new RenderPassBeginInfo
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = RenderPass,
            Framebuffer = FrameBuffers[framebufferIndex],
            RenderArea = new Rect2D(new Offset2D(0, 0), SwapchainExtent),
            ClearValueCount = 2,
            PClearValues = clearValues
        };

        BeginCommandBuffer(primary, CommandBufferUsageFlags.OneTimeSubmitBit);

        VkApi.CmdBeginRenderPass(primary, &renderPassInfo, SubpassContents.SecondaryCommandBuffers);

        fixed (CommandBuffer* cmds = _secondaryCommandBuffers)
        {
            VkApi.CmdExecuteCommands(primary, (uint)_secondaryCommandBuffers.Length, cmds);
        }

        VkApi.CmdEndRenderPass(primary);

        EndCommandBuffer(primary);

        return primary;
    }

    private Semaphore CreateSemaphore()
    {
        var semInfo = new SemaphoreCreateInfo
        {
            SType = StructureType.SemaphoreCreateInfo
        };

        Semaphore sem;
        var res = VkApi.CreateSemaphore(Device, &semInfo, null, &sem);

        if (res != Result.Success)
        {
            throw new VmaCS.VulkanResultException("Failed to create Semaphore!", res);
        }

        return sem;
    }

    private struct FrameCommandContext
    {
        public CommandBuffer CmdBuffer;
        public Fence Fence;
        public Semaphore ImageAvailable, RenderFinished;
    }
}
