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
            ImageTexture fire = SpriteFactory.Campfire[(int)(_time * 6) % 2];
            DrawTexture(fire, new Vector2(colony.CampX * Tile + 2, colony.CampY * Tile + 2));

            foreach (Colonist colonist in colony.Members)
                DrawColonist(colonist);
        }
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

        // Une petite touche rouge : il rapporte des baies.
        if (colonist.Carrying is not null)
            DrawRect(new Rect2(feet + new Vector2(3, -6), new Vector2(2, 2)), new Color(0.8f, 0.15f, 0.2f));
    }
}
