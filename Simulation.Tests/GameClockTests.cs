using GodColony.Simulation.Time;

namespace GodColony.Simulation.Tests;

public class GameClockTests
{
    [Fact]
    public void Une_annee_compte_20_jours_en_4_saisons()
    {
        Assert.Equal(20, TimeConstants.DaysPerYear);
        Assert.Equal(TimeConstants.TicksPerDay * 20, TimeConstants.TicksPerYear);
    }

    [Fact]
    public void Le_calendrier_passe_les_saisons_et_les_annees()
    {
        var clock = new GameClock(TimeConstants.TicksPerDay * 5);
        Assert.Equal(Season.Ete, clock.Season);
        Assert.Equal(1, clock.DayOfSeason);
        Assert.Equal(1, clock.Year);

        clock = new GameClock(TimeConstants.TicksPerYear + TimeConstants.TicksPerDay * 19);
        Assert.Equal(2, clock.Year);
        Assert.Equal(Season.Hiver, clock.Season);
        Assert.Equal(5, clock.DayOfSeason);
    }

    [Fact]
    public void Il_fait_nuit_a_minuit_et_jour_a_midi()
    {
        Assert.Equal(0f, new GameClock(0).Daylight);
        Assert.Equal(1f, new GameClock(TimeConstants.TicksPerDay / 2).Daylight);
    }
}
