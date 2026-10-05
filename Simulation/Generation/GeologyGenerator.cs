using GodColony.Simulation.Colonies;
using GodColony.Simulation.Map;
using GodColony.Simulation.World;

namespace GodColony.Simulation.Generation;

/// <summary>Un tirage propre à la région : aucun prélèvement dans les générateurs des habitants ou de la politique.</summary>
internal static class GeologyGenerator
{
    /// <summary>Le fer, l'argile et les denrées de surface affleurent ; les métaux nobles et les pierres précieuses se cachent sous terre.</summary>
    private static int DepthOf(ResourceType material, bool mountain) => material switch
    {
        ResourceType.MineralCoal => mountain ? 0 : 1,
        ResourceType.CopperOre => mountain ? 1 : 2,
        ResourceType.GoldOre => mountain ? 2 : 3,
        ResourceType.Ruby or ResourceType.Sapphire or ResourceType.Emerald or ResourceType.Diamond => 3,
        _ => 0,
    };

    /// <summary>
    /// La pierre précieuse d'une région à gemmes : le monde est partagé en provinces de quelques cases, chacune riche d'une pierre (rubis, saphir ou émeraude),
    /// avec de rares exceptions. Une grande zone commerciale réunit donc plusieurs pierres sans qu'aucune région ne les ait toutes ; le diamant reste exceptionnel.
    /// </summary>
    internal static ResourceType GemOf(int seed, WorldTile tile, Random random)
    {
        ResourceType[] zones = [ResourceType.Ruby, ResourceType.Sapphire, ResourceType.Emerald];
        int zone = (tile.Col / 5 + tile.Row / 5 + Math.Abs(seed % 3)) % 3;
        int roll = random.Next(12);
        return roll == 0 ? ResourceType.Diamond : roll == 1 ? zones[(zone + 1) % 3] : zones[zone];
    }

    internal static List<Deposit> Generate(int seed, WorldTile worldTile, LocalMap map, bool permanentIron)
    {
        int tile = worldTile.Index;
        Relief relief = worldTile.Relief;
        var random = new Random(unchecked(seed * 397 ^ tile * 7919 ^ 0x4A173));
        (int cx, int cy) = ColonyFounder.FindCampSite(map);
        var materials = new List<ResourceType> { ResourceType.IronOre, ResourceType.Clay };
        // Les denrées de négoce viennent de sources identifiables : marais salants (littoral, steppes, déserts), plantes d'épices (régions chaudes et humides),
        // essences des forêts. La denrée habituelle du biome existe toujours, même quand son environnement précis manque.
        Biome biome = map.Biome;
        if (worldTile.Coastal || biome is Biome.Desert or Biome.Steppe or Biome.Tundra or Biome.IceSheet) materials.Add(ResourceType.Salt);
        if (biome is Biome.Savanna or Biome.Swamp or Biome.TropicalForest or Biome.Grassland) materials.Add(ResourceType.Spices);
        if (biome is Biome.TemperateForest or Biome.BorealForest or Biome.TropicalForest) materials.Add(ResourceType.Hardwood);
        ResourceType native = Specialties.NativeOf(biome);
        if (!materials.Contains(native)) materials.Add(native);
        bool mountain = relief == Relief.Mountains;
        if (mountain || random.Next(3) == 0) materials.Add(ResourceType.MineralCoal);
        if (random.Next(mountain ? 2 : 3) == 0) materials.Add(ResourceType.CopperOre);
        if (random.Next(mountain ? 5 : 8) == 0) materials.Add(ResourceType.GoldOre);
        if (random.Next(mountain ? 4 : 15) == 0) materials.Add(GemOf(seed, worldTile, random));
        var result = new List<Deposit>();
        foreach (ResourceType material in materials)
        {
            var places = new List<(int X, int Y)>();
            for (int y = Math.Max(1, cy - 24); y < Math.Min(map.Height - 1, cy + 25); y++)
            for (int x = Math.Max(1, cx - 24); x < Math.Min(map.Width - 1, cx + 25); x++)
                if (map.IsWalkable(x, y) && !map.IsWater(x, y) && !map.IsRiver(x, y)
                    && (material != ResourceType.Hardwood || map.GetFlora(x, y) == FloraType.Tree)
                    && (material != ResourceType.Spices || map.GetFlora(x, y) == FloraType.Bush)) places.Add((x, y));
            if (places.Count == 0) continue;
            var place = places[random.Next(places.Count)];
            bool permanent = material is ResourceType.Clay or ResourceType.Salt or ResourceType.Spices or ResourceType.Hardwood
                || material == ResourceType.IronOre && permanentIron;
            int reserve = material is ResourceType.Ruby or ResourceType.Sapphire or ResourceType.Emerald or ResourceType.Diamond
                ? random.Next(3, 13) : material == ResourceType.GoldOre ? random.Next(24, 100) : random.Next(120, 401);
            result.Add(new Deposit { Id = checked(tile * 32 + result.Count + 1), Region = tile, Material = material,
                X = place.X, Y = place.Y, Mode = permanent ? DepositMode.Permanent : DepositMode.Finite,
                InitialReserve = reserve * (mountain ? 3 : 1), RemainingReserve = reserve * (mountain ? 3 : 1), DailyLimit = permanent ? 4 : mountain ? 16 : 8,
                Difficulty = material is ResourceType.GoldOre or ResourceType.Diamond ? 2 : 1, Depth = DepthOf(material, mountain) });
        }
        return result;
    }
}
