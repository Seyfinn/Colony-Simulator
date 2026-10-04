using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using GodColony.Simulation.Generation;
using GodColony.Simulation.Map;

namespace GodColony.View;

/// <summary>Milieux forestiers dérivés de la carte générée ; aucune règle de récolte n'en dépend.</summary>
public enum WoodlandBiome { TemperatePlain, Dryland, CoolForest, Highland, WetBank }

/// <summary>Répartition déterministe des apparences, figée avant que les colons ne transforment la carte.</summary>
public static class TreeDistribution
{
    public readonly record struct Shares(float Oak, float Birch, float Pine, float Willow)
    {
        public Shares Blend(Shares other, float amount) => new(
            Oak + (other.Oak - Oak) * amount, Birch + (other.Birch - Birch) * amount,
            Pine + (other.Pine - Pine) * amount, Willow + (other.Willow - Willow) * amount);

        public int Choose(float roll)
        {
            float choice = roll * (Oak + Birch + Pine + Willow);
            if (choice < Oak) return 0;
            if (choice < Oak + Birch) return 1;
            if (choice < Oak + Birch + Pine) return 2;
            return 3;
        }
    }

    private sealed record Forest(byte[] Styles, WoodlandBiome[] Biomes);
    private static readonly ConditionalWeakTable<LocalMap, Forest> Forests = new();
    private const int BankRadius = 6;

    // Pourcentages au cœur de chaque milieu. Les lisières mélangent ces profils.
    public static Shares Target(WoodlandBiome biome) => biome switch
    {
        WoodlandBiome.Dryland => new(65, 10, 24, 1),
        WoodlandBiome.CoolForest => new(25, 45, 25, 5),
        WoodlandBiome.Highland => new(10, 20, 68, 2),
        WoodlandBiome.WetBank => new(15, 20, 5, 60),
        _ => new(60, 25, 10, 5),
    };

    public static void Prepare(LocalMap map) => Forests.GetValue(map, Generate);
    public static int StyleAt(LocalMap map, int x, int y) => Forests.GetValue(map, Generate).Styles[y * map.Width + x];
    public static WoodlandBiome BiomeAt(LocalMap map, int x, int y) => Forests.GetValue(map, Generate).Biomes[y * map.Width + x];

    public static Shares WeightsFor(int elevation, float moisture, int waterDistance) => Environment(elevation, moisture, waterDistance).Shares;

    private static (Shares Shares, WoodlandBiome Biome) Environment(int elevation, float moisture, int waterDistance)
    {
        float dry = 1 - Smooth(0.34f, 0.44f, moisture);
        float cool = Smooth(0.5f, 0.63f, moisture);
        float high = Smooth(LocalMap.MountainElevation - 2, LocalMap.MountainElevation + 1, elevation);
        // Une source en montagne garde ses conifères ; les saules dominent les berges des vallées.
        float bank = (1 - Smooth(2, BankRadius, waterDistance)) * (1 - high * 0.85f);
        Shares shares = Target(WoodlandBiome.TemperatePlain)
            .Blend(Target(WoodlandBiome.Dryland), dry)
            .Blend(Target(WoodlandBiome.CoolForest), cool)
            .Blend(Target(WoodlandBiome.Highland), high)
            .Blend(Target(WoodlandBiome.WetBank), bank);
        WoodlandBiome biome = bank >= 0.7f ? WoodlandBiome.WetBank
            : high >= 0.5f ? WoodlandBiome.Highland
            : cool >= 0.55f ? WoodlandBiome.CoolForest
            : dry >= 0.55f ? WoodlandBiome.Dryland : WoodlandBiome.TemperatePlain;
        return (shares, biome);
    }

    private static Forest Generate(LocalMap map)
    {
        byte[] distance = WaterDistances(map);
        var styles = new byte[map.Width * map.Height];
        var biomes = new WoodlandBiome[styles.Length];
        for (int y = 0; y < map.Height; y++)
        for (int x = 0; x < map.Width; x++)
        {
            int index = y * map.Width + x;
            // Le champ d'humidité que la simulation a utilisé pour générer les sols et les forêts.
            float moisture = map.GetMoisture(x, y);
            var environment = Environment(map.GetElevation(x, y), moisture, distance[index]);
            float roll = Noise.Hash01(x, y, 107, map.Seed);
            // Quelques groupes cohérents au milieu d'une majorité de tirages individuels.
            // Les points irréguliers évitent les anciens bosquets en carrés de 9 × 9 cases.
            if (Noise.Hash01(x, y, 109, map.Seed) < 0.3f) roll = GroveRoll(x, y, map.Seed);
            styles[index] = (byte)environment.Shares.Choose(roll);
            biomes[index] = environment.Biome;
        }
        return new Forest(styles, biomes);
    }

    private static byte[] WaterDistances(LocalMap map)
    {
        var distance = new byte[map.Width * map.Height];
        Array.Fill(distance, (byte)BankRadius);
        var pending = new Queue<int>();
        for (int y = 0; y < map.Height; y++)
        for (int x = 0; x < map.Width; x++)
            if (map.IsWater(x, y) || map.IsRiver(x, y))
            {
                int index = y * map.Width + x;
                distance[index] = 0;
                pending.Enqueue(index);
            }
        while (pending.TryDequeue(out int current))
        {
            int next = distance[current] + 1;
            if (next >= BankRadius) continue;
            int x = current % map.Width, y = current / map.Width;
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0 || !map.InBounds(x + dx, y + dy)) continue;
                int neighbor = (y + dy) * map.Width + x + dx;
                if (distance[neighbor] <= next) continue;
                distance[neighbor] = (byte)next;
                pending.Enqueue(neighbor);
            }
        }
        return distance;
    }

    private static float GroveRoll(int x, int y, int seed)
    {
        const int cell = 7;
        int cellX = x / cell, cellY = y / cell, nearestX = cellX, nearestY = cellY;
        float nearest = float.MaxValue;
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            int cx = cellX + dx, cy = cellY + dy;
            float px = (cx + Noise.Hash01(cx, cy, 113, seed)) * cell;
            float py = (cy + Noise.Hash01(cx, cy, 127, seed)) * cell;
            float squared = (px - x) * (px - x) + (py - y) * (py - y);
            if (squared >= nearest) continue;
            nearest = squared; nearestX = cx; nearestY = cy;
        }
        return Noise.Hash01(nearestX, nearestY, 131, seed);
    }

    private static float Smooth(float from, float to, float value)
    {
        float t = Math.Clamp((value - from) / (to - from), 0, 1);
        return t * t * (3 - 2 * t);
    }
}
