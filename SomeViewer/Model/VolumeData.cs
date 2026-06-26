using OpenTK.Mathematics;

namespace SomeViewer.Model;

// CPU-side volume model: dimensions plus a normalized [0,1] density per voxel,
// stored z-major (index = z*Width*Height + y*Width + x — exactly the layout
// <see cref="DicomFolderLoader.LoadVolumeInt16"/> produces). The raw intensity
// range is kept so densities can be mapped back for window/level.
public sealed class VolumeData
{
    // Voxels along X (DICOM columns).
    public required int Width { get; init; }

    // Voxels along Y (DICOM rows).
    public required int Height { get; init; }

    // Voxels along Z (slice count).
    public required int Depth { get; init; }

    // Normalized densities in [0,1], length Width*Height*Depth.
    public required float[] Densities { get; init; }

    // Lowest raw intensity the densities were normalized from.
    public required short MinValue { get; init; }

    // Highest raw intensity the densities were normalized from.
    public required short MaxValue { get; init; }

    // Physical extent of the volume (dim * spacing per axis) normalized so the
    // longest axis is 1. Used as the render box scale so the volume keeps its
    // real proportions instead of being forced into a cube. Defaults to a cube.
    public Vector3 NormalizedExtent { get; init; } = Vector3.One;

    public long VoxelCount => (long)Width * Height * Depth;
}
