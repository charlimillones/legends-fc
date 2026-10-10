using System;
using System.Linq;
using LegendsFC.Core.Model;
using LegendsFC.Core.Season;
using LegendsFC.Core.Squad;
using LegendsFC.Core.Util;
using LegendsFC.Core.World;
using Xunit;
using Xunit.Abstractions;

/// <summary>Coaches (rules decided Oct 7; Carlos Oct 9: coaches speed training up, every player develops naturally).</summary>
public class CoachTests
{
    private static LegendsFC.Core.Data.GameData D => TestData.Data;
    private readonly ITestOutputHelper _out;
    public CoachTests(ITestOutputHelper o) { _out = o; }

    [Fact]
    public void World_EveryClubHasACoachPerGroup_BetterClubsBetterCoaches_AFreePool()
    {
        var w = new WorldGenerator(D).Generate(11);
        Assert.All(w.Clubs, c => Assert.Equal(new[] { "DEF", "FWD", "GK", "MID" }, Coaching.OfClub(w, c.Id).Select(x => x.Group).OrderBy(x => x)));
        Assert.Equal(D.Coaches.PoolSize, w.Coaches.Count(c => c.ClubId == null));
        double Avg(string league) => w.Clubs.Where(c => w.ClubLeague[c.Id] == league).SelectMany(c => Coaching.OfClub(w, c.Id)).Average(x => x.Rating);
        Assert.True(Avg("ENG-1") > Avg("ARG-2") + 10);
        Assert.All(w.Coaches, c => { Assert.InRange(c.Rating, 20, 95); Assert.False(string.IsNullOrWhiteSpace(c.Name)); });
        Assert.Equal(w.Coaches.Count, w.Coaches.Select(c => c.Id).Distinct().Count());
        // Every player starts with a coach of his group.
        Assert.All(w.Players.Where(p => p.ClubId != null), p => Assert.Equal(Coaching.GroupOf(p.MainPosition), w.Coaches.First(c => c.Id == p.CoachId).Group));
    }

    [Fact]
    public void LimitComesFromTheTrainingGrounds_PriceFromTheRating_ExPlayersCheaper()
    {
        var club = new Club();
        club.Facilities[Facility.TrainingGround] = new FacilityState { Level = 1, Condition = 100 };
        Assert.Equal(3, Coaching.Limit(club, D));
        club.Facilities[Facility.TrainingGround].Level = 10;
        Assert.Equal(30, Coaching.Limit(club, D));
        var coach = new Coach { Rating = 60 };
        Assert.Equal(Math.Round(25000 * Math.Pow(1.1, 20) * 2), Coaching.Price(coach, club, 2, D), 0);
        club.Id = "CLB-1"; coach.FormerClubIds.Add("CLB-1");
        Assert.Equal(Math.Round(25000 * Math.Pow(1.1, 20) * 2 * 0.7), Coaching.Price(coach, club, 2, D), 0);
    }

    [Fact]
    public void ACoachSpeedsGrowthUp_FewerPlayersFaster_ButEveryoneDevelopsNaturally()
    {
        double Growth(Func<GameWorld, Player, Club, bool> setup)
        {
            var w = new WorldGenerator(D).Generate(13);
            var club = w.Clubs.First(c => w.ClubLeague[c.Id] == "BRA-1");
            var p = LegendsFC.Core.Transfers.Market.Squad(w, club.Id).Where(x => w.SeasonStartYear - x.BirthYear <= 19).OrderBy(x => x.Id).First();
            setup(w, p, club);
            // Growth = attribute points gained, counting the hidden fractional progress.
            double Total() => p.Attributes.Values.Sum() + p.AttributeProgress.Sum();
            double before = Total();
            for (int i = 0; i < 20; i++) SeasonCycle.TrainOneWeek(w, D);
            return Total() - before;
        }
        // The user's club (no AI auto-assign), so each setup sticks.
        double natural = Growth((w, p, c) => { w.UserClubId = c.Id; p.CoachId = null; return true; });
        double crowded = Growth((w, p, c) => { w.UserClubId = c.Id; return true; });     // his group's coach with the whole group
        double alone = Growth((w, p, c) =>
        {
            w.UserClubId = c.Id;
            var coach = Coaching.OfClub(w, c.Id).First(x => x.Group == Coaching.GroupOf(p.MainPosition));
            coach.Rating = 90;
            foreach (var other in w.Players.Where(x => x.CoachId == coach.Id && x != p)) other.CoachId = null;
            return true;
        });
        _out.WriteLine($"20 weeks: natural {natural:F2}, crowded coach {crowded:F2}, top coach alone {alone:F2}");
        Assert.True(natural > 0);
        Assert.True(alone > crowded && alone > natural * 2);
    }

    [Fact]
    public void Hiring_RespectsLimitAndMoney_CoachesTrainOnlyTheirGroup()
    {
        var w = new WorldGenerator(D).Generate(17);
        var club = w.Clubs.First(c => w.ClubLeague[c.Id] == "ARG-2");
        club.Facilities[Facility.TrainingGround] = new FacilityState { Level = 2, Condition = 100 };   // limit 6
        var pool = w.Coaches.Where(c => c.ClubId == null).OrderBy(c => c.Rating).ToList();
        club.Balance = 0;
        Assert.Equal(HireResult.NotEnoughMoney, Coaching.Hire(w, club, pool[0], 2, D));
        club.Balance = 1_000_000_000;
        Assert.Equal(HireResult.Done, Coaching.Hire(w, club, pool[0], 2, D));
        Assert.Equal(w.SeasonStartYear + 2, pool[0].ContractEndYear);
        Assert.True(club.Balance < 1_000_000_000);
        Assert.Equal(HireResult.Done, Coaching.Hire(w, club, pool[1], 9, D));
        Assert.Equal(w.SeasonStartYear + 4, pool[1].ContractEndYear);                    // at most 4 seasons
        Assert.Equal(HireResult.LimitReached, Coaching.Hire(w, club, pool[2], 1, D));
        Assert.Equal(HireResult.NotAvailable, Coaching.Hire(w, club, Coaching.OfClub(w, club.Id)[0], 1, D));
        var gk = LegendsFC.Core.Transfers.Market.Squad(w, club.Id).First(p => p.MainPosition == Position.GK);
        var fwdCoach = Coaching.OfClub(w, club.Id).First(c => c.Group == "FWD");
        Assert.False(Coaching.Assign(w, gk, fwdCoach));
        Assert.True(Coaching.Assign(w, gk, Coaching.OfClub(w, club.Id).First(c => c.Group == "GK")));
        Coaching.Release(w, fwdCoach);
        Assert.Null(fwdCoach.ClubId);
        Assert.DoesNotContain(w.Players, p => p.CoachId == fwdCoach.Id);
    }

    [Fact]
    public void SeasonEnd_CoachesRetireAndMove_AiHires_ExPlayersBecomeCoaches()
    {
        var w = new WorldGenerator(D).Generate(19);
        var cycle = new SeasonCycle(D); var rng = new GameRandom(20);
        int ids = w.Coaches.Count;
        cycle.Advance(w, rng); cycle.Advance(w, rng);
        Assert.Contains(w.Coaches, c => c.FormerPlayerId != null);                         // retired players turned coach
        Assert.InRange(w.Coaches.Count(c => c.ClubId == null), D.Coaches.PoolSize, D.Coaches.PoolSize + 400);
        Assert.All(w.Coaches.Where(c => c.ClubId != null), c => Assert.True(c.ContractEndYear > w.SeasonStartYear));
        Assert.All(w.Coaches, c => Assert.True(w.SeasonStartYear - c.BirthYear < c.RetireAge));
        var big = w.Clubs.Where(c => c.Reputation >= 80 && c.Id != w.UserClubId).ToList();
        _out.WriteLine($"Coaches per club: rep 80+ {big.Average(c => Coaching.OfClub(w, c.Id).Count):F1}, all {w.Clubs.Average(c => Coaching.OfClub(w, c.Id).Count):F1}");
        Assert.True(big.Average(c => Coaching.OfClub(w, c.Id).Count) > 4.5);
        // Hiring never goes past the limit (coaches already there stay if the Training Grounds lose a level).
        Assert.All(w.Clubs, c => Assert.True(Coaching.OfClub(w, c.Id).Count <= Math.Max(4, Coaching.Limit(c, D))));
    }

    [Fact]
    public void TheUserHiresAndTheTrainingGroundsManagerSaysSo()
    {
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "lfc-coach-" + Guid.NewGuid().ToString("N"));
        try
        {
            var s = LegendsFC.Core.Saves.GameSession.NewWorld(D, new LegendsFC.Core.Saves.SaveStore(root, D.Saves), "x", 21, "EUR", "t");
            s.Autosave = false;
            s.PickClub(s.World.Clubs.First(c => s.World.ClubLeague[c.Id] == "ENG-1").Id, "t");
            var free = s.FreeCoaches.First(c => s.CoachPrice(c.Id, 2) < s.UserClub.Balance);
            Assert.Equal(HireResult.Done, s.HireCoach(free.Id, 2));
            Assert.Contains(free, s.MyCoaches);
            s.AutoAssignCoaches();
            Assert.Contains(s.World.Players, p => p.CoachId == free.Id);
            s.AdvanceWeek("t");
            Assert.Contains(s.World.Inbox.Messages, m => m.TriggerId == "MSG-COACH-SIGNED" && m.Text.Contains(free.Name) || m.TriggerId == "MSG-COACH-SIGNED");
            s.ReleaseCoach(free.Id);
            Assert.DoesNotContain(free, s.MyCoaches);
        }
        finally { try { System.IO.Directory.Delete(root, true); } catch { } }
    }
}
