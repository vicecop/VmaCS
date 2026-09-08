using Silk.NET.Vulkan;

namespace VmaCS;

/// <summary>
/// Base exception for Vulkan-related errors in VmaCS.
/// </summary>
public class VulkanResultException : ApplicationException
{
    /// <summary>
    /// Gets the Vulkan result code associated with this exception, if any.
    /// </summary>
    public Result? Result { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="VulkanResultException"/> class with a message.
    /// </summary>
    /// <param name="message">The error message.</param>
    public VulkanResultException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="VulkanResultException"/> class with a message and inner exception.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public VulkanResultException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="VulkanResultException"/> class with a Vulkan result code.
    /// </summary>
    /// <param name="res">The Vulkan result code.</param>
    public VulkanResultException(Result res)
        : base("Vulkan returned an API error code")
    {
        Result = res;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="VulkanResultException"/> class with a message and Vulkan result code.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="res">The Vulkan result code.</param>
    public VulkanResultException(string message, Result res)
        : base(message)
    {
        Result = res;
    }
}

/// <summary>
/// Exception thrown when a memory allocation fails.
/// </summary>
public class AllocationException : VulkanResultException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AllocationException"/> class with a message.
    /// </summary>
    /// <param name="message">The error message.</param>
    public AllocationException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AllocationException"/> class with a message and inner exception.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public AllocationException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AllocationException"/> class with a Vulkan result code.
    /// </summary>
    /// <param name="res">The Vulkan result code.</param>
    public AllocationException(Result res)
        : base(res)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AllocationException"/> class with a message and Vulkan result code.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="res">The Vulkan result code.</param>
    public AllocationException(string message, Result res)
        : base(message, res)
    {
    }
}

/// <summary>
/// Exception thrown when a defragmentation operation fails.
/// </summary>
public class DefragmentationException : VulkanResultException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DefragmentationException"/> class with a message.
    /// </summary>
    /// <param name="message">The error message.</param>
    public DefragmentationException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DefragmentationException"/> class with a Vulkan result code.
    /// </summary>
    /// <param name="res">The Vulkan result code.</param>
    public DefragmentationException(Result res)
        : base(res)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DefragmentationException"/> class with a message and Vulkan result code.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="res">The Vulkan result code.</param>
    public DefragmentationException(string message, Result res)
        : base(message, res)
    {
    }
}

/// <summary>
/// Exception thrown when a memory mapping operation fails.
/// </summary>
public class MapMemoryException : VulkanResultException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MapMemoryException"/> class with a message.
    /// </summary>
    /// <param name="message">The error message.</param>
    public MapMemoryException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MapMemoryException"/> class with a Vulkan result code.
    /// </summary>
    /// <param name="res">The Vulkan result code.</param>
    public MapMemoryException(Result res)
        : base("Mapping a Device Memory block encountered an issue", res)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MapMemoryException"/> class with a message and Vulkan result code.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="res">The Vulkan result code.</param>
    public MapMemoryException(string message, Result res)
        : base(message, res)
    {
    }
}

/// <summary>
/// Exception thrown when internal allocator validation fails.
/// </summary>
public class ValidationFailedException : ApplicationException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ValidationFailedException"/> class.
    /// </summary>
    public ValidationFailedException()
        : base("Validation of Allocator structures found a bug!")
    {
    }
}
