using System.Collections.Generic;
using Godot;
using GodColony.Simulation.Colonies;

namespace GodColony.View;

public partial class ColonistsView
{
    private void DrawPublicPlaces(Colony colony)
    {
        // Les espaces réservés sont un sol discret, sans inventer de mobilier ni de pavage réalisé.
        var cells = new HashSet<int>(colony.Layout.PlazaCells);
        foreach (PlotReservation parcel in colony.Layout.Parcels)
            if (parcel.Kind == ParcelKind.PublicSpace && parcel.State != ReservationState.Released)
                for (int y=parcel.Y;y<parcel.Y+parcel.Height;y++) for (int x=parcel.X;x<parcel.X+parcel.Width;x++)
                    cells.Add(y*colony.Map.Width+x);
        foreach (int cell in cells)
        {
            int x=cell%colony.Map.Width, y=cell/colony.Map.Width;
            if (!colony.Map.InBounds(x,y) || colony.Map.HasWater(x,y)) continue;
            DrawRect(new Rect2(new Vector2(x,y)*Tile+new Vector2(2,2),new Vector2(Tile-4,Tile-4)),new Color(.63f,.55f,.38f,.12f));
        }
        foreach (Building building in colony.Buildings)
            if (building.HasDoor)
            {
                Vector2 at = new Vector2(building.AccessX+.5f,building.AccessY+.5f)*Tile;
                DrawRect(new Rect2(at-new Vector2(3,2),new Vector2(6,4)),new Color(.76f,.66f,.43f,.8f));
                Vector2 entry = new Vector2(building.EntryX+.5f,building.EntryY+.5f)*Tile;
                Vector2 threshold = (at+entry)/2;
                Vector2 edge = entry.X == at.X ? new Vector2(3,0) : new Vector2(0,3);
                DrawLine(threshold-edge,threshold+edge,new Color(.24f,.21f,.16f,.8f),2);
            }
    }
}
