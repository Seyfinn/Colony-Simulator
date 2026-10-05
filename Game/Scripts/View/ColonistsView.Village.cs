using System;
using System.Linq;
using Godot;
using GodColony.Simulation.Colonies;
using GodColony.Simulation.Map;
using GodColony.Simulation.Time;

namespace GodColony.View;

public partial class ColonistsView
{
    private double VillageTime => _world.Clock.Ticks / (double)TimeConstants.TicksPerSecond;
    private VillageNotices _villageNotices = null!;

    /// <summary>Chaque enclos reçoit au plus sa capacité ; aucun animal de réserve n'est inventé.</summary>
    internal static int AnimalsInPen(Colony colony, Building pen, ResourceType species)
    {
        var pens = colony.Buildings.Where(b => b.IsComplete && b.Type == BuildingType.Pen).ToArray();
        int index = Array.IndexOf(pens, pen);
        int placesAvant = pens.Take(Math.Max(0, index)).Sum(b => Husbandry.CapacityOf(b, species));
        return index < 0 ? 0 : Math.Clamp(Husbandry.Count(colony, species) - placesAvant, 0, Husbandry.CapacityOf(pen, species));
    }

    private void AddPenAnimals(Building pen)
    {
        var origin = new Vector2(pen.X, pen.Y) * Tile;
        foreach (ResourceType species in Husbandry.Species)
        {
            string kind = species == ResourceType.Chickens ? "chicken" : species == ResourceType.Sheep ? "sheep" : "cow";
            int count = AnimalsInPen(_colony, pen, species);
            for (int rank = 0; rank < count; rank++)
            {
                int ordinal = rank; string animal = kind;
                int lane = species == ResourceType.Chickens ? 0 : species == ResourceType.Sheep ? 1 : 2;
                Vector2 feet = origin + new Vector2(14 + (rank * 19 + lane * 23) % (pen.Width * Tile - 28), 51 + lane * 12 + rank % 2 * 3);
                feet += new Vector2((float)Math.Sin(VillageTime * 0.7 + ordinal * 2 + lane) * 1.5f, 0);
                Vector2 stableFeet = feet.Round();
                _standing.Add((stableFeet.Y, () =>
                {
                    var sprite = VillageArt.Animal(animal, (int)(VillageTime * 2 + ordinal) % 4);
                    bool left = Math.Sin(VillageTime * 0.18 + ordinal + lane) < 0;
                    DrawSetTransform(stableFeet, 0, new Vector2(left ? -1 : 1, 1));
                    DrawTexture(sprite, new Vector2(-sprite.GetWidth() / 2f, -sprite.GetHeight()));
                    DrawSetTransform(Vector2.Zero, 0, Vector2.One);
                }));
            }
        }
        Vector2 basePoint = origin + new Vector2(0, pen.Height * Tile);
        _standing.Add((basePoint.Y - 5, () =>
        {
            // La clôture avant repasse devant les pattes, sans recouvrir les bêtes par le sol de l'enclos.
            Texture2D sprite = BuildingSprites.For(pen, BiomeVisuals.At(_colony.Map, pen.X, pen.Y));
            DrawTextureRectRegion(sprite, new Rect2(basePoint + new Vector2(0, -20), new Vector2(sprite.GetWidth(), 20)),
                new Rect2(0, sprite.GetHeight() - 20, sprite.GetWidth(), 20));
            DrawPenConnections(pen);
            if (_colony.EggsReady >= 1) DrawTextureRect(ResourceIcons.Get(ResourceType.Eggs), new Rect2(basePoint + new Vector2(40, -11), new Vector2(8, 8)), false);
            if (_colony.MilkReady >= 1) DrawTextureRect(ResourceIcons.Get(ResourceType.Milk), new Rect2(basePoint + new Vector2(50, -12), new Vector2(8, 8)), false);
        }));
    }

    /// <summary>Les parcelles accolées d'un même élevage communiquent par des passages ; un chantier garde sa clôture.</summary>
    private void DrawPenConnections(Building pen)
    {
        int principal = pen.IsExtension ? pen.ExtensionOfId : pen.Id;
        foreach (Building voisin in _colony.Buildings.Where(b => b != pen && b.Type == BuildingType.Pen && b.IsComplete
            && (b.Id == principal || b.ExtensionOfId == principal)))
        {
            int haut = Math.Max(pen.Y, voisin.Y), bas = Math.Min(pen.Y + pen.Height, voisin.Y + voisin.Height);
            int gauche = Math.Max(pen.X, voisin.X), droite = Math.Min(pen.X + pen.Width, voisin.X + voisin.Width);
            Rect2 passage;
            if (haut < bas && (pen.X + pen.Width == voisin.X || voisin.X + voisin.Width == pen.X))
                passage = new Rect2(Math.Max(pen.X, voisin.X) * Tile - 7, (haut + bas) * Tile / 2f, 14, 15);
            else if (gauche < droite && (pen.Y + pen.Height == voisin.Y || voisin.Y + voisin.Height == pen.Y))
                passage = new Rect2((gauche + droite) * Tile / 2f - 7, Math.Max(pen.Y, voisin.Y) * Tile - 15, 14, 44);
            else continue;
            // Chaque parcelle ouvre sa moitié du passage : le raccord ne repeint jamais le toit ou le mobilier de sa voisine.
            passage = passage.Intersection(new Rect2(new Vector2(pen.X, pen.Y) * Tile + new Vector2(0, 28),
                new Vector2(pen.Width * Tile, pen.Height * Tile - 28)));
            if (!passage.HasArea()) continue;
            DrawRect(passage, Color.Color8(130, 132, 87));
            for (float y = passage.Position.Y + 3; y < passage.End.Y - 1; y += 5)
                DrawLine(new Vector2(passage.Position.X + 2, y), new Vector2(passage.End.X - 2, y), Color.Color8(121, 122, 80));
            foreach (float x in new[] { passage.Position.X, passage.End.X - 2 })
            {
                DrawRect(new Rect2(new Vector2(x, passage.Position.Y), new Vector2(2, Math.Min(6, passage.Size.Y))), Color.Color8(111, 77, 49));
                DrawRect(new Rect2(new Vector2(x, passage.Position.Y), new Vector2(2, 1)), Color.Color8(177, 126, 73));
            }
        }
    }

    private void DrawVillageStatus(Colonist colonist)
    {
        Vector2 feet = DisplayPosition(colonist).Round();
        if (colonist.IsSleepingAtHome && colonist.Home is { } home)
            feet = new Vector2(home.X + 1, home.Y + 2) * Tile + new Vector2(0, -48);
        int slot = 0;
        float markerScale = Math.Max(1, 1 / Math.Max(0.2f, GetGlobalTransformWithCanvas().Scale.X));
        void Marker(string state, int index) => DrawTextureRect(VillageArt.Status(state), new Rect2(feet + new Vector2((12 + index * 13) * markerScale, -30 - 12 * markerScale), Vector2.One * (12 * markerScale)), false);
        if (colonist.Ailment != Ailment.None)
        {
            Marker(colonist.Ailment == Ailment.Sick ? "sick" : "injured", slot);
            slot++;
        }
        if (colonist.IsBoosted) Marker("boosted", slot);
        if (!colonist.IsSleeping && _colony.ColdSnapDaysLeft > 0 && Climate.ColdSeverity(_colony.Map.Biome) > 0)
        {
            float breath = (float)(VillageTime * 0.55 + colonist.Id * 0.23) % 1;
            DrawRect(new Rect2(feet + new Vector2(7 + breath * 5, -21 - breath * 2), new Vector2(3 + breath * 4, 2)), new Color(0.83f, 0.92f, 0.94f, (1 - breath) * 0.7f));
        }
    }

    private void DrawVillageGesture(Colonist colonist, Vector2 feet)
    {
        if (colonist.Activity is not { Started: true } activity) return;
        float bob = (float)Math.Round(Math.Sin(VillageTime * 4 + colonist.Id));
        Vector2 hand = feet + new Vector2(7, -10 + bob);
        Color wood = Color.Color8(161, 115, 66);
        if (activity.Kind == ActivityKind.Tend)
        {
            DrawTextureRect(ResourceIcons.Get(_colony.WoolReady > _colony.EggsReady ? ResourceType.Wool : ResourceType.Eggs), new Rect2(hand, new Vector2(10, 10)), false);
        }
        else if (activity.Kind == ActivityKind.Slaughter)
        {
            DrawLine(hand, hand + new Vector2(0, -8), wood, 2);
            DrawRect(new Rect2(hand + new Vector2(0, -10), new Vector2(5, 4)), Color.Color8(188, 205, 188));
        }
        else if (activity.Kind == ActivityKind.Heal)
        {
            DrawRect(new Rect2(hand, new Vector2(8, 6)), wood); DrawRect(new Rect2(hand + new Vector2(2, -2), new Vector2(4, 3)), Color.Color8(104, 158, 87));
            DrawRect(new Rect2(hand + new Vector2(3, 1), new Vector2(2, 4)), ArtDirection.Cream); DrawRect(new Rect2(hand + new Vector2(2, 2), new Vector2(4, 2)), ArtDirection.Cream);
        }
        else if (activity.Kind == ActivityKind.Study)
        {
            DrawRect(new Rect2(hand, new Vector2(9, 8)), wood); DrawRect(new Rect2(hand + Vector2.One, new Vector2(7, 6)), ArtDirection.Charcoal);
            DrawLine(hand + new Vector2(2, 3), hand + new Vector2(6, 3), ArtDirection.Cream); DrawLine(hand + new Vector2(2, 5), hand + new Vector2(5, 5), ArtDirection.Cream);
        }
        else if (activity.Kind == ActivityKind.Relax && activity.Building?.Type == BuildingType.Tavern)
        {
            DrawRect(new Rect2(hand, new Vector2(5, 6)), ArtDirection.Brass); DrawRect(new Rect2(hand, new Vector2(5, 2)), ArtDirection.Cream);
            DrawRect(new Rect2(hand + new Vector2(5, 2), new Vector2(2, 3)), wood, false);
        }
        else if (activity.Kind == ActivityKind.Craft && activity.Building?.Type == BuildingType.Loom)
        {
            DrawLine(hand, hand + new Vector2(9, -2 * bob), wood, 2); DrawLine(hand + new Vector2(0, 2), hand + new Vector2(9, 2), ArtDirection.Cream);
        }
        else if (activity.Kind == ActivityKind.Craft && activity.Building?.Type == BuildingType.Market)
        {
            DrawTextureRect(ResourceIcons.Get(ResourceType.Coins), new Rect2(hand, new Vector2(8, 8)), false);
        }
        else if (activity.Kind == ActivityKind.Eat && activity.Meal is ResourceType.Cake or ResourceType.Stew)
        {
            DrawRect(new Rect2(feet + new Vector2(-22, -35), new Vector2(18, 18)), ArtDirection.Charcoal);
            DrawTexture(ResourceIcons.Get(activity.Meal.Value), feet + new Vector2(-21, -34));
            DrawLine(hand, hand + new Vector2(-4, -5 - bob), ArtDirection.Cream, 2);
            if (activity.Meal == ResourceType.Cake && feet.DistanceTo(new Vector2(_colony.CampX + 0.5f, _colony.CampY + 0.5f) * Tile) < 128)
                for (int i = 0; i < 3; i++) DrawRect(new Rect2(feet + new Vector2(-8 + i * 8, -40 - i % 2 * 3 + bob), Vector2.One * 2), i % 2 == 0 ? ArtDirection.Brass : ArtDirection.Cream);
        }
    }

    internal static float SnowStrength(Colony colony, Season season) => season == Season.Hiver && Climate.ColdSeverity(colony.Map.Biome) > 0
        ? Math.Clamp(Climate.ColdSeverity(colony.Map.Biome) + (colony.ColdSnapDaysLeft > 0 ? 0.2f : 0), 0, 1) : 0;

    private void DrawSeasonGround(Node2D canvas)
    {
        using var scope = _settlement.Observe();
        float snow = SnowStrength(_colony, _world.Clock.Season);
        bool drought = _colony.DroughtDaysLeft > 0;
        if (snow <= 0 && !drought && _world.Clock.Season != Season.Automne) return;
        // Calque limité à la fenêtre visible : aucune repeinture ni mutation des morceaux de terrain.
        var inverse = GetGlobalTransformWithCanvas().AffineInverse();
        Vector2 tl = inverse * Vector2.Zero, br = inverse * GetViewportRect().Size;
        int x0 = Math.Clamp((int)(tl.X / Tile) - 1, 0, _colony.Map.Width), y0 = Math.Clamp((int)(tl.Y / Tile) - 1, 0, _colony.Map.Height);
        int x1 = Math.Clamp((int)(br.X / Tile) + 2, 0, _colony.Map.Width), y1 = Math.Clamp((int)(br.Y / Tile) + 2, 0, _colony.Map.Height);
        for (int y = y0; y < y1; y++) for (int x = x0; x < x1; x++)
        {
            Surface surface = _colony.Map.GetSurface(x, y);
            if (surface is Surface.Water or Surface.River) continue;
            Vector2 p = new Vector2(x, y) * Tile;
            if (snow > 0)
            {
                canvas.DrawRect(new Rect2(p, Vector2.One * Tile), new Color(0.83f, 0.9f, 0.9f, snow * 0.46f));
                int hash = (x * 197 + y * 103) & 255;
                if (hash < snow * 200)
                {
                    Vector2 patch = p + new Vector2(3 + hash % 9, 4 + hash % 13);
                    canvas.DrawColoredPolygon([patch + new Vector2(1, 3), patch + new Vector2(4, 1), patch + new Vector2(9, 1), patch + new Vector2(11, 3),
                        patch + new Vector2(17, 3), patch + new Vector2(18, 6), patch + new Vector2(14, 8), patch + new Vector2(7, 8), patch + new Vector2(5, 6), patch + new Vector2(0, 6)], new Color(0.91f, 0.95f, 0.93f, 0.4f));
                }
            }
            else if (drought)
            {
                canvas.DrawRect(new Rect2(p, Vector2.One * Tile), new Color(0.69f, 0.49f, 0.19f, 0.22f));
                if (surface is Surface.Grass or Surface.Dirt && (x + y) % 3 == 0)
                { canvas.DrawLine(p + new Vector2(6, 17), p + new Vector2(14, 13), new Color(0.3f, 0.24f, 0.16f, 0.4f)); canvas.DrawLine(p + new Vector2(14, 13), p + new Vector2(20, 20), new Color(0.3f, 0.24f, 0.16f, 0.4f)); }
            }
            else if (surface == Surface.Grass) canvas.DrawRect(new Rect2(p, Vector2.One * Tile), new Color(0.65f, 0.42f, 0.17f, 0.12f));
        }
    }

    private void DrawRecentEvents()
    {
        foreach (RecentEvent report in _colony.RecentEvents)
        {
            double age = (_world.Clock.Ticks - report.Ticks) / (double)TimeConstants.TicksPerSecond;
            if (age < 0 || age > (report.Kind == ColonyEventKind.Fire && report.Outcome == ColonyEventOutcome.Ruined ? 90 : 18)) continue;
            Vector2 position = new Vector2(report.X, report.Y) * Tile;
            if (report.Kind == ColonyEventKind.Fire)
            {
                bool ashes = age > (report.Outcome == ColonyEventOutcome.Extinguished ? 4 : 8);
                if (ashes && report.Outcome == ColonyEventOutcome.Extinguished) continue;
                if (ashes && _colony.Buildings.Any(b => b.X == report.X && b.Y == report.Y)) continue;
                _standing.Add((position.Y + 60, () => DrawTexture(VillageArt.Event(ashes ? "building_ashes" : "building_fire", (int)(VillageTime * 6) % 4), position + new Vector2(0, -16))));
            }
            else
            {
                int visitors = report.Kind == ColonyEventKind.Raid ? 3 : 1;
                for (int i = 0; i < visitors; i++)
                {
                    int ordinal = i; bool raid = report.Kind == ColonyEventKind.Raid;
                    float journey = age < 6 ? (float)age / 6 : age < 10 ? 1 : Math.Max(0, 1 - (float)(age - 10) / 8);
                    Vector2 feet = raid ? position + new Vector2(ordinal * 20 + journey * 32, ordinal * 8)
                        : new Vector2(1 + (_colony.CampX - 1) * journey, _colony.CampY + 0.5f) * Tile;
                    _standing.Add((feet.Y, () =>
                    {
                        PeopleLook people = _colony.Members.FirstOrDefault() is { } member ? PeoplesSprites.Describe(member, WoodlandBiome.TemperatePlain).People : PeopleLook.Human;
                        var sprite = RemainingArt.TraderFrames(raid ? PeopleLook.Orc : people)[(int)(VillageTime * 4) % 4];
                        DrawTexture(sprite, feet - new Vector2(16, 32), raid ? Color.Color8(173, 138, 130) : Colors.White);
                        if (raid) { DrawLine(feet + new Vector2(9, -6), feet + new Vector2(9, -28), ArtDirection.Brass, 2); DrawRect(new Rect2(feet + new Vector2(-11, -15), new Vector2(6, 8)), ArtDirection.Charcoal); }
                        else DrawTextureRect(ResourceIcons.Get(ResourceType.Spices), new Rect2(feet + new Vector2(-14, -18), new Vector2(10, 10)), false);
                        if (age is > 6 and < 10) DrawTextureRect(ResourceIcons.Get(report.Outcome == ColonyEventOutcome.Repelled ? ResourceType.Tools : report.Outcome == ColonyEventOutcome.Traded ? ResourceType.Spices : ResourceType.Coins), new Rect2(feet + new Vector2(-6, -46), new Vector2(12, 12)), false,
                            report.Outcome == ColonyEventOutcome.Unaffordable ? new Color(0.5f, 0.5f, 0.5f) : Colors.White);
                    }));
                }
            }
        }
    }
}
