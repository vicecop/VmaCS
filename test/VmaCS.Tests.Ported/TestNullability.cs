namespace VmaCS.Tests.Ported;

internal static class TestNullability
{
    public static T NonNull<T>(this T? value, string? message = null) where T : class
        => value ?? throw new InvalidOperationException(message ?? "Expected a non-null reference value.");

    public static T NonNull<T>(this T? value, string? message = null) where T : struct
        => value ?? throw new InvalidOperationException(message ?? "Expected a non-null value.");
}
