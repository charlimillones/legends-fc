using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using LegendsFC.Core.Saves;
using Newtonsoft.Json;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Proof that the played-match hook (Oct 11) changed nothing in sim mode: seeded careers, played week by week with no
/// played match, hash the whole world and random state after every week. The expected hashes were produced by the same
/// code on main at 8fb6840, before the hook. A deliberate rule change later will move them: rerun on that commit's parent
/// and update the constants in the same commit as the rule change.
/// </summary>
public class SimUnchangedTests : IDisposable
{
    private static LegendsFC.Core.Data.GameData D => TestData.Data;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lfc-unchanged-" + Guid.NewGuid().ToString("N"));
    private readonly ITestOutputHelper _out;
    public SimUnchangedTests(ITestOutputHelper o) { _out = o; }
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    /// <summary>A rolling SHA-256 of the full world and random state after each week, plus the last week's results.</summary>
    private string Run(ulong seed, string league, int rank, int weeks)
    {
        var s = GameSession.NewWorld(D, new SaveStore(Path.Combine(_root, seed.ToString()), D.Saves), "x", seed, "EUR", "t");
        s.Autosave = false;
        s.PickClub(s.World.Clubs.Where(c => s.World.ClubLeague[c.Id] == league).OrderByDescending(c => c.Reputation).ElementAt(rank).Id, "t");
        using (var sha = SHA256.Create())
        {
            byte[] h = new byte[32];
            int results = 0;
            for (int i = 0; i < weeks; i++)
            {
                var report = s.AdvanceWeek("t");
                results += report.MatchesPlayed;
                var snap = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { s.World, Rng = s.Rng.GetState() }, SaveStore.Json));
                h = sha.ComputeHash(h.Concat(sha.ComputeHash(snap)).ToArray());
            }
            string hex = BitConverter.ToString(h).Replace("-", "");
            _out.WriteLine($"seed {seed} {league}#{rank} {weeks} weeks, {results} matches: {hex}");
            return hex;
        }
    }

    [Fact]
    public void SimMode_WithoutAPlayedMatch_IsExactlyAsOnMain()
    {
        Assert.Equal(Expected77, Run(77, "ENG-1", 10, 60));
        Assert.Equal(Expected99, Run(99, "ESP-1", 3, 40));
    }

    private const string Expected77 = "0CC46C3D728EE0E8ED04ACE7FE8B8C2DE1956208F9B181CC1C1887CA18B61864";
    private const string Expected99 = "80EF0E7AEA3541BBBC49E0DF13506B1DCE9BC485E4CEC430BF6109F26FAAA124";
}
