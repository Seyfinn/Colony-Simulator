using System;
using Godot;
using GodColony.Simulation.Map;

namespace GodColony.View;

/// <summary>Le calque saisonnier garde ses commandes de dessin tant que le climat et le cadrage ne changent pas.</summary>
public partial class SeasonGround : Node2D
{
    public Action<Node2D>? Paint;
    public Func<int>? State;
    private int _state = -1;
    private Transform2D _transform;
    private Vector2 _size;
    private LocalMap? _map;
    private bool _dirty;
    internal int DrawCount { get; private set; }
    public void Init(LocalMap map) { _map = map; map.TileChanged += TileChanged; }
    private void TileChanged(int x, int y) => _dirty = true;
    public override void _ExitTree() { if (_map is not null) _map.TileChanged -= TileChanged; }
    public override void _Process(double delta)
    {
        int state = State?.Invoke() ?? 0;
        Transform2D transform = GetGlobalTransformWithCanvas();
        Vector2 size = GetViewportRect().Size;
        if (!_dirty && _state == state && _transform == transform && _size == size) return;
        _dirty = false;
        _state = state; _transform = transform; _size = size;
        QueueRedraw();
    }
    public override void _Draw() { DrawCount++; Paint?.Invoke(this); }
}
