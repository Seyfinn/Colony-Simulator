using GodColony.Simulation.Colonies;
using GodColony.Simulation.Persistence;
using GodColony.Simulation.Time;

namespace GodColony.Simulation.Tests;

/// <summary>Offrandes : matériaux réellement livrés, travail, monument unique, souhaits libres et sans faux exaucement.</summary>
public sealed class OfferingTests
{
    private static (WorldState World, Colony Colony) Prepared(bool shrine = true)
    {
        var world = new WorldState(11, startingColonists: 18, migration: false, lifecycle: false, trade: false);
        Colony colony = world.Colonies[0];
        Knowledge.Grant(colony, [Discovery.Masonry], world.Clock.Ticks);
        colony.Stock.Add(ResourceType.Stone, 300);
        colony.Stock.Add(ResourceType.Wood, 300);
        colony.Stock.Add(ResourceType.Food, 5000);
        foreach (Colonist c in colony.Members) c.Sector = WorkSector.Craft;
        if (shrine)
        {
            (int x, int y) = Urbanism.FindSite(colony.Map, colony, BuildingType.Shrine)!.Value;
            Urbanism.PlanBuilding(colony.Map, colony, BuildingType.Shrine, x, y).Progress = 1f;
        }
        foreach (FieldPlot plot in colony.Fields.SelectMany(f => f.Plots)) plot.Stage = CropStage.Growing;
        return (world, colony);
    }

    private static void Run(WorldState world, int hours)
    {
        for (int i = 0; i < hours * TimeConstants.TicksPerHour; i++) world.Step();
    }

    /// <summary>Avance heure par heure jusqu'à la condition (au plus <paramref name="maxHours"/> heures).</summary>
    private static void RunUntil(WorldState world, Func<bool> done, int maxHours = 24 * 20)
    {
        for (int hour = 0; hour < maxHours && !done(); hour++) Run(world, 1);
    }

    [Fact]
    public void Un_autel_est_livre_travaille_puis_dresse_une_seule_fois()
    {
        var (world, colony) = Prepared();
        RunUntil(world, () => colony.Monuments.Count > 0);
        OfferingProject project = Assert.Single(colony.Offerings);
        Assert.Equal(OfferingModel.SimpleAltar, project.Model);
        Assert.Equal(OfferingProjectState.Completed, project.State);
        Monument monument = Assert.Single(colony.Monuments);
        Assert.Equal(20, monument.Materials[ResourceType.Stone]);
        Assert.Equal(10, monument.Materials[ResourceType.Wood]);
        // Les matériaux sont incorporés une seule fois : le projet ne les détient plus.
        Assert.Empty(project.Delivered);
        // Le modèle n'est pas relancé aussitôt : le même investissement n'est pas présenté comme une nouvelle offrande.
        Run(world, 24 * 40);
        Assert.Single(colony.Monuments);
    }

    [Fact]
    public void Les_materiaux_livres_ne_sont_plus_vendables_et_la_crise_suspend_le_projet()
    {
        var (world, colony) = Prepared();
        RunUntil(world, () => colony.Offerings.Any(p => p.State == OfferingProjectState.Building));
        OfferingProject project = Assert.Single(colony.Offerings);
        // Les matériaux livrés ont quitté le stock disponible : le projet les détient et rien ne les revend.
        Assert.Equal(20, project.Delivered[ResourceType.Stone]);
        Assert.Equal(10, project.Delivered[ResourceType.Wood]);
        // Une disette gèle l'offrande, qui reprend ensuite son état.
        colony.Stock.TryTake(ResourceType.Food, colony.Stock.Get(ResourceType.Food), ResourceFlow.Loss);
        colony.Stock.TryTake(ResourceType.Grain, colony.Stock.Get(ResourceType.Grain), ResourceFlow.Loss);
        Run(world, 3);
        Assert.Equal(OfferingProjectState.Suspended, project.State);
        Assert.NotNull(project.BlockedReason);
        Assert.Equal(20, project.Delivered[ResourceType.Stone]);
        colony.Stock.Add(ResourceType.Food, 5000);
        Run(world, 3);
        Assert.NotEqual(OfferingProjectState.Suspended, project.State);
    }

    [Fact]
    public void Un_souhait_n_est_jamais_accorde_d_office_ni_marque_exauce_sans_effet()
    {
        var (world, colony) = Prepared();
        colony.Prayers.AutoApprove.Add(DecisionKind.Wish);
        RunUntil(world, () => colony.Wishes.Count > 0);
        DivineWish wish = Assert.Single(colony.Wishes);
        Prayer prayer = Assert.Single(colony.Prayers.All, p => p.Kind == DecisionKind.Wish);
        Assert.Equal(PrayerStatus.Pending, prayer.Status); // pas d'accord d'office
        Assert.Equal(wish.Id, prayer.WishId);
        // Ignorer n'a aucun effet.
        Run(world, 2);
        Assert.Equal(DivineWishStatus.AwaitingResponse, wish.Status);
        // Accorder applique le pouvoir à sa cible : l'exaucement naît avec un effet réel, jamais sans.
        world.AnswerPrayer(prayer, approve: true);
        Assert.NotNull(wish.AcceptedTicks);
        Assert.Equal(DivineWishStatus.Fulfilled, wish.Status);
        DivineEffect effect = Assert.Single(colony.DivineEffects);
        Assert.Equal(wish.Id, effect.WishId);
        // Un appel externe ne peut pas fabriquer un second exaucement.
        Assert.False(DivineWishes.TryFulfill(colony, wish, effect));
    }

    [Fact]
    public void Un_refus_est_enregistre_et_une_cible_disparue_invalide_le_souhait_sans_toucher_au_monument()
    {
        var (world, colony) = Prepared();
        RunUntil(world, () => colony.Wishes.Count > 0);
        DivineWish wish = Assert.Single(colony.Wishes);
        Prayer prayer = Assert.Single(colony.Prayers.All, p => p.Kind == DecisionKind.Wish);
        // La récolte visée passe : le souhait devient sans objet, la prière est retirée sans réponse, le monument reste.
        foreach (FieldPlot plot in colony.Fields.SelectMany(f => f.Plots)) plot.Stage = CropStage.Fallow;
        Run(world, 24 * 2);
        Assert.Equal(DivineWishStatus.TargetInvalid, wish.Status);
        Assert.Equal(PrayerStatus.Withdrawn, prayer.Status);
        Assert.Single(colony.Monuments);

        var (world2, colony2) = Prepared();
        RunUntil(world2, () => colony2.Wishes.Count > 0);
        Prayer asked = Assert.Single(colony2.Prayers.All, p => p.Kind == DecisionKind.Wish);
        world2.AnswerPrayer(asked, approve: false);
        Assert.Equal(DivineWishStatus.Refused, Assert.Single(colony2.Wishes).Status);
        Assert.Single(colony2.Monuments);
    }

    [Fact]
    public void Un_modele_prestigieux_exige_les_trois_pierres_et_son_projet_incorpore_les_pierres_reellement_livrees()
    {
        Assert.Contains(OfferingTemplate.All, t => new[] { ResourceType.Ruby, ResourceType.Sapphire, ResourceType.Emerald }.All(g => t.Materials.Any(m => m.Type == g)));
        var (world, colony) = Prepared();
        // Un peu plus que le strict nécessaire : l'orfèvrerie du village peut prélever un bijou entre-temps.
        colony.Stock.Add(ResourceType.Gold, 8);
        colony.Stock.Add(ResourceType.Emerald, 4);
        colony.Stock.Add(ResourceType.Sapphire, 4);
        colony.Stock.Add(ResourceType.Coins, 2000);
        RunUntil(world, () => colony.Monuments.Any(m => m.Model == OfferingModel.SimpleAltar), 24 * 20);
        // La statue des récoltes ne se lance que si ses fournisseurs sont plausibles : ici les pierres sont déjà en stock.
        RunUntil(world, () => colony.Monuments.Any(m => m.Model == OfferingModel.HarvestStatue), 24 * 90);
        Monument statue = Assert.Single(colony.Monuments, m => m.Model == OfferingModel.HarvestStatue);
        Assert.Equal(1, statue.Materials[ResourceType.Emerald]);
        Assert.InRange(colony.Stock.Get(ResourceType.Emerald), 0, 3); // une des quatre pierres est incorporée : elle ne peut plus être vendue
        Assert.Empty(colony.Offerings.Single(p => p.Model == OfferingModel.HarvestStatue).Delivered);
        Assert.Equal(1, colony.Monuments.Count(m => m.Model == OfferingModel.HarvestStatue));
    }

    [Fact]
    public void Un_projet_sans_sanctuaire_finit_abandonne_et_rend_ses_apports()
    {
        var (world, colony) = Prepared(shrine: false);
        Run(world, 24 * 2);
        Assert.Single(colony.Offerings);
        OfferingProject project = colony.Offerings[0];
        project.Delivered[ResourceType.Stone] = 5; // des apports déjà livrés
        colony.Stock.TryTake(ResourceType.Stone, 5, ResourceFlow.Transfer);
        int stone = colony.Stock.Get(ResourceType.Stone);
        // Le sanctuaire ne vient pas : on dépasse la patience.
        for (int i = 0; i < (Offerings.PatienceDays + 2) * 24 && project.IsActive; i++) Run(world, 1);
        if (project.State == OfferingProjectState.Cancelled)
        {
            Assert.Empty(project.Delivered);
            Assert.True(colony.Stock.Get(ResourceType.Stone) >= stone + 5 - 1);
            Assert.NotNull(project.BlockedReason);
        }
    }

    [Fact]
    public void Le_projet_en_chantier_et_le_souhait_reprennent_exactement_apres_sauvegarde()
    {
        var (world, colony) = Prepared();
        Run(world, 24 * 2);
        string path = Path.Combine(Path.GetTempPath(), "GodColony-offrande-" + Guid.NewGuid().ToString("N") + ".gcsave");
        try
        {
            WorldSave.Save(path, world);
            WorldState loaded = WorldSave.Load(path).World;
            Assert.Null(new WorldComparison().Difference(world, loaded));
            Run(world, 24 * 12); Run(loaded, 24 * 12);
            Assert.Equal(world.Colonies[0].Monuments.Count, loaded.Colonies[0].Monuments.Count);
            Assert.Null(new WorldComparison().Difference(world, loaded));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
