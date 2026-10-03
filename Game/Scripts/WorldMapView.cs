using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GodColony.Simulation;
using GodColony.Simulation.Colonies;
using GodColony.View;

namespace GodColony;

/// <summary>
/// La carte du monde, très rudimentaire : les colonies sont des pastilles, le fleuve qui les relie est tracé en bleu,
/// et les caravanes marchent d'une colonie à l'autre sous forme de petits carrés. À refaire avec les illustrations.
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
        var raw = _world.Colonies.Select(c => _world.WorldMap.PositionOf(c)).ToList();
        float minX = raw.Min(p => p.X), maxX = raw.Max(p => p.X), minY = raw.Min(p => p.Y), maxY = raw.Max(p => p.Y);
        float spanX = Math.Max(1f, maxX - minX), spanY = Math.Max(1f, maxY - minY);
        float scale = Math.Min((Size.X - 2 * Margin) / spanX, (Size.Y - 2 * Margin - 30) / spanY);
        Vector2 origin = new(Size.X / 2f - (minX + maxX) / 2f * scale, Size.Y / 2f + 14 - (minY + maxY) / 2f * scale);
        return raw.Select(p => origin + new Vector2(p.X, p.Y) * scale).ToList();
    }

    public override void _Draw()
    {
        if (_world is null)
            return;
        var font = ArtDirection.BodyFont;
        DrawRect(new Rect2(Vector2.Zero, Size), Panel);
        DrawRect(new Rect2(Vector2.Zero, Size), Edge, false, 2);
        DrawString(ArtDirection.HeadingFont, new Vector2(20, 30), "Carte du monde", HorizontalAlignment.Left, -1, 18, ArtDirection.Brass);
        DrawString(font, new Vector2(20, 50), "Les carrés sont des caravanes en marche · cliquez sur une colonie pour l'observer", HorizontalAlignment.Left, -1, 12, Muted);

        List<Vector2> points = Layout();

        // Le fleuve : il traverse les colonies dans l'ordre, chacune étant en amont de la suivante.
        for (int i = 0; i + 1 < points.Count; i++)
            DrawDashedLine(points[i], points[i + 1], River, 3, 9);
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
            if (i == Observed)
                DrawArc(points[i], DotRadius + 5, 0, Mathf.Tau, 32, ArtDirection.Brass, 2);
            DrawString(font, points[i] + new Vector2(-100, DotRadius + 18), colony.Name, HorizontalAlignment.Center, 200, 13, Ink);
            DrawString(font, points[i] + new Vector2(-100, DotRadius + 33),
                $"{colony.Species.Plural} · {colony.Members.Count} habitants · {colony.Stock.Get(ResourceType.Coins)} pièces",
                HorizontalAlignment.Center, 200, 10, Muted);
        }

        // Les caravanes : un carré à la couleur de la colonie qui l'envoie, qui avance sur la route.
        long now = _world.Clock.Ticks;
        foreach (Caravan caravan in _world.Caravans)
        {
            Vector2 from = points[_world.Colonies.IndexOf(caravan.From)], to = points[_world.Colonies.IndexOf(caravan.To)];
            Vector2 direction = (to - from).Normalized();
            Vector2 side = new Vector2(-direction.Y, direction.X) * 7f;
            Vector2 position = from.Lerp(to, caravan.RoutePosition(now)) + side;
            Color color = ColorOf(caravan.From.Species);
            DrawRect(new Rect2(position - new Vector2(5, 5), new Vector2(10, 10)), Ink);
            DrawRect(new Rect2(position - new Vector2(4, 4), new Vector2(8, 8)), color);
            string state = caravan.State == CaravanState.Outbound ? "→" : "←";
            DrawString(font, position + new Vector2(8, 4), $"{state} {caravan.Traders.Count} colons", HorizontalAlignment.Left, -1, 10, Ink);
        }
        if (_world.Caravans.Count == 0)
            DrawString(font, new Vector2(20, Size.Y - 16), "Aucune caravane en route.", HorizontalAlignment.Left, -1, 12, Muted);
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } click || _world is null)
            return;
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
