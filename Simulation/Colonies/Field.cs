namespace GodColony.Simulation.Colonies;

public enum CropStage { Fallow, Growing, Ripe }

/// <summary>Une parcelle d'un champ : une case de terre cultivée, qu'on sème, qui pousse, puis qu'on moissonne.</summary>
public sealed class FieldPlot(int x, int y)
{
    public int X { get; } = x;
    public int Y { get; } = y;
    public CropStage Stage { get; internal set; }

    /// <summary>Avancement de la pousse, de 0 (semé) à 1 (mûr).</summary>
    public float Growth { get; internal set; }
}

/// <summary>Un champ : un carré de 4 × 4 parcelles défriché près du camp.</summary>
public sealed class Field
{
    public const int Size = 4;

    public Field(int x, int y)
    {
        X = x;
        Y = y;
        for (int dy = 0; dy < Size; dy++)
        for (int dx = 0; dx < Size; dx++)
            Plots.Add(new FieldPlot(x + dx, y + dy));
    }

    /// <summary>Case en haut à gauche.</summary>
    public int X { get; }
    public int Y { get; }

    public List<FieldPlot> Plots { get; } = [];

    public bool Contains(int x, int y) => x >= X && y >= Y && x < X + Size && y < Y + Size;
}
