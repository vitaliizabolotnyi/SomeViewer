namespace SomeViewer.Rendering;

// Optional capability for renderers that expose interactive window/level and
// ray step-size tuning. The host (Window) checks for this on the
// active scene's renderer to decide whether the window/level/step keys apply
// and what tuning read-outs to show in the title.
public interface IVolumeControls
{
    // Current ray step size in volume-local units (smaller = higher quality).
    float StepSize { get; }

    // Current window center (normalized density in [0,1]).
    float WindowCenter { get; }

    // Current window width (normalized density span).
    float WindowWidth { get; }

    // Scale the ray step size by a factor, clamped to a sane range.
    void ScaleStepSize(float factor);

    // Shift the window center (normalized), clamped to [0,1].
    void AdjustWindowCenter(float delta);

    // Widen/narrow the window (normalized), clamped to a small positive minimum.
    void AdjustWindowWidth(float delta);

    // Reset window/level and step size to their defaults.
    void ResetSettings();
}
