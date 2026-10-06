using GodColony.Simulation.Colonies;
using GodColony.Simulation.Generation;
using GodColony.Simulation.Map;
using GodColony.Simulation.World;

namespace GodColony.Simulation.Tests;

/// <summary>Finitions des filières : vigne et climat, sources de négoce identifiables, gemmes complémentaires.</summary>
public sealed class FinishingTests
{
    [Fact]
    public void La_vigne_craint_le_froid_la_secheresse_et_l_humidite_excessive()
    {
        var world = new WorldState(42, startingColonists: 8, migration: false, lifecycle: false, trade: false);
        LocalMap map = world.Colonies[0].Map;
        Biome original = map.Biome;
        try
        {
            map.Biome = Biome.TemperateForest;
            float temperate = Farming.VineSuitability(map, 10, 10);
            map.Biome = Biome.Tundra;
            float cold = Farming.VineSuitability(map, 10, 10);
            map.Biome = Biome.Desert;
            float dry = Farming.VineSuitability(map, 10, 10);
            map.Biome = Biome.TropicalForest;
            float damp = Farming.VineSuitability(map, 10, 10);
            Assert.True(temperate >= Farming.MinVineSuitability);
            Assert.True(cold < Farming.MinVineSuitability, "Le froid de la toundra interdit la vigne.");
            Assert.True(dry < Farming.MinVineSuitability, "Un désert non irrigué aussi.");
            Assert.True(damp < temperate, "L'humidité de la jungle nuit à la vigne.");
        }
        finally { map.Biome = original; }
    }

    [Fact]
    public void Les_sources_de_negoce_viennent_de_l_environnement_et_la_denree_habituelle_existe_toujours()
    {
        var world = new WorldState(42, startingColonists: 8, migration: false, lifecycle: false, trade: false);
        int checkedCoast = 0, checkedForest = 0;
        foreach (WorldTile tile in world.WorldMap.Grid.Tiles.Where(t => t.Habitable).Take(50))
        {
            RegionState region = world.VisitRegion(tile.Index);
            ResourceType usual = Specialties.NativeOf(region.Map.Biome);
            // La denrée habituelle existe, à moins que sa plante ou son arbre manque sur la carte.
            if (region.Deposits.All(d => d.Material != usual))
                Assert.True(region.Map.Biome is Biome.TemperateForest or Biome.BorealForest or Biome.TropicalForest or Biome.Grassland or Biome.Savanna or Biome.Swamp,
                    "Seules les denrées qui dépendent d'une plante ou d'un arbre peuvent manquer.");
            if (tile.Coastal) { checkedCoast++; Assert.Contains(region.Deposits, d => d.Material == ResourceType.Salt); }
            if (region.Map.Biome is Biome.Desert or Biome.Steppe or Biome.Tundra) Assert.Contains(region.Deposits, d => d.Material == ResourceType.Salt);
            if (region.Map.Biome is Biome.Desert or Biome.Tundra) Assert.DoesNotContain(region.Deposits, d => d.Material == ResourceType.Hardwood);
            if (region.Map.Biome is Biome.TemperateForest or Biome.BorealForest) { checkedForest++; Assert.DoesNotContain(region.Deposits, d => d.Material == ResourceType.Spices); }
        }
        Assert.True(checkedCoast + checkedForest > 0, "L'échantillon couvre des littoraux ou des forêts.");
    }

    [Fact]
    public void La_denree_locale_suit_les_gites_connus_de_la_region()
    {
        var world = new WorldState(42, startingColonists: 8, migration: false, lifecycle: false, trade: false);
        Colony colony = world.Colonies[0];
        ResourceType usual = Specialties.NativeOf(colony.Map.Biome);
        ResourceType other = Specialties.Goods.First(g => g != usual);
        colony.DepositReports.RemoveAll(k => Specialties.Goods.Contains(k.Material));
        colony.DepositReports.Add(new DepositKnowledge { SiteId = 920001, Region = colony.PrimarySettlement.RegionTileIndex, Material = other, State = DepositObservation.Surveyed });
        Assert.Equal(other, Specialties.NativeOf(colony)); // seule source connue de la région
        colony.DepositReports.Add(new DepositKnowledge { SiteId = 920002, Region = colony.PrimarySettlement.RegionTileIndex, Material = usual, State = DepositObservation.Surveyed });
        Assert.Equal(usual, Specialties.NativeOf(colony)); // la denrée habituelle reprend la première place quand elle est connue
    }

    [Fact]
    public void Les_gemmes_sont_rares_par_province_complementaires_et_le_diamant_minoritaire()
    {
        var world = new WorldState(42, startingColonists: 8, migration: false, lifecycle: false, trade: false);
        var gemRegions = new List<(int Tile, ResourceType Gem)>();
        foreach (WorldTile tile in world.WorldMap.Grid.Tiles.Where(t => t.Habitable).Take(80))
        {
            var gems = world.VisitRegion(tile.Index).Deposits.Where(d => d.Material is ResourceType.Ruby or ResourceType.Sapphire or ResourceType.Emerald or ResourceType.Diamond).ToList();
            Assert.True(gems.Count <= 1, "Aucune région n'a toutes les pierres : une seule pierre par région.");
            if (gems.Count == 1) gemRegions.Add((tile.Index, gems[0].Material));
        }
        Assert.NotEmpty(gemRegions);
        Assert.True(gemRegions.Count < 60, "Les régions à gemmes sont minoritaires.");
        Assert.True(gemRegions.Count(g => g.Gem == ResourceType.Diamond) * 3 <= gemRegions.Count, "Le diamant reste minoritaire.");
        Assert.True(gemRegions.Select(g => g.Gem).Where(g => g != ResourceType.Diamond).Distinct().Count() >= 2, "Plusieurs pierres coexistent sur la carte : un grand réseau commercial les réunit.");
    }
}
