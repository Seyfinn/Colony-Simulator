using Godot;

namespace GodColony;

/// <summary>
/// Caméra : ZQSD, WASD ou flèches pour se déplacer, clic droit ou molette enfoncée pour glisser,
/// molette pour zoomer vers le curseur.
/// </summary>
public partial class CameraController : Camera2D
{
    private const float PanSpeed = 900f;
    private const float MinZoom = 0.25f;
    private const float MaxZoom = 6f;
    private const float ZoomStep = 1.15f;

    private bool _dragging;
    private bool _controlsEnabled = true;
    public float Sensitivity { get; set; } = 1;
    public Rect2 WorldBounds { get; set; }
    public bool ControlsEnabled
    {
        get => _controlsEnabled;
        set { _controlsEnabled = value; if (!value) _dragging = false; }
    }

    public override void _Process(double delta)
    {
        if (!ControlsEnabled || GetViewport().GuiGetFocusOwner() is LineEdit or SpinBox) return;
        if (_dragging && !Input.IsMouseButtonPressed(MouseButton.Right) && !Input.IsMouseButtonPressed(MouseButton.Middle))
            _dragging = false;
        var direction = Vector2.Zero;
        if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Z) || Input.IsKeyPressed(Key.Up)) direction.Y -= 1;
        if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) direction.Y += 1;
        if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Q) || Input.IsKeyPressed(Key.Left)) direction.X -= 1;
        if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) direction.X += 1;

        if (direction != Vector2.Zero)
        {
            Position += direction.Normalized() * PanSpeed * Sensitivity * (float)delta / Zoom.X;
            ConstrainPosition();
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!ControlsEnabled) return;
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true }:
                ZoomTowardMouse(ZoomStep);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true }:
                ZoomTowardMouse(1f / ZoomStep);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right or MouseButton.Middle } button:
                _dragging = button.Pressed;
                break;
            case InputEventMouseMotion motion when _dragging:
                Position -= motion.Relative / Zoom.X;
                ConstrainPosition();
                break;
        }
    }

    /// <summary>Zoome en gardant le point sous la souris immobile à l'écran.</summary>
    private void ZoomTowardMouse(float factor)
    {
        Vector2 before = GetGlobalMousePosition();
        float zoom = Mathf.Clamp(Zoom.X * factor, MinZoom, MaxZoom);
        Zoom = new Vector2(zoom, zoom);
        ForceUpdateScroll();
        Position += before - GetGlobalMousePosition();
        ConstrainPosition();
    }

    /// <summary>Un déplacement garde le centre sur le terrain pour éviter de perdre la carte dans le vide.</summary>
    private void ConstrainPosition()
    {
        if (WorldBounds.Size.X <= 0 || WorldBounds.Size.Y <= 0) return;
        Position = new Vector2(Mathf.Clamp(Position.X, WorldBounds.Position.X, WorldBounds.End.X),
            Mathf.Clamp(Position.Y, WorldBounds.Position.Y, WorldBounds.End.Y));
    }
}
