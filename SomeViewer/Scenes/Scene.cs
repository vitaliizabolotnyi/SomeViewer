using SomeViewer.Rendering;

namespace SomeViewer.Scenes;

// A switchable scene: a display Name plus a factory that builds
// the scene's renderer. The renderer is created lazily on activation (see
// ScenesController) so kernels/shaders are compiled and GPU
// resources allocated on switch. The factory shape lets a scene later capture
// its own VolumeData (e.g. a second MRI volume).
public sealed class Scene
{
    public Scene(string name, Func<IRenderer> createRenderer)
    {
        Name = name;
        CreateRenderer = createRenderer;
    }

    // Display name shown by the host window.
    public string Name { get; }

    // Builds a fresh renderer for this scene, called on activation.
    public Func<IRenderer> CreateRenderer { get; }
}
