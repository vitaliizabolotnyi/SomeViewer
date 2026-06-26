using OpenTK.Mathematics;
using SomeViewer.Rendering;

namespace SomeViewer.Scenes;

// Owns the list of Scenes and the single active renderer. Keys
// 1/2/3 in the host map to Activate. Switching disposes the
// previous renderer and creates + loads the target one (compile-on-switch), so
// only the active scene holds GPU resources.
public sealed class ScenesController : IDisposable
{
    private readonly IReadOnlyList<Scene> _scenes;
    private IRenderer? _activeRenderer;

    public ScenesController(IReadOnlyList<Scene> scenes)
    {
        _scenes = scenes;
    }

    // Index of the active scene, or -1 before the first activation.
    public int ActiveIndex { get; private set; } = -1;

    // Number of registered scenes.
    public int Count => _scenes.Count;

    // The active scene's renderer, or null before the first activation.
    public IRenderer? ActiveRenderer => _activeRenderer;

    // The active scene's display name, or an empty string if none is active.
    public string ActiveName => ActiveIndex >= 0 ? _scenes[ActiveIndex].Name : string.Empty;

    // Switch to the scene at index: dispose the current
    // renderer, then create and Load the target one
    // (kernels/shaders compiled, GPU resources allocated). No-op if the index is
    // out of range or already active.
    public void Activate(int index, int width, int height)
    {
        if (index < 0 || index >= _scenes.Count || index == ActiveIndex)
        {
            return;
        }

        _activeRenderer?.Dispose();

        IRenderer renderer = _scenes[index].CreateRenderer();
        renderer.Load(width, height);

        _activeRenderer = renderer;
        ActiveIndex = index;
    }

    public void Resize(int width, int height) => _activeRenderer?.Resize(width, height);

    public void Render(Matrix4 model, Matrix4 view, Matrix4 projection)
        => _activeRenderer?.Render(model, view, projection);

    public void Dispose()
    {
        _activeRenderer?.Dispose();
        _activeRenderer = null;
    }
}
