using System.Collections.Generic;
using Godot;
using GodColony.Simulation;
using GodColony.Simulation.Colonies;
using GodColony.Simulation.Time;

namespace GodColony.View;

/// <summary>Annonce les jalons nouveaux ; la reprise d'une partie ne rejoue pas les anciens.</summary>
public partial class VillageNotices : CanvasLayer
{
    private WorldState _world = null!;
    private Colony _colony = null!;
    private Settlement _settlement = null!;
    private readonly HashSet<string> _seen = [];
    private readonly Queue<(string Title, long Ticks)> _pending = [];
    private Label _text = null!;
    private PanelContainer _toast = null!;
    private ColorRect _cold = null!;
    private double _remaining;

    public void Init(WorldState world, Colony colony)
    {
        _world = world; _colony = colony; _settlement = colony.CurrentSettlement;
        foreach (string id in colony.Achievements.Keys) _seen.Add(id);
        Layer = 2;
        var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect); AddChild(root);
        _cold = new ColorRect { Color = new Color(0.32f, 0.58f, 0.87f, 0.055f), MouseFilter = Control.MouseFilterEnum.Ignore };
        _cold.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect); root.AddChild(_cold);
        _toast = new PanelContainer { Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        _toast.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomRight);
        _toast.OffsetLeft = -490; _toast.OffsetRight = -20; _toast.OffsetTop = -140; _toast.OffsetBottom = -60;
        _toast.AddThemeStyleboxOverride("panel", MenuStyle.Box(new Color(0.08f, 0.15f, 0.12f, 0.95f), ArtDirection.Brass, 12));
        root.AddChild(_toast);
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore }; row.AddThemeConstantOverride("separation", 10); _toast.AddChild(row);
        row.AddChild(new TextureRect { Texture = ResourceIcons.Get("Milestone"), CustomMinimumSize = new Vector2(24, 24),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = Control.MouseFilterEnum.Ignore });
        _text = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore };
        _text.AddThemeFontSizeOverride("font_size", 14); _text.AddThemeColorOverride("font_color", ArtDirection.Cream); row.AddChild(_text);
    }

    public override void _Process(double delta)
    {
        if (_world is null) return;
        using var scope = _settlement.Observe();
        _cold.Visible = _colony.ColdSnapDaysLeft > 0 && Climate.ColdSeverity(_colony.Map.Biome) > 0;
        foreach (Milestone milestone in Milestones.All)
            if (_colony.Achievements.TryGetValue(milestone.Id, out long ticks) && _seen.Add(milestone.Id)) _pending.Enqueue((milestone.Title, ticks));
        _remaining -= delta;
        if (_remaining <= 0 && _pending.TryDequeue(out var next))
        {
            var when = new GameClock(next.Ticks);
            _text.Text = $"Jalon atteint : {next.Title}\n{when.Season} · jour {when.DayOfSeason} · année {when.Year}";
            _remaining = 5;
        }
        _toast.Visible = _remaining > 0;
    }
}
