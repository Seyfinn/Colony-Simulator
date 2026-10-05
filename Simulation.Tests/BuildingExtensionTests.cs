using GodColony.Simulation.Colonies;
using GodColony.Simulation.Map;
using GodColony.Simulation.Persistence;
using GodColony.Simulation.Time;

namespace GodColony.Simulation.Tests;

public sealed class BuildingExtensionTests
{
    private static (WorldState Monde, Colony Colonie, Building Principal) Village(BuildingType type)
    {
        var monde = new WorldState(12345, 128, 128, startingColonists: 20, migration: false, lifecycle: false, trade: false);
        Colony colonie = monde.Colonies[0];
        for (int y = 0; y < colonie.Map.Height; y++)
        for (int x = 0; x < colonie.Map.Width; x++)
            colonie.Map.SetGenerated(x, y, 5, SoilType.Grass, FloraType.None, 0, .5f);
        foreach (Building chantier in colonie.ConstructionSites.ToArray()) colonie.Buildings.Remove(chantier);
        colonie.Stock.Add(ResourceType.Food, 10000);
        colonie.Stock.Add(ResourceType.Wood, 2000);
        colonie.Stock.Add(ResourceType.Stone, 2000);
        colonie.Stock.Add(ResourceType.Grain, 1000);
        colonie.Stock.Add(ResourceType.Tools, 100);
        Building principal = Assert.IsType<Building>(Urbanism.BuildInstantly(colonie.Map, colonie, type));
        return (monde, colonie, principal);
    }

    [Theory]
    [InlineData(BuildingType.Pen, 15, 0)]
    [InlineData(BuildingType.Market, 21, 9)]
    public void Les_habitants_livrent_et_batissent_une_extension_sans_fermer_le_batiment(BuildingType type, int bois, int pierre)
    {
        var (monde, colonie, principal) = Village(type);
        Building extension = Assert.IsType<Building>(SettlementPlanner.PlanExtension(colonie, type));
        Assert.Equal(principal.Id, extension.ExtensionOfId);
        Assert.Equal((2, 3, bois, pierre), (extension.Width, extension.Height, extension.WoodRequired, extension.StoneRequired));
        Assert.True(principal.IsComplete);
        Assert.Null(SettlementPlanner.PlanExtension(colonie, type));
        Assert.Empty(extension.Tiles.Intersect(colonie.Buildings.Where(b => b != extension).SelectMany(b => b.Tiles)));
        Assert.All(colonie.Buildings.Where(b => b.HasDoor), b => Assert.False(extension.Contains(b.AccessX, b.AccessY)));
        Assert.NotNull(colonie.Pathfinder.FindPath(colonie.CampX, colonie.CampY, extension.AccessX, extension.AccessY));
        PlotReservation parcelle = Assert.IsType<PlotReservation>(colonie.Layout.ParcelById(extension.ParcelId));
        Assert.Equal((2, 3), (parcelle.Width, parcelle.Height));
        Assert.Equal(principal.DistrictId, extension.DistrictId);
        Assert.Equal(type == BuildingType.Pen ? 8 : 0, Husbandry.Capacity(colonie));
        int chargeAvant = Trade.CapacityOf(colonie, colonie);
        for (int tick = 0; tick < 40 * TimeConstants.TicksPerDay && !extension.IsComplete; tick++) monde.Step();
        Assert.True(extension.IsComplete, $"Extension inachevée : {extension.Progress:P0}, bois {extension.WoodDelivered}, pierre {extension.StoneDelivered}.");
        Assert.Equal((bois, pierre, 0, 0), (extension.WoodDelivered, extension.StoneDelivered, extension.WoodInTransit, extension.StoneInTransit));
        if (type == BuildingType.Pen)
            Assert.Equal((12, 12, 6), (Husbandry.Capacity(colonie), Husbandry.CapacityOf(colonie, ResourceType.Sheep), Husbandry.CapacityOf(colonie, ResourceType.Cows)));
        else
            Assert.Equal(chargeAvant + 9, Trade.CapacityOf(colonie, colonie));
        Assert.Contains(colonie.Thoughts, t => t.Text.Contains(type == BuildingType.Pen ? "L'enclos est agrandi" : "Le marché est agrandi"));
    }

    [Fact]
    public void Les_ajouts_sont_progressifs_et_ne_remplacent_pas_les_betes_existantes()
    {
        var (monde, colonie, principal) = Village(BuildingType.Pen);
        Husbandry.OnPenBuilt(colonie, monde.Clock);
        int troupeau = Husbandry.Animals(colonie);
        for (int ajout = 1; ajout <= SettlementPlanner.MaxExtensions; ajout++)
        {
            Building extension = Assert.IsType<Building>(SettlementPlanner.PlanExtension(colonie, BuildingType.Pen));
            Assert.Contains(colonie.Buildings.Where(b => b != extension && (b == principal || b.ExtensionOfId == principal.Id)),
                voisin => PlacementChecks.Gap(voisin.X, voisin.Y, voisin.Width, voisin.Height, extension.X, extension.Y, 2, 3) == 0);
            extension.Progress = 1;
            ColonyBrain.OnBuildingComplete(colonie, extension, colonie.Map, monde.Clock);
            Assert.Equal(8 + ajout * 4, Husbandry.Capacity(colonie));
            Assert.Equal(troupeau, Husbandry.Animals(colonie));
        }
        Assert.Null(SettlementPlanner.PlanExtension(colonie, BuildingType.Pen));
    }

    [Fact]
    public void Un_agrandissement_refuse_un_terrain_sans_place_et_ne_reserve_rien()
    {
        var (_, colonie, principal) = Village(BuildingType.Pen);
        for (int y = principal.Y - 3; y < principal.Y + principal.Height + 3; y++)
        for (int x = principal.X - 2; x < principal.X + principal.Width + 2; x++)
            if (!principal.Contains(x, y)) colonie.Map.SetGenerated(x, y, 5, SoilType.Grass, FloraType.Tree, 1);
        int batiments = colonie.Buildings.Count, parcelles = colonie.Layout.Parcels.Count;
        Assert.Null(SettlementPlanner.PlanExtension(colonie, BuildingType.Pen));
        Assert.Equal((batiments, parcelles), (colonie.Buildings.Count, colonie.Layout.Parcels.Count));
    }

    [Fact]
    public void Un_marche_ne_s_agrandit_que_si_le_village_et_les_echanges_le_demandent()
    {
        var (_, colonie, _) = Village(BuildingType.Market);
        Knowledge.Grant(colonie, [Discovery.Commerce], 0);
        Assert.False(SettlementPlanner.WantsMarketExtension(colonie));
        colonie.ExportInterest[Specialties.NativeOf(colonie)] = 10;
        Assert.True(SettlementPlanner.WantsMarketExtension(colonie));
        Building extension = Assert.IsType<Building>(SettlementPlanner.PlanExtension(colonie, BuildingType.Market));
        extension.Progress = 1;
        Assert.False(SettlementPlanner.WantsMarketExtension(colonie));
    }

    [Fact]
    public void Un_chantier_d_extension_se_charge_et_continue_de_facon_identique()
    {
        var (monde, colonie, principal) = Village(BuildingType.Market);
        Building extension = Assert.IsType<Building>(SettlementPlanner.PlanExtension(colonie, BuildingType.Market));
        extension.WoodDelivered = 7;
        extension.Progress = .25f;
        string fichier = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".gcsave");
        try
        {
            WorldSave.Save(fichier, monde);
            WorldState charge = WorldSave.Load(fichier).World;
            Building copie = Assert.Single(charge.Colonies[0].Buildings, b => b.IsExtension);
            Assert.Equal((principal.Id, 2, 3, 7, .25f), (copie.ExtensionOfId, copie.Width, copie.Height, copie.WoodDelivered, copie.Progress));
            for (int tick = 0; tick < 800; tick++) { monde.Step(); charge.Step(); }
            Assert.Null(new WorldComparison().Difference(monde, charge));
        }
        finally { File.Delete(fichier); }
    }

    [Theory]
    [InlineData(BuildingType.Pen)]
    [InlineData(BuildingType.Market)]
    public void La_colonie_decide_d_agrandir_son_batiment_quand_le_besoin_apparait(BuildingType type)
    {
        var (monde, colonie, principal) = Village(type);
        Knowledge.Grant(colonie, [Discovery.Husbandry, Discovery.Commerce], 0);
        while (colonie.Buildings.Count(b => b.IsHut) * Building.HutCapacity < colonie.PresentMembers.Count + 4)
            Assert.NotNull(Urbanism.BuildInstantly(colonie.Map, colonie, BuildingType.Hut));
        colonie.FillVacancies();
        // Les autres besoins sont déjà pourvus : on observe la décision d'agrandir, sans attendre toute une progression technologique.
        int rang = 0;
        foreach (BuildingType service in Enum.GetValues<BuildingType>().Where(t => t != type && t is not (BuildingType.Hut or BuildingType.Dam)))
        {
            var batiment = new Building(service, 4 + rang % 8 * 10, 4 + rang / 8 * 8) { Progress = 1 };
            colonie.Buildings.Add(batiment);
            SettlementPlanner.Adopt(colonie, batiment);
            rang++;
        }
        if (type == BuildingType.Pen)
        {
            colonie.Chickens = Husbandry.PenCapacity;
            colonie.Stock.Add(ResourceType.Chickens, Husbandry.MaxSpareAnimals);
        }
        else colonie.ExportInterest[Specialties.NativeOf(colonie)] = 10;
        for (int heure = 0; heure < 8 && !colonie.Buildings.Any(b => b.IsExtension); heure++)
        {
            ColonyBrain.Think(colonie, colonie.Map, monde.Clock);
            SettlementPlanningScheduler.Drain(monde);
        }
        Building extension = Assert.Single(colonie.Buildings, b => b.IsExtension);
        Assert.Equal((type, principal.Id), (extension.Type, extension.ExtensionOfId));
        Assert.True(principal.IsComplete);
        Assert.False(extension.IsComplete);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public void Une_sauvegarde_refuse_une_extension_dont_le_lien_est_invalide(int identifiant)
    {
        var (monde, colonie, _) = Village(BuildingType.Market);
        Building extension = Assert.IsType<Building>(SettlementPlanner.PlanExtension(colonie, BuildingType.Market));
        extension.ExtensionOfId = identifiant;
        string fichier = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".gcsave");
        try
        {
            WorldSave.Save(fichier, monde);
            Assert.Throws<InvalidDataException>(() => WorldSave.Load(fichier));
        }
        finally { File.Delete(fichier); }
    }
}
