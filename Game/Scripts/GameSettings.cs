using Godot;

namespace GodColony;

/// <summary>Préférences locales conservées entre deux lancements.</summary>
public sealed class GameSettings
{
    private const string Path = "user://settings.cfg";
    public bool Fullscreen { get; set; }
    public bool VSync { get; set; } = true;
    public bool AmbientEffects { get; set; } = true;
    public float CameraSensitivity { get; set; } = 1;

    public static GameSettings Load(string path = Path)
    {
        var settings = new GameSettings();
        var config = new ConfigFile();
        if (config.Load(path) != Error.Ok) return settings;
        settings.Fullscreen = config.GetValue("display", "fullscreen", false).AsBool();
        settings.VSync = config.GetValue("display", "vsync", true).AsBool();
        settings.AmbientEffects = config.GetValue("game", "ambient_effects", true).AsBool();
        settings.CameraSensitivity = Mathf.Clamp(config.GetValue("game", "camera_sensitivity", 1f).AsSingle(), 0.5f, 2f);
        return settings;
    }

    public Error Save(string path = Path)
    {
        var config = new ConfigFile();
        config.SetValue("display", "fullscreen", Fullscreen);
        config.SetValue("display", "vsync", VSync);
        config.SetValue("game", "ambient_effects", AmbientEffects);
        config.SetValue("game", "camera_sensitivity", CameraSensitivity);
        return config.Save(path);
    }

    public void Apply()
    {
        DisplayServer.WindowSetMode(Fullscreen ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Windowed);
        DisplayServer.WindowSetVsyncMode(VSync ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);
    }
}
