using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GodColony.Simulation.Colonies;
using GodColony.Simulation.Time;

namespace GodColony.View;

/// <summary>Quatre courbes à la même échelle, avec lecture des quantités au survol.</summary>
public partial class ResourceChart : Control
{
    public static readonly (ResourceFlow Flow, string Title, Color Color)[] Flows =
    [
        (ResourceFlow.Production, "Production", DashboardStyle.Mint),
        (ResourceFlow.Usage, "Utilisation", Color.Color8(237, 147, 126)),
        (ResourceFlow.Purchase, "Achats", Color.Color8(111, 169, 173)),
        (ResourceFlow.Sale, "Ventes", DashboardStyle.Gold),
    ];
    private IReadOnlyList<ResourceHistory.Sample> _samples = [];
    private readonly HashSet<ResourceFlow> _visible = [ResourceFlow.Production, ResourceFlow.Usage, ResourceFlow.Purchase, ResourceFlow.Sale];
    private int _hover = -1;
    private float _top = 1;
    private static readonly Color Grid = DashboardStyle.PlotGrid;
    public Color ResourceColor { get; set; } = DashboardStyle.Gold;
    public int SampleCount => _samples.Count;
    private static string DateOf(long day) => $"{Sparkline.DateOf(day)} · jour {day % TimeConstants.DaysPerSeason + 1}";

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Pass;
        Resized += QueueRedraw;
        MouseExited += () => { _hover = -1; QueueRedraw(); };
    }

    public void SetData(IReadOnlyList<ResourceHistory.Sample> samples)
    {
        _samples = samples; _hover = -1; UpdateScale();
    }

    public void ShowFlow(ResourceFlow flow, bool visible)
    {
        if (visible) _visible.Add(flow); else _visible.Remove(flow);
        UpdateScale();
    }

    private void UpdateScale()
    {
        float max = 1;
        foreach (var sample in _samples)
        foreach (var flow in _visible) max = Math.Max(max, sample.Value(flow));
        float magnitude = MathF.Pow(10, MathF.Floor(MathF.Log10(max)));
        _top = MathF.Ceiling(max / magnitude) * magnitude;
        QueueRedraw();
    }

    private Rect2 Plot() => new(52, 26, Math.Max(1, Size.X - 70), Math.Max(1, Size.Y - 65));
    private float X(Rect2 plot, long day) => _samples.Count < 2 ? plot.End.X :
        plot.Position.X + plot.Size.X * (day - _samples[0].Day) / Math.Max(1, _samples[^1].Day - _samples[0].Day);
    private Vector2 Point(Rect2 plot, ResourceHistory.Sample sample, ResourceFlow flow) =>
        new(X(plot, sample.Day), plot.End.Y - plot.Size.Y * sample.Value(flow) / _top);

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is not InputEventMouseMotion motion || _samples.Count == 0) return;
        Rect2 plot = Plot();
        float t = Math.Clamp((motion.Position.X - plot.Position.X) / plot.Size.X, 0, 1);
        long day = _samples[0].Day + (long)Math.Round(t * (_samples[^1].Day - _samples[0].Day));
        _hover = Enumerable.Range(0, _samples.Count).MinBy(i => Math.Abs(_samples[i].Day - day));
        QueueRedraw();
    }

    public override void _Draw()
    {
        Font font = GetThemeFont("font", "Label");
        Rect2 plot = Plot();
        if (plot.Size.X < 40 || plot.Size.Y < 30) return;
        DrawRect(plot, DashboardStyle.PlotSurface);
        DrawLine(plot.Position, new Vector2(plot.End.X, plot.Position.Y), ResourceColor with { A = 0.55f }, 2);
        void Text(Vector2 position, string text, Color color) => DrawString(font, position, text, HorizontalAlignment.Left, -1, 11, color);
        Text(new Vector2(plot.Position.X, 14), "UNITÉS / JOUR", DashboardStyle.Muted);
        for (int i = 0; i <= 4; i++)
        {
            float value = _top * i / 4, y = plot.End.Y - plot.Size.Y * i / 4;
            DrawLine(new Vector2(plot.Position.X, y), new Vector2(plot.End.X, y), Grid, 1);
            string label = value.ToString("0.##");
            float width = font.GetStringSize(label, HorizontalAlignment.Left, -1, 11).X;
            Text(new Vector2(plot.Position.X - width - 7, y + 4), label, DashboardStyle.Muted);
        }
        if (_samples.Count == 0)
        {
            Text(plot.Position + new Vector2(18, plot.Size.Y / 2), "En attente du premier relevé…", DashboardStyle.Muted);
            return;
        }
        foreach (var (flow, _, color) in Flows.Where(f => _visible.Contains(f.Flow)))
        {
            Vector2[] points = _samples.Select(s => Point(plot, s, flow)).ToArray();
            if (points.Length > 1) DrawPolyline(points, color, 2, true);
            DrawCircle(points[^1], 3, color);
        }
        foreach (int index in new[] { 0, _samples.Count - 1 }.Distinct())
        {
            string date = DateOf(_samples[index].Day);
            float width = font.GetStringSize(date, HorizontalAlignment.Left, -1, 11).X;
            Text(new Vector2(index == 0 ? plot.Position.X : plot.End.X - width, plot.End.Y + 20), date, DashboardStyle.Muted);
        }
        if (_hover < 0 || _hover >= _samples.Count) return;
        var sample = _samples[_hover];
        float x = X(plot, sample.Day);
        DrawLine(new Vector2(x, plot.Position.Y), new Vector2(x, plot.End.Y), DashboardStyle.Muted, 1);
        var shown = Flows.Where(f => _visible.Contains(f.Flow)).ToArray();
        float boxWidth = Math.Min(215, plot.Size.X);
        var box = new Rect2(Math.Clamp(x + 12, plot.Position.X, plot.End.X - boxWidth), plot.Position.Y + 6, boxWidth, 28 + shown.Length * 20);
        DrawRect(box, DashboardStyle.Readout);
        DrawRect(box, ResourceColor with { A = 0.6f }, false);
        Text(box.Position + new Vector2(8, 17), DateOf(sample.Day), DashboardStyle.Ink);
        for (int i = 0; i < shown.Length; i++)
        {
            var (flow, title, color) = shown[i];
            Text(box.Position + new Vector2(8, 38 + i * 20), $"{title} : {sample.Value(flow):0.##} u/j", color);
            DrawCircle(Point(plot, sample, flow), 4, color);
        }
    }
}
