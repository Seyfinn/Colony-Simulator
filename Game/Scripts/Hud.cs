using System.Linq;
using Godot;
using GodColony.Simulation.Colonies;
using GodColony.Simulation.Time;

namespace GodColony;

/// <summary>Interface : bandeau d'informations en haut à gauche, fiche du colon sélectionné à droite.</summary>
public partial class Hud : CanvasLayer
{
    private Label _status = null!;
    private Label _colony = null!;
    private Label _shares = null!;
    private Label _tileInfo = null!;
    private Label _thoughts = null!;

    private const int ThoughtsShown = 8;

    private PanelContainer _colonistPanel = null!;
    private Label _colonistName = null!;
    private Label _colonistActivity = null!;
    private Label _colonistSector = null!;
    private Label _colonistSkills = null!;
    private NeedBar _food = null!, _rest = null!, _leisure = null!, _mood = null!;

    public override void _Ready()
    {
        PanelContainer info = MakePanel();
        info.Position = new Vector2(12, 12);
        AddChild(info);
        var column = new VBoxContainer();
        info.AddChild(column);

        _status = AddLabel(column, 18, new Color(1, 1, 1));
        _colony = AddLabel(column, 15, new Color(0.95f, 0.85f, 0.6f));
        _shares = AddLabel(column, 14, new Color(0.75f, 0.85f, 0.95f));
        _tileInfo = AddLabel(column, 14, new Color(0.8f, 0.8f, 0.8f));
        AddLabel(column, 13, new Color(0.65f, 0.65f, 0.65f)).Text =
            "Espace : pause   ·   1, 2, 3 : vitesses   ·   ZQSD / clic droit : déplacer   ·   molette : zoom   ·   clic gauche : choisir un colon (ou miner la roche)";

        // Les pensées de la colonie, en bas à gauche : on y voit son cerveau fonctionner.
        PanelContainer thoughtsPanel = MakePanel();
        thoughtsPanel.AnchorTop = thoughtsPanel.AnchorBottom = 1;
        thoughtsPanel.OffsetLeft = 12;
        thoughtsPanel.OffsetBottom = -12;
        thoughtsPanel.GrowVertical = Control.GrowDirection.Begin;
        AddChild(thoughtsPanel);
        var thoughtsColumn = new VBoxContainer();
        thoughtsPanel.AddChild(thoughtsColumn);
        AddLabel(thoughtsColumn, 15, new Color(0.95f, 0.85f, 0.6f)).Text = "Pensées de la colonie";
        _thoughts = AddLabel(thoughtsColumn, 14, new Color(0.88f, 0.88f, 0.88f));

        _colonistPanel = MakePanel();
        _colonistPanel.AnchorLeft = _colonistPanel.AnchorRight = 1;
        _colonistPanel.OffsetLeft = -300;
        _colonistPanel.OffsetRight = -12;
        _colonistPanel.OffsetTop = 12;
        _colonistPanel.Visible = false;
        AddChild(_colonistPanel);
        var colonistColumn = new VBoxContainer();
        colonistColumn.AddThemeConstantOverride("separation", 6);
        _colonistPanel.AddChild(colonistColumn);

        _colonistName = AddLabel(colonistColumn, 20, new Color(1, 1, 1));
        _colonistActivity = AddLabel(colonistColumn, 14, new Color(0.85f, 0.85f, 0.85f));
        _food = new NeedBar(colonistColumn, "Nourriture");
        _rest = new NeedBar(colonistColumn, "Repos");
        _leisure = new NeedBar(colonistColumn, "Détente");
        _mood = new NeedBar(colonistColumn, "Humeur");
        _colonistSector = AddLabel(colonistColumn, 14, new Color(0.95f, 0.85f, 0.6f));
        _colonistSkills = AddLabel(colonistColumn, 13, new Color(0.8f, 0.8f, 0.8f));
    }

    public void SetStatus(string text) => _status.Text = text;
    public void SetColony(string text) => _colony.Text = text;
    public void SetTileInfo(string text) => _tileInfo.Text = text;

    public void ShowShares(Colony colony)
    {
        var parts = WorkSectors.All.Select(s => $"{SectorName(s)} {colony.WorkShares[s] * 100:0} %");
        _shares.Text = "Répartition du travail : " + string.Join("  ·  ", parts);
    }

    /// <summary>Les dernières pensées, de la plus récente à la plus ancienne, datées (jour et heure).</summary>
    public void ShowThoughts(Colony colony)
    {
        var lines = colony.Thoughts
            .AsEnumerable()
            .Reverse()
            .Take(ThoughtsShown)
            .Select(t =>
            {
                long day = t.Ticks / TimeConstants.TicksPerDay + 1;
                int hour = (int)(t.Ticks % TimeConstants.TicksPerDay * 24 / TimeConstants.TicksPerDay);
                return $"J{day} {hour:00}h   {t.Text}";
            });
        _thoughts.Text = string.Join("\n", lines);
    }

    public void ShowColonist(Colonist? colonist)
    {
        _colonistPanel.Visible = colonist is not null;
        if (colonist is null)
            return;
        _colonistName.Text = colonist.Name;
        _colonistActivity.Text = Describe(colonist);
        _food.Set(colonist.Needs.Food);
        _rest.Set(colonist.Needs.Rest);
        _leisure.Set(colonist.Needs.Leisure);
        _mood.Set(colonist.Needs.Mood);
        _colonistSector.Text = $"Affecté à : {SectorName(colonist.Sector)}\n" +
                               (colonist.Home is null ? "Dort à la belle étoile" : "Dort dans une hutte");

        // Niveau et talent : « ★ » = très doué (apprend vite), « · » = peu doué.
        var lines = new System.Text.StringBuilder("Compétences\n");
        foreach (SkillType skill in Skills.All)
        {
            float talent = colonist.Skills.Talent(skill);
            string mark = talent > 1.2f ? " ★" : talent < 0.75f ? " ·" : "";
            lines.Append($"  {SkillName(skill),-14} {colonist.Skills.Level(skill):0.0}{mark}\n");
        }
        _colonistSkills.Text = lines.ToString().TrimEnd();
    }

    public static string SectorName(WorkSector sector) => sector switch
    {
        WorkSector.Food => "nourriture",
        WorkSector.Wood => "bois",
        WorkSector.Stone => "pierre",
        WorkSector.Construction => "construction",
        _ => "temps libre",
    };

    private static string SkillName(SkillType skill) => skill switch
    {
        SkillType.Foraging => "Cueillette",
        SkillType.Fishing => "Pêche",
        SkillType.Woodcutting => "Bûcheronnage",
        SkillType.Mining => "Minage",
        _ => "Construction",
    };

    private static string Describe(Colonist colonist)
    {
        Activity? activity = colonist.Activity;
        if (activity is null)
            return "Réfléchit à ce qu'il va faire";
        bool there = activity.Started;
        return activity.Kind switch
        {
            ActivityKind.Sleep => there ? "Dort" : "Va se coucher",
            ActivityKind.Eat => there ? "Mange" : "Va manger au camp",
            ActivityKind.Relax => there ? "Se détend près du feu" : "Va se détendre près du feu",
            ActivityKind.Forage => there ? "Cueille des baies" : "Part cueillir des baies",
            ActivityKind.ForageToEat => there ? "Mange des baies sauvages" : "Cherche des baies à manger",
            ActivityKind.Fish => there ? "Pêche" : "Part pêcher au lac",
            ActivityKind.Chop => there ? "Abat un arbre" : "Part couper du bois",
            ActivityKind.Mine => there ? "Taille la roche" : "Part à la carrière",
            ActivityKind.Deliver => "Rapporte sa récolte au camp",
            ActivityKind.FetchMaterials => "Va chercher du bois pour le chantier",
            ActivityKind.SupplySite => "Apporte du bois au chantier",
            ActivityKind.Build => there ? "Bâtit une hutte" : "Part sur le chantier",
            _ => "Se promène",
        };
    }

    private static PanelContainer MakePanel()
    {
        var panel = new PanelContainer();
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.08f, 0.08f, 0.1f, 0.8f),
            ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 8, ContentMarginBottom = 10,
            CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6, CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6,
        };
        panel.AddThemeStyleboxOverride("panel", style);
        return panel;
    }

    private static Label AddLabel(Container parent, int size, Color color)
    {
        var label = new Label();
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        parent.AddChild(label);
        return label;
    }

    /// <summary>Une ligne « nom — barre — pourcentage », la barre passant du rouge au vert.</summary>
    private sealed class NeedBar
    {
        private readonly ProgressBar _bar;
        private readonly Label _value;
        private readonly StyleBoxFlat _fill = new();

        public NeedBar(Container parent, string name)
        {
            var row = new HBoxContainer();
            parent.AddChild(row);
            var label = new Label { Text = name, CustomMinimumSize = new Vector2(90, 0) };
            row.AddChild(label);
            _bar = new ProgressBar
            {
                MinValue = 0, MaxValue = 1, Step = 0.001, ShowPercentage = false,
                CustomMinimumSize = new Vector2(120, 14), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            };
            _bar.AddThemeStyleboxOverride("fill", _fill);
            _bar.AddThemeStyleboxOverride("background", new StyleBoxFlat { BgColor = new Color(0.2f, 0.2f, 0.22f) });
            row.AddChild(_bar);
            _value = new Label { CustomMinimumSize = new Vector2(48, 0), HorizontalAlignment = HorizontalAlignment.Right };
            row.AddChild(_value);
        }

        public void Set(float value)
        {
            _bar.Value = value;
            _fill.BgColor = new Color(0.85f, 0.25f, 0.2f).Lerp(new Color(0.35f, 0.75f, 0.35f), value);
            _value.Text = $"{value * 100:0} %";
        }
    }
}
