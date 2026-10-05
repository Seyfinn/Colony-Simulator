using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Noise = GodColony.Simulation.Generation.Noise;
using GodColony.Simulation;
using GodColony.Simulation.Colonies;

namespace GodColony.View;

/// <summary>Dessine les feux de camp et les colons, avec un mouvement lissé entre deux ticks.</summary>
public partial class ColonistsView : Node2D
{
    private const int Tile = TerrainPainter.TileSize;

    private WorldState _world = null!;
    private double _time;
    private readonly Dictionary<Building, (double Time, double Phase)> _wheelPhases = [];
    private readonly List<(Colony Colony, PointLight2D Light)> _fireLights = [];

    /// <summary>Avancement entre le tick précédent et le suivant (0 à 1), pour lisser le mouvement.</summary>
    public float Alpha { get; set; }

    public Colonist? Selected { get; set; }

    /// <summary>Halos et braises réservés au mode d'observation.</summary>
    public bool AmbientEffectsEnabled { get; set; } = true;
    public double WaterAnimationTime { get; set; }

    /// <summary>La colonie observée : la vue ne dessine qu'elle, sur sa propre carte.</summary>
    private Colony _colony = null!;
    private Settlement _settlement = null!;

    public void Init(WorldState world, Colony colonyToShow)
    {
        _world = world;
        _colony = colonyToShow;
        _settlement = colonyToShow.CurrentSettlement;
        TextureFilter = TextureFilterEnum.Nearest;
        _villageNotices = new VillageNotices();
        AddChild(_villageNotices);
        _villageNotices.Init(world, colonyToShow);
        var seasonGround = new SeasonGround
        {
            ShowBehindParent = true, Paint = DrawSeasonGround,
            State = () => ((int)_world.Clock.Season << 10) | ((int)_settlement.Map.Biome << 2) | (_settlement.ColdSnapDaysLeft > 0 ? 2 : 0) | (_settlement.DroughtDaysLeft > 0 ? 1 : 0),
        };
        AddChild(seasonGround);
        seasonGround.Init(colonyToShow.Map);

        // Halo à plusieurs paliers : chaleur locale, sans surexposer le village en journée.
        foreach (Colony colony in new[] { colonyToShow })
        {
            var texture = new GradientTexture2D
            {
                Width = 384, Height = 384, Fill = GradientTexture2D.FillEnum.Radial,
                FillFrom = new Vector2(0.5f, 0.5f), FillTo = new Vector2(1f, 0.5f),
                Gradient = new Gradient
                {
                    Offsets = [0f, 0.2f, 0.6f, 1f],
                    Colors = [new Color(1, 1, 1, 0.85f), new Color(1, 1, 1, 0.55f), new Color(1, 1, 1, 0.15f), new Color(1, 1, 1, 0)],
                },
            };
            var light = new PointLight2D
            {
                Texture = texture, Color = Color.Color8(255, 190, 117), TextureScale = 0.85f,
                Position = new Vector2(colony.CampX + 0.5f, colony.CampY + 0.5f) * Tile,
            };
            AddChild(light);
            _fireLights.Add((colony, light));
        }
    }
    /// <summary>Position affichée d'un colon, en pixels (au niveau de ses pieds).</summary>
    public Vector2 DisplayPosition(Colonist colonist)
    {
        float x = Mathf.Lerp(colonist.PrevX, colonist.X, Alpha);
        float y = Mathf.Lerp(colonist.PrevY, colonist.Y, Alpha);
        return new Vector2(x, y) * TerrainPainter.TileSize + new Vector2(0, 8);
    }

    public override void _Process(double delta)
    {
        using var scope = _settlement.Observe();
        _time += delta;
        foreach (var (colony, light) in _fireLights)
        {
            light.Enabled = AmbientEffectsEnabled && HasFire(colony);
            float flicker = 0.96f + 0.04f * Mathf.Sin((float)_time * 9 + colony.CampX);
            light.Energy = (0.12f + 0.78f * (1 - _world.Clock.Daylight)) * flicker;
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        using var scope = _settlement.Observe();
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        if (AmbientEffectsEnabled) WaterEffects.Draw(this, _colony.Map, WaterAnimationTime);
        foreach (Colony colony in new[] { _colony })
        {
            DrawPublicPlaces(colony);
            // Au sol, sans épaisseur : les champs passent sous tout le reste.
            foreach (Field field in colony.Fields)
                DrawField(field);
            foreach (var known in colony.DepositReports.Where(k => k.Region == _settlement.RegionTileIndex))
            {
                var site = _world.Regions[known.Region].Deposits.FirstOrDefault(d => d.Id == known.SiteId);
                if(site is null) continue;
                Vector2 at = new Vector2(site.X,site.Y) * Tile;
                DrawRect(new Rect2(at + new Vector2(5,5),new Vector2(22,22)),new Color(.13f,.18f,.14f,.8f));
                DrawTextureRect(ResourceIcons.Get(known.Material), new Rect2(at + new Vector2(8,8),new Vector2(16,16)), false,
                    known.State == GodColony.Simulation.Map.DepositObservation.Depleted ? new Color(.5f,.5f,.5f,.6f) : Colors.White);
            }

            // Tout ce qui se dresse (tombes, bâtiments, feu, colons, plantes proches) est dessiné du nord au sud,
            // d'après la position de ses pieds : un colon passe derrière un arbre ou une hutte plus au sud que lui.
            _standing.Clear();
            _nearby.Clear();
            foreach (Grave grave in colony.Graves)
                if (grave.X >= 0)
                {
                    Grave g = grave;
                    _standing.Add((g.Y * Tile + 28, () => DrawGrave(g)));
                }
            foreach (Building building in colony.Buildings)
            {
                Building b = building;
                _standing.Add(((b.Y + b.Height) * Tile - (b.IsComplete && b.Type == BuildingType.Pen ? 29 : 4), () => DrawBuilding(b)));
                if (b.IsComplete && b.Type == BuildingType.Pen) AddPenAnimals(b);
                CollectFlora(b.X - 1, b.Y, b.X + b.Width, b.Y + b.Height + 2);
            }
            _standing.Add(((colony.CampY + 0.5f) * Tile + 9, () => DrawCampfire(colony)));

            // Ceux qui dorment dans leur hutte sont à l'intérieur : on ne les voit pas.
            foreach (Colonist colonist in colony.PresentMembers)
                if (!colonist.IsSleepingAtHome)
                    AddColonist(colonist);
            foreach (Colonist traveler in colony.Transients)
                AddColonist(traveler);

            // Les plantes à portée des colons et des bâtiments sont redessinées dans l'ordre, par-dessus ce qui est derrière elles.
            foreach ((int x, int y) in _nearby)
            {
                (int fx, int fy) = (x, y);
                _standing.Add((FloraPainter.FootY(fx, fy), () => FloraPainter.Draw(this, _colony.Map, fx, fy, new Vector2(fx, fy) * Tile, shadow: false)));
            }

            DrawRecentEvents();
            _standing.Sort((a, b) => a.Y.CompareTo(b.Y));
            foreach ((float _, Action draw) in _standing)
                draw();
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);

            var speakers = new HashSet<Colonist>();
            foreach (Colonist chatter in colony.PresentMembers)
            {
                if (chatter.Activity is not { Kind: ActivityKind.Chat, Started: true, Partner: { } partner }) continue;
                speakers.Add(chatter);
                speakers.Add(partner);
            }
            foreach (Colonist speaker in speakers) DrawBubble(speaker);
            foreach (Colonist member in colony.PresentMembers) DrawVillageStatus(member);
        }
    }

    // Réutilisés d'une image à l'autre pour ne pas réallouer.
    private readonly List<(float Y, Action Draw)> _standing = [];
    private readonly HashSet<(int X, int Y)> _nearby = [];

    private void AddColonist(Colonist colonist)
    {
        Vector2 feet = DisplayPosition(colonist).Round();
        _standing.Add((feet.Y, () => DrawColonist(colonist)));
        // Un arbre haut déborde sur deux ou trois cases au-dessus de son pied.
        int tx = colonist.TileX, ty = colonist.TileY;
        CollectFlora(tx - 1, ty, tx + 1, ty + 3);
    }

    private void CollectFlora(int x0, int y0, int x1, int y1)
    {
        for (int y = y0; y <= y1; y++)
        for (int x = x0; x <= x1; x++)
            if (_colony.Map.InBounds(x, y) && _colony.Map.GetFlora(x, y) != Simulation.Map.FloraType.None)
                _nearby.Add((x, y));
    }

    private bool HasFire(Colony colony) => colony.FireLit || !ColonyBrain.IsColdSeason(_world.Clock.Season);

    private void DrawCampfire(Colony colony)
    {
        Vector2 center = new Vector2(colony.CampX + 0.5f, colony.CampY + 0.5f) * Tile;
        DrawGroundShadow(center + new Vector2(2, 9), 14, 4, 0.2f);
        bool burning = HasFire(colony);
        Texture2D fire = burning ? SpriteFactory.Campfire[(int)(_time * 8) % 4] : SpriteFactory.Firepit;
        DrawTexture(fire, center - new Vector2(16, 19));
        if (!burning || !AmbientEffectsEnabled) return;
        for (int i = 0; i < 4; i++)
        {
            float rise = (float)(_time * 0.55 + i * 0.27) % 1;
            float drift = Mathf.Sin((float)_time * 1.8f + i * 2.4f) * 4;
            Vector2 ember = (center + new Vector2(drift + i - 2, -12 - rise * 24)).Round();
            DrawRect(new Rect2(ember, new Vector2(1, 2)), new Color(1, 0.76f, 0.39f, (1 - rise) * 0.75f));
        }
    }

    private void DrawField(Field field)
    {
        foreach (FieldPlot plot in field.Plots)
        {
            Vector2 origin = new Vector2(plot.X, plot.Y) * Tile;
            DrawRect(new Rect2(origin, new Vector2(Tile, Tile)), Color.Color8(118, 88, 61));
            if (_colony.DroughtDaysLeft > 0)
            {
                DrawLine(origin + new Vector2(4, 1), origin + new Vector2(12, 6), Color.Color8(76, 61, 43));
                DrawLine(origin + new Vector2(12, 6), origin + new Vector2(16, 2), Color.Color8(76, 61, 43));
                DrawLine(origin + new Vector2(22, 21), origin + new Vector2(28, 28), Color.Color8(76, 61, 43));
            }
            for (int row = 0; row < 3; row++)
            {
                int bed = 5 + row * 10;
                DrawRect(new Rect2(origin + new Vector2(1, bed), new Vector2(30, 5)), Color.Color8(149, 109, 71));
                DrawLine(origin + new Vector2(1, bed + 5), origin + new Vector2(30, bed + 5), Color.Color8(90, 73, 52));
                for (int column = 0; column < 3; column++)
                {
                    float variation = Noise.Hash01(plot.X * 3 + column, plot.Y * 3 + row, 42, 0);
                    Vector2 basePoint = origin + new Vector2(5 + column * 10 + (int)(variation * 2), bed + 3);
                    if (plot.Stage == CropStage.Fallow)
                    {
                        if (variation > 0.8f) DrawRect(new Rect2(basePoint, new Vector2(2, 1)), Color.Color8(185, 140, 93));
                        continue;
                    }
                    if (plot.Crop == CropKind.Grapes)
                    {
                        DrawLine(basePoint + new Vector2(-4,-9),basePoint + new Vector2(4,-9),Color.Color8(135,105,68),1);
                        DrawLine(basePoint, basePoint + new Vector2(0,-14),Color.Color8(135,105,68),2);
                        DrawCircle(basePoint + new Vector2(0,-11),4,Color.Color8(103,145,76));
                        if(plot.Stage == CropStage.Ripe) DrawRect(new Rect2(basePoint + new Vector2(1,-7),new Vector2(3,5)),Color.Color8(125,77,146));
                        continue;
                    }
                    bool ripe = plot.Stage == CropStage.Ripe;
                    int height = ripe ? 12 : 3 + (int)(plot.Growth * 8);
                    int sway = height > 6 ? (int)Math.Round(Math.Sin(_time * 1.6 + plot.X * 0.4 + row) * 0.8) : 0;
                    Vector2 tip = basePoint + new Vector2(sway, -height);
                    Color stalk = ripe || _colony.DroughtDaysLeft > 0 ? Color.Color8(214, 176, 83) : Color.Color8(108, 153, 87);
                    DrawLine(basePoint, tip, stalk, 1);
                    DrawLine(basePoint + new Vector2(0, -3), basePoint + new Vector2(-3, -5), stalk.Darkened(0.16f), 1);
                    DrawLine(basePoint + new Vector2(0, -5), basePoint + new Vector2(3, -7), stalk, 1);
                    if (plot.Crop == CropKind.Flax)
                    {
                        DrawRect(new Rect2(tip + new Vector2(-1,-1),new Vector2(3,3)),Color.Color8(132,171,213));
                        continue;
                    }
                    if (ripe)
                    {
                        for (int ear = 0; ear < 3; ear++)
                            DrawRect(new Rect2(tip + new Vector2(ear % 2 == 0 ? -1 : 1, ear * 2), new Vector2(2, 2)),
                                ear == 0 ? Color.Color8(245, 215, 127) : Color.Color8(227, 190, 100));
                    }
                }
            }
        }
        Vector2 corner = new Vector2(field.X, field.Y) * Tile;
        Vector2 size = new(Field.Size * Tile, Field.Size * Tile);
        DrawRect(new Rect2(corner + new Vector2(0, size.Y - 2), new Vector2(size.X, 3)), Color.Color8(81, 66, 46));
        DrawLine(corner, corner + new Vector2(size.X, 0), Color.Color8(179, 135, 82), 2);
        DrawLine(corner, corner + new Vector2(0, size.Y), Color.Color8(167, 123, 75), 2);
        foreach (Vector2 offset in new[] { Vector2.Zero, new Vector2(size.X - 3, 0), new Vector2(0, size.Y - 3), size - new Vector2(3, 3) })
            DrawRect(new Rect2(corner + offset, new Vector2(3, 3)), Color.Color8(203, 167, 104));
    }

    private void DrawGrave(Grave grave)
    {
        Vector2 origin = new Vector2(grave.X, grave.Y) * Tile;
        DrawGroundShadow(origin + new Vector2(17, 28), 11, 3, 0.25f);
        DrawTexture(SpriteFactory.Grave, origin);
    }

    private void DrawBubble(Colonist colonist)
    {
        var appearance = PeoplesSprites.Describe(colonist, BiomeVisuals.At(_colony.Map, colonist.TileX, colonist.TileY));
        float scale = colonist.Stage == LifeStage.Child ? 0.7f : 1;
        Vector2 origin = DisplayPosition(colonist).Round() + new Vector2(-12, -PeoplesSprites.Bounds(appearance).Size.Y * scale - 20);
        DrawTexture(SpriteFactory.ChatBubble, origin);
        for (int i = 0; i < 3; i++)
        {
            int height = 2 + (int)(Math.Sin(_time * 5 + i * 1.8) + 1);
            DrawRect(new Rect2(origin + new Vector2(6 + i * 5, 8 - height), new Vector2(2, height)), ArtDirection.Charcoal);
        }
    }

    private void DrawGroundShadow(Vector2 center, int rx, int ry, float opacity)
    {
        for (int y = -ry; y <= ry; y++)
        {
            int half = (int)(rx * Math.Sqrt(Math.Max(0, 1 - y * y / (float)(ry * ry))));
            DrawRect(new Rect2(center + new Vector2(-half, y), new Vector2(half * 2 + 1, 1)), new Color(0.16f, 0.2f, 0.16f, opacity));
        }
    }

    private void DrawSelection(Vector2 feet)
    {
        Vector2 anchor = feet + new Vector2(0, -1);
        DrawColoredPolygon([anchor + new Vector2(-12, 0), anchor + new Vector2(0, -5), anchor + new Vector2(12, 0), anchor + new Vector2(0, 5)],
            new Color(0.96f, 0.83f, 0.52f, 0.24f));
        foreach (int side in new[] { -1, 1 })
        {
            DrawLine(anchor + new Vector2(side * 12, -3), anchor + new Vector2(side * 12, 3), ArtDirection.Brass, 1);
            DrawLine(anchor + new Vector2(side * 12, 3), anchor + new Vector2(side * 7, 5), ArtDirection.Brass, 1);
        }
        int bob = (int)Math.Round(Math.Sin(_time * 3) * 1);
        Vector2 top = feet + new Vector2(0, -30 + bob);
        DrawColoredPolygon([top + new Vector2(-3, -3), top + new Vector2(3, -3), top], ArtDirection.Cream);
    }

    /// <summary>Grand ouvrage de pierre ancré sur les deux berges, avec sa vanne au centre du courant.</summary>
    private void DrawDam(Building dam)
    {
        var origin = new Vector2(dam.X, dam.Y) * Tile;
        Vector2 basePoint = origin + new Vector2(0, dam.Height * Tile);
        Texture2D sprite = BuildingSprites.For(dam, BiomeVisuals.At(_colony.Map, dam.RiverX, dam.RiverY));
        if (!dam.IsComplete)
        {
            DrawWorkshopSite(dam, new Rect2(origin, new Vector2(dam.Width, dam.Height) * Tile), basePoint);
            return;
        }
        DrawGroundShadow(basePoint + new Vector2(sprite.GetWidth() / 2f, -2), sprite.GetWidth() / 2, 5, 0.25f);
        DrawTexture(sprite, basePoint - new Vector2(0, sprite.GetHeight()));
    }

    private void DrawBuilding(Building building)
    {
        if (building.IsDam)
        {
            DrawDam(building);
            return;
        }
        var origin = new Vector2(building.X, building.Y) * Tile;
        var footprint = new Rect2(origin, new Vector2(building.Width, building.Height) * Tile);
        Vector2 basePoint = origin + new Vector2(0, footprint.Size.Y);
        if (building.IsComplete)
        {
            DrawGroundShadow(basePoint + new Vector2(footprint.Size.X / 2, -1), (int)footprint.Size.X / 2 - 3, 5, 0.22f);
            Texture2D sprite = building.Type == BuildingType.Cask ? WorkshopArt.Cask(building, _world.Clock.Ticks)
                : BuildingSprites.For(building, BiomeVisuals.At(_colony.Map, building.X, building.Y));
            // Les états du fût partagent une source ; l'équipement garde sa petite emprise même pendant la fermentation.
            if (building.Type == BuildingType.Cask)
                DrawTextureRect(sprite, new Rect2(basePoint - new Vector2(0, footprint.Size.Y + 16), new Vector2(footprint.Size.X, footprint.Size.Y + 16)), false);
            else DrawTexture(sprite, basePoint - new Vector2(0, sprite.GetHeight()));
            if (building.Type == BuildingType.Pen) DrawPenConnections(building);
            if (building.Type == BuildingType.Cask && WorkshopArt.CaskState(building, _world.Clock.Ticks) == "brewing")
                foreach (Vector2 bubble in WorkshopArt.Bubbles(_world.Clock.Ticks))
                    DrawRect(new Rect2(basePoint - new Vector2(0, 48) + bubble * new Vector2(0.5f, 0.6f), new Vector2(1, 1)), ArtDirection.Cream);
            if (building.Type == BuildingType.Mill)
            {
                float flow = Hydrology.MillFlow(_colony.Map, building);
                // Intégrer le débit évite un saut de pose quand le courant change ; le temps se fige en pause.
                var previous = _wheelPhases.GetValueOrDefault(building, (WaterAnimationTime, 0d));
                double phase = (previous.Item2 + Math.Max(0, WaterAnimationTime - previous.Item1) * Math.Clamp(flow, 0, 1) * 6) % 4;
                _wheelPhases[building] = (WaterAnimationTime, phase);
                DrawTexture(RemainingArt.WheelFrame((int)phase), basePoint + new Vector2(footprint.Size.X - 24, -40));
            }
            return;
        }
        if (!building.IsHut)
        {
            DrawWorkshopSite(building, footprint, basePoint);
            return;
        }
        // Fondations de pierre, plancher, ossature puis charpente selon les travaux réels.
        Color timber = Color.Color8(111, 77, 49), light = Color.Color8(172, 127, 78);
        DrawRect(footprint.Grow(-4), new Color(0.45f, 0.33f, 0.2f, 0.48f));
        DrawRect(footprint.Grow(-5), Color.Color8(117, 118, 102), false, 2);
        for (int x = 7; x < 58; x += 8)
            DrawRect(new Rect2(basePoint + new Vector2(x, -6), new Vector2(6, 3)), Color.Color8(164, 158, 134));
        if (building.Progress > 0.15f)
            for (int y = 31; y < 57; y += 5)
            {
                DrawRect(new Rect2(origin + new Vector2(8, y), new Vector2(47, 4)), timber);
                DrawLine(origin + new Vector2(8, y), origin + new Vector2(54, y), light);
            }
        if (building.Progress > 0.3f)
        {
            foreach (int x in new[] { 6, 26, 53 })
            {
                DrawRect(new Rect2(basePoint + new Vector2(x, -34), new Vector2(3, 28)), timber);
                DrawRect(new Rect2(basePoint + new Vector2(x, -34), new Vector2(1, 28)), light);
            }
            DrawRect(new Rect2(basePoint + new Vector2(6, -34), new Vector2(50, 3)), timber);
        }
        if (building.Progress > 0.55f)
        {
            for (int x = 7; x < 55; x += 12)
                DrawLine(basePoint + new Vector2(x, -34), basePoint + new Vector2(x + 9, -68), light, 2);
            DrawLine(basePoint + new Vector2(16, -68), basePoint + new Vector2(53, -68), timber, 3);
        }
        if (building.Progress > 0.8f)
        {
            int wallRows = (int)((building.Progress - 0.8f) / 0.2f * 25);
            DrawRect(new Rect2(basePoint + new Vector2(9, -7 - wallRows), new Vector2(15, wallRows)), Color.Color8(205, 180, 131));
            DrawRect(new Rect2(basePoint + new Vector2(40, -7 - wallRows), new Vector2(13, wallRows)), Color.Color8(205, 180, 131));
        }
        int logs = building.WoodDelivered / 2;
        for (int i = 0; i < logs; i++)
        {
            Vector2 log = origin + new Vector2(8 + i % 3 * 13, 8 + i / 3 * 5);
            DrawRect(new Rect2(log, new Vector2(12, 4)), timber);
            DrawRect(new Rect2(log, new Vector2(12, 1)), light);
            DrawRect(new Rect2(log, new Vector2(2, 4)), Color.Color8(197, 154, 101));
        }
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }

    private void DrawWorkshopSite(Building building, Rect2 footprint, Vector2 basePoint)
    {
        Color timber = Color.Color8(111, 77, 49), light = Color.Color8(172, 127, 78);
        if (building.Type == BuildingType.MineDepot)
        {
            DrawRect(footprint.Grow(-6), new Color(.43f, .39f, .29f, .3f));
            for (int x = 10; x < footprint.Size.X - 8; x += 18)
            {
                DrawRect(new Rect2(footprint.Position + new Vector2(x, 6), new Vector2(3, 5)), timber);
                DrawRect(new Rect2(footprint.Position + new Vector2(x, 6), new Vector2(2, 1)), light);
                DrawLine(footprint.Position + new Vector2(x, 25 + x % 13), footprint.Position + new Vector2(x + 5, 25 + x % 13), light.Darkened(.3f));
            }
            DrawLine(footprint.Position + new Vector2(10, 8), footprint.Position + new Vector2(footprint.Size.X - 10, 8), light.Darkened(.2f));
        }
        else DrawRect(footprint.Grow(-5), Color.Color8(117, 118, 102), false, 2);
        Texture2D sprite = BuildingSprites.For(building, BiomeVisuals.At(_colony.Map, building.X, building.Y));
        // La maçonnerie et les équipements apparaissent du sol vers le toit ; chaque atelier garde sa forme.
        int rows = Math.Clamp((int)(building.Progress * sprite.GetHeight()), 4, sprite.GetHeight());
        int width = sprite.GetWidth();
        DrawTextureRectRegion(sprite, new Rect2(basePoint - new Vector2(0, rows), new Vector2(width, rows)),
            new Rect2(0, sprite.GetHeight() - rows, width, rows));
        if (building.Type == BuildingType.MineDepot && building.Progress > .12f)
        {
            // Les échafaudages montent avec l'ouvrage ; leurs planches et leur échelle rendent le chantier lisible avant la pose de la roue.
            int hauteur = (int)(48 + building.Progress * 86);
            foreach (int x in new[] { 75, 139 })
            {
                DrawRect(new Rect2(basePoint + new Vector2(x, -hauteur), new Vector2(3, hauteur - 5)), timber);
                DrawLine(basePoint + new Vector2(x, -hauteur), basePoint + new Vector2(x, -6), light);
            }
            for (int y = 25; y < hauteur; y += 28)
            {
                DrawRect(new Rect2(basePoint + new Vector2(72, -y), new Vector2(73, 4)), timber);
                DrawLine(basePoint + new Vector2(72, -y), basePoint + new Vector2(145, -y), light);
                for (int x = 80; x < 144; x += 9) DrawLine(basePoint + new Vector2(x, -y), basePoint + new Vector2(x, -y + 3), timber.Darkened(.4f));
                DrawLine(basePoint + new Vector2(78, -y + 3), basePoint + new Vector2(138, -Math.Max(5, y - 25)), light.Darkened(.12f));
            }
            DrawLine(basePoint + new Vector2(144, -6), basePoint + new Vector2(150, -hauteur + 12), timber, 2);
            DrawLine(basePoint + new Vector2(150, -6), basePoint + new Vector2(156, -hauteur + 12), light, 2);
            for (int y = 10; y < hauteur - 12; y += 6)
            {
                float x = 144 + (y - 6) * 6f / (hauteur - 18);
                DrawLine(basePoint + new Vector2(x, -y), basePoint + new Vector2(x + 6, -y), light);
            }
        }
        else
        {
            foreach (int x in new[] { 4, width - 7 })
            {
                DrawRect(new Rect2(basePoint + new Vector2(x, -49), new Vector2(2, 47)), timber);
                DrawLine(basePoint + new Vector2(x, -49), basePoint + new Vector2(x, -3), light);
            }
            DrawLine(basePoint + new Vector2(5, -23), basePoint + new Vector2(width - 6, -23), light, 2);
            DrawLine(basePoint + new Vector2(5, -23), basePoint + new Vector2(width / 2, -3), timber, 2);
        }
        for (int i = 0; i < Math.Min(8, building.StoneDelivered / 3); i++)
        {
            Vector2 rock = footprint.Position + new Vector2(8 + i % 4 * 7, 8 + i / 4 * 5);
            DrawRect(new Rect2(rock, new Vector2(6, 4)), Color.Color8(135, 147, 132));
            DrawLine(rock, rock + new Vector2(5, 0), Color.Color8(194, 192, 159));
        }
        for (int i = 0; i < Math.Min(5, building.WoodDelivered / 2); i++)
        {
            Vector2 log = footprint.Position + new Vector2(width - 25, 8 + i * 4);
            DrawRect(new Rect2(log, new Vector2(15, 3)), timber);
            DrawLine(log, log + new Vector2(14, 0), light);
        }
    }

    private void DrawColonist(Colonist colonist)
    {
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        Vector2 feet = DisplayPosition(colonist).Round();
        var appearance = PeoplesSprites.Describe(colonist, BiomeVisuals.At(_colony.Map, colonist.TileX, colonist.TileY));
        bool warrior = CivilizationArt.IsWarrior(_world, colonist);
        bool traveler = !warrior && (_colony.Transients.Contains(colonist) || _world.Caravans.Exists(c => c.Traders.Contains(colonist)));
        ImageTexture[] frames = traveler ? RemainingArt.TraderFrames(appearance.People) : PeoplesSprites.Get(appearance);
        Vector2 spriteOffset = new(-frames[0].GetWidth() / 2f, -frames[0].GetHeight());
        float scale = colonist.Stage == LifeStage.Child ? 0.7f : 1f;
        DrawGroundShadow(feet, (int)(PeoplesSprites.ShadowRadius(appearance.People) * scale), 2, 0.24f);

        if (colonist == Selected)
            DrawSelection(feet);

        if (colonist.IsSleeping)
        {
            // Une lune accompagne la pose de repos.
            Rect2I bounds = PeoplesSprites.Bounds(appearance);
            Vector2 center = new(bounds.Position.X + bounds.Size.X / 2f, bounds.Position.Y + bounds.Size.Y / 2f);
            DrawSetTransform(feet + new Vector2(0, -4), Mathf.Pi / 2f, new Vector2(scale, scale));
            DrawTexture(PeoplesSprites.Rest(appearance), -center);
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
            float bob = Mathf.Sin((float)_time * 2 + colonist.Id) * 2;
            DrawTexture(SpriteFactory.Sleep, feet + new Vector2(5, -bounds.Size.Y * scale - 4 + bob), new Color(1, 1, 1, 0.85f));
            return;
        }

        bool walking = colonist.X != colonist.PrevX || colonist.Y != colonist.PrevY;
        int frame = walking ? (int)(colonist.DistanceWalked * 6f) % frames.Length : 0;
        DrawSetTransform(feet, 0, new Vector2(scale, scale));
        DrawTexture(frames[frame], spriteOffset, colonist.Ailment == Ailment.Sick ? Color.Color8(209, 219, 197) : Colors.White);
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);

        DrawVillageGesture(colonist, feet);
        if (warrior) CivilizationArt.DrawEquipment(this, feet, appearance, colonist.X < colonist.PrevX, frame);
        if (colonist.Ailment == Ailment.Injured) DrawRect(new Rect2(feet + new Vector2(-3, -22), new Vector2(5, 2)), ArtDirection.Cream);

        // Ce qu'il rapporte au camp, en petit sous le bras.
        if (colonist.Carrying is { } load)
        {
            Vector2 parcel = feet + PeoplesSprites.CarryOffset(appearance.People) * scale;
            DrawRect(new Rect2(parcel - Vector2.One, new Vector2(12, 12) * scale), ArtDirection.Charcoal);
            DrawTextureRect(ResourceIcons.Get(load.Type), new Rect2(parcel, new Vector2(10, 10) * scale), false);
        }
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }

}
