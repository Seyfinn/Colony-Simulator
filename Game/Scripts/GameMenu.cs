using System;
using Godot;
using GodColony.Simulation.Time;
using GodColony.Simulation.Persistence;
using GodColony.View;

namespace GodColony;

public sealed record WorldCreationOptions(int Seed, int Size, int Colonies, int Founders,
    bool Migration, bool Lifecycle, bool Trade, GameSpeed Speed);

/// <summary>Accueil, pause, création de monde et préférences, utilisables au clavier comme à la souris.</summary>
public partial class GameMenu : CanvasLayer
{
    public event Action<WorldCreationOptions>? WorldRequested;
    public event Action? ResumeRequested;
    public event Action? HomeRequested;
    public event Action? SettingsChanged;
    public event Action<int>? SaveRequested;
    public event Action<int, bool>? LoadRequested;
    public Func<int, SaveSlot>? ReadSaveSlot { get; set; }
    private Control _root = null!;
    private VBoxContainer _body = null!;
    private ConfirmationDialog _replace = null!;
    private ConfirmationDialog _fileConfirm = null!;
    private Action? _confirmedAction;
    private string _saveMessage = "";
    private bool _saveError;
    private string _busyTitle = "Création du monde…";
    private GameSettings _settings = null!;
    private string _page = "home";
    private bool _pausedPage;
    private WorldCreationOptions? _pendingWorld;
    public bool HasWorld { get; set; }
    public bool IsOpen => _root.Visible;
    public bool IsBusy => _page == "busy" && IsOpen;

    public void Init(GameSettings settings) => _settings = settings;

    public override void _Ready()
    {
        Layer = 30;
        _root = new Control { Name = "MenuPrincipal", MouseFilter = Control.MouseFilterEnum.Stop, Theme = MenuStyle.Theme() };
        AddChild(_root);
        _root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var shade = new ColorRect { Color = new Color(0.025f, 0.055f, 0.05f, 0.96f), MouseFilter = Control.MouseFilterEnum.Ignore };
        _root.AddChild(shade);
        shade.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var center = new CenterContainer();
        _root.AddChild(center);
        center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(600, 0) };
        center.AddChild(panel);
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(556, 610), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        panel.AddChild(scroll);
        _body = MenuStyle.Column(scroll, 12);
        _replace = new ConfirmationDialog
        {
            Title = "Créer un nouveau monde ?", DialogText = "La partie actuelle sera remplacée. Les changements depuis la dernière sauvegarde seront perdus.",
            OkButtonText = "Créer le monde", CancelButtonText = "Annuler",
        };
        _root.AddChild(_replace);
        _replace.Confirmed += () => { if (_pendingWorld is { } options) Generate(options); };
        _fileConfirm = new ConfirmationDialog { CancelButtonText = "Annuler" };
        _root.AddChild(_fileConfirm);
        _fileConfirm.Confirmed += () => { Action? action = _confirmedAction; _confirmedAction = null; action?.Invoke(); };
        _fileConfirm.Canceled += () => _confirmedAction = null;
        ShowHome();
    }

    public void ShowHome()
    {
        _pausedPage = false;
        ShowPage("home");
    }

    public void ShowPause()
    {
        _pausedPage = true;
        ShowPage("home");
    }

    public void Close() => _root.Hide();
    public void ShowCreation() => ShowPage("world");
    public void ShowSettings() => ShowPage("settings");
    public void ShowSaveResult(string message, bool error = false) => ShowFileResult("save", message, error);
    public void ShowLoadError(string message) => ShowFileResult("load", message, true);
    private void ShowFileResult(string page, string message, bool error)
    {
        _saveMessage = message; _saveError = error;
        ShowPage(page);
    }
    public void RequestQuickLoad() => RequestLoad(SaveSlots.QuickSlot, false);

    public void Back()
    {
        if (_fileConfirm.Visible) { _fileConfirm.Hide(); _confirmedAction = null; return; }
        if (_replace.Visible) { _replace.Hide(); return; }
        if (_page == "busy") return;
        if (_page != "home") ShowPage("home");
        else if (HasWorld) ResumeRequested?.Invoke();
    }

    private void ShowPage(string page)
    {
        _page = page;
        _root.Show();
        foreach (Node child in _body.GetChildren()) { _body.RemoveChild(child); child.QueueFree(); }
        MenuStyle.Text(_body, "GODCOLONY", 42, ArtDirection.Brass);
        MenuStyle.Text(_body, "Un monde entre vos mains", 17, MenuStyle.Muted);
        _body.AddChild(new HSeparator());
        switch (page)
        {
            case "home": BuildHome(); break;
            case "world": BuildWorld(); break;
            case "settings": BuildSettings(); break;
            case "help": BuildHelp(); break;
            case "save": BuildSaves(false); break;
            case "load": BuildSaves(true); break;
            case "busy": MenuStyle.Text(_body, _busyTitle, 23); MenuStyle.Text(_body, "Préparation du monde et de ses habitants.", 15, MenuStyle.Muted); break;
        }
        if (page is not "home" and not "busy") MenuStyle.Button(_body, "← Retour", () => ShowPage("home"));
        foreach (Node child in _body.GetChildren())
            if (child is Button button) { button.GrabFocus(); break; }
    }

    private void BuildHome()
    {
        MenuStyle.Text(_body, _pausedPage ? "Partie en pause" : "Bienvenue", 24);
        MenuStyle.Text(_body, "Façonnez un monde, fondez des colonies et observez leurs habitants construire leur histoire.", 15, MenuStyle.Muted, true);
        if (HasWorld) MenuStyle.Button(_body, "Reprendre la partie", () => ResumeRequested?.Invoke());
        if (HasWorld) MenuStyle.Button(_body, "Sauvegarder la partie", () => { _saveMessage = ""; ShowPage("save"); });
        MenuStyle.Button(_body, "Charger une partie", () => { _saveMessage = ""; ShowPage("load"); });
        MenuStyle.Button(_body, "Créer un monde", () => ShowPage("world"));
        MenuStyle.Button(_body, "Paramètres", () => ShowPage("settings"));
        MenuStyle.Button(_body, "Comment jouer", () => ShowPage("help"));
        if (_pausedPage) MenuStyle.Button(_body, "Retour à l'accueil", () => { HomeRequested?.Invoke(); ShowHome(); });
        MenuStyle.Button(_body, "Quitter le jeu", () => GetTree().Quit());
        MenuStyle.Text(_body, "F5 : sauvegarde rapide · F9 : charger la sauvegarde rapide\nPensez à sauvegarder avant de quitter le jeu.", 12, MenuStyle.Muted, true);
    }

    private void BuildSaves(bool loading)
    {
        MenuStyle.Text(_body, loading ? "Charger une partie" : "Sauvegarder la partie", 24);
        if (!loading && !HasWorld) { MenuStyle.Text(_body, "Créez ou chargez un monde avant de sauvegarder.", 15, MenuStyle.Muted, true); return; }
        if (_saveMessage.Length > 0) MenuStyle.Text(_body, _saveMessage, 14, _saveError ? MenuStyle.Error : ArtDirection.Sage, true);
        for (int i = 0; i <= SaveSlots.QuickSlot; i++)
        {
            int index = i;
            SaveSlot slot = ReadSaveSlot?.Invoke(index) ?? new SaveSlot(index, false, null, null, false);
            if (!loading && i == SaveSlots.QuickSlot) continue;
            var card = new PanelContainer();
            _body.AddChild(card);
            var column = MenuStyle.Column(card, 6);
            MenuStyle.Text(column, i == SaveSlots.QuickSlot ? "Sauvegarde rapide · F5" : $"Emplacement {i + 1}", 17, ArtDirection.Brass);
            string description = slot.Info is { } info
                ? $"{info.Name} · {info.Colonies} colonies · {info.Population} habitants\nAn {new GameClock(info.Ticks).Year} · {info.SavedAt.ToLocalTime():dd/MM/yyyy HH:mm}"
                : slot.Error ?? "Emplacement vide";
            MenuStyle.Text(column, description, 13, MenuStyle.Muted, true);
            if (loading)
            {
                var load = MenuStyle.Button(column, "Charger", () => RequestLoad(index, false));
                load.Name = $"Charger{index}"; load.Disabled = slot.Info is null;
                if (slot.BackupAvailable)
                    MenuStyle.Button(column, "Charger la version précédente", () => RequestLoad(index, true)).Name = $"ChargerSecours{index}";
            }
            else
            {
                var save = MenuStyle.Button(column, slot.Exists ? "Remplacer cette sauvegarde" : "Sauvegarder ici", () =>
                {
                    if (!slot.Exists) SaveRequested?.Invoke(index);
                    else ConfirmFile("Remplacer la sauvegarde ?", "La sauvegarde actuelle de cet emplacement sera remplacée. Sa version précédente sera conservée en secours.",
                        "Remplacer", () => SaveRequested?.Invoke(index));
                });
                save.Name = $"Sauvegarder{index}";
            }
        }
    }

    private void RequestLoad(int slot, bool backup)
    {
        SaveSlot? state = ReadSaveSlot?.Invoke(slot);
        if (!(backup ? state?.BackupAvailable ?? false : state?.Exists ?? false)) { ShowLoadError("Aucune sauvegarde disponible dans cet emplacement."); return; }
        if (!HasWorld) LoadRequested?.Invoke(slot, backup);
        else ConfirmFile("Charger la partie ?", "La partie actuelle sera remplacée. Les changements depuis votre dernière sauvegarde seront perdus.",
            "Charger", () => LoadRequested?.Invoke(slot, backup));
    }

    private void ConfirmFile(string title, string text, string action, Action confirmed)
    {
        _confirmedAction = confirmed;
        _fileConfirm.Title = title; _fileConfirm.DialogText = text; _fileConfirm.OkButtonText = action;
        _fileConfirm.PopupCentered();
    }

    private void BuildWorld()
    {
        MenuStyle.Text(_body, "Créer un monde", 24);
        MenuStyle.Text(_body, "Graine · le même nombre reproduit le même terrain", 14, MenuStyle.Muted);
        var seedRow = new HBoxContainer();
        _body.AddChild(seedRow);
        var seed = new LineEdit { Name = "Graine", Text = "12345", MaxLength = 11, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        seedRow.AddChild(seed);
        MenuStyle.Button(seedRow, "Aléatoire", () => seed.Text = Random.Shared.Next(1, int.MaxValue).ToString());
        MenuStyle.Text(_body, "Taille des régions", 14, MenuStyle.Muted);
        var size = new OptionButton { Name = "TailleMonde" };
        size.AddItem("Petite · 128 × 128 cases", 128); size.AddItem("Standard · 200 × 200 cases", 200); size.AddItem("Grande · 256 × 256 cases", 256); size.Select(1);
        _body.AddChild(size);
        MenuStyle.Text(_body, "Colonies initiales", 14, MenuStyle.Muted);
        var colonies = new OptionButton { Name = "ColoniesInitiales" };
        colonies.AddItem("Monde vierge · fondez vous-même la première colonie", 0);
        for (int i = 1; i <= 4; i++) colonies.AddItem($"{i} colonie{(i > 1 ? "s" : "")} déjà installée{(i > 1 ? "s" : "")}", i);
        _body.AddChild(colonies);
        MenuStyle.Text(_body, "Habitants par colonie initiale", 14, MenuStyle.Muted);
        var founders = new SpinBox { Name = "FondateursInitiaux", MinValue = 5, MaxValue = 20, Step = 1, Value = 8 };
        _body.AddChild(founders);
        founders.Editable = false;
        colonies.ItemSelected += index => founders.Editable = index != 0;
        var migration = MenuStyle.Check(_body, "Voyageurs et migrations", true);
        var lifecycle = MenuStyle.Check(_body, "Naissances, vieillissement et décès", true);
        var trade = MenuStyle.Check(_body, "Commerce et caravanes entre colonies", true);
        MenuStyle.Text(_body, "Vitesse de départ", 14, MenuStyle.Muted);
        var speed = new OptionButton();
        speed.AddItem("Observation · ×1", 1); speed.AddItem("Rapide · ×4", 4); speed.AddItem("Très rapide · ×30", 30);
        _body.AddChild(speed);
        var error = MenuStyle.Text(_body, "", 14, MenuStyle.Error, true);
        MenuStyle.Button(_body, "Créer et explorer", () =>
        {
            if (!int.TryParse(seed.Text.Trim(), out int number)) { error.Text = "La graine doit être un nombre entier compris entre −2147483648 et 2147483647."; return; }
            _pendingWorld = new WorldCreationOptions(number, size.GetSelectedId(), colonies.GetSelectedId(), (int)founders.Value,
                migration.ButtonPressed, lifecycle.ButtonPressed, trade.ButtonPressed, (GameSpeed)speed.GetSelectedId());
            if (HasWorld) _replace.PopupCentered(); else Generate(_pendingWorld);
        });
    }

    private void Generate(WorldCreationOptions options)
    {
        RunFileOperation("Création du monde…", () => WorldRequested?.Invoke(options));
    }

    public void RunFileOperation(string title, Action action)
    {
        _busyTitle = title;
        ShowPage("busy");
        Callable.From((Action)(async () =>
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            action();
        })).CallDeferred();
    }

    private void BuildSettings()
    {
        MenuStyle.Text(_body, "Paramètres", 24);
        var fullscreen = MenuStyle.Check(_body, "Plein écran", _settings.Fullscreen);
        var vsync = MenuStyle.Check(_body, "Synchronisation verticale", _settings.VSync);
        var ambience = MenuStyle.Check(_body, "Effets d'ambiance · halos, braises et eau animée", _settings.AmbientEffects);
        MenuStyle.Text(_body, "Vitesse de déplacement de la caméra", 14, MenuStyle.Muted);
        var sensitivity = new HSlider { MinValue = 0.5, MaxValue = 2, Step = 0.1, Value = _settings.CameraSensitivity, CustomMinimumSize = new Vector2(0, 28) };
        _body.AddChild(sensitivity);
        var value = MenuStyle.Text(_body, $"×{sensitivity.Value:0.0}", 14, ArtDirection.Brass);
        sensitivity.ValueChanged += amount => value.Text = $"×{amount:0.0}";
        var result = MenuStyle.Text(_body, "", 14, MenuStyle.Muted, true);
        MenuStyle.Button(_body, "Appliquer les paramètres", () =>
        {
            _settings.Fullscreen = fullscreen.ButtonPressed; _settings.VSync = vsync.ButtonPressed;
            _settings.AmbientEffects = ambience.ButtonPressed; _settings.CameraSensitivity = (float)sensitivity.Value;
            _settings.Apply();
            Error error = _settings.Save();
            SettingsChanged?.Invoke();
            result.Text = error == Error.Ok ? "Paramètres appliqués et conservés pour les prochains lancements." : "Paramètres appliqués ; impossible de les enregistrer sur ce disque.";
        });
    }

    private void BuildHelp()
    {
        MenuStyle.Text(_body, "Comment jouer", 24);
        MenuStyle.Text(_body, "1. Créez un monde vierge ou déjà peuplé.\n\n2. Cliquez sur « Fonder une colonie », choisissez son peuple et ses habitants, puis son emplacement sur la carte du monde.\n\n3. Sur le terrain, choisissez une zone plate. Le contour vert indique un camp valide ; le rouge signale un obstacle. Validez pour faire apparaître les fondateurs.\n\n4. Observez le travail, les naissances, le commerce et les prières de vos colonies.", 15, MenuStyle.Ink, true);
        MenuStyle.Text(_body, "ZQSD / WASD / flèches : déplacer la caméra\nClic droit ou molette maintenue : glisser · Molette : zoom\nClic gauche : sélectionner un habitant / miner la roche\nEspace : pause · 1 / 2 / 3 : vitesse\nF5 : sauvegarde rapide · F9 : charger la sauvegarde rapide\nÉchap : annuler la fondation, fermer un panneau ou ouvrir le menu", 14, MenuStyle.Muted, true);
    }
}
