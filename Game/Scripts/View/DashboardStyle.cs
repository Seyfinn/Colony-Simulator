using Godot;
using GodColony.Simulation.Colonies;

namespace GodColony.View;

/// <summary>Composants visuels communs aux tableaux d'économie et de production.</summary>
internal static class DashboardStyle
{
    public static readonly Color Gold = ArtDirection.Brass, Mint = MenuStyle.Mint, Muted = MenuStyle.Muted,
        Ink = MenuStyle.Ink, Warning = MenuStyle.Error;
    public static readonly Color Surface = new(0.09f, 0.16f, 0.135f), PlotSurface = new(0.055f, 0.115f, 0.09f),
        PlotGrid = new(0.255f, 0.35f, 0.29f, 0.55f), Readout = new(0.045f, 0.095f, 0.075f, 0.98f);

    /// <summary>Teintes des matériaux et denrées, reprises de leurs pixels natifs.</summary>
    public static Color ResourceTint(ResourceType good) => good switch
    {
        ResourceType.Food or ResourceType.Meat or ResourceType.SaltedMeat => Color.Color8(237, 147, 126),
        ResourceType.Grain or ResourceType.Bread or ResourceType.Beer or ResourceType.Cake or ResourceType.Coins => Color.Color8(222, 175, 78),
        ResourceType.Wood or ResourceType.Hardwood or ResourceType.Chickens => Color.Color8(190, 139, 83),
        ResourceType.IronOre => Color.Color8(224, 156, 83),
        ResourceType.Stone or ResourceType.Iron or ResourceType.Tools or ResourceType.Salt => Color.Color8(187, 211, 193),
        ResourceType.Charcoal => Color.Color8(130, 149, 133),
        ResourceType.Flour or ResourceType.Wool or ResourceType.Eggs or ResourceType.Sheep or ResourceType.Cows => Color.Color8(245, 233, 201),
        ResourceType.Milk or ResourceType.Fish or ResourceType.Stew => Color.Color8(111, 169, 173),
        _ => Color.Color8(137, 198, 158),
    };

    public static Color MetricTint(ColonyMetric metric) => metric switch
    {
        ColonyMetric.FoodDays => ResourceTint(ResourceType.Grain), ColonyMetric.Coins => ResourceTint(ResourceType.Coins),
        ColonyMetric.Mood => Mint, _ => Ink,
    };

    public static PanelContainer Card(Node parent, int padding = 12)
    {
        var card = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        card.AddThemeStyleboxOverride("panel", MenuStyle.Box(Surface, MenuStyle.Edge, padding));
        parent.AddChild(card);
        return card;
    }

    public static HBoxContainer Row(Node parent, int gap = 8)
    {
        var row = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", gap); parent.AddChild(row); return row;
    }

    public static Label Text(Node parent, string text, int size = 13, Color? color = null, bool wrap = false)
    {
        var label = MenuStyle.Text(parent, text, size, color ?? Ink, wrap);
        label.MouseFilter = Control.MouseFilterEnum.Ignore;
        return label;
    }

    public static Label Pill(Node parent, string text, Color color)
    {
        var label = Text(parent, text, 11, color);
        label.AddThemeStyleboxOverride("normal", MenuStyle.Box(new Color(0.045f, 0.095f, 0.075f), Colors.Transparent, 6));
        label.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        return label;
    }

    public static TextureRect Icon(Node parent, ResourceType good, int size = 24) => Picture(parent, ResourceIcons.Get(good), size);

    public static TextureRect Picture(Node parent, Texture2D texture, int size = 24)
    {
        var icon = new TextureRect
        {
            Texture = texture, CustomMinimumSize = new Vector2(size, size),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest, MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        parent.AddChild(icon); return icon;
    }

    public static ProgressBar Bar(Node parent, Color color, int height = 7)
    {
        var bar = new ProgressBar
        {
            MinValue = 0, MaxValue = 1, Step = 0.001, ShowPercentage = false,
            CustomMinimumSize = new Vector2(20, height), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        bar.AddThemeStyleboxOverride("background", MenuStyle.Box(new Color(0.035f, 0.075f, 0.06f), Colors.Transparent, 0));
        bar.AddThemeStyleboxOverride("fill", MenuStyle.Box(color, Colors.Transparent, 0));
        parent.AddChild(bar); return bar;
    }

    public static void ColorBar(ProgressBar bar, Color color) => ((StyleBoxFlat)bar.GetThemeStylebox("fill")).BgColor = color;

    public static Label Metric(Node parent, string title, ResourceType icon, Color color)
    {
        var row = Row(Card(parent, 10));
        Icon(row, icon, 28);
        var column = MenuStyle.Column(row, 1);
        Text(column, title, 11, Muted);
        var value = Text(column, "—", 24, color);
        value.ClipText = true; value.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        return value;
    }

    public static void Spacer(Node parent) => parent.AddChild(new Control
    {
        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore,
    });

    public static Color SectorColor(WorkSector sector) => sector switch
    {
        WorkSector.Food => Color.Color8(137, 198, 158), WorkSector.Farm => Color.Color8(221, 193, 105),
        WorkSector.Wood => Color.Color8(190, 145, 103), WorkSector.Stone => Color.Color8(143, 182, 196),
        WorkSector.Construction => Color.Color8(229, 163, 114), WorkSector.Craft => Color.Color8(185, 165, 217),
        _ => Color.Color8(142, 165, 157),
    };

    public static ResourceType SectorIcon(WorkSector sector) => sector switch
    {
        WorkSector.Food => ResourceType.Food, WorkSector.Farm => ResourceType.Grain, WorkSector.Wood => ResourceType.Wood,
        WorkSector.Stone => ResourceType.Stone, WorkSector.Construction => ResourceType.Tools, _ => ResourceType.Iron,
    };
}
