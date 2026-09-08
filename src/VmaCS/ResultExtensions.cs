using Silk.NET.Vulkan;

namespace VmaCS;

/// <summary>
/// Extension methods for <see cref="Result"/>.
/// </summary>
public static class ResultExtensions
{
    /// <summary>
    /// Throws a <c>TException</c> if <paramref name="throwOnError"/> is true and the result is not success;
    /// otherwise returns the result.
    /// </summary>
    /// <typeparam name="TException">Type of exception to throw.</typeparam>
    /// <param name="result">The Vulkan Result to check.</param>
    /// <param name="throwOnError">If true, throw an exception on failure; otherwise return the result.</param>
    /// <param name="message">Message to include in the exception.</param>
    /// <returns>The Vulkan Result.</returns>
    public static Result ThrowOrReturn<TException>(this Result result, bool throwOnError, string message)
        where TException : VulkanResultException
    {
        if (result.IsSuccess())
        {
            return result;
        }

        if (throwOnError)
        {
            throw (TException)Activator.CreateInstance(typeof(TException), message, result)!;
        }

        return result;
    }

    /// <summary>
    /// Indicates whether the result represents success.
    /// </summary>
    /// <param name="result">The Vulkan Result to check.</param>
    /// <returns>True if the result is success; false otherwise.</returns>
    public static bool IsSuccess(this Result result) => result == Result.Success;

    /// <summary>
    /// Indicates whether the result represents an error.
    /// </summary>
    /// <param name="result">The Vulkan Result to check.</param>
    /// <returns>True if the result is an error; false otherwise.</returns>
    public static bool IsError(this Result result) => result != Result.Success;
}
