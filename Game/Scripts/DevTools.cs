using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GodColony.Simulation.Map;

namespace GodColony;

/// <summary>
/// Mesure des performances (option --perf=N) : la durée de chaque image et la part qu'y prend la simulation,
/// résumées dans la console au bout de N images. Pour comparer deux versions sur la même machine :
/// <c>godot --path Game --fixed-fps 60 -- --advance-hours=480 --speed=30 --perf=1800</c>.
/// </summary>
public sealed class PerfProbe(int frames)
{
    private readonly List<double> _frames = new(frames), _simulation = new(frames);
    private ulong _last, _startupMs;

    /// <summary>À appeler une fois par image ; renvoie vrai quand toutes les images sont mesurées.</summary>
    public bool Frame(double simulationMs)
    {
        ulong now = Time.GetTicksUsec();
        if (_last != 0)
        {
            // La deuxième image : la carte est peinte, le jeu est prêt (temps compté depuis le lancement du moteur).
            if (_frames.Count == 0)
                _startupMs = now / 1000;
            _frames.Add((now - _last) / 1000.0);
            _simulation.Add(simulationMs);
        }
        _last = now;
        return _frames.Count >= frames;
    }

    public string Summary()
    {
        double[] sorted = [.. _frames.Order()];
        double Percentile(double p) => sorted[Math.Min(sorted.Length - 1, (int)(sorted.Length * p))];
        return $"prêt après {_startupMs} ms ; {sorted.Length} images : moyenne {_frames.Average():0.00} ms, médiane {Percentile(0.5):0.00}, " +
               $"95 % {Percentile(0.95):0.00}, 99 % {Percentile(0.99):0.00}, pire {sorted[^1]:0.00} ms ; " +
               $"simulation {_simulation.Average():0.00} ms par image (pire {_simulation.Max():0.00}) ; " +
               $"images de plus de 16,7 ms : {sorted.Count(f => f > 1000 / 60.0)}, de plus de 33 ms : {sorted.Count(f => f > 1000 / 30.0)}";
    }
}

/// <summary>Outils de développement, utilisés seulement pour tester l'affichage.</summary>
public static class DevTools
{
    /// <summary>
    /// Creuse une carrière en gradins dans la plus haute montagne proche du centre,
    /// et renvoie la case centrale pour y placer la caméra.
    /// </summary>
    public static (int X, int Y) DigDemoQuarry(LocalMap map)
    {
        (int cx, int cy) = FindPeakNearCenter(map);
        for (int dy = -5; dy <= 5; dy++)
        for (int dx = -7; dx <= 7; dx++)
        {
            // Plus on est près du centre, plus on creuse profond : cela forme des gradins.
            int depth = 5 - Math.Max(Math.Abs(dx) * 5 / 7, Math.Abs(dy));
            int x = cx + dx, y = cy + dy;
            for (int i = 0; i < depth && map.CanMine(x, y); i++)
                map.Mine(x, y);
        }
        return (cx, cy);
    }

    private static (int, int) FindPeakNearCenter(LocalMap map)
    {
        (int, int) best = (map.Width / 2, map.Height / 2);
        float bestScore = float.MinValue;
        for (int y = 8; y < map.Height - 8; y++)
        for (int x = 8; x < map.Width - 8; x++)
        {
            if (!map.IsMountain(x, y)) continue;
            float distance = MathF.Sqrt((x - map.Width / 2f) * (x - map.Width / 2f) + (y - map.Height / 2f) * (y - map.Height / 2f));
            float score = map.GetElevation(x, y) * 10f - distance;
            if (score > bestScore)
            {
                bestScore = score;
                best = (x, y);
            }
        }
        return best;
    }
}
