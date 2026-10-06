using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GodColony.Simulation.Colonies;
using GodColony.Simulation.Nature;
using GodColony.Simulation.Map;
using GodColony.Simulation.Time;

namespace GodColony.View;

/// <summary>
/// La nature sauvage et le transport dans le village : les hardes qui paissent, rôdent ou fuient (formes procédurales : rond brun pour une proie, rouge pour un prédateur,
/// contour doré pour un alpha), les bêtes en apprivoisement et les bêtes de trait à l'enclos, les chiens des chasseurs, les charrettes, et l'infobulle au survol d'une harde ou d'un porteur
/// (le texte vient de la simulation : <see cref="WildHerd.Describe"/>, <see cref="TransportView"/>).
/// </summary>
public partial class ColonistsView
{
    private string _hoverLabel = "";
    private Vector2 _hoverAt;
    /// <summary>Point de survol local imposé uniquement par la scène de validation.</summary>
    internal Vector2? HoverPositionForValidation { get; set; }

    /// <summary>Position affichée d'une harde, lissée entre l'heure précédente et l'heure courante.</summary>
    private Vector2 HerdPosition(WildHerd herd)
    {
        // Les hardes avancent à l'heure : l'interpolation d'un tick répétait leur déplacement à chaque image.
        float hour = Mathf.Clamp((float)((_world.Clock.Ticks / (double)TimeConstants.TicksPerHour) % 1) + Alpha / TimeConstants.TicksPerHour, 0, 1);
        return new Vector2(Mathf.Lerp(herd.PrevX, herd.X, hour), Mathf.Lerp(herd.PrevY, herd.Y, hour)) * Tile;
    }

    private void AddHerds()
    {
        foreach (WildHerd herd in _settlement.Herds)
        {
            WildHerd h = herd;
            Vector2 feet = HerdPosition(h).Round();
            _standing.Add((feet.Y, () => DrawHerd(h, feet)));
        }
    }

    private static Color SpeciesColor(WildSpecies species) => species switch
    {
        WildSpecies.Rabbit => Color.Color8(168, 135, 98),
        WildSpecies.Deer => Color.Color8(165, 118, 72),
        WildSpecies.Boar => Color.Color8(96, 78, 66),
        WildSpecies.Wolf => Color.Color8(142, 78, 70),
        WildSpecies.Bear => Color.Color8(128, 52, 44),
        WildSpecies.Junglefowl => Color.Color8(176, 102, 62),
        WildSpecies.Mouflon => Color.Color8(190, 168, 128),
        WildSpecies.Aurochs => Color.Color8(80, 62, 52),
        _ => Color.Color8(139, 89, 58),
    };

    /// <summary>Une harde : une bête dessinée par individu (six au plus), les petits plus menus ; un alpha porte un contour doré.</summary>
    private void DrawHerd(WildHerd herd, Vector2 feet)
    {
        int visible = Math.Min(herd.Count, herd.IsAlpha ? 1 : 6);
        float scale = herd.Species is WildSpecies.Bear or WildSpecies.Aurochs ? 1.6f : herd.Species is WildSpecies.Rabbit or WildSpecies.Junglefowl ? 0.65f : 1f;
        for (int i = 0; i < visible; i++)
        {
            Vector2 offset = herd.IsAlpha ? Vector2.Zero : new((i % 3 - 1) * 26 + i / 3 * 8, i / 3 * 20 - 3);
            bool young = i < herd.Young;
            float bob = herd.State == HerdState.Fleeing ? (float)Math.Sin(_time * 14 + i) * 1.2f : 0f;
            DrawWildAnimal(herd.Species, feet + offset + new Vector2(0, bob), young ? 0.65f : 1f, herd.State);
        }
        if (herd.IsAlpha)
            DrawArc(feet + new Vector2(0, -9 * scale), 15 * scale + 4, 0, Mathf.Tau, 28, ArtDirection.Brass, 2);
        if (herd.State == HerdState.Stalking)
            DrawString(ArtDirection.BodyFont, feet + new Vector2(-3, -22 * scale), "!", HorizontalAlignment.Left, -1, 14, Color.Color8(222, 86, 70));
    }

    private void DrawWildAnimal(WildSpecies species, Vector2 feet, float size, HerdState state)
    {
        if (!AssetLibrary.NativeFallbackForValidation)
        {
            var sprite = NatureArt.Animal(species);
            Vector2 extent = new(sprite.GetWidth() * size, sprite.GetHeight() * size);
            DrawTextureRect(sprite, new Rect2(feet - new Vector2(extent.X / 2, extent.Y), extent), false);
            return;
        }
        float scale = species is WildSpecies.Bear or WildSpecies.Aurochs ? 1.6f : species is WildSpecies.Rabbit or WildSpecies.Junglefowl ? 0.65f : 1f;
        DrawAnimal(feet, SpeciesColor(species), size * scale, WildSpeciesInfo.IsPredator(species), state);
    }

    private void DrawAnimal(Vector2 feet, Color coat, float scale, bool predator, HerdState state)
    {
        DrawGroundShadow(feet + new Vector2(0, 1), (int)(8 * scale), Math.Max(2, (int)(3 * scale)), 0.22f);
        DrawSetTransform(feet + new Vector2(0, -5 * scale), 0, new Vector2(1.45f * scale, scale));
        DrawCircle(Vector2.Zero, 5.5f, coat);
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        Vector2 head = feet + new Vector2(7 * scale, -9 * scale);
        DrawCircle(head, 3.4f * scale, coat.Lightened(0.08f));
        foreach (float leg in new[] { -5f, -2f, 3f, 6f })
            DrawLine(feet + new Vector2(leg * scale, -3 * scale), feet + new Vector2(leg * scale, 0), coat.Darkened(0.35f), 1);
        DrawLine(feet + new Vector2(-8 * scale, -8 * scale), feet + new Vector2(-11 * scale, -10 * scale), coat.Darkened(0.2f), 1);
        // L'œil : sombre pour une proie, rouge pour un prédateur affamé qui traque.
        Color eye = predator ? (state is HerdState.Stalking or HerdState.Attacking ? Color.Color8(255, 70, 52) : Color.Color8(230, 140, 110)) : ArtDirection.Charcoal;
        DrawRect(new Rect2(head + new Vector2(1.5f * scale, -1), new Vector2(1.5f, 1.5f)), eye);
    }

    /// <summary>À l'enclos : les bêtes apprivoisées en route (une longe les retient), les chevaux et les bœufs de trait.</summary>
    private void AddPenExtras(Building pen)
    {
        int slot = new[] { ResourceType.Chickens, ResourceType.Sheep, ResourceType.Cows }.Sum(s => Math.Min(AnimalsInPen(_colony, pen, s), PenDisplayLimit(pen)));
        foreach (TamingAnimal animal in (pen == Husbandry.PenSite(_colony) ? _settlement.Taming : Enumerable.Empty<TamingAnimal>()).Take(3))
        {
            int index = slot++;
            Vector2 feet = PenAnimalPosition(pen, index);
            _standing.Add((feet.Y, () =>
            {
                DrawWildAnimal(Taming.WildFormOf(animal.Species), feet, animal.IsYoung ? 0.5f : 0.65f, HerdState.Grazing);
                DrawLine(feet + new Vector2(5, -10), feet + new Vector2(5, -20), ArtDirection.Brass, 1); // la longe
            }));
        }
        foreach ((ResourceType species, int count) in new[] { (ResourceType.Horses, AnimalsInPen(_colony, pen, ResourceType.Horses)), (ResourceType.Oxen, AnimalsInPen(_colony, pen, ResourceType.Oxen)) })
            for (int i = 0; i < Math.Min(count, PenDisplayLimit(pen)); i++)
            {
                Vector2 feet = PenAnimalPosition(pen, slot++);
                _standing.Add((feet.Y, () => DrawWildAnimal(species == ResourceType.Horses ? WildSpecies.Horse : WildSpecies.Aurochs, feet, .65f, HerdState.Grazing)));
            }
    }

    /// <summary>Les silhouettes représentatives partagent des places distinctes, à l'écart de l'abri et de la clôture.</summary>
    private static Vector2 PenAnimalPosition(Building pen, int slot)
    {
        int columns = Math.Max(2, (pen.Width * Tile - 18) / 22);
        int index = slot + (pen.IsExtension ? 0 : 2); // les deux premières places sont sous l'abri
        return new Vector2(pen.X * Tile + 14 + index % columns * 22, pen.Y * Tile + 51 + index / columns * 18);
    }

    private static int PenDisplayLimit(Building pen) => pen.Width <= 2 ? 1 : 2;

    /// <summary>Les charrettes des porteurs, et le chien qui suit un chasseur.</summary>
    private void AddTransportMarks(Colony colony)
    {
        bool dogs = colony.Stock.Get(ResourceType.Dogs) > 0;
        foreach (Colonist colonist in colony.PresentMembers)
        {
            Vector2 feet = DisplayPosition(colonist).Round();
            if (colonist.UsingCart)
                _standing.Add((feet.Y + 8, () =>
                {
                    DrawTextureRect(NatureArt.Cart, new Rect2(feet + new Vector2(10, -24), new Vector2(32, 32)), false);
                    if (colonist.Carrying is { } load)
                        DrawTextureRect(ResourceIcons.Get(load.Type), new Rect2(feet + new Vector2(20, -18), new Vector2(12, 12)), false);
                    DrawLine(feet + new Vector2(7, -9), feet + new Vector2(14, -5), ArtDirection.Brass, 1);
                }));
            if (dogs && colonist.Activity is { Kind: ActivityKind.Hunt or ActivityKind.GreatHunt })
            {
                _standing.Add((feet.Y + 4, () =>
                {
                if (!AssetLibrary.NativeFallbackForValidation)
                    DrawTextureRect(NatureArt.Dog, new Rect2(feet + new Vector2(-40, -28), new Vector2(32, 32)), false);
                else DrawAnimal(feet + new Vector2(-15, 2), Color.Color8(168, 150, 120), 0.7f, false, HerdState.Grazing);
                }));
            }
        }
    }

    /// <summary>Les cueillettes sont visibles uniquement tant que la case contient encore des portions.</summary>
    private void AddWildResources()
    {
        if (!_world.Regions.TryGetValue(_settlement.RegionTileIndex, out var region)) return;
        void Add(Dictionary<int, int> patches, ResourceType kind)
        {
            foreach ((int cell, int left) in patches)
            {
                if (left <= 0) continue;
                int x = cell % _settlement.Map.Width, y = cell / _settlement.Map.Width;
                if (!_settlement.Map.InBounds(x, y) || _colony.Buildings.Any(b => x >= b.X && x < b.X + b.Width && y >= b.Y && y < b.Y + b.Height)) continue;
                Vector2 feet = new(x * Tile + 23, y * Tile + 27);
                _standing.Add((feet.Y, () =>
                {
                    if (kind == ResourceType.Honey)
                    {
                        DrawRect(new Rect2(feet + new Vector2(-5, -12), new Vector2(10, 12)), ArtDirection.Brass.Darkened(0.25f));
                        for (int row = -10; row < 0; row += 3) DrawLine(feet + new Vector2(-6, row), feet + new Vector2(5, row), ArtDirection.Charcoal, 1);
                    }
                    else DrawTextureRect(ResourceIcons.Get(kind), new Rect2(feet + new Vector2(-8, -16), new Vector2(16, 16)), false);
                }));
            }
        }
        Add(region.Wildlife.Hives, ResourceType.Honey);
        Add(region.Wildlife.Mushrooms, ResourceType.Mushrooms);
        Add(region.Wildlife.Herbs, ResourceType.Herbs);
    }

    private void DrawBridges()
    {
        foreach (BridgeSite site in _settlement.BridgeSites)
            NatureArt.DrawBridge(this, _settlement.Map, site);
    }

    /// <summary>Le texte au survol : une harde (espèce, nombre, état, nom de l'alpha) ou un porteur (contenu et destination).</summary>
    private void UpdateHover()
    {
        _hoverLabel = "";
        Vector2 mouse = HoverPositionForValidation ?? GetLocalMousePosition();
        float best = 26f;
        foreach (WildHerd herd in _settlement.Herds)
        {
            float distance = (HerdPosition(herd) + new Vector2(0, -8) - mouse).Length();
            if (distance < best) { best = distance; _hoverLabel = herd.Describe(); _hoverAt = mouse; }
        }
        float porterBest = 18f;
        foreach (Colonist colonist in _colony.PresentMembers)
        {
            if (colonist.IsSleepingAtHome || TransportView.TransportLabel(colonist) is not { Length: > 0 } label)
                continue;
            float distance = (DisplayPosition(colonist) + new Vector2(0, -12) - mouse).Length();
            if (distance < porterBest && distance < best + 8f) { porterBest = distance; _hoverLabel = label; _hoverAt = mouse; }
        }
        if (_hoverLabel.Length == 0 && _colony.Buildings.FirstOrDefault(b => b.Type == BuildingType.Pen && b.IsComplete
            && new Rect2(b.X * Tile, b.Y * Tile, b.Width * Tile, b.Height * Tile).HasPoint(mouse)) is { } pen)
        {
            var animals = Husbandry.Species.Select(s => (Species: s, Count: AnimalsInPen(_colony, pen, s))).Where(p => p.Count > 0)
                .Select(p => $"{p.Count} {Trade.GoodName(p.Species, p.Count)}").ToList();
            if (pen == Husbandry.PenSite(_colony) && _settlement.Taming.Count > 0) animals.Add($"{_settlement.Taming.Count} en apprivoisement");
            _hoverLabel = animals.Count == 0 ? "Enclos vide" : "Enclos : " + string.Join(", ", animals);
            _hoverAt = mouse;
        }
        if (_hoverLabel.Length == 0 && _colony.Buildings.FirstOrDefault(b => new Rect2(b.X * Tile, b.Y * Tile, b.Width * Tile, b.Height * Tile).HasPoint(mouse)) is { } building)
        {
            _hoverLabel = Building.NameOf(building.Type) + (building.IsExtension ? $" · extension de #{building.ExtensionOfId}" : "");
            if (!building.IsComplete) _hoverLabel += $" · chantier {building.Progress:P0}";
            if (building.OutputStock is not null)
                _hoverLabel += " · sortie à livrer : " + string.Join(", ", Enum.GetValues<ResourceType>().Where(g => building.OutputUnits(g) > 0).Select(g => $"{building.OutputUnits(g)} {ResourceCatalog.Name(g)}"));
            CivicUse? use = building.Type switch { BuildingType.School => CivicUse.Study, BuildingType.Infirmary => CivicUse.Recover, BuildingType.Tavern => CivicUse.Relax, _ => null };
            if (use is { } service && _coverage.TryGetValue(service, out var coverage))
                _hoverLabel += coverage.Gap switch { ServiceGap.Saturated => " · ne suffit plus : saturé", ServiceGap.OutOfReach => " · ne suffit plus : logements hors de portée", _ => $" · {CivicServices.Occupancy(_colony, building, service)}/{CivicServices.CapacityOf(service)} places" };
            _hoverAt = mouse;
        }
        foreach (Monument monument in _colony.Monuments.Where(m => m.SettlementId == _settlement.Id))
            if (new Rect2(new Vector2(monument.X, monument.Y) * Tile, Vector2.One * Tile).HasPoint(mouse))
            {
                _hoverLabel = OfferingTemplate.Of(monument.Model).Name + " · achevé · "
                    + string.Join(", ", monument.Materials.Select(p => $"{p.Value} {ResourceCatalog.Name(p.Key)}"));
                _hoverAt = mouse; break;
            }
    }

    private void DrawHoverLabel()
    {
        if (_hoverLabel.Length == 0)
            return;
        var font = ArtDirection.BodyFont;
        Vector2 extent = font.GetStringSize(_hoverLabel, HorizontalAlignment.Left, -1, 12);
        Vector2 at = _hoverAt + new Vector2(16, -20);
        Transform2D inverse = GetGlobalTransformWithCanvas().AffineInverse();
        Vector2 first = inverse * Vector2.Zero, last = inverse * GetViewportRect().Size;
        at.X = Mathf.Clamp(at.X, first.X + 6, Math.Max(first.X + 6, last.X - extent.X - 6));
        at.Y = Mathf.Clamp(at.Y, first.Y + extent.Y + 6, Math.Max(first.Y + extent.Y + 6, last.Y - 8));
        DrawRect(new Rect2(at + new Vector2(-6, -extent.Y - 1), extent + new Vector2(12, 8)), new Color(ArtDirection.Charcoal, 0.92f));
        DrawString(font, at, _hoverLabel, HorizontalAlignment.Left, -1, 12, ArtDirection.Cream);
    }
}
