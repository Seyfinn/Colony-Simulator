using GodColony.Simulation.Colonies;
using GodColony.Simulation.World;

namespace GodColony.Simulation.Nature;

/// <summary>Les bêtes sauvages d'une région. Les valeurs sont stables (sauvegardes) : on n'en ajoute qu'à la fin.</summary>
public enum WildSpecies : byte { Rabbit = 0, Deer = 1, Boar = 2, Wolf = 3, Bear = 4, Junglefowl = 5, Mouflon = 6, Aurochs = 7, Horse = 8 }

/// <summary>Une proie se chasse et se mange ; un prédateur attaque les troupeaux, les voyageurs et les travailleurs isolés.</summary>
public enum WildRole : byte { Prey, Predator }

/// <summary>Ce que fait une harde : elle broute, rôde, fuit les colons, traque une cible ou l'attaque.</summary>
public enum HerdState : byte { Grazing, Roaming, Fleeing, Stalking, Attacking }

/// <summary>La fiche de chaque espèce sauvage : rôle, taille de harde, viande, danger, abondance par biome et forme domestique.</summary>
public static class WildSpeciesInfo
{
    public static readonly WildSpecies[] All = Enum.GetValues<WildSpecies>();

    public static WildRole Role(WildSpecies species) => species is WildSpecies.Wolf or WildSpecies.Bear ? WildRole.Predator : WildRole.Prey;
    public static bool IsPredator(WildSpecies species) => Role(species) == WildRole.Predator;

    public static string Name(WildSpecies species, bool plural = false) => species switch
    {
        WildSpecies.Rabbit => plural ? "lapins" : "lapin",
        WildSpecies.Deer => plural ? "cerfs" : "cerf",
        WildSpecies.Boar => plural ? "sangliers" : "sanglier",
        WildSpecies.Wolf => plural ? "loups" : "loup",
        WildSpecies.Bear => plural ? "ours" : "ours",
        WildSpecies.Junglefowl => plural ? "coqs sauvages" : "coq sauvage",
        WildSpecies.Mouflon => plural ? "mouflons" : "mouflon",
        WildSpecies.Aurochs => plural ? "aurochs" : "aurochs",
        _ => plural ? "chevaux sauvages" : "cheval sauvage",
    };

    /// <summary>Bêtes d'une harde.</summary>
    public static int HerdSize(WildSpecies species) => species switch
    {
        WildSpecies.Rabbit => 6,
        WildSpecies.Deer => 5,
        WildSpecies.Boar => 3,
        WildSpecies.Wolf => 4,
        WildSpecies.Bear => 1,
        WildSpecies.Junglefowl => 5,
        WildSpecies.Mouflon => 5,
        WildSpecies.Aurochs => 3,
        _ => 4,
    };

    /// <summary>Repas que donne une bête abattue (comme <see cref="Husbandry.MeatYield"/> pour les bêtes d'enclos).</summary>
    public static int Meat(WildSpecies species) => species switch
    {
        WildSpecies.Rabbit => 3,
        WildSpecies.Deer => 20,
        WildSpecies.Boar => 25,
        WildSpecies.Wolf => 10,
        WildSpecies.Bear => 40,
        WildSpecies.Junglefowl => 5,
        WildSpecies.Mouflon => 18,
        WildSpecies.Aurochs => 50,
        _ => 35,
    };

    /// <summary>Peaux que donne une bête abattue.</summary>
    public static int Hides(WildSpecies species) => species switch
    {
        WildSpecies.Rabbit or WildSpecies.Junglefowl => 0,
        WildSpecies.Aurochs or WildSpecies.Bear => 2,
        _ => 1,
    };

    /// <summary>Dangerosité d'une bête acculée ou affamée, de 0 à 1 (la chance d'attaquer ou de blesser en découle).</summary>
    public static float Danger(WildSpecies species) => species switch
    {
        WildSpecies.Wolf => 0.3f,
        WildSpecies.Bear => 0.5f,
        WildSpecies.Boar => 0.25f,
        WildSpecies.Aurochs => 0.15f,
        WildSpecies.Horse => 0.05f,
        _ => 0f,
    };

    /// <summary>Force qu'oppose un alpha à ses chasseurs (voir <see cref="Hunting.ResolveGreatHunt"/>).</summary>
    public static float AlphaDanger(WildSpecies species) => species == WildSpecies.Bear ? 6f : 5f;

    /// <summary>Cases parcourues par heure.</summary>
    public static int Speed(WildSpecies species) => species switch
    {
        WildSpecies.Horse => 4,
        WildSpecies.Deer or WildSpecies.Wolf => 3,
        WildSpecies.Junglefowl => 1,
        _ => 2,
    };

    /// <summary>Secondes de travail d'une chasse à vitesse ×1.</summary>
    public static float HuntSeconds(WildSpecies species) => species switch
    {
        WildSpecies.Rabbit or WildSpecies.Junglefowl => 3f,
        WildSpecies.Aurochs or WildSpecies.Bear or WildSpecies.Boar or WildSpecies.Wolf => 6f,
        _ => 4.5f,
    };

    /// <summary>Croissance logistique par jour : les proies se multiplient vite, les prédateurs lentement.</summary>
    public static float GrowthRate(WildSpecies species) => IsPredator(species) ? 0.01f : species is WildSpecies.Rabbit or WildSpecies.Junglefowl ? 0.045f : 0.03f;

    /// <summary>Ce que devient la bête une fois apprivoisée (null : on ne l'apprivoise pas).</summary>
    public static ResourceType? DomesticForm(WildSpecies species) => species switch
    {
        WildSpecies.Junglefowl => ResourceType.Chickens,
        WildSpecies.Mouflon => ResourceType.Sheep,
        WildSpecies.Aurochs => ResourceType.Cows,
        WildSpecies.Horse => ResourceType.Horses,
        WildSpecies.Wolf => ResourceType.Dogs,
        _ => null,
    };

    /// <summary>Un jeune s'apprivoise en moins de jours qu'un adulte ; un loup, une bête de trait ou une aurochs sont plus longs.</summary>
    public static int TamingDays(WildSpecies species, bool young) => (young ? 3 : 8) + (species is WildSpecies.Wolf or WildSpecies.Horse or WildSpecies.Aurochs ? 1 : 0);

    /// <summary>Nombre de bêtes que la région peut nourrir, sans l'effet de l'homme ni de la saison.</summary>
    public static int BaseCap(Biome biome, WildSpecies species)
    {
        int b = (int)biome;
        // Ordre des biomes : Océan, Banquise, Toundra, Taïga, Forêt tempérée, Prairie, Steppe, Désert, Savane, Jungle, Marais.
        int[] row = species switch
        {
            WildSpecies.Rabbit => [0, 2, 12, 25, 40, 50, 35, 12, 25, 20, 12],
            WildSpecies.Deer => [0, 0, 10, 30, 40, 25, 10, 2, 15, 20, 8],
            WildSpecies.Boar => [0, 0, 0, 15, 25, 10, 5, 0, 10, 25, 15],
            WildSpecies.Wolf => [0, 2, 10, 12, 10, 6, 8, 2, 6, 4, 3],
            WildSpecies.Bear => [0, 3, 3, 6, 4, 0, 0, 0, 0, 2, 2],
            WildSpecies.Junglefowl => [0, 0, 0, 0, 10, 15, 5, 0, 15, 30, 10],
            WildSpecies.Mouflon => [0, 2, 12, 8, 6, 15, 25, 12, 6, 0, 0],
            WildSpecies.Aurochs => [0, 0, 4, 6, 12, 30, 22, 2, 20, 0, 3],
            _ => [0, 0, 0, 0, 4, 20, 25, 4, 15, 0, 0],
        };
        return row[b];
    }

    /// <summary>Le nom d'un alpha, tiré de deux listes (sans hasard : l'indice vient de la partie).</summary>
    public static string AlphaName(WildSpecies species, int index)
    {
        string[] wolves = ["Croc-Gris", "Œil-de-Givre", "l'Ombre des Bois", "Dent-Longue", "Hurle-Nuit"];
        string[] bears = ["Vieille Griffe", "le Grand Brun", "Pas-Lourd", "Mâchoire-de-Pierre", "le Roi des Ronces"];
        string[] list = species == WildSpecies.Bear ? bears : wolves;
        return list[(int)((uint)index % (uint)list.Length)];
    }
}
