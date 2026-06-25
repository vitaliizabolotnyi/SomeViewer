namespace SomeViewer.Volumes;

using OpenTK.Mathematics;

/// <summary>
/// CPU-side volume model: dimensions plus a normalized [0,1] density per voxel,
/// stored z-major (index = z*Width*Height + y*Width + x — exactly the layout
/// <see cref="DicomFolderLoader.LoadVolumeInt16"/> produces). The raw intensity
/// range is kept so later milestones can map densities back for window/level.
/// </summary>
public sealed class VolumeData
{
    /// <summary>Voxels along X (DICOM columns).</summary>
    public required int Width { get; init; }

    /// <summary>Voxels along Y (DICOM rows).</summary>
    public required int Height { get; init; }

    /// <summary>Voxels along Z (slice count).</summary>
    public required int Depth { get; init; }

    /// <summary>Normalized densities in [0,1], length <c>Width*Height*Depth</c>.</summary>
    public required float[] Densities { get; init; }

    /// <summary>Lowest raw intensity the densities were normalized from.</summary>
    public required short MinValue { get; init; }

    /// <summary>Highest raw intensity the densities were normalized from.</summary>
    public required short MaxValue { get; init; }

    /// <summary>
    /// Physical extent of the volume (dim * spacing per axis) normalized so the
    /// longest axis is 1. Used as the render box scale so the volume keeps its
    /// real proportions instead of being forced into a cube. Defaults to a cube.
    /// </summary>
    public Vector3 NormalizedExtent { get; init; } = Vector3.One;

    public long VoxelCount => (long)Width * Height * Depth;
}
