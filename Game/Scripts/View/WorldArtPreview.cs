using System;
using System.IO;
using Godot;
using GodColony.Simulation;
using GodColony.Simulation.Colonies;

namespace GodColony.View;

/// <summary>Scène isolée : le vrai contrôle de carte, avec des voyages préparés pour la capture.</summary>
public partial class WorldArtPreview : Node
{
    private string? _capture;
    private int _frames = 24;

    public override void _Ready()
    {
        VerifyPeopleLoading();
        if (BuildingSprites.DamStage(0) != 0 || BuildingSprites.DamStage(0.34f) != 1 || BuildingSprites.DamStage(0.7f) != 2)
            throw new InvalidOperationException("Étapes du barrage incorrectes.");
        var world = new WorldState(42, startingColonists: 10, colonyCount: 4, migration: false, lifecycle: false, trade: false);
        Caravan? first = null;
        for (int i = 0; i < 3; i++)
        {
            var from = world.Colonies[i];
            var to = world.Colonies[i + 1];
            from.Stock.Add(ResourceType.Wood, 12);
            var caravan = Trade.Depart(world, new TradePlan(from, to, [new TradeLine(ResourceType.Wood, 4, 1, true)], 50, 5, 2));
            first ??= caravan;
        }
        if (first is null) throw new InvalidOperationException("Aucune caravane de validation.");
        // Horloge figée à mi-trajet pour montrer les sprites ; aucune partie sauvegardée n'est utilisée.
        long middle = (first.ArriveTicks + first.DepartTicks) / 2;
        while (world.Clock.Ticks < middle) world.Clock.Advance();
        var map = new GodColony.WorldMapView { Position = new Vector2(50, 50), Size = new Vector2(1500, 800), Observed = 0 };
        map.Init(world);
        AddChild(map);
        foreach (string arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--capture=")) _capture = arg["--capture=".Length..];
        GD.Print("Validation : chargement/remplacement/repos/portrait/fallback des personnages ; étapes du barrage ; trois caravanes sur le vrai WorldMapView.");
    }

    private static void VerifyPeopleLoading()
    {
        const string stem = "res://Assets/peoples/human_adult_f";
        string[] paths = new string[5];
        for (int i = 0; i < 5; i++)
        {
            paths[i] = ProjectSettings.GlobalizePath(stem + (i == 4 ? "_rest.png" : $"_{i}.png"));
            if (File.Exists(paths[i])) throw new InvalidOperationException("Fixture déjà occupée : " + paths[i]);
        }
        var appearance = new ColonistAppearance(3, PeopleLook.Human, WoodlandBiome.TemperatePlain, Sex: Sex.Female);
        try
        {
            var expected = RemainingArt.Trader(PeopleLook.Elf, 1);
            for (int i = 0; i < paths.Length; i++)
                if (expected.SavePng(paths[i]) != Error.Ok) throw new InvalidOperationException("Écriture fixture.");
            AssetLibrary.Reload();
            var supplied = PeoplesSprites.Get(appearance);
            if (supplied[0].GetWidth() != 32 || supplied[0].GetImage().GetData().AsSpan().SequenceEqual(expected.GetData()) == false
                || PeoplesSprites.Rest(appearance).GetWidth() != 32 || PeoplesSprites.Portrait(appearance).GetWidth() != 32)
                throw new InvalidOperationException("Remplacement PNG des personnages incorrect.");
            File.Delete(paths[2]);
            AssetLibrary.Reload();
            if (PeoplesSprites.Get(appearance)[0].GetWidth() != 16) throw new InvalidOperationException("Secours natif incorrect.");
        }
        finally
        {
            foreach (string path in paths) if (File.Exists(path)) File.Delete(path);
            AssetLibrary.Reload();
        }
    }

    public override void _Process(double delta)
    {
        if (_capture is null || --_frames != 0) return;
        GetViewport().GetTexture().GetImage().SavePng(_capture);
        GetTree().Quit();
    }
}
