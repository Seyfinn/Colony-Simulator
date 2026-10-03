using System;
using System.Collections.Generic;
using Godot;

namespace GodColony.View;

public static partial class BuildingSprites
{
    private static readonly Dictionary<(int Stage, bool Side), ImageTexture> DamWorks = [];

    public static int DamStage(float progress) => Math.Clamp((int)(progress * 3), 0, 2);

    /// <summary>Pieux, culées, puis vanne en montage ; même ancrage que le barrage fini.</summary>
    public static ImageTexture DamConstruction(float progress, bool side)
    {
        int stage = DamStage(progress);
        string suffix = side ? "side_" : "";
        var provided = AssetLibrary.Get($"buildings/dam_construction_{suffix}{stage}.png");
        if (provided is not null && provided.GetWidth() == 32 && provided.GetHeight() == 48) return provided;
        var key = (stage, side);
        if (!DamWorks.TryGetValue(key, out var native))
            DamWorks[key] = native = PaintDamConstruction(stage, side).Texture();
        return native;
    }

    public static void ExportDamConstruction()
    {
        string folder = ProjectSettings.GlobalizePath("res://Assets/buildings");
        System.IO.Directory.CreateDirectory(folder);
        foreach (bool side in new[] { false, true })
        for (int stage = 0; stage < 3; stage++)
        {
            string file = $"dam_construction_{(side ? "side_" : "")}{stage}.png";
            Error result = PaintDamConstruction(stage, side).Image.SavePng(System.IO.Path.Combine(folder, file));
            if (result != Error.Ok) throw new InvalidOperationException($"Export {file} : {result}");
        }
        AssetLibrary.Reload();
    }

    private static PixelArt PaintDamConstruction(int stage, bool side)
    {
        var a = new PixelArt(32, 48);
        if (side)
        {
            // Les appuis suivent la silhouette de SideDam, sans projeter une retenue fictive.
            foreach (var (x, y) in new[] { (7, 23), (24, 32) })
            {
                Beam(a, x, y, 3, 47 - y);
                a.Line(x - 1, 46, x + 2, y + 5, WoodLight);
                a.Box(x, y, 3, 1, StoneLight);
            }
            a.Line(8, 25, 25, 34, C(211, 193, 143));
            if (stage >= 1)
            {
                Masonry(a, 8, stage == 1 ? 35 : 20, 12, stage == 1 ? 9 : 10, Stone, 5, 4);
                Masonry(a, 13, 39, 15, 8, Stone, 5, 4);
                a.Line(13, 39, 27, 42, StoneLight);
            }
            if (stage == 2)
            {
                Beam(a, 21, 12, 3, 22); Beam(a, 25, 25, 3, 18);
                a.Line(22, 12, 27, 26, WoodLight);
                a.Box(19, 29, 4, 5, Wood); a.Box(19, 29, 1, 5, WoodLight);
                a.Box(21, 14, 1, 17, StoneLight);
                a.Line(5, 18, 13, 43, Wood);
                a.Line(4, 27, 13, 30, WoodLight);
            }
        }
        else
        {
            foreach (int x in new[] { 5, 24 })
            {
                Beam(a, x, 25, 3, 22);
                a.Box(x, 25, 3, 1, StoneLight);
                a.Line(x + 1, 31, x == 5 ? 1 : 30, 45, WoodLight);
            }
            a.Line(6, 28, 25, 28, C(211, 193, 143));
            if (stage >= 1)
            {
                int top = stage == 1 ? 37 : 28;
                Masonry(a, 0, top, 10, 46 - top, Stone, 5, 4);
                Masonry(a, 22, top, 10, 46 - top, Stone, 5, 4);
                a.Box(0, top, 10, 1, StoneLight); a.Box(22, top, 10, 1, StoneLight);
            }
            if (stage == 2)
            {
                Beam(a, 5, 16, 3, 26); Beam(a, 24, 16, 3, 26);
                Beam(a, 4, 16, 25, 4);
                a.Box(14, 19, 2, 17, StoneLight);
                a.Box(10, 31, 12, 3, Wood); a.Box(10, 31, 12, 1, WoodLight);
                a.Line(2, 21, 2, 41, Wood);
                for (int y = 24; y < 41; y += 5) a.Box(1, y, 9, 1, WoodLight);
            }
        }
        // Matériaux rangés au pied, distincts du mur déjà monté.
        if (stage < 2)
        {
            a.Box(11, 44, 7, 3, Mortar);
            a.Box(12, 44, 4, 2, StoneLight); a.Box(16, 45, 3, 2, Stone);
            a.Line(12, 41, 19, 43, Wood); a.Line(12, 40, 19, 42, WoodLight);
        }
        return a;
    }
}
