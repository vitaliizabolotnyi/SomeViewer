using OpenTK.Mathematics;

namespace SomeViewer.Rendering;

/// <summary>
/// Trackball/zoom camera for inspecting a volume: the eye orbits a fixed target
/// (the volume center) at a given distance, but the orientation is accumulated as
/// a <see cref="Quaternion"/> rather than yaw/pitch angles. Each drag rotates about
/// screen-aligned axes relative to the
/// current view, so there is no fixed up vector, no pole clamping, no gimbal
/// lock, and roll accumulates freely — the volume feels glued to the cursor from
/// any orientation. Uses the same OpenTK view/projection helpers, so it matches
/// the raycaster's vec*matrix convention.
/// </summary>
public sealed class TrackballCamera
{
    private const float MinDistance = 0.5f;
    private const float MaxDistance = 20f;

    private readonly float _initialDistance;

    private Quaternion _orientation = Quaternion.Identity;
    private float _distance;

    public TrackballCamera(float distance, float aspectRatio)
    {
        _initialDistance = distance;
        _distance = distance;
        AspectRatio = aspectRatio;
    }

    /// <summary>Point the camera orbits around (volume center).</summary>
    public Vector3 Target { get; set; } = Vector3.Zero;

    /// <summary>Viewport aspect ratio for the projection.</summary>
    public float AspectRatio { get; set; }

    /// <summary>Vertical field of view (radians).</summary>
    public float Fov { get; set; } = MathHelper.PiOver4;

    /// <summary>Current eye distance from the target.</summary>
    public float Distance => _distance;

    /// <summary>Accumulated trackball orientation (camera-local -> world).</summary>
    public Quaternion Orientation => _orientation;

    /// <summary>
    /// Zoom factor relative to the initial distance (&gt;1 = zoomed in). Closer
    /// distances increase perspective foreshortening, so this tracks how strong
    /// the depth-scaling effect is.
    /// </summary>
    public float ZoomFactor => _initialDistance / _distance;

    /// <summary>
    /// When true, render with an orthographic projection so depth no longer
    /// scales the image (no perspective foreshortening). Default for this volume
    /// viewer, since perspective makes the far side look scaled — most noticeable
    /// when zoomed in or when the anisotropic slab is rotated edge-on.
    /// </summary>
    public bool Orthographic { get; set; } = true;

    /// <summary>
    /// Rotate the trackball by screen-drag deltas (right/down positive) already
    /// scaled to radians. Horizontal drags spin about the camera's local up axis,
    /// vertical drags about its local right axis. The increment is post-multiplied
    /// so it stays relative to the current view: the axes track the screen, so
    /// there is no fixed up direction, no pole clamp, and roll can accumulate.
    /// Signs are chosen so the volume follows the cursor.
    /// </summary>
    public void Rotate(float deltaX, float deltaY)
    {
        Quaternion delta = Quaternion.FromAxisAngle(Vector3.UnitY, -deltaX)
                         * Quaternion.FromAxisAngle(Vector3.UnitX, deltaY);

        // Post-multiply (local frame) and renormalize to fight drift from the
        // many small incremental multiplications a drag produces.
        _orientation = Quaternion.Normalize(_orientation * delta);
    }

    /// <summary>Roll about the view direction (radians); handy for a dedicated roll gesture.</summary>
    public void Roll(float deltaRoll)
    {
        _orientation = Quaternion.Normalize(_orientation * Quaternion.FromAxisAngle(Vector3.UnitZ, deltaRoll));
    }

    /// <summary>Zoom multiplicatively (scroll); keeps the distance in a sane range.</summary>
    public void Zoom(float delta)
    {
        _distance = MathHelper.Clamp(_distance * MathF.Exp(-delta), MinDistance, MaxDistance);
    }

    /// <summary>Restore the initial orientation and distance.</summary>
    public void Reset()
    {
        _orientation = Quaternion.Identity;
        _distance = _initialDistance;
    }

    /// <summary>Flip between perspective and orthographic projection.</summary>
    public void ToggleOrthographic()
    {
        Orthographic = !Orthographic;
    }

    public Matrix4 GetViewMatrix()
    {
        // Place the eye along the camera-local +Z axis at the current distance,
        // then rotate that offset (and the up vector) by the accumulated
        // orientation. With identity orientation this is a plain front-on pose:
        // eye at (0, 0, distance) looking at the target.
        Vector3 eye = Target + Vector3.Transform(new Vector3(0f, 0f, _distance), _orientation);
        Vector3 up = Vector3.Transform(Vector3.UnitY, _orientation);

        return Matrix4.LookAt(eye, Target, up);
    }

    public Matrix4 GetProjectionMatrix()
    {
        if (Orthographic)
        {
            // Match the perspective view's apparent size at the target plane:
            // the visible half-height there is distance * tan(fov/2). No depth
            // foreshortening, so the far side no longer looks scaled.
            float halfHeight = _distance * MathF.Tan(Fov * 0.5f);
            float halfWidth = halfHeight * AspectRatio;
            return Matrix4.CreateOrthographicOffCenter(
                -halfWidth, halfWidth, -halfHeight, halfHeight, 0.01f, 100f);
        }

        return Matrix4.CreatePerspectiveFieldOfView(Fov, AspectRatio, 0.01f, 100f);
    }
}
