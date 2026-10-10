using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using LegendsFC.Core.Inbox;
using LegendsFC.Core.Model;
using LegendsFC.Core.Saves;
using LegendsFC.Core.Season;
using LegendsFC.Core.Util;
using LegendsFC.Core.World;
using Newtonsoft.Json;
using Xunit;
using Xunit.Abstractions;

/// <summary>Manager messages (rules confirmed Oct 8, wordings accepted Oct 9).</summary>
public class InboxTests : IDisposable
{
    private static LegendsFC.Core.Data.GameData D => TestData.Data;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lfc-inbox-" + Guid.NewGuid().ToString("N"));
    private readonly ITestOutputHelper _out;
    public InboxTests(ITestOutputHelper o) { _out = o; }
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private GameSession NewSession(ulong seed, string league = "ENG-1", bool strongest = true)
    {
        var s = GameSession.NewWorld(D, new SaveStore(_root, D.Saves), "Inbox", seed, "EUR", "t");
        s.Autosave = false;
        var clubs = s.World.Clubs.Where(c => s.World.ClubLeague[c.Id] == league);
        s.PickClub((strongest ? clubs.OrderByDescending(c => c.Reputation) : clubs.OrderBy(c => c.Reputation)).First().Id, "t");
        return s;
    }

    // Two seasons of one career, shared by the tests.
    private static readonly object Gate = new object();
    private static (GameSession s, List<(int season, int week, List<InboxMessage> msgs)> weeks) _career;
    private (GameSession s, List<(int season, int week, List<InboxMessage> msgs)> weeks) Career()
    {
        lock (Gate)
        {
            if (_career.s != null) return _career;
            var s = NewSession(17);
            var weeks = new List<(int, int, List<InboxMessage>)>();
            for (int i = 0; i < 104; i++)
            {
                int season = s.World.SeasonStartYear, week = s.World.Calendar.Week + 1;
                if (i == 20) s.UpgradeFacility(Facility.ClubStore);
                if (i == 40) s.World.Clubs.First(c => c.Id == s.World.UserClubId).Facilities[Facility.MedicalCentre].Condition = 61;
                if (i == 45) s.RepairAll();
                s.AdvanceWeek("t");
                weeks.Add((season, week, s.NewMessages));
            }
            return _career = (s, weeks);
        }
    }

    [Fact]
    public void Data_45Triggers_5WordingsEach_44Live()
    {
        var bank = D.Messages;
        Assert.Equal(45, bank.Triggers.Count);
        Assert.Equal(45, bank.Triggers.Select(t => t.Id).Distinct().Count());
        Assert.All(bank.Triggers, t => Assert.Equal(5, t.Wordings.Count));
        Assert.All(bank.Triggers, t => Assert.InRange(t.Priority, 1, 4));
        Assert.Equal(44, bank.Triggers.Count(t => t.Live));   // 27 + 8 squad and tactics + 3 coaches + 4 scouting + 2 events (Oct 9)
        Assert.All(bank.Triggers.Where(t => t.Name.Contains("injur") || t.Name.Contains("Illness")), t => Assert.Equal(1, t.Priority));
        var senders = new HashSet<string>(Enum.GetNames(typeof(Facility))) { "any" };
        Assert.All(bank.Triggers, t => Assert.Contains(t.Sender, senders));
        Assert.Equal(6, bank.Roles.Count);
        var known = new HashSet<string> { "general", "facility", "level", "n", "name", "role", "club", "event", "opponent", "day", "competition", "player", "attribute", "coach", "position",
                                          "injury", "score", "trophy", "amount", "up/down", "scout", "region/position", "region", "country", "age", "formation" };
        foreach (var t in bank.Triggers) foreach (var wd in t.Wordings)
            foreach (Match m in Regex.Matches(wd, @"\[([a-z/]+)\]")) Assert.Contains(m.Groups[1].Value, known);
    }

    [Fact]
    public void AtMostTwoMessagesAWeek_NoWordingTwiceInASeason_NoTriggerTwiceInARow()
    {
        var (s, weeks) = Career();
        Assert.All(weeks, w => Assert.InRange(w.msgs.Count, 0, 2));
        var all = weeks.SelectMany(x => x.msgs).Where(m => m.Kind == MessageKind.Manager).ToList();   // as delivered (the inbox is cleared at each window)
        Assert.True(all.Count >= 40, all.Count + " messages in 2 seasons");
        foreach (var g in all.GroupBy(m => m.Sender))
        {
            var seq = g.OrderBy(m => m.Id).Select(m => m.TriggerId).ToList();
            for (int i = 1; i < seq.Count; i++)
                if (!D.Messages.Get(seq[i]).Repeatable) Assert.NotEqual(seq[i - 1], seq[i]);
        }
        Assert.All(all, m => Assert.DoesNotMatch(@"\[[a-z/]+\]", m.Text));      // every slot filled
        var fired = all.GroupBy(m => m.TriggerId).ToDictionary(g => g.Key, g => g.Count());
        foreach (var kv in fired.OrderBy(k => k.Key)) _out.WriteLine($"{kv.Key}: {kv.Value}");
        foreach (var t in new[] { "MSG-INTAKE", "MSG-TOP-PROSPECT", "MSG-MONTHLY-REPORT", "MSG-UPGRADED", "MSG-COND-60", "MSG-REPAIRED" })
            Assert.True(fired.ContainsKey(t), t);
        Assert.True(fired.ContainsKey("MSG-ATTRIBUTE-UP") || fired.ContainsKey("MSG-GOOD-WEEK"));
        Assert.True(fired.ContainsKey("MSG-INJURED") || fired.ContainsKey("MSG-SERIOUS-INJURY"));
        Assert.True(fired.ContainsKey("MSG-BACK-IN-TRAINING"));
        Assert.True(fired.Keys.Count(k => k.StartsWith("MSG-")) >= 12, fired.Count + " different triggers");
        Assert.All(all, m => Assert.True(D.Messages.Get(m.TriggerId).Live));
    }

    [Fact]
    public void WordingsNeverRepeatWithinASeason()
    {
        var (_, weeks) = Career();
        foreach (var g in weeks.SelectMany(x => x.msgs).Where(m => m.Kind == MessageKind.Manager).GroupBy(m => (m.SeasonStartYear, m.TriggerId)))
        {
            var t = D.Messages.Get(g.Key.TriggerId);
            if (t.Repeatable) continue;
            Assert.InRange(g.Count(), 1, t.Wordings.Count);
            var idx = g.Select(m => t.Wordings.FindIndex(wd => Regex.IsMatch(m.Text, "^" + Regex.Replace(Regex.Escape(wd), @"\\\[[a-z/]+]", ".+") + "$"))).ToList();
            Assert.DoesNotContain(-1, idx);
            Assert.Equal(idx.Count, idx.Distinct().Count());
        }
    }

    [Fact]
    public void MessagesNeverRevealHiddenInformation()
    {
        var (_, weeks) = Career();
        var hidden = D.Archetypes.Select(a => a.Name).Concat(D.Personalities.Select(p => p.Name)).Where(x => !string.IsNullOrWhiteSpace(x) && x.Length > 3).ToList();
        foreach (var m in weeks.SelectMany(x => x.msgs).Where(m => m.Kind == MessageKind.Manager))
        {
            Assert.DoesNotContain("potential", m.Text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("archetype", m.Text, StringComparison.OrdinalIgnoreCase);
            foreach (var h in hidden) Assert.False(Regex.IsMatch(m.Text, @"\b" + Regex.Escape(h) + @"\b"), h + " in: " + m.Text);
        }
    }

    [Fact]
    public void HigherPriorityGoesFirst_LowerWaits_ThenIsDropped()
    {
        var s = NewSession(5);
        var w = s.World;
        InboxEngine.Queue(w, "MSG-MONTHLY-REPORT", Facility.ClubStore, D, ("amount", "1"));        // priority 4
        InboxEngine.Queue(w, "MSG-RIVAL-INTEREST", Facility.ScoutingCentre, D, ("club", "A"), ("player", "B"));   // 3
        InboxEngine.Queue(w, "MSG-COND-30", Facility.Stadium, D, ("facility", "Stadium"), ("n", "25"));     // 2
        s.AdvanceWeek("t");
        var first = s.NewMessages.Select(m => m.TriggerId).ToList();
        Assert.Equal(new[] { "MSG-COND-30", "MSG-RIVAL-INTEREST" }, first.Take(2));
        Assert.Contains(w.Inbox.Pending, p => p.TriggerId == "MSG-MONTHLY-REPORT");
        // Waiting messages are dropped after 2 weeks if higher ones keep coming.
        for (int i = 0; i < 3; i++)
        {
            InboxEngine.Queue(w, "MSG-COND-60", Facility.TrainingGround, D, ("facility", "Training Grounds"), ("n", "55"));
            InboxEngine.Queue(w, "MSG-COND-60", Facility.Academy, D, ("facility", "Youth Academy"), ("n", "55"));
            s.AdvanceWeek("t");
        }
        Assert.DoesNotContain(w.Inbox.Pending, p => p.TriggerId == "MSG-MONTHLY-REPORT" && p.Values["amount"] == "1");
    }

    [Fact]
    public void TheInboxNeverChangesWhatHappensInTheGame()
    {
        // Two identical careers; one has its inbox history wiped mid-way. Everything else must stay identical.
        var s1 = GameSession.NewWorld(D, new SaveStore(Path.Combine(_root, "p"), D.Saves), "x", 23, "EUR", "t"); s1.Autosave = false;
        var s2 = GameSession.NewWorld(D, new SaveStore(Path.Combine(_root, "q"), D.Saves), "x", 23, "EUR", "t"); s2.Autosave = false;
        string club = s1.World.Clubs.First(c => s1.World.ClubLeague[c.Id] == "ENG-1").Id;
        s1.PickClub(club, "t"); s2.PickClub(club, "t");
        for (int i = 0; i < 30; i++)
        {
            s1.AdvanceWeek("t"); s2.AdvanceWeek("t");
            if (i == 3) s2.World.Inbox = new InboxState();   // a different inbox history...
        }
        s2.World.Inbox = s1.World.Inbox = new InboxState();
        Assert.Equal(JsonConvert.SerializeObject(s1.World, SaveStore.Json), JsonConvert.SerializeObject(s2.World, SaveStore.Json));   // ...never changes the game
        Assert.Equal(s1.Rng.GetState(), s2.Rng.GetState());
    }

    [Fact]
    public void TheInboxIsSavedWithTheWorld()
    {
        var store = new SaveStore(Path.Combine(_root, "save"), D.Saves);
        var s = GameSession.NewWorld(D, store, "x", 31, "EUR", "t");
        s.Autosave = false;
        s.PickClub(s.World.Clubs.First(c => s.World.ClubLeague[c.Id] == "BRA-1").Id, "t");
        for (int i = 0; i < 12; i++) s.AdvanceWeek("t");
        Assert.NotEmpty(s.World.Inbox.Messages);
        InboxEngine.MarkRead(s.World, s.World.Inbox.Messages[0].Id);
        s.Save("t");
        var back = GameSession.Load(D, store, s.SlotId);
        Assert.Equal(JsonConvert.SerializeObject(s.World.Inbox), JsonConvert.SerializeObject(back.World.Inbox));
        Assert.True(back.World.Inbox.Messages[0].Read);
        Assert.Equal(s.World.Inbox.UnreadCount, back.World.Inbox.UnreadCount);
    }
}
