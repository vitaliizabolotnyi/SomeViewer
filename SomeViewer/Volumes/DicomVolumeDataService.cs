namespace SomeViewer.Volumes;

using OpenTK.Mathematics;

/// <summary>
/// Wraps <see cref="DicomFolderLoader"/> behind the <see cref="IVolumeDataService"/>
/// seam: loads a series as 16-bit voxels and normalizes them to [0,1] so the GPU
/// texture unit can return filterable floats.
/// </summary>
public sealed class DicomVolumeDataService : IVolumeDataService
{
    public VolumeData Load(string source, int seriesIndex = 0)
    {
        var allSeries = DicomFolderLoader.LoadFolder(source);
        if (allSeries.Count == 0)
        {
            throw new InvalidOperationException($"No DICOM series found in '{source}'.");
        }

        var series = allSeries[Math.Clamp(seriesIndex, 0, allSeries.Count - 1)];
        short[] voxels = DicomFolderLoader.LoadVolumeInt16(series);

        // Find the intensity range so we can normalize to [0,1] for sampling.
        short min = short.MaxValue;
        short max = short.MinValue;
        for (int i = 0; i < voxels.Length; i++)
        {
            short v = voxels[i];
            if (v < min)
            {
                min = v;
            }

            if (v > max)
            {
                max = v;
            }
        }

        float range = Math.Max(1, max - min);
        var densities = new float[voxels.Length];
        for (int i = 0; i < voxels.Length; i++)
        {
            densities[i] = (voxels[i] - min) / range;
        }

        // Physical extent (voxel count * spacing) normalized so the longest axis
        // is 1, so the render box keeps the volume's real proportions.
        var physical = new Vector3(
            (float)(series.Columns * series.PixelSpacingX),
            (float)(series.Rows * series.PixelSpacingY),
            (float)(series.Depth * series.SliceSpacing));

        float longest = Math.Max(physical.X, Math.Max(physical.Y, physical.Z));
        Vector3 extent = longest > 1e-6f ? physical / longest : Vector3.One;

        return new VolumeData
        {
            Width = series.Columns,
            Height = series.Rows,
            Depth = series.Depth,
            Densities = densities,
            MinValue = min,
            MaxValue = max,
            NormalizedExtent = extent,
        };
    }
}
