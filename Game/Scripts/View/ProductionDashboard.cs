using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GodColony.Simulation.Colonies;
using GodColony.Simulation.Time;

namespace GodColony.View;

/// <summary>Répartition du travail, recettes réelles des ateliers et coûts mesurés.</summary>
public partial class ProductionDashboard : VBoxContainer
{
    private static readonly BuildingType[] Workshops = [BuildingType.Mill, BuildingType.Oven, BuildingType.Kiln,
        BuildingType.Bloomery, BuildingType.Forge, BuildingType.Loom, BuildingType.Market, BuildingType.Tavern, BuildingType.Cask, BuildingType.PotteryKiln, BuildingType.Tannery, BuildingType.Goldsmith];
    private Label _workers = null!, _workshops = null!, _emptyCosts = null!, _constructionCosts = null!;
    private WorkforceChart _chart = null!;
    private PanelContainer _costCard = null!;
    private GridContainer _sectorsGrid = null!, _recipesGrid = null!;
    private readonly Dictionary<WorkSector, (Label Count, Label Share, ProgressBar Bar)> _sectors = [];
    private readonly Dictionary<ResourceType, CostRow> _costs = [];
    private readonly List<WorkshopCard> _recipes = [];
    private readonly Dictionary<ResourceType, (Control Row, Label Amount)> _deliveries = [];
    private Label _deliveryState = null!;

    public override void _Ready()
    {
        Name = "TableauProduction"; Theme = MenuStyle.Theme();
        SizeFlagsHorizontal = SizeFlags.ExpandFill; AddThemeConstantOverride("separation", 10);
        var metrics = DashboardStyle.Row(this);
        _workers = DashboardStyle.Metric(metrics, "Main-d'œuvre", ResourceType.Tools, DashboardStyle.Ink);
        _workshops = DashboardStyle.Metric(metrics, "Ateliers construits", ResourceType.Iron, DashboardStyle.Gold);
        DashboardStyle.Text(this, "RÉPARTITION PRÉVUE DU TRAVAIL", 11, DashboardStyle.Gold);
        _chart = new WorkforceChart { CustomMinimumSize = new Vector2(0, 16), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        AddChild(_chart);
        _sectorsGrid = Grid(this);
        foreach (WorkSector sector in WorkSectors.All)
        {
            Color color = DashboardStyle.SectorColor(sector);
            var card = DashboardStyle.Card(_sectorsGrid, 8);
            var column = MenuStyle.Column(card, 4);
            var row = DashboardStyle.Row(column, 6);
            if (sector == WorkSector.Free) DashboardStyle.Picture(row, SpriteFactory.Sleep, 20);
            else DashboardStyle.Icon(row, DashboardStyle.SectorIcon(sector), 20);
            var title = DashboardStyle.Text(row, SectorTitle(sector), 12, color);
            title.SizeFlagsHorizontal = SizeFlags.ExpandFill; title.ClipText = true;
            title.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            var count = DashboardStyle.Text(row, "", 11, DashboardStyle.Muted);
            var share = DashboardStyle.Text(row, "", 12, color);
            share.CustomMinimumSize = new Vector2(40, 0); share.HorizontalAlignment = HorizontalAlignment.Right;
            var bar = DashboardStyle.Bar(column, color, 4);
            _sectors.Add(sector, (count, share, bar));
            card.TooltipText = "Pourcentage du temps de travail prévu et nombre d'habitants affectés à ce secteur.\nUne affectation ne signifie pas que l'habitant travaille à cet instant.";
        }
        var deliveryCard = DashboardStyle.Card(this);
        var deliveries = MenuStyle.Column(deliveryCard, 7);
        DashboardStyle.Text(deliveries, "LIVRAISONS LOCALES", 11, DashboardStyle.Gold);
        _deliveryState = DashboardStyle.Text(deliveries, "", 11, DashboardStyle.Muted, true);
        var deliveryFlow = new HFlowContainer(); deliveryFlow.AddThemeConstantOverride("h_separation", 14);
        deliveryFlow.AddThemeConstantOverride("v_separation", 6); deliveries.AddChild(deliveryFlow);
        foreach (ResourceType good in Enum.GetValues<ResourceType>().Where(g => g != ResourceType.Coins))
        {
            var row = DashboardStyle.Row(deliveryFlow, 5); DashboardStyle.Icon(row, good, 24);
            var amount = DashboardStyle.Text(row, "", 12, DashboardStyle.Mint);
            amount.Name = $"LivraisonLocale{good}";
            row.TooltipText = $"{ResourceIcons.Name(good)} transportés vers le stock ; ils ne sont pas encore disponibles.";
            _deliveries.Add(good, (row, amount));
        }
        DashboardStyle.Text(this, "MATIÈRES PREMIÈRES → PRODUITS", 11, DashboardStyle.Gold);
        _recipesGrid = Grid(this);
        foreach (BuildingType type in Workshops)
        {
            var card = new WorkshopCard { Workshop = type }; _recipesGrid.AddChild(card); _recipes.Add(card);
        }
        foreach (Recipe recipe in ExtendedIndustry.Recipes.GroupBy(r => r.Output).Select(g => g.First())
            .Where(r => r.Workshop is BuildingType.Bloomery or BuildingType.Forge or BuildingType.Loom or BuildingType.Cask))
        {
            var card = new WorkshopCard { Workshop = recipe.Workshop, Variant = recipe }; _recipesGrid.AddChild(card); _recipes.Add(card);
        }
        DashboardStyle.Text(this, "Les quantités correspondent à un lot. Les jauges de fabrication suivent les actions commencées.",
            11, DashboardStyle.Muted, true);
        DashboardStyle.Text(this, "COÛTS MESURÉS · HEURES PAR UNITÉ", 11, DashboardStyle.Gold);
        _emptyCosts = DashboardStyle.Text(this, "Les premières récoltes et fabrications feront apparaître les coûts mesurés, déplacements compris.",
            12, DashboardStyle.Muted, true);
        _costCard = DashboardStyle.Card(this);
        var costs = MenuStyle.Column(_costCard, 8);
        foreach (ResourceType good in Enum.GetValues<ResourceType>().Where(g => g != ResourceType.Coins))
        {
            var row = new CostRow { Good = good }; costs.AddChild(row); _costs.Add(good, row);
        }
        _constructionCosts = DashboardStyle.Text(this, "", 12, DashboardStyle.Muted, true);
        Resized += () => _sectorsGrid.Columns = _recipesGrid.Columns = Size.X >= 510 ? 2 : 1;
    }

    private static GridContainer Grid(Node parent)
    {
        var grid = new GridContainer { Columns = 2, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", 8); grid.AddThemeConstantOverride("v_separation", 8);
        parent.AddChild(grid); return grid;
    }

    private static string SectorTitle(WorkSector sector) => sector switch
    {
        WorkSector.Food => "Cueillette & pêche", WorkSector.Farm => "Agriculture", WorkSector.Wood => "Bois",
        WorkSector.Stone => "Pierre & minerai", WorkSector.Construction => "Construction",
        WorkSector.Craft => "Artisanat", _ => "Temps libre",
    };

    public void Refresh(Colony colony, long ticks)
    {
        Colonist[] workers = colony.Workers.ToArray();
        _workers.Text = workers.Length.ToString();
        _workshops.Text = colony.Buildings.Count(b => b.IsWorkshop && b.IsComplete).ToString();
        _chart.Set(colony.WorkShares);
        foreach (var (sector, view) in _sectors)
        {
            float share = colony.WorkShares[sector];
            view.Bar.Value = share; view.Share.Text = $"{share * 100:0} %";
            int people = workers.Count(w => w.Sector == sector);
            view.Count.Text = $"{people} pers.";
        }
        Colonist[] carriers = colony.PresentMembers.Concat(colony.Transients).Distinct()
            .Where(m => m.Colony == colony && m.Carrying is not null).ToArray();
        int toStock = carriers.Count(m => m.CarryingTo is null), toSites = carriers.Length - toStock;
        _deliveryState.Text = toStock + toSites == 0 ? "Aucune livraison en cours."
            : $"{toStock} habitant(s) vers le stock · {toSites} vers les chantiers";
        foreach (var (good, view) in _deliveries)
        {
            int amount = carriers.Where(m => m.CarryingTo is null && m.Carrying!.Value.Type == good)
                .Sum(m => m.Carrying!.Value.Amount);
            view.Row.Visible = amount > 0; view.Amount.Text = $"{amount} {Trade.GoodName(good, amount)}";
        }
        foreach (WorkshopCard card in _recipes) card.Refresh(colony, ticks);
        double maximum = _costs.Keys.Select(g => colony.Labor.HoursPerUnit(g) ?? 0).DefaultIfEmpty(0).Max();
        _emptyCosts.Visible = !_costs.Keys.Any(g => colony.Labor.HoursPerUnit(g) is not null);
        foreach (var (good, view) in _costs) view.Refresh(colony.Labor, maximum);
        _costCard.Visible = !_emptyCosts.Visible;
        var construction = new List<string>();
        if (colony.Labor.HoursPerHut is { } hut) construction.Add($"Hutte : {hut:0.0} h");
        if (colony.Labor.HoursPerCanalTile is { } canal) construction.Add($"Canal : {canal:0.0} h par case");
        _constructionCosts.Text = string.Join("   ·   ", construction); _constructionCosts.Visible = construction.Count > 0;
    }

    private sealed partial class WorkforceChart : Control
    {
        private float[] _shares = new float[WorkSectors.All.Length];
        public void Set(IReadOnlyDictionary<WorkSector, float> shares)
        {
            for (int i = 0; i < _shares.Length; i++) _shares[i] = Math.Max(0, shares[WorkSectors.All[i]]);
            TooltipText = string.Join("\n", WorkSectors.All.Select((s, i) => $"{SectorTitle(s)} : {_shares[i] * 100:0} %"));
            QueueRedraw();
        }
        public override void _Draw()
        {
            DrawRect(new Rect2(Vector2.Zero, Size), MenuStyle.Edge);
            float total = _shares.Sum(), x = 0;
            if (total <= 0) return;
            for (int i = 0; i < _shares.Length; i++)
            {
                float width = _shares[i] / total * Size.X;
                DrawRect(new Rect2(x, 0, width, Size.Y), DashboardStyle.SectorColor(WorkSectors.All[i]));
                x += width;
            }
        }
    }

    private sealed partial class WorkshopCard : PanelContainer
    {
        public BuildingType Workshop;
        public Recipe? Variant;
        private Label _count = null!, _state = null!, _inputs = null!;
        private HBoxContainer _flow = null!;
        private ProgressBar _progress = null!;
        private Recipe? _recipe;
        public override void _Ready()
        {
            Name = $"Recipe{Workshop}{Variant?.Output.ToString() ?? ""}"; SizeFlagsHorizontal = SizeFlags.ExpandFill;
            AddThemeStyleboxOverride("panel", MenuStyle.Box(new Color(0.09f, 0.16f, 0.135f), MenuStyle.Edge, 10));
            var column = MenuStyle.Column(this, 7); var heading = DashboardStyle.Row(column, 7);
            DashboardStyle.Picture(heading, SpriteFactory.BuildingSprite(Workshop.ToString()), 30);
            var title = DashboardStyle.Text(heading, Variant is null ? Building.NameOf(Workshop) : ResourceIcons.Name(Variant.Output), 13);
            title.SizeFlagsHorizontal = SizeFlags.ExpandFill; title.ClipText = true;
            title.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            _count = DashboardStyle.Text(heading, "", 11, DashboardStyle.Muted);
            _flow = DashboardStyle.Row(column, 7);
            _state = DashboardStyle.Text(column, "", 11, DashboardStyle.Muted, true);
            _state.Name = $"RecipeState{Workshop}{Variant?.Output.ToString() ?? ""}";
            _inputs = DashboardStyle.Text(column, "", 11, DashboardStyle.Gold, true);
            _progress = DashboardStyle.Bar(column, DashboardStyle.Mint, 4);
            _progress.Name = $"RecipeProgress{Workshop}{Variant?.Output.ToString() ?? ""}";
        }

        private void SetRecipe(Recipe recipe)
        {
            if (_recipe is not null && _recipe.Output == recipe.Output && _recipe.OutputAmount == recipe.OutputAmount
                && _recipe.Inputs.AsSpan().SequenceEqual(recipe.Inputs)) return;
            _recipe = recipe;
            foreach (Node child in _flow.GetChildren()) { _flow.RemoveChild(child); child.QueueFree(); }
            if (recipe.Inputs.Length == 0) DashboardStyle.Text(_flow, "Région", 12, DashboardStyle.Muted);
            for (int i = 0; i < recipe.Inputs.Length; i++)
            {
                if (i > 0) DashboardStyle.Text(_flow, "+", 14, DashboardStyle.Muted);
                Ingredient(_flow, recipe.Inputs[i].Type, recipe.Inputs[i].Amount, false);
            }
            DashboardStyle.Text(_flow, "→", 20, DashboardStyle.Gold);
            Ingredient(_flow, recipe.Output, recipe.OutputAmount, true);
        }

        private static void Ingredient(Node parent, ResourceType good, int amount, bool output)
        {
            var row = DashboardStyle.Row(parent, 4);
            row.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
            DashboardStyle.Icon(row, good, output ? 26 : 22);
            DashboardStyle.Text(row, amount.ToString(), output ? 16 : 13, output ? DashboardStyle.Ink : DashboardStyle.Muted);
            row.TooltipText = $"{amount} {Trade.GoodName(good, amount)}";
        }

        public void Refresh(Colony colony, long ticks)
        {
            Recipe recipe = Variant ?? (Workshop == BuildingType.Tavern ? Cuisine.StewRecipe : Workshop == BuildingType.Cask ? Cuisine.BeerRecipe : Crafting.RecipeFor(colony, Workshop));
            Activity? active = colony.PresentMembers.Select(m => m.Activity).FirstOrDefault(a => a?.Building?.Type == Workshop && a.InputsTaken && (Variant is null || a.CommittedRecipe?.Output == Variant.Output));
            if (active?.CommittedRecipe is { } committed) recipe = committed;
            SetRecipe(recipe);
            int count = colony.Buildings.Count(b => b.Type == Workshop && b.IsComplete);
            bool planned = colony.Buildings.Any(b => b.Type == Workshop && !b.IsComplete);
            _count.Text = count > 0 ? $"×{count}" : planned ? "En chantier" : "À construire";
            if (Workshop == BuildingType.Market && count > 0)
            {
                int extensions = colony.Buildings.Count(b => b.Type == Workshop && b.IsExtension && b.IsComplete);
                _count.Text = $"×{count - extensions}" + (extensions > 0 ? $" · {extensions} extension(s)" : "")
                    + (planned ? " · agrandissement en chantier" : "");
            }
            Activity[] jobs = colony.PresentMembers.Select(m => m.Activity).OfType<Activity>()
                .Where(a => a.Kind == ActivityKind.Craft && a.Building?.Type == Workshop && (Variant is null || a.Product == Variant.Output)).ToArray();
            Activity[] started = jobs.Where(a => a.Started).ToArray();
            var missing = ToolChain.MissingInputs(colony, recipe);
            Color color;
            if (started.Length > 0)
            {
                _state.Text = $"{started.Length} lot{(started.Length > 1 ? "s" : "")} en fabrication";
                color = DashboardStyle.Mint;
            }
            else if (jobs.Length > 0) { _state.Text = $"{jobs.Length} artisan{(jobs.Length > 1 ? "s" : "")} en trajet"; color = DashboardStyle.Gold; }
            else if (count == 0) { _state.Text = planned ? "Atelier en construction" : "Atelier non construit"; color = DashboardStyle.Muted; }
            else if (missing.Count > 0)
            {
                _state.Text = "Manque : " + string.Join(", ", missing.Select(i => ResourceIcons.Name(i.Type)));
                color = DashboardStyle.Warning;
            }
            else { _state.Text = "Matières disponibles"; color = DashboardStyle.Mint; }
            Building[] brewing = Workshop == BuildingType.Cask
                ? colony.Buildings.Where(b => b.Type == Workshop && b.IsComplete && b.IsBrewing && (Variant is null || (b.BrewProduct ?? ResourceType.Beer) == Variant.Output)).ToArray() : [];
            if (brewing.Length > 0)
            {
                double days = Math.Max(0, brewing.Min(b => b.BrewReadyTicks) - ticks) / (double)TimeConstants.TicksPerDay;
                string state = days > 0 ? $"Prochain fût prêt dans {days:0.0} j"
                    : Civic.Has(colony, BuildingType.Tavern) ? "Prêt à être tiré" : "Prêt · attend une taverne";
                _state.Text = $"{brewing.Length} fût(s) · {brewing.Sum(b => b.BrewProduct == ResourceType.Wine ? 2 : Cuisine.BeerRecipe.OutputAmount)} chopes\n{state}"
                    + (started.Length > 0 ? $"\n{started.Length} lot(s) en préparation" : "");
                color = days > 0 ? DashboardStyle.Gold : DashboardStyle.Mint;
            }
            _state.AddThemeColorOverride("font_color", color);
            var engaged = started.Where(a => a.InputsInventory is not null).SelectMany(a => Enum.GetValues<ResourceType>()
                .Select(g => (Good: g, Amount: a.InputsInventory!.Get(g)))).Where(p => p.Amount > 0)
                .GroupBy(p => p.Good).Select(g => $"{g.Sum(p => p.Amount)} {Trade.GoodName(g.Key)}");
            _inputs.Text = string.Join(" · ", engaged);
            _inputs.Visible = _inputs.Text.Length > 0;
            if (_inputs.Visible) _inputs.Text = "Intrants engagés : " + _inputs.Text;
            _progress.Visible = started.Length > 0 || brewing.Length > 0;
            _progress.Value = brewing.Length > 0
                ? brewing.Average(b => Math.Clamp(1 - (b.BrewReadyTicks - ticks)
                    / (double)((b.BrewProduct == ResourceType.Wine ? 3 : Cuisine.BrewDays) * TimeConstants.TicksPerDay), 0, 1))
                : started.Length == 0 ? 0 : started.Average(a => a.DurationTicks > 0 ? Math.Clamp(a.ElapsedTicks / a.DurationTicks, 0, 1) : 0);
            _progress.TooltipText = brewing.Length > 0 ? "Avancement moyen de la fermentation des fûts remplis." : "Avancement moyen des fabrications commencées.";
            TooltipText = $"{Building.NameOf(Workshop)} · un lot produit {recipe.OutputAmount} {Trade.GoodName(recipe.Output, recipe.OutputAmount)}\n"
                + string.Join("\n", recipe.Inputs.Select(i => $"{ResourceIcons.Name(i.Type)} : {colony.Stock.Get(i.Type)} en stock, {i.Amount} nécessaires"))
                + "\nLes réserves protégées et les objectifs de la colonie décident du lancement d'une fabrication.";
        }
    }

    private sealed partial class CostRow : VBoxContainer
    {
        public ResourceType Good;
        private Label _value = null!, _produced = null!;
        private ProgressBar _bar = null!;
        public override void _Ready()
        {
            Name = $"ProductionCost{Good}"; AddThemeConstantOverride("separation", 3);
            var row = DashboardStyle.Row(this, 7); DashboardStyle.Icon(row, Good, 20);
            DashboardStyle.Text(row, ResourceIcons.Name(Good), 12);
            DashboardStyle.Spacer(row);
            _produced = DashboardStyle.Text(row, "", 11, DashboardStyle.Muted);
            _value = DashboardStyle.Text(row, "", 13, DashboardStyle.Gold);
            _bar = DashboardStyle.Bar(this, DashboardStyle.Gold, 4);
            TooltipText = "Coût récent d'une unité, déplacements compris. Les barres comparent les heures de travail nécessaires ; la quantité produite est le cumul depuis la fondation.";
        }
        public void Refresh(LaborLedger labor, double maximum)
        {
            double? cost = labor.HoursPerUnit(Good); Visible = cost is not null;
            if (cost is null) return;
            _value.Text = $"{cost:0.0} h"; _produced.Text = $"{labor.TotalProduced(Good):N0} produits";
            _bar.Value = maximum > 0 ? cost.Value / maximum : 0;
        }
    }
}
