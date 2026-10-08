using LegendsFC.Core.Model;
using Newtonsoft.Json;
using Xunit;

public class WorldModelTests
{
    [Fact]
    public void ClubRoundTripsThroughJson()
    {
        var c = new Club { Id = "CLB-000001", Name = "Test FC", CountryId = "ENG", Division = 1, Reputation = 70, Balance = 25_000_000 };
        c.Facilities[Facility.ScoutingCentre] = new FacilityState { Level = 4, Condition = 81.5 };
        var back = JsonConvert.DeserializeObject<Club>(JsonConvert.SerializeObject(c));
        Assert.Equal(4, back.Facilities[Facility.ScoutingCentre].Level);
        Assert.Equal(81.5, back.Facilities[Facility.ScoutingCentre].Condition);
        Assert.Equal(25_000_000, back.Balance);
    }

    [Fact]
    public void AttributesRejectValuesOutside1To99()
    {
        var a = new AttributeSet();
        Assert.Throws<System.ArgumentOutOfRangeException>(() => a[Attr.Pace] = 0);
        Assert.Throws<System.ArgumentOutOfRangeException>(() => a[Attr.Pace] = 100);
    }

    [Fact]
    public void PlayerKeepsAttributesAndHiddenFieldsThroughJson()
    {
        var p = new Player { Id = "PLY-0000001", MainPosition = Position.LB, Foot = Foot.Left, ArchetypeId = "ARC-FB-OVERLAPPER",
            Potential = 81, DeclineStartAge = 30, RetireAge = 35, Attributes = AttributeSet.Uniform(50) };
        p.Attributes[Attr.Pace] = 88;
        var back = JsonConvert.DeserializeObject<Player>(JsonConvert.SerializeObject(p));
        Assert.Equal(88, back.Attributes[Attr.Pace]);
        Assert.Equal(50, back.Attributes[Attr.Distribution]);
        Assert.Equal(81, back.Potential);
        Assert.Equal(Position.LB, back.MainPosition);
    }
}
