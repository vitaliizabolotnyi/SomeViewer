using OpenTK.Mathematics;

namespace SomeViewer.Rendering;

/// <summary>
/// Orbit/zoom camera for inspecting a volume: the eye orbits a fixed target (the
/// volume center) on a sphere defined by yaw, pitch, and distance. Better suited
/// to a volume viewer than the FPS-style <see cref="LearnOpenTK.Common.Camera"/>.
/// Uses the same OpenTK view/projection helpers, so it matches the raycaster's
/// vec*matrix convention.
/// </summary>
public sealed class OrbitCamera
{
    private const float MinPitch = -1.55f; // just shy of the poles (±~89°)
    private const float MaxPitch = 1.55f;
    private const float MinDistance = 0.5f;
    private const float MaxDistance = 20f;

    private readonly float _initialDistance;

    private float _yaw;
    private float _pitch;
    private float _distance;

    public OrbitCamera(float distance, float aspectRatio)
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

    /// <summary>
    /// Zoom factor relative to the initial distance (&gt;1 = zoomed in). Closer
    /// distances increase perspective foreshortening, so this tracks how strong
    /// the depth-scaling effect is.
    /// </summary>
    public float ZoomFactor => _initialDistance / _distance;

    /// <summary>Current orbit yaw in radians (unwrapped).</summary>
    public float Yaw => _yaw;

    /// <summary>Current orbit pitch in radians (clamped near the poles).</summary>
    public float Pitch => _pitch;

    /// <summary>
    /// When true, render with an orthographic projection so depth no longer
    /// scales the image (no perspective foreshortening). Default for this volume
    /// viewer, since perspective makes the far side look scaled — most noticeable
    /// when zoomed in or when the anisotropic slab is rotated edge-on.
    /// </summary>
    public bool Orthographic { get; set; } = true;

    /// <summary>Orbit by screen-drag deltas (radians); pitch is clamped near the poles.</summary>
    public void Orbit(float deltaYaw, float deltaPitch)
    {
        _yaw += deltaYaw;
        _pitch = MathHelper.Clamp(_pitch + deltaPitch, MinPitch, MaxPitch);
    }

    /// <summary>Zoom multiplicatively (scroll); keeps the distance in a sane range.</summary>
    public void Zoom(float delta)
    {
        _distance = MathHelper.Clamp(_distance * MathF.Exp(-delta), MinDistance, MaxDistance);
    }

    /// <summary>Restore the initial yaw/pitch/distance.</summary>
    public void Reset()
    {
        _yaw = 0f;
        _pitch = 0f;
        _distance = _initialDistance;
    }

    /// <summary>Flip between perspective and orthographic projection.</summary>
    public void ToggleOrthographic()
    {
        Orthographic = !Orthographic;
    }

    public Matrix4 GetViewMatrix()
    {
        // Eye on a sphere around the target (spherical -> cartesian).
        Vector3 eye = Target + new Vector3(
            _distance * MathF.Cos(_pitch) * MathF.Sin(_yaw),
            _distance * MathF.Sin(_pitch),
            _distance * MathF.Cos(_pitch) * MathF.Cos(_yaw));

        return Matrix4.LookAt(eye, Target, Vector3.UnitY);
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
