using System;
using Godot;
using GodColony.Simulation.Colonies;
using GodColony.View;

namespace GodColony;

/// <summary>Une fondation en deux étapes : région du monde, puis emplacement du feu sur le terrain.</summary>
public partial class ColonyCreationPanel : CanvasLayer
{
    public event Action? RegionRequested;
    public event Action? ConfirmRequested;
    public event Action? CancelRequested;
    public event Action? SuggestedSiteRequested;
    private PanelContainer _panel = null!;
    private LineEdit _name = null!;
    private OptionButton _species = null!;
    private SpinBox _founders = null!;
    private CheckBox _randomSize = null!;
    private Label _step = null!, _description = null!, _site = null!, _error = null!, _provisions = null!;
    private Button _region = null!, _confirm = null!, _suggest = null!;
    private bool _terrainStage;
    private bool _validSite;
    public bool IsOpen => _panel.Visible;
    public Species Species => Species.All[_species.Selected];
    public string ColonyName => _name.Text.Trim();
    public bool RandomSize => _randomSize.ButtonPressed;
    /// <summary>Le nombre choisi, ou un tirage entre 5 et 15 en mode aléatoire (à appeler une seule fois, à la confirmation).</summary>
    public int Founders => RandomSize ? ColonyFounder.RandomFounderCount(Random.Shared) : (int)_founders.Value;

    public override void _Ready()
    {
        Layer = 15;
        var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Theme = MenuStyle.Theme() };
        AddChild(root);
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _panel = new PanelContainer { Name = "CreationColonie", Visible = false };
        root.AddChild(_panel);
        _panel.AnchorLeft = _panel.AnchorRight = 1;
        _panel.AnchorBottom = 1;
        _panel.OffsetLeft = -388; _panel.OffsetRight = -16; _panel.OffsetTop = 182; _panel.OffsetBottom = -60;
        root.Resized += () =>
        {
            _panel.OffsetLeft = -16 - InterfaceLayout.SideWidth(root.Size);
            _panel.OffsetTop = 98;
        };
        _panel.OffsetLeft = -16 - InterfaceLayout.SideWidth(root.Size);
        _panel.OffsetTop = 98;
        var frame = MenuStyle.Column(_panel, 8);
        MenuStyle.Text(frame, "Fonder une colonie", 24, ArtDirection.Brass);
        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        frame.AddChild(scroll);
        var body = MenuStyle.Column(scroll);
        _step = MenuStyle.Text(body, "", 13, ArtDirection.Sage, true);
        MenuStyle.Text(body, "Nom de la colonie", 14, MenuStyle.Muted);
        _name = new LineEdit { Name = "NomColonie", MaxLength = 40, PlaceholderText = "Ex. : Clairerive" };
        body.AddChild(_name);
        _name.TextChanged += _ => UpdateConfirm();
        MenuStyle.Text(body, "Peuple fondateur", 14, MenuStyle.Muted);
        _species = new OptionButton { Name = "PeupleFondateur" };
        foreach (Species species in Species.All) _species.AddItem(species.Plural);
        body.AddChild(_species);
        _description = MenuStyle.Text(body, "", 13, MenuStyle.Muted, true);
        _species.ItemSelected += _ => UpdateSpecies();
        MenuStyle.Text(body, "Nombre de fondateurs", 14, MenuStyle.Muted);
        _founders = new SpinBox { Name = "NombreFondateurs", MinValue = 5, MaxValue = 20, Step = 1, Value = 8 };
        body.AddChild(_founders);
        _randomSize = new CheckBox
        {
            Name = "ColonieAleatoire",
            Text = $"Colonie aléatoire ({ColonyFounder.MinRandomFounders} à {ColonyFounder.MaxRandomFounders} habitants)",
        };
        body.AddChild(_randomSize);
        _randomSize.Toggled += _ => UpdateProvisions();
        _provisions = MenuStyle.Text(body, "", 13, MenuStyle.Muted, true);
        _founders.ValueChanged += _ => UpdateProvisions();
        var actions = MenuStyle.Column(frame, 6);
        _site = MenuStyle.Text(actions, "", 13, MenuStyle.Ink, true);
        _error = MenuStyle.Text(actions, "", 13, MenuStyle.Error, true);
        _region = MenuStyle.Button(actions, "Choisir sur la carte du monde", () => RegionRequested?.Invoke());
        _region.Name = "ChoisirRegion";
        _suggest = MenuStyle.Button(actions, "Emplacement conseillé", () => SuggestedSiteRequested?.Invoke());
        _suggest.Name = "SiteConseille";
        var confirmRow = new HBoxContainer();
        confirmRow.AddThemeConstantOverride("separation", 8);
        actions.AddChild(confirmRow);
        _confirm = MenuStyle.Button(confirmRow, "Fonder la colonie", () => ConfirmRequested?.Invoke());
        _confirm.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _confirm.Name = "ConfirmerFondation";
        MenuStyle.Primary(_confirm);
        MenuStyle.Button(confirmRow, "Annuler", () => CancelRequested?.Invoke());
        UpdateSpecies(); UpdateProvisions();
    }

    public void Open(int colonyNumber)
    {
        _name.Text = $"Colonie {colonyNumber}";
        _species.Select(0); _founders.Value = 8; _randomSize.ButtonPressed = false;
        UpdateSpecies(); UpdateProvisions();
        _panel.Show();
        ShowRegionStage();
        _name.GrabFocus();
        _name.SelectAll();
    }

    public void Close() => _panel.Hide();

    public void ShowRegionStage()
    {
        _terrainStage = false; _validSite = false;
        _species.Disabled = false;
        _step.Text = "1 / 2 · Choisissez une région";
        _site.Text = "Cliquez sur un espace libre de la carte du monde. Chaque colonie dispose de sa propre région.";
        _site.AddThemeColorOverride("font_color", MenuStyle.Ink);
        _error.Text = "";
        _region.Text = "Choisir sur la carte du monde";
        _suggest.Visible = false;
        UpdateConfirm();
    }

    public void ShowTerrainStage(string region)
    {
        _terrainStage = true; _validSite = false;
        _species.Disabled = true;
        _step.Text = $"2 / 2 · Placez le camp · {region}";
        _site.Text = "Cliquez sur le terrain pour fixer le camp. Cherchez de l'eau, des baies, des arbres et de la roche à proximité.";
        _site.AddThemeColorOverride("font_color", MenuStyle.Ink);
        _error.Text = "";
        _region.Text = "← Choisir une autre région";
        _suggest.Visible = true;
        UpdateConfirm();
    }

    public void SetSite(int x, int y, bool valid, string reason)
    {
        _validSite = valid;
        _site.Text = $"Camp choisi : ({x}, {y})\n{reason}";
        _site.AddThemeColorOverride("font_color", valid ? ArtDirection.Sage : MenuStyle.Error);
        _error.Text = "";
        UpdateConfirm();
    }

    public void SetError(string message) => _error.Text = message;
    private void UpdateConfirm() => _confirm.Disabled = !_terrainStage || !_validSite || ColonyName.Length == 0;
    private void UpdateProvisions()
    {
        _founders.Editable = !RandomSize;
        string dotation = RandomSize
            ? $"{ColonyFounder.MinRandomFounders * 4} à {ColonyFounder.MaxRandomFounders * 4} provisions selon le tirage"
            : $"{(int)_founders.Value * 4} provisions";
        _provisions.Text = $"Dotation : {dotation} et {ColonyFounder.StartingCoins} pièces.\nLes fondateurs organisent ensuite leur travail automatiquement.";
    }
    private void UpdateSpecies() => _description.Text = _species.Selected switch
    {
        1 => "Nains · hautes terres. Habiles mineurs et forgerons ; longue durée de vie.",
        2 => "Elfes · forêts. Habiles cueilleurs ; très longue durée de vie.",
        3 => "Orques · steppes. Vigoureux travailleurs ; croissance rapide.",
        _ => "Humains · plaines tempérées. Talents équilibrés et terres fertiles.",
    };
}
