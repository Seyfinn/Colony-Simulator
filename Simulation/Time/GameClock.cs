namespace GodColony.Simulation.Time;

public enum Season { Printemps, Ete, Automne, Hiver }

/// <summary>
/// Le calendrier compressé du jeu : 4 saisons de 5 jours, soit 20 jours par an.
/// La simulation avance par "ticks" ; à vitesse ×1, il y a 20 ticks par seconde réelle.
/// </summary>
public static class TimeConstants
{
    public const int TicksPerSecond = 20;
    public const int SecondsPerDay = 45;
    public const int TicksPerDay = TicksPerSecond * SecondsPerDay;
    public const int DaysPerSeason = 5;
    public const int SeasonsPerYear = 4;
    public const int DaysPerYear = DaysPerSeason * SeasonsPerYear;
    public const int TicksPerYear = TicksPerDay * DaysPerYear;
}

public sealed class GameClock
{
    public long Ticks { get; private set; }

    public GameClock(long startTicks = 0) => Ticks = startTicks;

    public void Advance() => Ticks++;

    /// <summary>Année en cours, à partir de 1.</summary>
    public int Year => (int)(Ticks / TimeConstants.TicksPerYear) + 1;

    /// <summary>Jour dans l'année, de 0 à 19.</summary>
    public int DayOfYear => (int)(Ticks % TimeConstants.TicksPerYear / TimeConstants.TicksPerDay);

    public Season Season => (Season)(DayOfYear / TimeConstants.DaysPerSeason);

    /// <summary>Jour dans la saison, de 1 à 5.</summary>
    public int DayOfSeason => DayOfYear % TimeConstants.DaysPerSeason + 1;

    /// <summary>Moment de la journée, de 0 (minuit) à 1 (minuit suivant).</summary>
    public float TimeOfDay => Ticks % TimeConstants.TicksPerDay / (float)TimeConstants.TicksPerDay;

    public int Hour => (int)(TimeOfDay * 24);
    public int Minute => (int)(TimeOfDay * 24 * 60) % 60;

    /// <summary>Luminosité du jour, de 0 (nuit noire) à 1 (plein jour). Le soleil se lève vers 6 h et se couche vers 20 h.</summary>
    public float Daylight
    {
        get
        {
            float hours = TimeOfDay * 24f;
            float sun = MathF.Sin(MathF.PI * (hours - 6f) / 14f);
            return Math.Clamp(sun * 1.6f + 0.2f, 0f, 1f);
        }
    }
}

/// <summary>Les vitesses de jeu. La valeur est le multiplicateur de temps.</summary>
public enum GameSpeed
{
    Pause = 0,
    Observation = 1,
    Rapide = 4,
    TresRapide = 30,
}
