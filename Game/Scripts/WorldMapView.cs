using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GodColony.Simulation;
using GodColony.Simulation.Colonies;
using GodColony.View;

namespace GodColony;

/// <summary>
/// La carte du monde : marqueurs par peuple, fleuve et caravanes animées avec secours procédural.
/// Un clic sur une colonie la fait observer.
/// </summary>
public partial class WorldMapView : Control
{
    private static readonly Color Ink = Color.Color8(230, 237, 221);
    private static readonly Color Muted = Color.Color8(150, 174, 162);
    private static readonly Color Panel = new(0.065f, 0.115f, 0.105f, 0.97f);
    private static readonly Color Edge = Color.Color8(65, 89, 75);
    private static readonly Color River = Color.Color8(91, 151, 157);
    private const float DotRadius = 13f;
    private const float Margin = 80f;

    public event Action<int>? ColonyClicked;
    public event Action<float, float>? SiteClicked;
    public bool PickingSite { get; set; }
    private string _placementMessage = "";

    private WorldState _world = null!;

    /// <summary>Colonie actuellement observée (cerclée d'or).</summary>
    public int Observed { get; set; }

    public void Init(WorldState world) => _world = world;

    public static Color ColorOf(Species species) =>
        species == Species.Dwarf ? Color.Color8(214, 142, 84)
        : species == Species.Elf ? Color.Color8(133, 198, 167)
        : species == Species.Orc ? Color.Color8(205, 98, 86)
        : Color.Color8(226, 190, 119);

    public override void _Process(double delta)
    {
        if (Visible)
            QueueRedraw();
    }

    /// <summary>Position à l'écran (dans ce contrôle) de chaque colonie, la carte du monde étant mise à l'échelle pour tenir.</summary>
    private List<Vector2> Layout()
    {
        return _world.Colonies.Select(c => ToScreen(_world.WorldMap.PositionOf(c))).ToList();
    }

    private Rect2 Land => new(Margin, 76, Math.Max(1, Size.X - 2 * Margin), Math.Max(1, Size.Y - 120));
    private Vector2 ToScreen((float X, float Y) point) => Land.Position +
        new Vector2((point.X + WorldMap.Extent) / (2 * WorldMap.Extent) * Land.Size.X,
            (point.Y + WorldMap.Extent) / (2 * WorldMap.Extent) * Land.Size.Y);
    private (float X, float Y) ToWorld(Vector2 point)
    {
        Vector2 uv = (point - Land.Position) / Land.Size;
        return ((uv.X * 2 - 1) * WorldMap.Extent, (uv.Y * 2 - 1) * WorldMap.Extent);
    }

    public override void _Draw()
    {
        if (_world is null)
            return;
        var font = ArtDirection.BodyFont;
        DrawRect(new Rect2(Vector2.Zero, Size), Panel);
        if (AssetLibrary.Get("world/map_background.png") is { } background)
            DrawTextureRect(background, new Rect2(Vector2.Zero, Size), false);
        TextureFilter = TextureFilterEnum.Nearest;
        DrawRect(new Rect2(Vector2.Zero, Size), Edge, false, 2);
        DrawString(ArtDirection.HeadingFont, new Vector2(20, 30), "Carte du monde", HorizontalAlignment.Left, -1, 18, ArtDirection.Brass);
        DrawString(font, new Vector2(20, 50), PickingSite
            ? "Cliquez dans le cadre pour choisir une région libre."
            : "Cliquez sur une colonie pour l'observer · fondez de nouveaux peuples", HorizontalAlignment.Left, -1, 12, Muted);

        DrawRect(Land, new Color(0, 0, 0, 0.12f));
        DrawRect(Land, Edge, false, 1);
        if (PickingSite && Land.HasPoint(GetLocalMousePosition()))
        {
            Vector2 mouse = GetLocalMousePosition();
            var point = ToWorld(mouse);
            bool valid = _world.WorldMap.CanPlace(point.X, point.Y, out _);
            Color color = valid ? ArtDirection.Sage : MenuStyle.Error;
            DrawCircle(mouse, 13, new Color(color, 0.35f));
            DrawArc(mouse, 18, 0, Mathf.Tau, 32, color, 2);
        }

        List<Vector2> points = Layout();

        // Le fleuve : il traverse les colonies dans l'ordre, chacune étant en amont de la suivante.
        for (int i = 0; i + 1 < points.Count; i++)
        {
            if (AssetLibrary.Get("world/river_segment.png") is { } segment)
            {
                Vector2 delta = points[i + 1] - points[i];
                DrawSetTransform(points[i], delta.Angle());
                for (float along = 0; along < delta.Length(); along += 16)
                {
                    float width = Math.Min(16, delta.Length() - along);
                    DrawTextureRectRegion(segment, new Rect2(along, -4, width, 8), new Rect2(0, 0, width, 8));
                }
                DrawSetTransform(Vector2.Zero);
            }
            else DrawDashedLine(points[i], points[i + 1], River, 3, 9);
        }
        // Les distances (jours de marche) entre colonies voisines.
        for (int i = 0; i < points.Count; i++)
        for (int j = i + 1; j < points.Count; j++)
        {
            DrawLine(points[i], points[j], new Color(1, 1, 1, 0.08f), 1);
            float days = _world.WorldMap.TravelDays(_world.Colonies[i], _world.Colonies[j]);
            if (j == i + 1 || points.Count == 2)
            {
                // Au milieu de la route, décalée sur le côté pour ne pas masquer le fleuve.
                Vector2 along = (points[j] - points[i]).Normalized();
                Vector2 label = (points[i] + points[j]) / 2 + new Vector2(-along.Y, along.X) * 24f;
                DrawString(font, label - new Vector2(16, 0), $"{days:0.0} j", HorizontalAlignment.Center, 32, 11, Muted);
            }
        }

        // Les colonies.
        for (int i = 0; i < points.Count; i++)
        {
            Colony colony = _world.Colonies[i];
            Color color = ColorOf(colony.Species);
            DrawCircle(points[i], DotRadius, color.Darkened(0.35f));
            DrawCircle(points[i], DotRadius - 3, color);
            if (AssetLibrary.Get($"world/colony_{PeoplesSprites.LookOf(colony.Species).ToString().ToLowerInvariant()}.png") is { } marker)
                DrawTexture(marker, points[i] - new Vector2(16, 16));
            if (i == Observed)
                DrawArc(points[i], DotRadius + 5, 0, Mathf.Tau, 32, ArtDirection.Brass, 2);
            DrawString(font, points[i] + new Vector2(-100, DotRadius + 18), colony.Name, HorizontalAlignment.Center, 200, 13, Ink);
            DrawString(font, points[i] + new Vector2(-100, DotRadius + 33),
                $"{colony.Species.Plural} · {colony.Members.Count} habitants · {colony.Stock.Get(ResourceType.Coins)} pièces",
                HorizontalAlignment.Center, 200, 10, Muted);
        }

        // Les caravanes suivent la position simulée ; seul leur dessin change.
        long now = _world.Clock.Ticks;
        foreach (Caravan caravan in _world.Caravans)
        {
            Vector2 from = points[_world.Colonies.IndexOf(caravan.From)], to = points[_world.Colonies.IndexOf(caravan.To)];
            Vector2 direction = (to - from).Normalized();
            Vector2 side = new Vector2(-direction.Y, direction.X) * 7f;
            Vector2 position = from.Lerp(to, caravan.RoutePosition(now)) + side;
            bool left = (to.X - from.X) * (caravan.State == CaravanState.Outbound ? 1 : -1) < 0;
            CaravanSprites.Draw(this, position, now / (double)GodColony.Simulation.Time.TimeConstants.TicksPerSecond, left);
            string state = caravan.State == CaravanState.Outbound ? "→" : "←";
            DrawString(font, position + new Vector2(18, -6), $"{state} {caravan.Traders.Count} colons", HorizontalAlignment.Left, -1, 10, Ink);
        }
        DrawString(font, new Vector2(20, Size.Y - 16), PickingSite ? _placementMessage
            : _world.Colonies.Count == 0 ? "Monde vierge · fondez votre première colonie."
            : _world.Caravans.Count == 0 ? "Aucune caravane en route." : "", HorizontalAlignment.Left, Size.X - 40, 12, Muted);
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } click || _world is null)
            return;
        if (PickingSite)
        {
            if (Land.HasPoint(click.Position))
            {
                GetViewport().GuiGetFocusOwner()?.ReleaseFocus();
                var point = ToWorld(click.Position);
                if (_world.WorldMap.CanPlace(point.X, point.Y, out _placementMessage)) SiteClicked?.Invoke(point.X, point.Y);
            }
            AcceptEvent();
            return;
        }
        List<Vector2> points = Layout();
        for (int i = 0; i < points.Count; i++)
            if (points[i].DistanceTo(click.Position) <= DotRadius + 8)
            {
                ColonyClicked?.Invoke(i);
                AcceptEvent();
                return;
            }
    }
}
