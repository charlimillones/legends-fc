using System;
using System.IO;
using System.Linq;
using LegendsFC.Core.Match;
using LegendsFC.Core.Saves;
using Xunit;

/// <summary>Done check (Oct 11): a full live match, played from the real squads, ends up as the result in the season.</summary>
[Collection("Heavy match batches")]
public class LiveMatchSeasonTests : IDisposable
{
    private static LegendsFC.Core.Data.GameData D => TestData.Data;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lfc-live-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    [Fact]
    public void LiveMatch_FromRealSquads_IsRecordedAsTheSeasonResult()
    {
        var s = GameSession.NewWorld(D, new SaveStore(_root, D.Saves), "x", 77, "EUR", "t");
        s.Autosave = false;
        s.PickClub(s.World.Clubs.Where(c => s.World.ClubLeague[c.Id] == "ENG-1").OrderByDescending(c => c.Reputation).ElementAt(10).Id, "t");
        for (int i = 0; i < 8; i++) s.AdvanceWeek("t");
        var fx = s.NextUserFixture();
        Assert.NotNull(fx);

        var setup = LiveMatchFactory.FromSheets(s.World, D, fx.CompetitionId, fx.HomeSheet, fx.AwaySheet, new MatchSettings { UserSide = -1 }, 1234);
        var m = new LiveMatch(setup, D.Match);
        var input = new ControlInput();
        for (int t = 0; t < 40000 && !m.IsOver; t++) m.Step(input);
        Assert.True(m.IsOver);
        var played = m.Result().ToPlayedMatch();
        Assert.Equal(22, played.Players.Count);

        var report = s.AdvanceWeek("t", played);
        var r = report.UserMatches.First(x => x.Home == fx.Fixture.Home && x.Away == fx.Fixture.Away);
        Assert.Equal(m.HomeGoals, r.HomeGoals);
        Assert.Equal(m.AwayGoals, r.AwayGoals);
    }
}
