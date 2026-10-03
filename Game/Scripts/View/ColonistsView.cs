using Godot;
using GodColony.Simulation;
using GodColony.Simulation.Colonies;

namespace GodColony.View;

/// <summary>Dessine les feux de camp et les colons, avec un mouvement lissé entre deux ticks.</summary>
public partial class ColonistsView : Node2D
{
    private const int Tile = TerrainPainter.TileSize;

    private WorldState _world = null!;
    private double _time;

    /// <summary>Avancement entre le tick précédent et le suivant (0 à 1), pour lisser le mouvement.</summary>
    public float Alpha { get; set; }

    public Colonist? Selected { get; set; }

    public void Init(WorldState world)
    {
        _world = world;
        TextureFilter = TextureFilterEnum.Nearest;

        // Chaque feu de camp éclaire les alentours, ce qui se voit surtout la nuit.
        foreach (Colony colony in world.Colonies)
        {
            var glow = new GradientTexture2D
            {
                Width = 256, Height = 256,
                Fill = GradientTexture2D.FillEnum.Radial,
                FillFrom = new Vector2(0.5f, 0.5f), FillTo = new Vector2(1f, 0.5f),
                Gradient = new Gradient { Colors = [new Color(1, 1, 1, 1), new Color(1, 1, 1, 0)] },
            };
            AddChild(new PointLight2D
            {
                Texture = glow,
                Color = new Color(1f, 0.7f, 0.4f),
                Energy = 0.9f,
                TextureScale = 0.8f,
                Position = new Vector2(colony.CampX + 0.5f, colony.CampY + 0.5f) * Tile,
            });
        }
    }

    /// <summary>Position affichée d'un colon, en pixels (au niveau de ses pieds).</summary>
    public Vector2 DisplayPosition(Colonist colonist)
    {
        float x = Mathf.Lerp(colonist.PrevX, colonist.X, Alpha);
        float y = Mathf.Lerp(colonist.PrevY, colonist.Y, Alpha);
        return new Vector2(x, y) * Tile + new Vector2(0, 4);
    }

    public override void _Process(double delta)
    {
        _time += delta;
        QueueRedraw();
    }

    public override void _Draw()
    {
        foreach (Colony colony in _world.Colonies)
        {
            foreach (Field field in colony.Fields)
                DrawField(field);
            foreach (Building building in colony.Buildings)
                DrawBuilding(building);

            if (colony.FireLit || !Simulation.Colonies.ColonyBrain.IsColdSeason(_world.Clock.Season))
            {
                ImageTexture fire = SpriteFactory.Campfire[(int)(_time * 6) % 2];
                DrawTexture(fire, new Vector2(colony.CampX * Tile + 2, colony.CampY * Tile + 2));
            }

            // Ceux qui dorment dans leur hutte sont à l'intérieur : on ne les voit pas.
            foreach (Colonist colonist in colony.Members)
                if (!colonist.IsSleepingAtHome)
                    DrawColonist(colonist);
            foreach (Colonist traveler in colony.Transients)
                DrawColonist(traveler);

            // Une bulle au-dessus de ceux qui bavardent, et de ceux à qui l'on parle.
            foreach (Colonist chatter in colony.Members)
            {
                if (chatter.Activity is not { Kind: ActivityKind.Chat, Started: true, Partner: { } partner })
                    continue;
                DrawBubble(chatter);
                DrawBubble(partner);
            }
        }
    }

    /// <summary>Terre labourée ; les jeunes pousses verdissent puis les épis dorent à la maturité.</summary>
    private void DrawField(Field field)
    {
        foreach (FieldPlot plot in field.Plots)
        {
            var origin = new Vector2(plot.X, plot.Y) * Tile;
            DrawRect(new Rect2(origin, new Vector2(Tile, Tile)), new Color(0.42f, 0.29f, 0.17f));
            for (int furrow = 3; furrow < Tile; furrow += 5)
                DrawRect(new Rect2(origin + new Vector2(0, furrow), new Vector2(Tile, 1)), new Color(0.34f, 0.23f, 0.13f));

            if (plot.Stage == CropStage.Fallow)
                continue;
            bool ripe = plot.Stage == CropStage.Ripe;
            int height = ripe ? 9 : 2 + (int)(plot.Growth * 6);
            Color stalk = ripe ? new Color(0.9f, 0.76f, 0.25f) : new Color(0.35f, 0.65f, 0.25f).Lerp(new Color(0.6f, 0.7f, 0.25f), plot.Growth);
            for (int row = 0; row < 3; row++)
            for (int column = 0; column < 4; column++)
                DrawRect(new Rect2(origin + new Vector2(1 + column * 4, 4 + row * 5 - height + 3), new Vector2(2, height)), stalk);
        }
    }

    private void DrawBubble(Colonist colonist)
    {
        Vector2 head = DisplayPosition(colonist) + new Vector2(0, -17);
        DrawRect(new Rect2(head + new Vector2(-5, -6), new Vector2(11, 6)), new Color(1, 1, 1, 0.92f));
        DrawRect(new Rect2(head + new Vector2(-1, 0), new Vector2(3, 2)), new Color(1, 1, 1, 0.92f));
        int dots = 1 + (int)(_time * 3) % 3;
        for (int i = 0; i < dots; i++)
            DrawRect(new Rect2(head + new Vector2(-3 + i * 3, -4), new Vector2(2, 2)), new Color(0.2f, 0.2f, 0.25f));
    }

    private void DrawBuilding(Building building)
    {
        var origin = new Vector2(building.X, building.Y) * Tile;
        var footprint = new Rect2(origin, new Vector2(building.Width, building.Height) * Tile);

        if (building.IsComplete)
        {
            DrawTexture(SpriteFactory.Hut, origin + new Vector2(0, footprint.Size.Y - 40));
            return;
        }

        // Chantier : fondations, tas de bois livré, poteaux qui montent avec l'avancement.
        DrawRect(footprint, new Color(0.45f, 0.33f, 0.2f, 0.55f));
        DrawRect(footprint, new Color(0.3f, 0.2f, 0.1f), false, 1f);
        int logs = building.WoodDelivered / 2;
        for (int i = 0; i < logs; i++)
            DrawRect(new Rect2(origin + new Vector2(2 + i % 3 * 4, footprint.Size.Y - 4 - i / 3 * 2), new Vector2(4, 2)), new Color(0.55f, 0.36f, 0.2f));
        float postHeight = 4 + 18 * building.Progress;
        foreach (float x in new[] { 3f, footprint.Size.X - 5f })
            DrawRect(new Rect2(origin + new Vector2(x, footprint.Size.Y - postHeight), new Vector2(2, postHeight)), new Color(0.5f, 0.32f, 0.18f));
        if (building.Progress > 0.5f)
            DrawRect(new Rect2(origin + new Vector2(3, footprint.Size.Y - postHeight), new Vector2(footprint.Size.X - 6, 2)), new Color(0.5f, 0.32f, 0.18f));
    }

    private void DrawColonist(Colonist colonist)
    {
        Vector2 feet = DisplayPosition(colonist);
        ImageTexture[] frames = SpriteFactory.Colonist(colonist.Id);

        if (colonist == Selected)
            DrawArc(feet + new Vector2(0, -1), 6f, 0, Mathf.Tau, 20, new Color(1f, 0.92f, 0.4f), 1.2f);

        if (colonist.IsSleeping)
        {
            // Allongé sur le côté, avec des « z » qui montent.
            DrawSetTransform(feet + new Vector2(-6, -3), Mathf.Pi / 2f, Vector2.One);
            DrawTexture(frames[0], Vector2.Zero);
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
            float rise = (float)(_time * 0.8 + colonist.Id * 0.37) % 1f;
            DrawString(ThemeDB.FallbackFont, feet + new Vector2(2, -6 - rise * 6), "z",
                HorizontalAlignment.Left, -1, 7, new Color(1, 1, 1, 1f - rise));
            return;
        }

        int frame = (int)(colonist.DistanceWalked * 3f) % 2;
        DrawTexture(frames[frame], feet - new Vector2(4, 12));

        // Ce qu'il rapporte au camp, en petit sous le bras.
        if (colonist.Carrying is { } load)
            DrawRect(new Rect2(feet + new Vector2(3, -7), new Vector2(3, 3)), CarryColor(load.Type));
    }

    private static Color CarryColor(ResourceType type) => type switch
    {
        ResourceType.Food => new Color(0.8f, 0.15f, 0.2f),
        ResourceType.Wood => new Color(0.55f, 0.36f, 0.2f),
        ResourceType.Stone => new Color(0.6f, 0.6f, 0.6f),
        _ => new Color(0.7f, 0.35f, 0.2f),
    };
}
