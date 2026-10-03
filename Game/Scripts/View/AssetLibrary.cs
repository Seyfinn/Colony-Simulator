using System.Collections.Generic;
using System.IO;
using Godot;

namespace GodColony.View;

/// <summary>
/// Les images fournies dans <c>Game/Assets/</c>. Quand un fichier existe, il remplace le dessin en code ;
/// quand il manque, le jeu garde son dessin procédural : on peut donc livrer les images une par une.
/// Les noms et les tailles attendus sont décrits dans <c>Game/Assets/CAHIER_DES_CHARGES.md</c>.
/// </summary>
public static class AssetLibrary
{
    private const string Root = "res://Assets/";
    private static readonly Dictionary<string, ImageTexture?> Cache = [];

    /// <summary>L'image <paramref name="relativePath"/> (par exemple <c>icons/coins.png</c>), ou null si elle n'existe pas.</summary>
    public static ImageTexture? Get(string relativePath)
    {
        if (Cache.TryGetValue(relativePath, out ImageTexture? cached))
            return cached;

        ImageTexture? texture = null;
        string file = ProjectSettings.GlobalizePath(Root + relativePath);
        if (File.Exists(file))
        {
            Image? image = Image.LoadFromFile(file);
            if (image is not null && !image.IsEmpty())
            {
                image.Convert(Image.Format.Rgba8);
                texture = ImageTexture.CreateFromImage(image);
            }
            else
                GD.PushWarning($"Image illisible : {relativePath}");
        }
        Cache[relativePath] = texture;
        return texture;
    }

    /// <summary>Les images d'une animation, numérotées à partir de 0 (<c>mill_wheel_{0}.png</c>) ; null s'il en manque une.</summary>
    public static ImageTexture[]? Frames(string pattern, int count)
    {
        var frames = new ImageTexture[count];
        for (int i = 0; i < count; i++)
        {
            ImageTexture? frame = Get(string.Format(pattern, i));
            if (frame is null)
                return null;
            frames[i] = frame;
        }
        return frames;
    }

    /// <summary>Oublie les images chargées (pour recharger des fichiers modifiés pendant le développement).</summary>
    public static void Reload() => Cache.Clear();
}
