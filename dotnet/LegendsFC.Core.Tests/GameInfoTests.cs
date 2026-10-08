using LegendsFC.Core;
using Xunit;

public class GameInfoTests
{
    [Fact]
    public void StartSeasonIs2026() => Assert.Equal(2026, GameInfo.StartSeasonYear);
}
