using System.Collections.Generic;
using System.Linq;
using LegendsFC.Core.Career;
using LegendsFC.Core.Inbox;
using LegendsFC.Core.Model;
using LegendsFC.Core.Money;
using LegendsFC.Core.Saves;
using LegendsFC.Core.Season;
using LegendsFC.Core.Transfers;
using LegendsFC.Core.World;
using Newtonsoft.Json;
using Xunit;

/// <summary>Long careers (Oct 10 night, 20-season test run): small saves, trimmed history, spare money, fairer objectives.</summary>
public class LongCareerTests
{
    private static LegendsFC.Core.Data.GameData D => TestData.Data;

    [Fact]
    public void StatLines_SaveAsShortArrays_AndOldObjectsStillLoad()
    {
        var p = new ArchivedPlayer { Id = "P1" };
        p.Stats.Add(new StatLine { Season = 2026, CompetitionId = "ENG-1", ClubId = "C1", Apps = 30, Starts = 28, SubApps = 2, Minutes = 2500, Goals = 12, Assists = 5, CleanSheets = 0, Yellows = 3, Reds = 1, Rated = 29, PlayerOfTheMatch = 4, RatingSum = 145.79999999999998 });
        p.Stats.Add(new StatLine { Season = 2025, ClubId = "C2" });
        string json = JsonConvert.SerializeObject(p, SaveStore.Json);
        Assert.Contains("[2026,\"ENG-1\",\"C1\",30,28,2,2500,12,5,0,3,1,29,4,145.79999999999998]", json);
        var back = JsonConvert.DeserializeObject<ArchivedPlayer>(json, SaveStore.Json);
        Assert.Equal(JsonConvert.SerializeObject(p.Stats), JsonConvert.SerializeObject(back.Stats));
        Assert.Null(back.Stats[1].CompetitionId);

        var old = JsonConvert.DeserializeObject<List<StatLine>>("[{\"Season\":2024,\"CompetitionId\":null,\"ClubId\":\"C9\",\"Apps\":7,\"Goals\":2,\"Rated\":7,\"RatingSum\":46.2,\"AverageRating\":6.6}]", SaveStore.Json);
        Assert.Equal(2024, old[0].Season); Assert.Equal(7, old[0].Apps); Assert.Equal(2, old[0].Goals); Assert.Equal(6.6, old[0].AverageRating, 6);
    }

    [Fact]
    public void SeasonEnd_TrimsOldTransfersAndInbox_ButNeverAnOpenDecision()
    {
        var w = new WorldGenerator(D).Generate(5);
        w.UserClubId = w.Clubs[0].Id;
        int year = w.SeasonStartYear;
        for (int s = 0; s < 8; s++) w.Market.History.Add(new TransferRecord { Season = year - s, PlayerId = "P" + s });
        w.Career.Decisions.Add(new Decision { Id = 1 });
        w.Inbox.Messages.Add(new InboxMessage { Id = 1, Kind = MessageKind.Decision, DecisionId = 1 });   // the oldest message, still open
        for (int i = 2; i <= 400; i++) w.Inbox.Messages.Add(new InboxMessage { Id = i });
        new SeasonCycle(D).TrimHistory(w);
        Assert.Equal(D.Saves.KeepTransferSeasons, w.Market.History.Count);
        Assert.All(w.Market.History, h => Assert.True(year - h.Season < D.Saves.KeepTransferSeasons));
        Assert.Equal(D.Saves.KeepInboxMessages, w.Inbox.Messages.Count);
        Assert.Contains(w.Inbox.Messages, m => m.Id == 1);
        Assert.Contains(w.Inbox.Messages, m => m.Id == 400);
        Assert.DoesNotContain(w.Inbox.Messages, m => m.Id == 2);
    }

    [Fact]
    public void SpareMoney_RaisesTheWageBar_OnlyAboveOneSeasonOfIncome()
    {
        var f = D.Finance;
        var club = new Club { Id = "C", Reputation = 70, Balance = 300_000_000, LastSeasonIncome = 100_000_000 };
        double withSpare = Finance.WageBar(club, "ENG-1", "standard", f);
        club.Balance = 90_000_000;
        double noSpare = Finance.WageBar(club, "ENG-1", "standard", f);
        Assert.Equal(f.SpareMoneyToWageBar * 200_000_000, withSpare - noSpare, 0);
        club.LastSeasonIncome = 0; club.Balance = 300_000_000;                               // nothing settled yet: no spare money
        Assert.Equal(noSpare, Finance.WageBar(club, "ENG-1", "standard", f), 0);
    }

    [Fact]
    public void AMissedObjective_CostsMoreTheFurtherItIsMissed()
    {
        var standard = D.Board.Objectives["standard"];
        double one = Board.MissedConfidence(standard, 1), three = Board.MissedConfidence(standard, 3), ten = Board.MissedConfidence(standard, 10);
        Assert.True(one < 0 && three < one && ten <= three);
        Assert.Equal(standard.ConfidenceMissed, ten);                                         // never more than the full penalty
        Assert.Equal(-standard.ConfidencePerPlaceMissed, one);
    }

    [Fact]
    public void RetiredPlayers_AreArchivedSmall_NeverPlayedOnesAreDropped()
    {
        var w = new WorldGenerator(D).Generate(8);
        var cycle = new SeasonCycle(D); var rng = new LegendsFC.Core.Util.GameRandom(9);
        cycle.Advance(w, rng); cycle.Advance(w, rng);
        Assert.NotEmpty(w.RetiredPlayers);
        Assert.DoesNotContain(w.Players, p => p.Retired && p.RetiredYear < w.SeasonStartYear);
        Assert.All(w.RetiredPlayers, a => Assert.True(a.Stats.Count > 0 || a.FormerClubIds.Count > 0));
        Assert.All(w.RetiredPlayers, a => Assert.Equal(a.Stats.Count, a.Stats.Select(s => (s.Season, s.ClubId)).Distinct().Count()));   // one line per season and club
        var any = w.RetiredPlayers[0];
        Assert.Equal(any.Name, w.PlayerName(any.Id));
    }
}
