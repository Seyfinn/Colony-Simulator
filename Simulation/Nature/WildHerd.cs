namespace GodColony.Simulation.Nature;

/// <summary>
/// Une harde visible sur la carte locale d'un établissement. Ses bêtes sont <b>empruntées</b> à la population de la région
/// (<see cref="RegionWildlife"/>) et lui sont rendues quand la harde disparaît : aucune bête n'est créée par l'affichage.
/// </summary>
public sealed class WildHerd
{
    public int Id { get; internal set; }
    public WildSpecies Species { get; internal set; }

    /// <summary>Bêtes de la harde, petits compris.</summary>
    public int Count { get; internal set; }

    /// <summary>Petits (compris dans <see cref="Count"/>) : ils s'apprivoisent mieux.</summary>
    public int Young { get; internal set; }

    public float X { get; internal set; }
    public float Y { get; internal set; }

    /// <summary>Position à l'heure précédente : l'affichage interpole entre les deux.</summary>
    public float PrevX { get; internal set; }
    public float PrevY { get; internal set; }

    public float TargetX { get; internal set; }
    public float TargetY { get; internal set; }
    public HerdState State { get; internal set; }

    /// <summary>Un alpha (grand ours, loup meneur) terrorise la région ; une grande chasse peut l'abattre.</summary>
    public bool IsAlpha { get; internal set; }
    public string AlphaName { get; internal set; } = "";

    /// <summary>Faim d'un prédateur, de 0 à 1 : au-delà de la moitié, il traque une cible.</summary>
    public float Hunger { get; internal set; }

    /// <summary>L'identifiant du chasseur qui vise cette harde (0 si personne).</summary>
    public int ReservedBy { get; internal set; }

    public int TileX => (int)X;
    public int TileY => (int)Y;

    public bool IsPredator => WildSpeciesInfo.IsPredator(Species);

    public string Describe()
    {
        string state = State switch
        {
            HerdState.Grazing => "broute",
            HerdState.Roaming => "rôde",
            HerdState.Fleeing => "fuit",
            HerdState.Stalking => "traque",
            _ => "attaque",
        };
        string name = IsAlpha ? $"{WildSpeciesInfo.Name(Species)} alpha « {AlphaName} »" : $"{Count} {WildSpeciesInfo.Name(Species, Count > 1)}";
        return Young > 0 ? $"{name} (dont {Young} petit{(Young > 1 ? "s" : "")}), {state}" : $"{name}, {state}";
    }
}
