using System;
using System.Linq;
using LegendsFC.Core.Model;
using LegendsFC.Core.Rules;
using LegendsFC.Core.Util;
using LegendsFC.Core.World;
using Xunit;

public class MarketValueTests
{
    private static MarketValueConfig C => TestData.Data.MarketValue;

    [Fact]
    public void Rated90At25IsAbout182M() => Assert.InRange(MarketValue.Eur(90, 6.5, 25, 90, 3, C), 181e6, 183e6);

    [Fact]
    public void FormSwingIsAtMost30Percent()
    {
        double mid = MarketValue.Eur(75, 6.5, 25, 75, 3, C);
        Assert.Equal(0.7, MarketValue.Eur(75, 1, 25, 75, 3, C) / mid, 6);
        Assert.Equal(1.3, MarketValue.Eur(75, 10, 25, 75, 3, C) / mid, 6);
    }

    [Fact]
    public void KnownPotentialIsPricedInForTheYoung()
    {
        Assert.True(MarketValue.Eur(60, 6.5, 19, 80, 3, C) > 5 * MarketValue.Eur(60, 6.5, 19, 60, 3, C));
        Assert.Equal(MarketValue.Eur(60, 6.5, 26, 80, 3, C), MarketValue.Eur(60, 6.5, 26, 60, 3, C)); // no premium at 26
    }
}

public class ProtegeTests
{
    [Fact]
    public void TopHalfOfTheAcademy_PricedBelowMarketValue()
    {
        var d = TestData.Data;
        var gen = new WorldGenerator(d);
        var w = gen.Generate(21);
        var club = w.Clubs.First();
        int level = club.Facilities[Facility.Academy].Level;
        double median = d.WorldGen.Potential.AcademyBase + d.WorldGen.Potential.AcademyPerLevel * level;
        var rng = new GameRandom(5);
        for (int i = 0; i < 200; i++)
        {
            var (p, price) = gen.CreateYearlyProtege(w, club, rng, Position.ST, null);
            Assert.True(p.Potential >= Math.Round(median) - 0.5, $"potential {p.Potential} below academy median {median}");
            Assert.Equal(Position.ST, p.MainPosition);
            double rating = PositionRating.Base(p.Attributes, p.MainPosition, d.PositionRatings);
            double value = MarketValue.Eur(rating, 6.5, 16, p.Potential, p.ContractEndYear - w.SeasonStartYear, d.MarketValue);
            Assert.True(price < value, $"price {price} vs value {value:F0}");
            Assert.True(price > 0);
        }
    }

    [Fact]
    public void ChoosePersonalityInstead()
    {
        var gen = new WorldGenerator(TestData.Data);
        var w = gen.Generate(22);
        var (p, _) = gen.CreateYearlyProtege(w, w.Clubs[3], new GameRandom(1), null, "PER-LEADER");
        Assert.Equal("PER-LEADER", p.PersonalityId);
        Assert.NotEqual("ARC-RARE-A-KEEPER", p.ArchetypeId);
    }

    [Fact]
    public void MustChooseExactlyOne_AndAKeeperCannotBeChosen()
    {
        var gen = new WorldGenerator(TestData.Data);
        var w = gen.Generate(23);
        Assert.Throws<ArgumentException>(() => gen.CreateYearlyProtege(w, w.Clubs[0], new GameRandom(1), Position.CB, "PER-LEADER"));
        Assert.Throws<ArgumentException>(() => gen.CreateYearlyProtege(w, w.Clubs[0], new GameRandom(1), null, null));
        Assert.Throws<ArgumentException>(() => gen.CreateYearlyProtege(w, w.Clubs[0], new GameRandom(1), null, "PER-A-KEEPER"));
    }
}
