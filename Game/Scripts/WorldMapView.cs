using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GodColony.Simulation;
using GodColony.Simulation.Colonies;
using GodColony.Simulation.World;
using GodColony.View;

namespace GodColony;

/// <summary>
/// La carte du monde : une grille d'hexagones colorés par biome, avec le relief, les fleuves, les colonies et les caravanes
/// qui suivent leur route de case en case. Les PNG par biome et les reliefs illustrent le terrain (T-012).
/// Un clic sur une colonie la fait observer ; pendant une fondation, un clic sur une case libre la choisit.
/// La molette zoome, le clic droit (ou du milieu) maintenu fait glisser la carte.
/// </summary>
public partial class WorldMapView : Control
{
    private static readonly Color Ink = Color.Color8(230, 237, 221);
    private static readonly Color Muted = Color.Color8(150, 174, 162);
    private static readonly Color Panel = new(0.065f, 0.115f, 0.105f, 0.97f);
    private static readonly Color Edge = Color.Color8(65, 89, 75);
    private static readonly Color RiverColor = Color.Color8(78, 140, 196);
    private const float DotRadius = 9f;
    private const float Sqrt3 = 1.7320508f;

    public event Action<int>? ColonyClicked;
    public event Action<int>? SiteClicked;
    public bool PickingSite { get; set; }

    /// <summary>Les régions conseillées pendant une fondation (la meilleure d'abord) et celle qui est mise en avant (-1 : aucune).</summary>
    public IReadOnlyList<int> Suggestions { get; set; } = [];
    public int CurrentSuggestion { get; set; } = -1;
    private string _placementMessage = "";

    private WorldState _world = null!;

    /// <summary>Colonie actuellement observée (cerclée d'or).</summary>
    public int Observed { get; set; }

    /// <summary>Zoom (1 = tout le monde tient dans le cadre) et décalage de la carte, en pixels.</summary>
    private float _zoom = 1f;
    private Vector2 _pan;
    private bool _dragging;
    private int _hovered = -1;

    /// <summary>À la première ouverture, la carte se centre sur les colonies, un peu zoomée (comme dans RimWorld).</summary>
    private bool _needsFocus;
    private const float StartZoom = 2.2f;

    public void Init(WorldState world)
    {
        _world = world;
        _zoom = 1f;
        _pan = Vector2.Zero;
        _needsFocus = true;
    }

    /// <summary>Centre la carte sur le milieu des colonies (ou sur le monde entier s'il n'y en a pas).</summary>
    private void FocusOnColonies()
    {
        _needsFocus = false;
        if (_world.Colonies.Count == 0)
            return;
        _zoom = StartZoom;
        _pan = Vector2.Zero;
        Vector2 middle = Vector2.Zero;
        foreach (Colony colony in _world.Colonies)
            middle += CenterOf(_world.WorldMap.TileOf(colony));
        middle /= _world.Colonies.Count;
        _pan = Land.Position + Land.Size / 2 - middle;
    }

    public static Color ColorOf(Species species) =>
        species == Species.Dwarf ? Color.Color8(214, 142, 84)
        : species == Species.Elf ? Color.Color8(133, 198, 167)
        : species == Species.Orc ? Color.Color8(205, 98, 86)
        : Color.Color8(226, 190, 119);

    /// <summary>Palette commune aux images, à la légende et au secours procédural.</summary>
    public static Color BiomeColor(Biome biome) => WorldBiomeArt.Ground(biome);

    // ---------- Géométrie des hexagones ----------

    private Rect2 Land => new(16, 64, Math.Max(1, Size.X - 32), Math.Max(1, Size.Y - 64 - 52));

    /// <summary>Largeur d'un hexagone à l'écran, en pixels.</summary>
    private float HexWidth
    {
        get
        {
            WorldGrid grid = _world.WorldMap.Grid;
            float fit = Math.Min(Land.Size.X / (grid.Width + 0.5f), Land.Size.Y / ((grid.Height - 1) * 0.8660254f + 1.1547f));
            return fit * _zoom;
        }
    }

    private Vector2 Origin
    {
        get
        {
            WorldGrid grid = _world.WorldMap.Grid;
            float w = HexWidth;
            var size = new Vector2((grid.Width + 0.5f) * w, ((grid.Height - 1) * 0.8660254f + 1.1547f) * w);
            // Centré dans le cadre, puis décalé par le glisser ; le premier centre est à une demi-case du coin.
            return Land.Position + (Land.Size - size) / 2 + _pan + new Vector2(w / 2, w / Sqrt3);
        }
    }

    private Vector2 CenterOf(int index)
    {
        WorldTile tile = _world.WorldMap.Grid[index];
        (float x, float y) = WorldGrid.Center(tile.Col, tile.Row);
        return Origin + new Vector2(x, y) * HexWidth;
    }

    /// <summary>Où se trouve le centre d'une case dans ce contrôle (pour les tests d'interface).</summary>
    public Vector2 ScreenPositionOf(int tile) => CenterOf(tile);

    /// <summary>Ramène une case au centre du cadre quand la carte est zoomée (dézoomée, tout est déjà visible).</summary>
    public void CenterOn(int tile)
    {
        if (_zoom > 1f)
            _pan += Land.Position + Land.Size / 2 - CenterOf(tile);
    }

    private Vector2[] Hexagon(Vector2 center, float shrink = 1f)
    {
        float radius = HexWidth / Sqrt3 * shrink;
        var points = new Vector2[6];
        for (int i = 0; i < 6; i++)
        {
            float angle = Mathf.DegToRad(60 * i - 30);
            points[i] = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        }
        return points;
    }

    /// <summary>La case sous un point de l'écran, ou -1 hors de la carte.</summary>
    private int TileAt(Vector2 point)
    {
        WorldGrid grid = _world.WorldMap.Grid;
        Vector2 local = (point - Origin) / HexWidth;
        int row = Mathf.RoundToInt(local.Y / 0.8660254f);
        int col = Mathf.RoundToInt(local.X - 0.5f * (row & 1));
        int best = -1;
        float bestDistance = float.MaxValue;
        for (int r = row - 1; r <= row + 1; r++)
        for (int c = col - 1; c <= col + 1; c++)
        {
            if (!grid.InBounds(c, r))
                continue;
            float distance = CenterOf(grid.IndexOf(c, r)).DistanceSquaredTo(point);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = grid.IndexOf(c, r);
            }
        }
        float radius = HexWidth / Sqrt3;
        return best >= 0 && bestDistance <= radius * radius && Land.HasPoint(point) ? best : -1;
    }

    // ---------- Dessin ----------

    /// <summary>Un calque de dessin : le terrain (fixe, redessiné seulement au zoom ou au déplacement) ou ce qui bouge.</summary>
    private sealed partial class Layer : Control
    {
        public Action<CanvasItem>? Paint;
        public override void _Draw() => Paint?.Invoke(this);
    }

    private Layer? _terrain, _overlay;
    private (float Zoom, Vector2 Pan, Vector2 Size, WorldState? World) _terrainKey;

    public override void _Ready()
    {
        ClipContents = true;
        _terrain = new Layer { MouseFilter = MouseFilterEnum.Ignore, Paint = PaintTerrain };
        _overlay = new Layer { MouseFilter = MouseFilterEnum.Ignore, Paint = PaintOverlay };
        foreach (Layer layer in new[] { _terrain, _overlay })
        {
            AddChild(layer);
            layer.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        }
    }

    public override void _Process(double delta)
    {
        if (!Visible || _world is null || _terrain is null || _overlay is null)
            return;
        if (_needsFocus && Size.X > 100)
            FocusOnColonies();
        var key = (_zoom, _pan, Size, _world);
        if (key != _terrainKey)
        {
            _terrainKey = key;
            _terrain.QueueRedraw();
        }
        _overlay.QueueRedraw();
    }

    public override void _Draw() => DrawRect(new Rect2(Vector2.Zero, Size), Panel);

    private void PaintTerrain(CanvasItem g)
    {
        if (_world is null || Size.X <= 0 || Size.Y <= 0)
            return;
        WorldGrid grid = _world.WorldMap.Grid;
        g.DrawRect(Land, BiomeColor(Biome.Ocean).Darkened(0.2f));
        float w = HexWidth;
        // Tant que le contrôle n'a pas sa taille, les cases n'ont pas de surface : rien à dessiner.
        if (w < 2f)
            return;
        Rect2 visible = Land.Grow(w);
        foreach (WorldTile tile in grid.Tiles)
        {
            Vector2 center = CenterOf(tile.Index);
            if (!visible.HasPoint(center))
                continue;
            Color color = BiomeColor(tile.Biome);
            // Une légère variation de teinte par case évite l'effet « damier uni ».
            float jitter = (((tile.Col * 73856093) ^ (tile.Row * 19349663)) & 15) / 15f - 0.5f;
            color = color.Lightened(0.04f * jitter);
            if (AssetLibrary.Get($"world/hex_{BiomeKey(tile.Biome)}.png") is { } texture)
            {
                // Le masque pixel art peut laisser un interstice d'un pixel aux zooms fractionnaires.
                // Un aplat de même couleur sous le PNG raccorde les cases ; il reste dans le calque fixe.
                g.DrawColoredPolygon(Hexagon(center, 1.02f), color);
                g.DrawTextureRect(texture, HexBox(center), false);
            }
            else
                g.DrawColoredPolygon(Hexagon(center, 1.02f), color);
            DrawRelief(g, tile, center, w);
        }
        DrawRivers(g, grid, visible);
    }

    private void PaintOverlay(CanvasItem g)
    {
        if (_world is null || Size.X <= 0 || Size.Y <= 0)
            return;
        var font = ArtDirection.BodyFont;
        _hovered = TileAt(GetLocalMousePosition());
        if (_hovered >= 0)
        {
            bool valid = !PickingSite || _world.WorldMap.CanSettle(_hovered, out _);
            Color outline = PickingSite ? (valid ? ArtDirection.Sage : MenuStyle.Error) : Ink;
            Vector2[] hex = Hexagon(CenterOf(_hovered));
            g.DrawPolyline([.. hex, hex[0]], outline, 2);
        }

        if (PickingSite)
            DrawSuggestions(g, font);
        DrawPacts(g);
        DrawColonies(g, font);
        DrawCaravans(g, font);
        DrawWarParties(g, font);

        // Le titre et la ligne du bas passent par-dessus les cases qui débordent du cadre quand on zoome.
        g.DrawRect(new Rect2(0, 0, Size.X, Land.Position.Y), Panel);
        g.DrawRect(new Rect2(0, Land.End.Y, Size.X, Size.Y - Land.End.Y), Panel);
        g.DrawRect(new Rect2(0, 0, Land.Position.X, Size.Y), Panel);
        g.DrawRect(new Rect2(Land.End.X, 0, Size.X - Land.End.X, Size.Y), Panel);
        g.DrawRect(Land, Edge, false, 1);
        g.DrawRect(new Rect2(Vector2.Zero, Size), Edge, false, 2);
        g.DrawString(ArtDirection.HeadingFont, new Vector2(20, 30), "Carte du monde", HorizontalAlignment.Left, -1, 18, ArtDirection.Brass);
        g.DrawString(font, new Vector2(20, 50), PickingSite
            ? "Cliquez sur une case libre pour y fonder la colonie : chaque case donne sa propre région."
            : "Cliquez sur une colonie pour l'observer · molette : zoom · clic droit maintenu : déplacer",
            HorizontalAlignment.Left, -1, 12, Muted);
        DrawLegend(g, font);
        g.DrawString(font, new Vector2(20, Size.Y - 18), BottomLine(), HorizontalAlignment.Left, Size.X - 40, 12, Muted);
    }

    /// <summary>Les régions conseillées : un contour numéroté par case, plus épais et plein pour celle qui est proposée.</summary>
    private void DrawSuggestions(CanvasItem g, Font font)
    {
        for (int i = 0; i < Suggestions.Count; i++)
        {
            bool current = Suggestions[i] == CurrentSuggestion;
            Vector2 center = CenterOf(Suggestions[i]);
            Vector2[] hex = Hexagon(center, 0.9f);
            if (current)
                g.DrawColoredPolygon(hex, new Color(ArtDirection.Brass, 0.28f));
            g.DrawPolyline([.. hex, hex[0]], ArtDirection.Brass, current ? 3 : 2);
            g.DrawString(font, center + new Vector2(-20, 5), (i + 1).ToString(), HorizontalAlignment.Center, 40, 13,
                current ? Ink : ArtDirection.Brass);
        }
    }

    /// <summary>Nom de fichier d'un biome : <c>world/hex_&lt;clé&gt;.png</c> (voir le cahier des charges, T-012).</summary>
    public static string BiomeKey(Biome biome) => biome switch
    {
        Biome.Ocean => "ocean", Biome.IceSheet => "ice", Biome.Tundra => "tundra", Biome.BorealForest => "taiga",
        Biome.TemperateForest => "temperate_forest", Biome.Grassland => "grassland", Biome.Steppe => "steppe",
        Biome.Desert => "desert", Biome.Savanna => "savanna", Biome.TropicalForest => "jungle", Biome.Swamp => "swamp",
        _ => "unknown",
    };

    /// <summary>Le rectangle d'une image d'hexagone (32 × 37 à l'échelle 1) centré sur la case.</summary>
    private Rect2 HexBox(Vector2 center)
    {
        float w = HexWidth * 1.02f, h = w * 2f / Sqrt3;
        return new Rect2(center - new Vector2(w, h) / 2, new Vector2(w, h));
    }

    /// <summary>Collines : deux bosses ; montagnes : un pic ; sommets infranchissables : un pic enneigé.</summary>
    private void DrawRelief(CanvasItem g, WorldTile tile, Vector2 center, float w)
    {
        if (tile.IsOcean || tile.Relief == Relief.Flat)
            return;
        string reliefKey = tile.Relief switch { Relief.Hills => "hills", Relief.Mountains => "mountains", _ => "peaks" };
        if (AssetLibrary.Get($"world/relief_{reliefKey}.png") is { } overlay)
        {
            g.DrawTextureRect(overlay, HexBox(center), false);
            return;
        }
        Color shade = BiomeColor(tile.Biome).Darkened(0.35f);
        float s = w * 0.28f;
        if (tile.Relief == Relief.Hills)
        {
            g.DrawArc(center + new Vector2(-s * 0.6f, s * 0.3f), s * 0.55f, Mathf.Pi, Mathf.Tau, 8, shade, Math.Max(1, w / 12));
            g.DrawArc(center + new Vector2(s * 0.6f, s * 0.1f), s * 0.55f, Mathf.Pi, Mathf.Tau, 8, shade, Math.Max(1, w / 12));
            return;
        }
        float h = tile.Relief == Relief.Impassable ? 1.5f : 1.2f;
        Vector2[] peak = [center + new Vector2(-s * 1.1f, s * 0.7f), center + new Vector2(0, -s * h), center + new Vector2(s * 1.1f, s * 0.7f)];
        g.DrawColoredPolygon(peak, Color.Color8(112, 104, 96));
        if (tile.Relief == Relief.Impassable)
            g.DrawColoredPolygon([center + new Vector2(-s * 0.42f, -s * 0.45f), center + new Vector2(0, -s * h), center + new Vector2(s * 0.42f, -s * 0.45f)],
                Color.Color8(240, 244, 248));
    }

    /// <summary>Chaque case de rivière est reliée à la case vers laquelle elle coule ; les grands fleuves sont plus épais.</summary>
    private void DrawRivers(CanvasItem g, WorldGrid grid, Rect2 visible)
    {
        float w = HexWidth;
        foreach (WorldTile tile in grid.Tiles)
        {
            if (tile.River == 0 || tile.IsOcean || tile.FlowsTo < 0)
                continue;
            Vector2 from = CenterOf(tile.Index);
            if (!visible.HasPoint(from))
                continue;
            Vector2 to = CenterOf(tile.FlowsTo);
            if (grid[tile.FlowsTo].IsOcean)
                to = from.Lerp(to, 0.6f);
            g.DrawLine(from, to, RiverColor, Math.Max(1.5f, w * (tile.River == 2 ? 0.2f : 0.1f)), true);
        }
    }

    private void DrawColonies(CanvasItem g, Font font)
    {
        for (int i = 0; i < _world.Colonies.Count; i++)
        {
            Colony colony = _world.Colonies[i];
            Vector2 point = CenterOf(_world.WorldMap.TileOf(colony));
            Color color = ColorOf(colony.Species);
            g.DrawCircle(point, DotRadius, color.Darkened(0.45f));
            g.DrawCircle(point, DotRadius - 2.5f, color);
            if (AssetLibrary.Get($"world/colony_{PeoplesSprites.LookOf(colony.Species).ToString().ToLowerInvariant()}.png") is { } marker)
                g.DrawTexture(marker, point - new Vector2(16, 16));
            if (i == Observed)
                g.DrawArc(point, DotRadius + 4, 0, Mathf.Tau, 32, ArtDirection.Brass, 2);
            // Les noms se chevauchent quand on voit tout le monde : on les montre une fois un peu zoomé.
            if (_zoom < 1.6f && i != Observed)
                continue;
            g.DrawString(font, point + new Vector2(-100, DotRadius + 15), colony.Name, HorizontalAlignment.Center, 200, 12, Ink);
            g.DrawString(font, point + new Vector2(-100, DotRadius + 28), $"{colony.Species.Plural} · {colony.Members.Count} hab.",
                HorizontalAlignment.Center, 200, 10, Muted);
        }
    }

    /// <summary>Les caravanes avancent sur leur route de case en case, à la position que calcule la simulation.</summary>
    private void DrawCaravans(CanvasItem g, Font font)
    {
        long now = _world.Clock.Ticks;
        foreach (Caravan caravan in _world.Caravans)
        {
            if (_world.WorldMap.Route(caravan.From, caravan.To) is not { } route)
                continue;
            var path = route.Tiles.Select(CenterOf).ToArray();
            if (path.Length >= 2)
                g.DrawPolyline(path, new Color(1, 1, 1, 0.25f), 1.5f);
            (int a, int b, float t) = route.At(caravan.RoutePosition(now));
            Vector2 position = CenterOf(a).Lerp(CenterOf(b), t);
            bool outbound = caravan.State == CaravanState.Outbound;
            bool left = (CenterOf(b).X - CenterOf(a).X) * (outbound ? 1 : -1) < 0;
            CaravanSprites.Draw(g, position, now / (double)GodColony.Simulation.Time.TimeConstants.TicksPerSecond, left);
            g.DrawString(font, position + new Vector2(14, -8), $"{(outbound ? "→" : "←")} {caravan.To.Name}",
                HorizontalAlignment.Left, -1, 10, Ink);
        }
    }

    /// <summary>Les pactes entre colonies : un trait vert pour une alliance, rouge pour une guerre, pointillé clair pour une trêve.</summary>
    private void DrawPacts(CanvasItem g)
    {
        foreach (Pact pact in _world.Pacts)
        {
            Vector2 a = CenterOf(_world.WorldMap.TileOf(pact.A)), b = CenterOf(_world.WorldMap.TileOf(pact.B));
            // Un liseré sombre détache le trait des cases claires.
            g.DrawLine(a, b, new Color(ArtDirection.Charcoal, 0.75f), pact.Kind == PactKind.Truce ? 3.5f : 6f, true);
            switch (pact.Kind)
            {
                case PactKind.Alliance:
                    g.DrawLine(a, b, ArtDirection.Sage, 3f, true);
                    break;
                case PactKind.War:
                    g.DrawDashedLine(a, b, MenuStyle.Error, 3f, 8f);
                    Vector2 middle = (a + b) / 2;
                    g.DrawCircle(middle, 9, new Color(0.25f, 0.06f, 0.05f, 0.95f));
                    g.DrawTexture(CivilizationArt.WarPact(), middle - new Vector2(8, 8));
                    break;
                default:
                    g.DrawDashedLine(a, b, new Color(ArtDirection.Cream, 0.5f), 1.5f, 5f);
                    break;
            }
        }
    }

    /// <summary>Les bandes de guerriers suivent leur route réelle ; quatre poses et une étiquette qui évite les noms des colonies.</summary>
    private void DrawWarParties(CanvasItem g, Font font)
    {
        long now = _world.Clock.Ticks;
        foreach (WarParty party in _world.WarParties)
        {
            if (_world.WorldMap.Route(party.From, party.To) is not { } route)
                continue;
            (int a, int b, float t) = route.At(party.RoutePosition(now));
            Vector2 position = CenterOf(a).Lerp(CenterOf(b), t);
            bool outbound = party.State == WarPartyState.Outbound;
            bool left = (CenterOf(b).X - CenterOf(a).X) * (outbound ? 1 : -1) < 0;
            CivilizationArt.DrawWarband(g, position, now, left, !outbound);
            // Chercher une place autour de la bande qui laisse lisibles les noms des colonies.
            string label = $"⚔ {party.Warriors.Count} {(outbound ? "→" : "←")} {(outbound ? party.To.Name : party.From.Name)}";
            Vector2 extent = font.GetStringSize(label, fontSize: 11);
            Vector2 at = WarLabelPosition(position, extent);
            g.DrawRect(new Rect2(at + new Vector2(-4, -extent.Y), extent + new Vector2(8, 4)), new Color(ArtDirection.Charcoal, 0.9f));
            g.DrawString(font, at, label, HorizontalAlignment.Left, -1, 11, outbound ? MenuStyle.Error.Lightened(0.2f) : Ink);
        }
    }

    private Vector2 WarLabelPosition(Vector2 position, Vector2 extent)
    {
        for (int ring = 0; ring < 12; ring++)
        {
            float gap = 18 + ring * 24;
            Vector2[] offsets = [new(-extent.X / 2, -gap), new(gap, -18), new(-extent.X - gap, -18), new(-extent.X / 2, gap + extent.Y)];
            foreach (Vector2 offset in offsets)
            {
                Vector2 at = position + offset;
                Rect2 box = new(at + new Vector2(-4, -extent.Y), extent + new Vector2(8, 4));
                if (Land.Encloses(box) && _world.Colonies.All(c => !box.Intersects(new Rect2(CenterOf(_world.WorldMap.TileOf(c)) + new Vector2(-100, -18), new Vector2(200, 62)))))
                    return at;
            }
        }
        return position + new Vector2(-extent.X / 2, -18);
    }

    private static readonly Biome[] LegendOrder =
    [
        Biome.IceSheet, Biome.Tundra, Biome.BorealForest, Biome.TemperateForest, Biome.Grassland, Biome.Steppe,
        Biome.Desert, Biome.Savanna, Biome.TropicalForest, Biome.Swamp, Biome.Ocean,
    ];

    private void DrawLegend(CanvasItem g, Font font)
    {
        const float rowHeight = 15f, width = 128f;
        var box = new Rect2(Size.X - width - 24, 64 + 8, width, LegendOrder.Length * rowHeight + 10);
        g.DrawRect(box, new Color(Panel, 0.85f));
        g.DrawRect(box, Edge, false, 1);
        for (int i = 0; i < LegendOrder.Length; i++)
        {
            Vector2 at = box.Position + new Vector2(8, 6 + i * rowHeight);
            g.DrawRect(new Rect2(at, new Vector2(11, 11)), BiomeColor(LegendOrder[i]));
            g.DrawString(font, at + new Vector2(17, 10), BiomeInfo.Of(LegendOrder[i]).Name, HorizontalAlignment.Left, -1, 10, Ink);
        }
    }

    /// <summary>La ligne du bas : la case survolée (biome, relief, climat), ou l'état des caravanes.</summary>
    private string BottomLine()
    {
        if (_hovered >= 0)
        {
            WorldTile tile = _world.WorldMap.Grid[_hovered];
            if (tile.IsOcean)
                return "Océan";
            string line = $"{tile.Describe()} · {tile.Temperature:0} °C en moyenne · pluies {tile.Rainfall * 100:0} %";
            if (tile.River > 0) line += tile.River == 2 ? " · grand fleuve" : " · rivière";
            if (tile.Coastal) line += " · côte";
            if (tile.Habitable) line += $" · sol {tile.Info.SoilRichness * 100:0} %";
            if (_world.WorldMap.ColonyAt(_hovered) is { } colony)
            {
                line += $" · {colony.Name} ({Knowledge.AgeName(Knowledge.AgeOf(colony)).ToLowerInvariant()})";
                if (Observed >= 0 && Observed < _world.Colonies.Count && _world.Colonies[Observed] is { } observed && observed != colony)
                    line += $" · {observed.Name} la juge {Diplomacy.Attitude(observed.OpinionOf(colony)).ToLowerInvariant()} ({observed.OpinionOf(colony):+0;−0;0})"
                        + Diplomacy.PactBetween(_world, observed, colony)?.Kind switch
                        {
                            PactKind.Alliance => ", alliée",
                            PactKind.War => ", en guerre",
                            PactKind.Truce => ", en trêve",
                            _ => "",
                        };
            }
            else if (PickingSite)
            {
                _world.WorldMap.CanSettle(_hovered, out string reason);
                line += $" — {reason}";
            }
            return line;
        }
        if (PickingSite)
            return _placementMessage;
        if (_world.Colonies.Count == 0)
            return "Monde vierge · fondez votre première colonie.";
        string status = _world.Caravans.Count == 0 ? "Aucune caravane en route." : $"{_world.Caravans.Count} caravane(s) en route.";
        int wars = _world.Pacts.Count(p => p.Kind == PactKind.War), alliances = _world.Pacts.Count(p => p.Kind == PactKind.Alliance);
        if (wars > 0) status += $" · {wars} guerre{(wars > 1 ? "s" : "")}";
        if (_world.WarParties.Count > 0) status += $" · {_world.WarParties.Count} bande{(_world.WarParties.Count > 1 ? "s" : "")} de guerriers en marche";
        if (alliances > 0) status += $" · {alliances} alliance{(alliances > 1 ? "s" : "")}";
        return status;
    }

    // ---------- Souris ----------

    public override void _GuiInput(InputEvent @event)
    {
        if (_world is null)
            return;
        switch (@event)
        {
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown } wheel:
                Zoom(wheel.ButtonIndex == MouseButton.WheelUp ? 1.2f : 1f / 1.2f, wheel.Position);
                AcceptEvent();
                return;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right or MouseButton.Middle } drag:
                _dragging = drag.Pressed;
                AcceptEvent();
                return;
            case InputEventMouseMotion motion when _dragging:
                _pan += motion.Relative;
                AcceptEvent();
                return;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } click:
                Click(click.Position);
                AcceptEvent();
                return;
        }
    }

    private void Zoom(float factor, Vector2 around)
    {
        float before = _zoom;
        _zoom = Math.Clamp(_zoom * factor, 1f, 6f);
        if (_zoom == 1f)
        {
            _pan = Vector2.Zero;
            return;
        }
        // On garde sous la souris le point qui y était.
        Vector2 center = Land.Position + Land.Size / 2 + _pan;
        _pan += (around - center) * (1 - _zoom / before);
    }

    private void Click(Vector2 position)
    {
        int tile = TileAt(position);
        if (PickingSite)
        {
            if (tile < 0)
                return;
            GetViewport().GuiGetFocusOwner()?.ReleaseFocus();
            if (_world.WorldMap.CanSettle(tile, out _placementMessage))
                SiteClicked?.Invoke(tile);
            return;
        }
        for (int i = 0; i < _world.Colonies.Count; i++)
            if (CenterOf(_world.WorldMap.TileOf(_world.Colonies[i])).DistanceTo(position) <= Math.Max(DotRadius + 6, HexWidth * 0.6f))
            {
                ColonyClicked?.Invoke(i);
                return;
            }
    }
}
