using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GodColony.Simulation;
using GodColony.Simulation.Colonies;
using GodColony.Simulation.Time;
using GodColony.View;

namespace GodColony;

/// <summary>
/// Le progrès et les relations d'une colonie : son âge, le savoir qu'elle étudie, l'arbre des savoirs ; puis ce qu'elle pense
/// de chacune des autres colonies (et pourquoi), ses alliances, ses guerres et ses bandes de guerriers en marche.
/// Les contrôles sont créés une fois ; chaque rafraîchissement ne change que leurs valeurs.
/// </summary>
public partial class CivilizationPanel : VBoxContainer
{
    private static readonly Color Ink = DashboardStyle.Ink, Muted = DashboardStyle.Muted, Known = DashboardStyle.Mint, Danger = DashboardStyle.Warning;

    private Button _knowledgeTab = null!, _relationsTab = null!;
    private VBoxContainer _knowledge = null!, _relations = null!;
    private Label _age = null!, _research = null!, _researchDetail = null!;
    private ProgressBar _researchBar = null!;
    private readonly Dictionary<Discovery, (PanelContainer Card, Label Name, Label State, TextureRect Icon)> _cards = [];
    private Label _war = null!, _noNeighbor = null!;
    private VBoxContainer _rows = null!;
    private readonly List<RelationRow> _relationRows = [];

    public bool ShowingRelations => _relations.Visible;

    public override void _Ready()
    {
        Name = "TableauCivilisation";
        Theme = MenuStyle.Theme();
        TextureFilter = TextureFilterEnum.Nearest;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        AddThemeConstantOverride("separation", 12);

        var tabs = new HBoxContainer();
        tabs.AddThemeConstantOverride("separation", 8);
        AddChild(tabs);
        _knowledgeTab = Tab(tabs, "Savoirs et âges", "OngletSavoirs", ShowKnowledge);
        _relationsTab = Tab(tabs, "Relations entre colonies", "OngletRelations", ShowRelations);

        BuildKnowledge();
        BuildRelations();
        ShowKnowledge();
    }

    private static Button Tab(Node parent, string text, string name, Action pressed)
    {
        var button = new Button
        {
            Text = text, Name = name, ToggleMode = true, CustomMinimumSize = new Vector2(0, 36),
            SizeFlagsHorizontal = SizeFlags.ExpandFill, FocusMode = FocusModeEnum.None,
        };
        button.Pressed += pressed;
        parent.AddChild(button);
        return button;
    }

    public void ShowKnowledge()
    {
        _knowledge.Visible = true; _relations.Visible = false;
        _knowledgeTab.SetPressedNoSignal(true); _relationsTab.SetPressedNoSignal(false);
    }

    public void ShowRelations()
    {
        _knowledge.Visible = false; _relations.Visible = true;
        _knowledgeTab.SetPressedNoSignal(false); _relationsTab.SetPressedNoSignal(true);
    }

    // ---------- Savoirs ----------

    private void BuildKnowledge()
    {
        _knowledge = MenuStyle.Column(this, 10);
        _knowledge.Name = "Savoirs";
        _age = MenuStyle.Text(_knowledge, "", 20, ArtDirection.Brass);

        var study = DashboardStyle.Card(_knowledge, 12);
        var studyRow = DashboardStyle.Row(study, 12);
        DashboardStyle.Picture(studyRow, CivilizationArt.KnowledgeIcon(Discovery.Writing), 32);
        var column = MenuStyle.Column(studyRow, 6);
        column.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _research = MenuStyle.Text(column, "", 15, Ink);
        _researchBar = DashboardStyle.Bar(column, DashboardStyle.Gold, 10);
        _researchDetail = MenuStyle.Text(column, "", 12, Muted, wrap: true);

        MenuStyle.Text(_knowledge, "La colonie étudie d'abord ce qui lui manque pour bâtir ce qu'elle veut ; les caravanes rapportent "
            + "une part des savoirs de leurs hôtes (le double entre alliés). Survolez un savoir pour voir ce qu'il apporte.", 12, Muted, wrap: true);

        foreach (int tier in new[] { 1, 2, 3 })
        {
            MenuStyle.Text(_knowledge, tier switch { 1 => "SAVOIRS DU CAMP", 2 => "SAVOIRS DU VILLAGE", _ => "SAVOIRS DU BOURG" }, 11, ArtDirection.Brass);
            var grid = new GridContainer { Columns = 3, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            grid.AddThemeConstantOverride("h_separation", 8);
            grid.AddThemeConstantOverride("v_separation", 8);
            _knowledge.AddChild(grid);
            foreach (DiscoveryInfo info in Knowledge.All.Where(d => d.Tier == tier))
            {
                var card = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Stop, Name = $"Savoir{info.Id}" };
                grid.AddChild(card);
                var heading = DashboardStyle.Row(card, 8);
                var icon = DashboardStyle.Picture(heading, CivilizationArt.KnowledgeIcon(info.Id));
                icon.Name = $"IconeSavoir{info.Id}";
                var inner = MenuStyle.Column(heading, 2);
                inner.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                Label name = MenuStyle.Text(inner, info.Name, 13, Ink);
                Label state = MenuStyle.Text(inner, "", 11, Muted);
                name.ClipText = state.ClipText = true;
                card.TooltipText = Tooltip(info);
                _cards[info.Id] = (card, name, state, icon);
            }
        }
    }

    private static string Tooltip(DiscoveryInfo info)
    {
        string text = $"{info.Name}\n{info.Effect}";
        var buildings = Knowledge.Unlocks(info.Id).Select(Building.NameOf).ToList();
        if (buildings.Count > 0)
            text += $"\nPermet de bâtir : {string.Join(", ", buildings)}.";
        if (info.Requires.Count > 0)
            text += $"\nDemande : {string.Join(", ", info.Requires.Select(Knowledge.Name))}.";
        return text;
    }

    private static string YearOf(long ticks) => $"an {ticks / TimeConstants.TicksPerYear + 1}";

    private void RefreshKnowledge(Colony colony)
    {
        Age age = Knowledge.AgeOf(colony);
        _age.Text = $"{Knowledge.AgeName(age)} · {colony.Known.Count} savoirs sur {Knowledge.All.Count}";
        float daily = Knowledge.DailyPoints(colony);
        if (colony.Researching is { } target)
        {
            float cost = Knowledge.CostFor(colony, target), progress = Knowledge.Progress(colony, target);
            _research.Text = $"Étude en cours : {Knowledge.Name(target)}";
            _researchBar.Value = Math.Clamp(progress / cost, 0f, 1f);
            _researchBar.Visible = true;
            float days = Math.Max(0f, cost - progress) / daily;
            _researchDetail.Text = $"{Knowledge.Info(target).Effect}\n{progress:0.0} / {cost:0.0} points · {daily:0.0} par jour"
                + (Civic.Has(colony, BuildingType.School) ? " (école)" : "") + $" · encore {days:0} jour{(days >= 2 ? "s" : "")}";
        }
        else
        {
            _research.Text = colony.Known.Count == Knowledge.All.Count ? "Tous les savoirs sont connus." : "Rien à étudier pour l'instant.";
            _researchBar.Visible = false;
            _researchDetail.Text = $"{daily:0.0} points de savoir par jour.";
        }

        foreach ((Discovery id, (PanelContainer card, Label name, Label state, TextureRect icon)) in _cards)
        {
            bool known = colony.Known.TryGetValue(id, out long when);
            bool current = colony.Researching == id;
            bool open = Knowledge.CanResearch(colony, id);
            float progress = Knowledge.Progress(colony, id);
            state.Text = known ? $"✓ connu ({YearOf(when)})"
                : current ? $"à l'étude · {progress / Knowledge.CostFor(colony, id) * 100:0} %"
                : open ? progress > 0 ? $"entamé · {progress / Knowledge.CostFor(colony, id) * 100:0} %" : "accessible"
                : $"demande : {string.Join(", ", Knowledge.Info(id).Requires.Where(r => !Knowledge.Has(colony, r)).Select(Knowledge.Name))}";
            Color edge = known ? Known : current ? DashboardStyle.Gold : MenuStyle.Edge;
            Color fill = DashboardStyle.Surface.Lerp(known ? Known : current ? DashboardStyle.Gold : DashboardStyle.PlotSurface, known || current ? 0.12f : 0.25f);
            card.AddThemeStyleboxOverride("panel", MenuStyle.Box(fill, edge, 8));
            name.AddThemeColorOverride("font_color", known || current || open ? Ink : Muted);
            state.AddThemeColorOverride("font_color", known ? Known : current ? ArtDirection.Brass : Muted);
            icon.Modulate = known || current || open ? Colors.White : new Color(0.45f, 0.52f, 0.47f);
        }
    }

    // ---------- Relations ----------

    private void BuildRelations()
    {
        _relations = MenuStyle.Column(this, 10);
        _relations.Name = "Relations";
        MenuStyle.Text(_relations, "Chaque colonie pense quelque chose des autres, de −100 (haine) à +100 (amitié). Les échanges, le même peuple, "
            + "des racines communes et les présents rapprochent ; les rancunes (barrage, querelles de frontière, attaques), l'envie et le voisinage "
            + "éloignent. Alliance, guerre et paix vous sont demandées par une prière.", 12, Muted, wrap: true);
        _war = MenuStyle.Text(_relations, "", 14, Danger, wrap: true);
        _noNeighbor = MenuStyle.Text(_relations, "Aucune autre colonie dans le monde pour l'instant.", 13, Muted);
        _rows = MenuStyle.Column(_relations, 8);
    }

    private void RefreshRelations(WorldState world, Colony colony)
    {
        var others = world.Colonies.Where(c => c != colony).ToList();
        _noNeighbor.Visible = others.Count == 0;
        while (_relationRows.Count < others.Count)
        {
            var row = new RelationRow { Name = $"Relation{_relationRows.Count}" };
            _rows.AddChild(row);
            _relationRows.Add(row);
        }
        for (int i = 0; i < _relationRows.Count; i++)
        {
            _relationRows[i].Visible = i < others.Count;
            if (i < others.Count)
                _relationRows[i].Refresh(world, colony, others[i]);
        }

        var lines = new List<string>();
        foreach (Colony enemy in Diplomacy.Enemies(world, colony))
            lines.Add($"⚔ En guerre contre {enemy.Name} : {colony.BattlesWon} batailles gagnées, {colony.BattlesLost} perdues · lassitude {colony.WarWeariness:0}.");
        foreach (WarParty party in world.WarParties.Where(p => p.From == colony || p.To == colony))
        {
            int percent = (int)(party.RoutePosition(world.Clock.Ticks) * 100);
            lines.Add(party.From == colony
                ? party.State == WarPartyState.Outbound
                    ? $"{party.Warriors.Count} de nos guerriers marchent sur {party.To.Name} ({percent} % du chemin)."
                    : $"Nos guerriers rentrent de {party.To.Name}" + (party.Victory == true ? " avec leur butin." : ".")
                : party.State == WarPartyState.Outbound
                    ? $"{party.Warriors.Count} guerriers de {party.From.Name} marchent sur nous ({percent} % du chemin)."
                    : $"Les guerriers de {party.From.Name} repartent chez eux.");
        }
        _war.Text = string.Join("\n", lines);
        _war.Visible = lines.Count > 0;
    }

    public void Refresh(WorldState world, Colony colony)
    {
        if (_knowledge.Visible) RefreshKnowledge(colony);
        else RefreshRelations(world, colony);
    }

    /// <summary>Une colonie voisine : ce que nous en pensons (et pourquoi), ce qu'elle pense de nous, notre pacte.</summary>
    private sealed partial class RelationRow : PanelContainer
    {
        private Label _title = null!, _pact = null!, _detail = null!;
        private TextureRect _emblem = null!;
        private OpinionBar _bar = null!;

        public override void _Ready()
        {
            MouseFilter = MouseFilterEnum.Stop;
            var column = MenuStyle.Column(this, 4);
            var heading = DashboardStyle.Row(column);
            _emblem = DashboardStyle.Picture(heading, CivilizationArt.KnowledgeIcon(Discovery.Diplomacy));
            _title = MenuStyle.Text(heading, "", 15, Ink);
            _title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            _title.ClipText = true;
            _pact = MenuStyle.Text(heading, "", 13, Muted);
            _pact.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            _pact.AddThemeStyleboxOverride("normal", MenuStyle.Box(DashboardStyle.Readout, Colors.Transparent, 6));
            _bar = new OpinionBar { CustomMinimumSize = new Vector2(0, 12), SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
            column.AddChild(_bar);
            _detail = MenuStyle.Text(column, "", 12, Muted, wrap: true);
        }

        public void Refresh(WorldState world, Colony colony, Colony other)
        {
            float ours = colony.OpinionOf(other), theirs = other.OpinionOf(colony);
            Pact? pact = Diplomacy.PactBetween(world, colony, other);
            _title.Text = $"{other.Name} · {other.Species.Plural} · {other.Members.Count} hab. · {Knowledge.AgeName(Knowledge.AgeOf(other))}";
            (string pactText, Color pactColor) = pact?.Kind switch
            {
                PactKind.Alliance => ("Alliée", Known),
                PactKind.War => ($"En guerre depuis {(world.Clock.Ticks - pact.SinceTicks) / TimeConstants.TicksPerDay} j", Danger),
                PactKind.Truce => ($"Trêve, encore {Math.Max(0, (pact.UntilTicks - world.Clock.Ticks) / TimeConstants.TicksPerDay)} j", ArtDirection.Brass),
                _ => (Diplomacy.Attitude(ours), ours <= Diplomacy.WaryOpinion ? Danger : ours >= Diplomacy.CordialOpinion ? Known : Muted),
            };
            _pact.Text = pactText;
            _emblem.Texture = pact?.Kind == PactKind.War ? CivilizationArt.WarPact() : CivilizationArt.KnowledgeIcon(Discovery.Diplomacy);
            _pact.AddThemeColorOverride("font_color", pactColor);
            _bar.Ours = ours;
            _bar.Theirs = theirs;
            _bar.QueueRedraw();

            var reasons = Diplomacy.OpinionFactors(world, colony, other).OrderBy(f => f.Value)
                .Select(f => $"{f.Reason} {f.Value:+0;−0}");
            string distance = world.WorldMap.Connected(colony, other)
                ? $"{world.WorldMap.TravelDays(colony, other):0.0} jours de marche" : "injoignable par la terre";
            _detail.Text = $"Notre opinion {ours:+0;−0;0} ({Diplomacy.Attitude(ours).ToLowerInvariant()}) · la leur {theirs:+0;−0;0} "
                + $"({Diplomacy.Attitude(theirs).ToLowerInvariant()}) · {distance}\n{string.Join(" · ", reasons)}";
            Color edge = pact?.Kind == PactKind.War ? Danger : pact?.Kind == PactKind.Alliance ? Known : MenuStyle.Edge;
            AddThemeStyleboxOverride("panel", MenuStyle.Box(DashboardStyle.Surface, edge, 12));
            TooltipText = "Ce qui fait notre opinion (elle s'en rapproche de 3 points par jour) :\n"
                + string.Join("\n", Diplomacy.OpinionFactors(world, colony, other).Select(f => $"  {f.Reason} : {f.Value:+0;−0}"))
                + $"\n  = {Diplomacy.TargetOpinion(world, colony, other):+0;−0;0}";
        }
    }

    /// <summary>Une jauge de −100 à +100 : notre opinion en plein, la leur en repère fin.</summary>
    private sealed partial class OpinionBar : Control
    {
        public float Ours { get; set; }
        public float Theirs { get; set; }

        public override void _Draw()
        {
            float w = Size.X, h = Size.Y, mid = w / 2;
            DrawRect(new Rect2(0, 0, w, h), DashboardStyle.PlotSurface);
            DrawRect(new Rect2(0, 0, w, h), MenuStyle.Edge, false, 1);
            float x = mid + Ours / 100f * mid;
            Color color = Ours < 0 ? Danger.Lerp(Muted, 1f + Ours / 100f) : Known.Lerp(Muted, 1f - Ours / 100f);
            DrawRect(new Rect2(Math.Min(mid, x), 2, Math.Abs(x - mid), h - 4), color);
            DrawLine(new Vector2(mid, 0), new Vector2(mid, h), Muted, 1);
            float t = mid + Theirs / 100f * mid;
            DrawLine(new Vector2(t, -1), new Vector2(t, h + 1), ArtDirection.Cream, 2);
        }
    }
}
