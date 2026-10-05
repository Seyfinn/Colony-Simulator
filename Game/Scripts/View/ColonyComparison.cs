using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GodColony.Simulation.Time;

namespace GodColony.View;

/// <summary>
/// La comparaison des colonies de la vue chiffrée : une courbe par colonie, sur une même échelle, pour voir laquelle grandit,
/// s'enrichit ou s'épuise. Les couleurs suivent l'ordre des colonies (jamais leur rang) ; les noms sont posés au bout des courbes
/// quand ils ne se chevauchent pas, la légende fait le reste. Au survol, un trait donne la valeur de chaque colonie à cette date.
/// </summary>
public partial class ColonyComparison : Control
{
    /// <summary>
    /// Les teintes des huit premières colonies, dans l'ordre. Ordre et teintes vérifiés pour les daltonismes et le contraste
    /// sur le fond des cartes (#172922) ; au-delà de huit, les colonies n'ont pas de couleur et restent hors de ce graphique.
    /// </summary>
    public static readonly Color[] Palette =
    [
        Color.FromHtml("#3987e5"), Color.FromHtml("#d95926"), Color.FromHtml("#199e70"), Color.FromHtml("#c98500"),
        Color.FromHtml("#d55181"), Color.FromHtml("#008300"), Color.FromHtml("#9085e9"), Color.FromHtml("#e66767"),
    ];

    public readonly record struct Series(string Name, Color Color, IReadOnlyList<ColonyHistory.Sample> Samples);

    private const float PlotLeft = 54, PlotRight = 128, PlotTop = 10, PlotBottom = 24;
    private const int FontSize = 11;
    private static readonly Color Grid = DashboardStyle.PlotGrid;

    public Color Surface { get; set; } = DashboardStyle.Surface;

    private IReadOnlyList<Series> _series = [];
    private ColonyMetric _metric;
    private Func<float, string> _format = value => value.ToString("0");
    private float _top = 1, _step = 1;
    private long _first, _last;
    private long? _hoverDay;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Pass;
        MouseExited += () => { _hoverDay = null; QueueRedraw(); };
        Resized += QueueRedraw;
    }

    /// <param name="format">La valeur telle que l'affichent les cartes des colonies (« 54 », « 4,1 j »).</param>
    /// <param name="ceiling">Haut d'échelle minimal, pour qu'une courbe basse ne paraisse pas pleine.</param>
    public void SetData(IReadOnlyList<Series> series, ColonyMetric metric, Func<float, string> format, float ceiling)
    {
        _series = series;
        _metric = metric;
        _format = format;
        var filled = series.Where(s => s.Samples.Count > 0).ToList();
        _first = filled.Count == 0 ? 0 : filled.Min(s => s.Samples[0].Day);
        _last = filled.Count == 0 ? 0 : filled.Max(s => s.Samples[^1].Day);
        float top = ceiling;
        foreach (Series line in filled)
        foreach (ColonyHistory.Sample sample in line.Samples)
            top = Math.Max(top, sample.Value(metric));
        _step = NiceStep(top / 3);
        _top = MathF.Ceiling(top / _step - 0.001f) * _step;
        QueueRedraw();
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is not InputEventMouseMotion motion || _series.Count == 0 || _last <= _first)
            return;
        Rect2 plot = Plot();
        float t = Math.Clamp((motion.Position.X - plot.Position.X) / plot.Size.X, 0, 1);
        long day = _first + (long)MathF.Round(t * (_last - _first));
        // On se cale sur le relevé le plus proche de la plus longue série (les relevés de toutes les colonies tombent les mêmes jours).
        Series longest = _series.MaxBy(s => s.Samples.Count);
        if (longest.Samples is { Count: > 0 } samples)
            day = samples.MinBy(s => Math.Abs(s.Day - day)).Day;
        if (day == _hoverDay)
            return;
        _hoverDay = day;
        QueueRedraw();
    }

    public override void _Draw()
    {
        Font font = GetThemeFont("font", "Label");
        Rect2 plot = Plot();
        if (plot.Size.X < 40 || plot.Size.Y < 30)
            return;
        float bottom = plot.End.Y;
        DrawRect(plot, DashboardStyle.PlotSurface);

        // L'échelle : des filets fins et discrets, les valeurs à gauche.
        for (float value = 0; value <= _top + _step * 0.01f; value += _step)
        {
            float y = Y(plot, value);
            DrawLine(new Vector2(plot.Position.X, y), new Vector2(plot.End.X, y), value == 0 ? Grid with { A = 1 } : Grid, 1);
            string label = Tick(value);
            float width = font.GetStringSize(label, HorizontalAlignment.Left, -1, FontSize).X;
            DrawString(font, new Vector2(plot.Position.X - 8 - width, y + font.GetAscent(FontSize) / 2 - 1), label,
                HorizontalAlignment.Left, -1, FontSize, DashboardStyle.Muted);
        }
        DrawYears(font, plot);

        if (_series.All(s => s.Samples.Count == 0))
            return;
        foreach (Series line in _series)
        {
            if (line.Samples.Count > 1)
                DrawPolyline(line.Samples.Select(s => Point(plot, s)).ToArray(), line.Color, 2, antialiased: true);
        }
        var ends = new List<(Vector2 Point, Series Line)>();
        foreach (Series line in _series)
        {
            if (line.Samples.Count == 0) continue;
            Vector2 end = Point(plot, line.Samples[^1]);
            Marker(end, line.Color);
            ends.Add((end, line));
        }

        // Le nom au bout de chaque courbe, s'il ne chevauche pas un autre : sinon, la légende et le survol suffisent.
        float lastLabel = float.NegativeInfinity;
        foreach (var (point, line) in ends.OrderBy(e => e.Point.Y))
        {
            if (point.Y - lastLabel < FontSize + 3) continue;
            lastLabel = point.Y;
            string name = Clip(font, line.Name, PlotRight - 14);
            DrawString(font, new Vector2(point.X + 9, point.Y + font.GetAscent(FontSize) / 2 - 1), name,
                HorizontalAlignment.Left, -1, FontSize, DashboardStyle.Ink);
        }

        if (_hoverDay is { } day)
            DrawReadout(font, plot, day, bottom);
    }

    private void DrawYears(Font font, Rect2 plot)
    {
        if (_last <= _first) return;
        int firstYear = (int)(_first / TimeConstants.DaysPerYear) + 1, lastYear = (int)(_last / TimeConstants.DaysPerYear) + 1;
        int step = (int)Math.Max(1, NiceStep(Math.Max(1, (lastYear - firstYear) / 6f)));
        for (int year = (firstYear + step - 1) / step * step; year <= lastYear; year += step)
        {
            long day = (long)(year - 1) * TimeConstants.DaysPerYear;
            if (day < _first) continue;
            float x = X(plot, day);
            DrawLine(new Vector2(x, plot.End.Y), new Vector2(x, plot.End.Y + 4), Grid with { A = 1 }, 1);
            string label = $"an {year}";
            float width = font.GetStringSize(label, HorizontalAlignment.Left, -1, FontSize).X;
            DrawString(font, new Vector2(x - width / 2, plot.End.Y + 6 + font.GetAscent(FontSize)), label,
                HorizontalAlignment.Left, -1, FontSize, DashboardStyle.Muted);
        }
    }

    /// <summary>Le trait de lecture et, dans un encadré, la valeur de chaque colonie ce jour-là (la valeur d'abord, le nom ensuite).</summary>
    private void DrawReadout(Font font, Rect2 plot, long day, float bottom)
    {
        float x = X(plot, day);
        DrawLine(new Vector2(x, plot.Position.Y), new Vector2(x, bottom), DashboardStyle.Muted with { A = 0.6f }, 1);
        var rows = new List<(float Value, Series Line)>();
        foreach (Series line in _series)
        {
            if (At(line.Samples, day) is not { } sample) continue;
            rows.Add((sample.Value(_metric), line));
            Marker(Point(plot, sample), line.Color);
        }
        if (rows.Count == 0) return;
        rows.Sort((a, b) => b.Value.CompareTo(a.Value));
        string title = Sparkline.DateOf(day);
        float rowHeight = FontSize + 6, width = font.GetStringSize(title, HorizontalAlignment.Left, -1, FontSize).X;
        foreach (var (value, series) in rows)
            width = Math.Max(width, 22 + font.GetStringSize($"{_format(value)}   {series.Name}", HorizontalAlignment.Left, -1, FontSize).X);
        var box = new Rect2(0, plot.Position.Y, width + 16, rowHeight * (rows.Count + 1) + 8);
        box.Position = new Vector2(x + 12 + box.Size.X <= Size.X ? x + 12 : Math.Max(0, x - 12 - box.Size.X), box.Position.Y);
        DrawRect(box, DashboardStyle.Readout);
        DrawRect(box, Grid with { A = 1 }, false, 1);
        float ascent = font.GetAscent(FontSize);
        DrawString(font, box.Position + new Vector2(8, 4 + ascent), title, HorizontalAlignment.Left, -1, FontSize, DashboardStyle.Muted);
        for (int i = 0; i < rows.Count; i++)
        {
            var (value, series) = rows[i];
            float y = box.Position.Y + 4 + rowHeight * (i + 1);
            DrawLine(new Vector2(box.Position.X + 8, y + ascent / 2 + 1), new Vector2(box.Position.X + 22, y + ascent / 2 + 1), series.Color, 2);
            string number = _format(value);
            DrawString(font, new Vector2(box.Position.X + 28, y + ascent), number, HorizontalAlignment.Left, -1, FontSize, DashboardStyle.Ink);
            float numberWidth = font.GetStringSize(number + "   ", HorizontalAlignment.Left, -1, FontSize).X;
            DrawString(font, new Vector2(box.Position.X + 28 + numberWidth, y + ascent), series.Name, HorizontalAlignment.Left, -1, FontSize, DashboardStyle.Muted);
        }
    }

    private void Marker(Vector2 point, Color color)
    {
        DrawCircle(point, 6, Surface);
        DrawCircle(point, 4, color);
    }

    private Rect2 Plot() => new(PlotLeft, PlotTop, Size.X - PlotLeft - PlotRight, Size.Y - PlotTop - PlotBottom);

    private float X(Rect2 plot, long day) =>
        _last <= _first ? plot.End.X : plot.Position.X + plot.Size.X * (day - _first) / (_last - _first);

    private float Y(Rect2 plot, float value) => plot.End.Y - plot.Size.Y * Math.Clamp(value / _top, 0, 1);

    private Vector2 Point(Rect2 plot, ColonyHistory.Sample sample) => new(X(plot, sample.Day), Y(plot, sample.Value(_metric)));

    /// <summary>Le relevé de ce jour-là, ou le dernier avant lui ; rien si la colonie n'existait pas encore.</summary>
    private static ColonyHistory.Sample? At(IReadOnlyList<ColonyHistory.Sample> samples, long day)
    {
        if (samples.Count == 0 || samples[0].Day > day) return null;
        int low = 0, high = samples.Count - 1;
        while (low < high)
        {
            int middle = (low + high + 1) / 2;
            if (samples[middle].Day <= day) low = middle;
            else high = middle - 1;
        }
        return samples[low];
    }

    private string Tick(float value) => _metric switch
    {
        ColonyMetric.FoodDays => $"{value:0.#} j",
        ColonyMetric.Mood => $"{value * 100:0} %",
        _ => value.ToString("N0"),
    };

    private static string Clip(Font font, string text, float width)
    {
        if (font.GetStringSize(text, HorizontalAlignment.Left, -1, FontSize).X <= width) return text;
        while (text.Length > 1 && font.GetStringSize(text + "…", HorizontalAlignment.Left, -1, FontSize).X > width)
            text = text[..^1];
        return text + "…";
    }

    /// <summary>Un pas d'échelle rond (1, 2, 2,5 ou 5 fois une puissance de dix).</summary>
    private static float NiceStep(float raw)
    {
        if (raw <= 0) return 1;
        float magnitude = MathF.Pow(10, MathF.Floor(MathF.Log10(raw)));
        float residual = raw / magnitude;
        return magnitude * (residual <= 1 ? 1 : residual <= 2 ? 2 : residual <= 2.5f ? 2.5f : residual <= 5 ? 5 : 10);
    }
}
