using System;
using GodColony.Simulation.Colonies;
using GodColony.Simulation.Map;

namespace GodColony.View;

public static partial class TerrainPainter
{
    /// <summary>La chaussée reste dans les cellules réellement aménagées, y compris dans les virages.</summary>
    private static void PaintRoad(LocalMap map, int x, int y, byte[] pixels, int stride, int ox, int oy)
    {
        RoadSurface surface = map.Roads.SurfaceAt(y * map.Width + x);
        if (surface == RoadSurface.Bridge)
        {
            // Le pont entier est dessiné par ColonistsView depuis BridgeSite, au-dessus de l'eau.
            return;
        }
        if (surface == RoadSurface.None || map.HasWater(x,y) || map.IsCanal(x,y)) return;
        int mask = 0, ordinal = 0;
        for (int dy=-1;dy<=1;dy++) for (int dx=-1;dx<=1;dx++)
        {
            if (dx == 0 && dy == 0) continue;
            if (dx != 0 && dy != 0 && ((map.InBounds(x+dx,y) && map.Roads.SurfaceAt(y*map.Width+x+dx) != RoadSurface.None)
                || (map.InBounds(x,y+dy) && map.Roads.SurfaceAt((y+dy)*map.Width+x) != RoadSurface.None))) { ordinal++; continue; }
            if (map.InBounds(x+dx,y+dy) && map.Roads.SurfaceAt((y+dy)*map.Width+x+dx) != RoadSurface.None)
                mask |= 1 << ordinal;
            ordinal++;
        }
        float radius = surface == RoadSurface.DirtRoad ? 7 : 3;
        for (int py=0;py<TileSize;py++) for (int px=0;px<TileSize;px++)
        {
            float ax = px - 15.5f, ay = py - 15.5f, distance = ax*ax+ay*ay;
            ordinal = 0;
            for (int dy=-1;dy<=1;dy++) for (int dx=-1;dx<=1;dx++)
            {
                if (dx == 0 && dy == 0) continue;
                if ((mask & (1 << ordinal++)) == 0) continue;
                float t = Math.Clamp((ax*dx+ay*dy)/(16*(dx*dx+dy*dy)),0,1);
                float rx = ax - t*16*dx, ry = ay - t*16*dy;
                distance = Math.Min(distance,rx*rx+ry*ry);
            }
            if (distance > radius*radius) continue;
            int index = ((oy+py)*stride+ox+px)*4;
            var ground = new Rgb(pixels[index],pixels[index+1],pixels[index+2]);
            bool speck = ((x*32+px)*17+(y*32+py)*29)%31 == 0;
            var earth = speck ? new Rgb(167,145,104) : new Rgb(135,115,81);
            float opacity = distance > (radius-1)*(radius-1) ? .35f : surface == RoadSurface.Trail ? .55f : .85f;
            Rgb color = Blend(ground,earth,opacity);
            pixels[index]=color.R; pixels[index+1]=color.G; pixels[index+2]=color.B;
        }
    }

}
