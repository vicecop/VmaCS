namespace VmaCS;

internal static class BitOps
{
#if NETSTANDARD2_0 || NETSTANDARD2_1
    public static int PopCount(uint v)
    {
        v -= (v >> 1) & 0x55555555u;
        v = (v & 0x33333333u) + ((v >> 2) & 0x33333333u);
        return (int)(((v + (v >> 4)) & 0x0F0F0F0Fu) * 0x01010101u >> 24);
    }

    public static int PopCount(ulong v) => PopCount((uint)(v & 0xFFFFFFFFu)) + PopCount((uint)(v >> 32));

    public static int LeadingZeroCount(uint v)
    {
        if (v == 0)
        {
            return 32;
        }

        var r = 0;
        while ((v & 0x80000000u) == 0)
        {
            v <<= 1;
            r++;
        }

        return r;
    }

    public static int LeadingZeroCount(ulong v)
    {
        if (v == 0)
        {
            return 64;
        }

        var r = 0;
        while ((v & 0x8000000000000000ul) == 0)
        {
            v <<= 1;
            r++;
        }

        return r;
    }

    public static int TrailingZeroCount(uint v)
    {
        if (v == 0)
        {
            return 32;
        }

        var c = 0;
        while ((v & 1) == 0)
        {
            v >>= 1;
            c++;
        }

        return c;
    }

    public static int TrailingZeroCount(ulong v)
    {
        if (v == 0)
        {
            return 64;
        }

        var c = 0;
        while ((v & 1) == 0)
        {
            v >>= 1;
            c++;
        }

        return c;
    }
#else
    public static int PopCount(uint v) => System.Numerics.BitOperations.PopCount(v);

    public static int PopCount(ulong v) => System.Numerics.BitOperations.PopCount(v);

    public static int LeadingZeroCount(uint v) => System.Numerics.BitOperations.LeadingZeroCount(v);

    public static int LeadingZeroCount(ulong v) => System.Numerics.BitOperations.LeadingZeroCount(v);

    public static int TrailingZeroCount(uint v) => System.Numerics.BitOperations.TrailingZeroCount(v);

    public static int TrailingZeroCount(ulong v) => System.Numerics.BitOperations.TrailingZeroCount(v);
#endif
}