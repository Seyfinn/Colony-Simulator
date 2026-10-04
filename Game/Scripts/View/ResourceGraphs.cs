using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GodColony.Simulation.Colonies;

namespace GodColony.View;

/// <summary>Choix d'une ressource et comparaison de ses quatre flux, en unités par jour.</summary>
public partial class ResourceGraphs : VBoxContainer
{
    private OptionButton _resource = null!, _period = null!;
    private Label _stock = null!, _note = null!;
    private TextureRect _goodIcon = null!;
    private PanelContainer _stockCard = null!;
    private ResourceChart _chart = null!;
    private readonly Dictionary<ResourceFlow, Label> _averages = [];
    private Colony? _colony;
    private ResourceHistory? _history;
    private int _version = -1;
    public ResourceType Good { get; private set; } = ResourceType.Food;

    public override void _Ready()
    {
        Name = "GraphiquesRessources";
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        AddThemeConstantOverride("separation", 10);
        var choice = DashboardStyle.Row(this, 8);
        _resource = new OptionButton { Name = "RessourceGraphique", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (ResourceType good in Enum.GetValues<ResourceType>().Where(g => g != ResourceType.Coins))
            _resource.AddIconItem(ResourceIcons.Get(good), ResourceIcons.Name(good), (int)good);
        choice.AddChild(_resource);
        _resource.ItemSelected += index => { Good = (ResourceType)_resource.GetItemId((int)index); UpdateGraph(); };
        _period = new OptionButton { Name = "PeriodeGraphique" };
        foreach (int days in new[] { 20, 60, ResourceHistory.MaxSamples }) _period.AddItem($"{days} jours", days);
        _period.Select(1);
        _period.ItemSelected += _ => UpdateGraph();
        choice.AddChild(_period);
        _stockCard = DashboardStyle.Card(this, 8);
        var stockRow = DashboardStyle.Row(_stockCard);
        _goodIcon = DashboardStyle.Icon(stockRow, Good, 24);
        _stock = DashboardStyle.Text(stockRow, "", 13, DashboardStyle.ResourceTint(Good));
        _stock.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        var legend = new HFlowContainer();
        legend.AddThemeConstantOverride("h_separation", 8); legend.AddThemeConstantOverride("v_separation", 6);
        _chart = new ResourceChart { Name = "CourbeRessources", CustomMinimumSize = new Vector2(0, 220), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (var (flow, title, color) in ResourceChart.Flows)
        {
            var toggle = new CheckButton { Text = title, ButtonPressed = true, Name = $"Flux{flow}" };
            toggle.AddThemeColorOverride("font_color", color);
            toggle.AddThemeColorOverride("font_pressed_color", color);
            toggle.AddThemeColorOverride("font_hover_pressed_color", color);
            toggle.AddThemeFontSizeOverride("font_size", 12);
            toggle.Toggled += visible => { _chart.ShowFlow(flow, visible); };
            legend.AddChild(toggle);
        }
        var chartCard = DashboardStyle.Card(this, 8);
        chartCard.AddChild(_chart);
        AddChild(legend);
        var averages = new GridContainer { Columns = 2, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        averages.AddThemeConstantOverride("h_separation", 18); averages.AddThemeConstantOverride("v_separation", 6);
        AddChild(averages);
        foreach (var (flow, _, color) in ResourceChart.Flows)
        {
            var row = DashboardStyle.Row(DashboardStyle.Card(averages, 6), 6);
            row.AddChild(new ColorRect { Color = color, CustomMinimumSize = new Vector2(3, 18), MouseFilter = MouseFilterEnum.Ignore });
            _averages[flow] = DashboardStyle.Text(row, "", 12, color);
        }
        _note = DashboardStyle.Text(this, "", 11, DashboardStyle.Muted, true);
        DashboardStyle.Text(this, "Utilisation : repas, chauffage, élevage, matériaux affectés aux chantiers et recettes. Les pertes et transferts sont exclus. Achats et ventes : échanges conclus, colporteurs compris.\nSurvolez le graphe pour lire les valeurs ; cliquez sur la légende pour masquer une courbe. L'historique recommence au chargement.", 11, DashboardStyle.Muted, true);
        GetViewport().SizeChanged += ResizeGraph;
        ResizeGraph();
        UpdateGraph();
    }

    private void ResizeGraph()
    {
        bool compact = GetViewportRect().Size.Y < 760;
        _chart.CustomMinimumSize = new Vector2(0, compact ? 145 : 220);
        _stockCard.Visible = !compact;
    }

    public void Refresh(Colony colony, ResourceHistory? history)
    {
        bool changed = colony != _colony || history != _history || (history?.Version ?? -1) != _version;
        _colony = colony; _history = history; _version = history?.Version ?? -1;
        _stock.Text = $"Stock actuel : {colony.Stock.Get(Good):N0} · {ResourceIcons.Name(Good)}";
        if (changed) UpdateGraph();
    }

    private void UpdateGraph()
    {
        Color accent = DashboardStyle.ResourceTint(Good);
        _goodIcon.Texture = ResourceIcons.Get(Good);
        _stock.AddThemeColorOverride("font_color", accent);
        _chart.ResourceColor = accent;
        int days = _period.GetSelectedId();
        ResourceHistory.Sample[] samples = (_colony is null ? [] : _history?.Of(_colony, Good) ?? []).TakeLast(days).ToArray();
        _chart.SetData(samples);
        if (_colony is not null) _stock.Text = $"Stock actuel : {_colony.Stock.Get(Good):N0} · {ResourceIcons.Name(Good)}";
        foreach (var (flow, title, _) in ResourceChart.Flows)
            _averages[flow].Text = samples.Length == 0 ? $"{title} : —" : $"{title} : {samples.Average(s => s.Value(flow)):0.##} u/j";
        _note.Text = samples.Length == 0 ? "Le premier relevé apparaîtra au prochain jour de jeu."
            : $"Moyennes sur {samples.Length} relevés · unités par jour · {Sparkline.DateOf(samples[0].Day)} → {Sparkline.DateOf(samples[^1].Day)}";
    }
}
