using System;
using System.Linq;
using LegendsFC.Core.Data;
using LegendsFC.Core.Model;
using Xunit;

public class DataValidationTests
{
    private static GameData D => TestData.Data;

    [Fact]
    public void AllFilesLoad() => Assert.All(GameData.Files, f => Assert.True(System.IO.File.Exists(System.IO.Path.Combine(TestData.Root, f)), f));

    [Fact]
    public void SixFacilities_WithTheirDisplayNames_IdsUnchanged()
    {
        Assert.Equal(new[] { "Stadium", "TrainingGround", "Academy", "MedicalCentre", "ClubStore", "ScoutingCentre" }, Enum.GetNames(typeof(Facility)));
        Assert.Equal(6, D.Facilities.Count);
        Assert.Equal(Enum.GetValues(typeof(Facility)).Cast<Facility>(), D.Facilities.Select(f => f.Id));
        Assert.Equal("Stadium", D.FacilityName(Facility.Stadium));
        Assert.Equal("Training Grounds", D.FacilityName(Facility.TrainingGround));
        Assert.Equal("Youth Academy", D.FacilityName(Facility.Academy));
        Assert.Equal("Medical Building", D.FacilityName(Facility.MedicalCentre));
        Assert.Equal("Club Store", D.FacilityName(Facility.ClubStore));
        Assert.Equal("Scouting Centre", D.FacilityName(Facility.ScoutingCentre));
    }

    [Fact]
    public void ThirtySevenArchetypes_FourPerGroup_PlusOneRare()
    {
        Assert.Equal(37, D.Archetypes.Count);
        foreach (PositionGroup g in Enum.GetValues(typeof(PositionGroup)))
            Assert.Equal(4, D.Archetypes.Count(a => a.Group == g.ToString()));
        Assert.Single(D.Archetypes, a => a.IsAnyOutfield);
    }

    [Fact]
    public void IdsAreUnique()
    {
        Assert.Equal(D.Archetypes.Count, D.Archetypes.Select(a => a.Id).Distinct().Count());
        Assert.Equal(D.Personalities.Count, D.Personalities.Select(p => p.Id).Distinct().Count());
    }

    [Fact]
    public void ArchetypeAttributesAndPositionTokensAreValid()
    {
        foreach (var a in D.Archetypes)
        {
            foreach (var attr in a.Plus.Concat(a.Minus)) Assert.True(Enum.TryParse(attr, out Attr _), a.Id + ": " + attr);
            foreach (var t in a.Comfortable.Concat(a.Cover)) Assert.True(Positions.IsValidToken(t), a.Id + ": " + t);
            Assert.True(a.Plus.Count > 0 || a.Balanced, a.Id + " needs strengths or 'balanced'");
        }
    }

    [Fact]
    public void WrongFootExemptions_AreTheConfirmedFive()
    {
        var exempt = D.Archetypes.Where(a => a.WrongFootExempt).Select(a => a.Name).OrderBy(n => n);
        Assert.Equal(new[] { "Inside Forward", "Inverted Full-Back", "Trickster", "Wide 10", "Wide Carrier" }, exempt);
    }

    [Fact]
    public void ThirteenPersonalities_AKeeperIsLinkedOneToOne()
    {
        Assert.Equal(13, D.Personalities.Count);
        var special = Assert.Single(D.Personalities, p => p.ArchetypeOnly);
        Assert.Equal("A Keeper", special.Name);
        var keeper = Assert.Single(D.Archetypes, a => a.LinkedPersonalityId != null);
        Assert.Equal(special.Id, keeper.LinkedPersonalityId);
        Assert.True(keeper.IsAnyOutfield);
        Assert.Equal(0.001, keeper.SpawnPerOutfieldPlayer);
        Assert.Contains("GK", keeper.Cover);
        Assert.Equal(0.0, special.SpawnWeight); // never rolled randomly
    }

    [Fact]
    public void PersonalityShareIsInsideConfirmedRange()
        => Assert.InRange(D.PersonalitySettings.ShareWithPersonality, 0.40, 0.50);

    [Fact]
    public void RatingWeightsCoverEveryPositionAndSumToOne()
    {
        foreach (var p in Positions.All)
        {
            var w = D.PositionRatings.Weights[LegendsFC.Core.Rules.PositionRating.WeightKey(p)];
            Assert.Equal(1.0, w.Values.Sum(), 6);
            Assert.All(w.Keys, k => Assert.True(Enum.TryParse(k, out Attr _), k));
        }
    }

    [Fact]
    public void RelatedMapCoversEveryGroupWithValidTokens()
    {
        foreach (PositionGroup g in Enum.GetValues(typeof(PositionGroup)))
        {
            Assert.True(D.PositionRules.Related.ContainsKey(g.ToString()), g.ToString());
            Assert.All(D.PositionRules.Related[g.ToString()], t => Assert.True(Positions.IsValidToken(t), t));
        }
    }
}
