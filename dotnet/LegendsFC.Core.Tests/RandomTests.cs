using System.Linq;
using LegendsFC.Core.Util;
using Xunit;

public class RandomTests
{
    [Fact]
    public void SameSeedSameSequence()
    {
        var a = new GameRandom(42); var b = new GameRandom(42);
        Assert.Equal(Enumerable.Range(0, 100).Select(_ => a.NextUInt64()), Enumerable.Range(0, 100).Select(_ => b.NextUInt64()));
    }

    [Fact]
    public void DifferentSeedsDiffer() => Assert.NotEqual(new GameRandom(1).NextUInt64(), new GameRandom(2).NextUInt64());

    [Fact]
    public void NextIntStaysInRangeAndHitsBothEnds()
    {
        var r = new GameRandom(7);
        var values = Enumerable.Range(0, 10000).Select(_ => r.NextInt(2, 4)).ToList();
        Assert.All(values, v => Assert.InRange(v, 2, 4));
        Assert.Contains(2, values); Assert.Contains(4, values);
    }

    [Fact]
    public void ChanceMatchesProbability()
    {
        var r = new GameRandom(9);
        double rate = Enumerable.Range(0, 100000).Count(_ => r.Chance(0.001)) / 100000.0;
        Assert.InRange(rate, 0.0007, 0.0013); // A Keeper rarity check
    }
}
