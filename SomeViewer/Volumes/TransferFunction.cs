namespace SomeViewer.Volumes;

/// <summary>
/// 1D RGBA transfer function: maps a normalized density [0,1] to a color and
/// opacity. Built by linearly interpolating sorted control points into a flat
/// RGBA lookup table (LUT) that the raycast kernel samples once per step.
/// </summary>
public sealed class TransferFunction
{
    /// <summary>A density stop with its straight (non-premultiplied) RGBA color.</summary>
    public readonly record struct ControlPoint(float Density, float R, float G, float B, float A);

    /// <summary>Number of RGBA entries in <see cref="Lut"/>.</summary>
    public int Resolution { get; }

    /// <summary>Flat RGBA table, length <c>Resolution * 4</c>, values in [0,1].</summary>
    public float[] Lut { get; }

    private TransferFunction(int resolution, float[] lut)
    {
        Resolution = resolution;
        Lut = lut;
    }

    /// <summary>
    /// Builds a LUT by linearly interpolating <paramref name="points"/> (any order;
    /// sorted internally by density) across <paramref name="resolution"/> samples.
    /// </summary>
    public static TransferFunction FromControlPoints(IReadOnlyList<ControlPoint> points, int resolution = 256)
    {
        if (points.Count == 0)
        {
            throw new ArgumentException("At least one control point is required.", nameof(points));
        }

        if (resolution < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(resolution), "Resolution must be at least 2.");
        }

        var sorted = points.OrderBy(p => p.Density).ToArray();
        var lut = new float[resolution * 4];

        for (int i = 0; i < resolution; i++)
        {
            float density = i / (float)(resolution - 1);
            (float r, float g, float b, float a) = Interpolate(sorted, density);

            int o = i * 4;
            lut[o + 0] = r;
            lut[o + 1] = g;
            lut[o + 2] = b;
            lut[o + 3] = a;
        }

        return new TransferFunction(resolution, lut);
    }

    /// <summary>
    /// A default CT-style ramp: transparent air, reddish soft tissue, white bone.
    /// Densities are normalized over the loaded volume's intensity range.
    /// </summary>
    public static TransferFunction CreateDefault(int resolution = 256) =>
        FromControlPoints(new ControlPoint[]
        {
            //          density   R      G      B      A
            new ControlPoint(0.00f, 0.00f, 0.00f, 0.00f, 0.00f), // air -> transparent
            new ControlPoint(0.30f, 0.00f, 0.00f, 0.00f, 0.00f), // still transparent
            new ControlPoint(0.45f, 0.75f, 0.30f, 0.20f, 0.15f), // soft tissue, reddish
            new ControlPoint(0.55f, 0.85f, 0.55f, 0.35f, 0.30f), // muscle/organ
            new ControlPoint(0.70f, 0.95f, 0.85f, 0.55f, 0.55f), // denser tissue, yellow
            new ControlPoint(0.85f, 1.00f, 0.95f, 0.85f, 0.85f), // bone, near white
            new ControlPoint(1.00f, 1.00f, 1.00f, 1.00f, 0.95f), // dense bone, white
        }, resolution);

    private static (float R, float G, float B, float A) Interpolate(ControlPoint[] sorted, float density)
    {
        if (density <= sorted[0].Density)
        {
            return (sorted[0].R, sorted[0].G, sorted[0].B, sorted[0].A);
        }

        ControlPoint last = sorted[^1];
        if (density >= last.Density)
        {
            return (last.R, last.G, last.B, last.A);
        }

        for (int i = 1; i < sorted.Length; i++)
        {
            ControlPoint hi = sorted[i];
            if (density <= hi.Density)
            {
                ControlPoint lo = sorted[i - 1];
                float span = hi.Density - lo.Density;
                float t = span > 1e-6f ? (density - lo.Density) / span : 0f;
                return (
                    lo.R + (hi.R - lo.R) * t,
                    lo.G + (hi.G - lo.G) * t,
                    lo.B + (hi.B - lo.B) * t,
                    lo.A + (hi.A - lo.A) * t);
            }
        }

        return (last.R, last.G, last.B, last.A);
    }
}
