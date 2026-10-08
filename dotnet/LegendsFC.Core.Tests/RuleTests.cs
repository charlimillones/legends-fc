using LegendsFC.Core.Model;
using LegendsFC.Core.Rules;
using Xunit;

public class LuckyCharmTests
{
    [Theory]
    [InlineData(0, 0.0)]
    [InlineData(1, 0.10)]
    [InlineData(2, 0.18)]
    [InlineData(3, 0.244)]
    public void MatchesConfirmedValues(int charms, double expected)
        => Assert.Equal(expected, LuckyCharm.TeamLuck(charms, TestData.Data.LuckyCharm), 6);

    [Fact]
    public void NeverReachesFiftyPercent_EvenWithAWholeSquadOfCharms() => Assert.True(LuckyCharm.TeamLuck(60, TestData.Data.LuckyCharm) < 0.5);
}

public class ProbabilityTests
{
    private static ProbabilityConfig C => TestData.Data.Probability;

    [Theory]
    [InlineData(0.0, 5)]
    [InlineData(1.0, 95)]
    [InlineData(0.537, 55)]
    [InlineData(0.66, 65)]
    [InlineData(0.674, 65)]
    [InlineData(0.676, 70)]
    public void ClampedAndRoundedToFive(double p, int shown) => Assert.Equal(shown, Probability.DisplayPercent(p, C));
}

public class OutOfPositionTests
{
    private static PositionFitResult Fit(string archetypeId, Position main, Foot foot, Position target)
    {
        var d = TestData.Data;
        return OutOfPosition.Evaluate(d.Archetype(archetypeId), main, foot, target, d.PositionRules, d.OutOfPosition);
    }

    [Theory]
    // archetype comes first
    [InlineData("ARC-CB-SPRINTER-CB", Position.CB, Foot.Left, Position.LB, PositionFit.Comfortable, 0.0)]
    [InlineData("ARC-CB-SPRINTER-CB", Position.CB, Foot.Right, Position.DM, PositionFit.Cover, 0.05)]
    [InlineData("ARC-FB-INVERTED-FULL-BACK", Position.LB, Foot.Left, Position.CM, PositionFit.Comfortable, 0.0)] // not on the map, archetype allows it
    // related map
    [InlineData("ARC-CB-AERIAL-TOWER", Position.CB, Foot.Left, Position.LB, PositionFit.Related, 0.10)]
    [InlineData("ARC-ST-TARGET-MAN", Position.ST, Foot.Right, Position.AM, PositionFit.Related, 0.10)]
    // unrelated
    [InlineData("ARC-ST-POACHER", Position.ST, Foot.Right, Position.CB, PositionFit.Unrelated, 0.30)]
    [InlineData("ARC-FB-LOCKDOWN", Position.LB, Foot.Left, Position.AM, PositionFit.Unrelated, 0.30)]
    [InlineData("ARC-GK-SHOT-STOPPER", Position.GK, Foot.Right, Position.ST, PositionFit.Unrelated, 0.30)]
    [InlineData("ARC-ST-POACHER", Position.ST, Foot.Right, Position.GK, PositionFit.Unrelated, 0.30)]
    // A Keeper covers GK at 5% from any outfield position
    [InlineData("ARC-RARE-A-KEEPER", Position.ST, Foot.Right, Position.GK, PositionFit.Cover, 0.05)]
    [InlineData("ARC-RARE-A-KEEPER", Position.CB, Foot.Right, Position.GK, PositionFit.Cover, 0.05)]
    // natural position, right foot
    [InlineData("ARC-ST-POACHER", Position.ST, Foot.Left, Position.ST, PositionFit.Natural, 0.0)]
    public void Tiers(string arch, Position main, Foot foot, Position target, PositionFit fit, double drop)
    {
        var r = Fit(arch, main, foot, target);
        Assert.Equal(fit, r.Fit);
        Assert.Equal(drop, r.Drop, 6);
    }

    [Fact]
    public void WrongFootAddsFive()
    {
        var r = Fit("ARC-FB-LOCKDOWN", Position.LB, Foot.Left, Position.RB); // opposite FB = cover, left foot at RB
        Assert.Equal(PositionFit.Cover, r.Fit);
        Assert.True(r.WrongFoot);
        Assert.Equal(0.10, r.Drop, 6);
    }

    [Fact]
    public void WrongFootExemptArchetypeIsNotPenalised()
    {
        var r = Fit("ARC-W-INSIDE-FORWARD", Position.RW, Foot.Left, Position.RW);
        Assert.False(r.WrongFoot);
        Assert.Equal(0.0, r.Drop, 6);
    }

    [Fact]
    public void WorstCaseIsThirtyFive() // unrelated + wrong foot
        => Assert.Equal(0.35, Fit("ARC-ST-POACHER", Position.ST, Foot.Right, Position.LB).Drop, 6);
}

public class PositionRatingTests
{
    private static AttributeSet SprinterCb()
    {
        var a = AttributeSet.Uniform(30);
        a[Attr.Pace] = 82; a[Attr.Acceleration] = 80; a[Attr.Tackling] = 78; a[Attr.Positioning] = 70; a[Attr.Heading] = 60;
        a[Attr.Strength] = 70; a[Attr.Passing] = 55; a[Attr.Crossing] = 50; a[Attr.Dribbling] = 55; a[Attr.Stamina] = 70; a[Attr.Shooting] = 40;
        return a;
    }

    private static AttributeSet AerialTower()
    {
        var a = AttributeSet.Uniform(30);
        a[Attr.Pace] = 50; a[Attr.Acceleration] = 48; a[Attr.Tackling] = 74; a[Attr.Positioning] = 72; a[Attr.Heading] = 82;
        a[Attr.Strength] = 82; a[Attr.Passing] = 55; a[Attr.Crossing] = 40; a[Attr.Dribbling] = 45; a[Attr.Stamina] = 65; a[Attr.Shooting] = 40;
        return a;
    }

    [Fact]
    public void WorkedExamplesFromTheOct8Discussion()
    {
        var d = TestData.Data;
        Assert.Equal(70.2, PositionRating.Base(SprinterCb(), Position.CB, d.PositionRatings), 1);
        Assert.Equal(71.6, PositionRating.Base(AerialTower(), Position.CB, d.PositionRatings), 1);

        var sprinterAtLb = OutOfPosition.Evaluate(d.Archetype("ARC-CB-SPRINTER-CB"), Position.CB, Foot.Left, Position.LB, d.PositionRules, d.OutOfPosition);
        Assert.Equal(67.9, PositionRating.AtPosition(SprinterCb(), Position.LB, sprinterAtLb, d.PositionRatings), 1);

        var towerAtLb = OutOfPosition.Evaluate(d.Archetype("ARC-CB-AERIAL-TOWER"), Position.CB, Foot.Left, Position.LB, d.PositionRules, d.OutOfPosition);
        Assert.Equal(52.2, PositionRating.AtPosition(AerialTower(), Position.LB, towerAtLb, d.PositionRatings), 1); // 58.0 - 10%
    }
}
