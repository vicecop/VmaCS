namespace VmaCS.Tests.Unit;

public class MappingHysteresisTests
{
    [Fact]
    public void Initial_ExtraMappingIsZero()
    {
        var h = new MappingHysteresis();
        Assert.Equal(0u, h.ExtraMapping);
    }

    [Fact]
    public void PostMap_SixTimes_DoesNotEngage()
    {
        var h = new MappingHysteresis();
        for (var i = 0; i < 6; i++)
        {
            Assert.False(h.PostMap());
        }
        Assert.Equal(0u, h.ExtraMapping);
    }

    [Fact]
    public void PostMap_SevenTimes_EngagesExtraMapping()
    {
        var h = new MappingHysteresis();
        var engaged = false;
        for (var i = 0; i < 7; i++)
        {
            engaged = h.PostMap();
        }
        Assert.True(engaged);
        Assert.Equal(1u, h.ExtraMapping);
    }

    [Fact]
    public void PostMap_AfterEngage_KeepsExtraMapping()
    {
        var h = new MappingHysteresis();
        for (var i = 0; i < 7; i++)
        {
            h.PostMap();
        }

        Assert.Equal(1u, h.ExtraMapping);

        h.PostMap(); // already-mapped branch -> PostMinorCounter
        Assert.Equal(1u, h.ExtraMapping);
    }

    [Fact]
    public void PostUnmap_AfterEngage_KeepsExtraMapping()
    {
        var h = new MappingHysteresis();
        for (var i = 0; i < 7; i++)
        {
            h.PostMap();
        }

        h.PostUnmap();
        Assert.Equal(1u, h.ExtraMapping);
    }

    [Fact]
    public void PostAlloc_AfterEngage_KeepsExtraMapping()
    {
        var h = new MappingHysteresis();
        for (var i = 0; i < 7; i++)
        {
            h.PostMap();
        }

        h.PostAlloc();
        Assert.Equal(1u, h.ExtraMapping);
    }

    [Fact]
    public void PostFree_SevenTimes_AfterEngage_ReleasesExtraMapping()
    {
        var h = new MappingHysteresis();
        for (var i = 0; i < 7; i++)
        {
            h.PostMap();
        }

        Assert.Equal(1u, h.ExtraMapping);

        var released = false;
        for (var i = 0; i < 7; i++)
        {
            released = h.PostFree();
        }

        Assert.True(released);
        Assert.Equal(0u, h.ExtraMapping);
    }

    [Fact]
    public void PostUnmap_WhenNotEngaged_RoutesThroughMinorCounter()
    {
        var h = new MappingHysteresis();
        h.PostUnmap();
        Assert.Equal(0u, h.ExtraMapping);
    }
}
