namespace SomeViewer.Volumes;

/// <summary>
/// Loads a volume source (a DICOM folder today) into a <see cref="VolumeData"/>.
/// Kept as a seam so the data backend can change without touching the renderer.
/// </summary>
public interface IVolumeDataService
{
    /// <summary>
    /// Loads <paramref name="source"/> and returns the series at
    /// <paramref name="seriesIndex"/> as a normalized volume.
    /// </summary>
    VolumeData Load(string source, int seriesIndex = 0);
}
