using System;
using System.Collections.Generic;
using Godot;
using GodColony.Simulation.Time;

namespace GodColony.View;

/// <summary>
/// Une petite courbe de la vue chiffrée : une ligne de 2 px sur un voile léger, posée sur une ligne de base à zéro,
/// avec un point à l'extrémité (la valeur du jour). Au survol, un trait suit le pointeur et donne la date et la valeur
/// du relevé le plus proche.
/// </summary>
public partial class Sparkline : Control
{
    /// <summary>Marge autour du tracé : le point final (rayon 4, anneau de 2) ne doit pas être rogné.</summary>
    private const float Inset = 6;

    private static readonly Color Baseline = DashboardStyle.PlotGrid;

    public Color LineColor { get; set; } = DashboardStyle.Mint;

    /// <summary>Le fond de la carte : l'anneau autour du point final prend sa couleur pour le détacher de la ligne.</summary>
    public Color Surface { get; set; } = DashboardStyle.Surface;

    /// <summary>La valeur en mots, pour la lecture au survol (« 54 habitants »).</summary>
    public Func<float, string> Format { get; set; } = value => value.ToString("0");

    private IReadOnlyList<ColonyHistory.Sample> _samples = [];
    private ColonyMetric _metric;
    private float _top = 1;
    private int _hover = -1;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Pass;
        MouseExited += () => { _hover = -1; QueueRedraw(); };
    }

    /// <param name="ceiling">Valeur minimale du haut de l'échelle (1 pour l'humeur, par exemple), pour qu'une courbe plate ne paraisse pas pleine.</param>
    public void SetData(IReadOnlyList<ColonyHistory.Sample> samples, ColonyMetric metric, float ceiling)
    {
        _samples = samples;
        _metric = metric;
        float top = ceiling;
        foreach (ColonyHistory.Sample sample in samples)
            top = Math.Max(top, sample.Value(metric));
        _top = top <= 0 ? 1 : top;
        if (_hover >= samples.Count) _hover = -1;
        QueueRedraw();
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is not InputEventMouseMotion motion || _samples.Count == 0)
            return;
        int nearest = Nearest(motion.Position.X);
        if (nearest == _hover)
            return;
        _hover = nearest;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_samples.Count == 0)
            return;
        float left = Inset, right = Size.X - Inset, bottom = Size.Y - Inset;
        DrawLine(new Vector2(left, bottom), new Vector2(right, bottom), Baseline, 1);

        var line = new Vector2[_samples.Count];
        for (int i = 0; i < line.Length; i++)
            line[i] = PointOf(i);
        if (line.Length > 1)
        {
            var area = new Vector2[line.Length + 2];
            line.CopyTo(area, 0);
            area[^2] = new Vector2(line[^1].X, bottom);
            area[^1] = new Vector2(line[0].X, bottom);
            DrawColoredPolygon(area, LineColor with { A = 0.1f });
            DrawPolyline(line, LineColor, 2, antialiased: true);
        }
        Marker(line[^1]);

        if (_hover < 0)
            return;
        Vector2 point = line[_hover];
        DrawLine(new Vector2(point.X, Inset - 4), new Vector2(point.X, bottom), DashboardStyle.Muted with { A = 0.6f }, 1);
        Marker(point);
        ColonyHistory.Sample sample = _samples[_hover];
        string text = $"{Format(sample.Value(_metric))} · {DateOf(sample.Day)}";
        Font font = GetThemeFont("font", "Label");
        const int fontSize = 12;
        Vector2 size = font.GetStringSize(text, HorizontalAlignment.Left, -1, fontSize) + new Vector2(10, 4);
        // La lecture se place à côté du trait, du côté où il reste de la place, et loin du point lu pour ne pas le cacher.
        float x = point.X + 8 + size.X <= Size.X ? point.X + 8 : Math.Max(0, point.X - 8 - size.X);
        float y = point.Y < Size.Y / 2 ? Size.Y - size.Y : 0;
        var box = new Rect2(x, y, size.X, size.Y);
        DrawRect(box, DashboardStyle.Readout);
        DrawRect(box, Baseline, false, 1);
        DrawString(font, new Vector2(box.Position.X + 5, box.Position.Y + 2 + font.GetAscent(fontSize)), text,
            HorizontalAlignment.Left, -1, fontSize, DashboardStyle.Ink);
    }

    private void Marker(Vector2 point)
    {
        DrawCircle(point, 6, Surface);
        DrawCircle(point, 4, LineColor);
    }

    private Vector2 PointOf(int index)
    {
        float left = Inset, width = Size.X - 2 * Inset, bottom = Size.Y - Inset, height = Size.Y - 2 * Inset;
        long first = _samples[0].Day, span = Math.Max(1, _samples[^1].Day - first);
        float x = _samples.Count == 1 ? left + width : left + width * (_samples[index].Day - first) / span;
        float y = bottom - height * Math.Clamp(_samples[index].Value(_metric) / _top, 0, 1);
        return new Vector2(x, y);
    }

    private int Nearest(float x)
    {
        int best = 0;
        float distance = float.MaxValue;
        for (int i = 0; i < _samples.Count; i++)
        {
            float d = Math.Abs(PointOf(i).X - x);
            if (d < distance) { distance = d; best = i; }
        }
        return best;
    }

    public static string DateOf(long day)
    {
        var clock = new GameClock(day * TimeConstants.TicksPerDay);
        string season = clock.Season switch
        {
            Season.Printemps => "printemps", Season.Ete => "été", Season.Automne => "automne", _ => "hiver",
        };
        return $"an {clock.Year}, {season}";
    }
}
