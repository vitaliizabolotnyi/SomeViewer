using OpenTK.Mathematics;

namespace SomeViewer.Rendering;

/// <summary>
/// Minimal rendering seam. The <see cref="Window"/> owns a camera and delegates
/// drawing to the active renderer, the CUDA-backed <see cref="VolumeRenderer"/>.
/// </summary>
public interface IRenderer : IDisposable
{
    /// <summary>Create GPU resources. Must be called once an OpenGL context is current.</summary>
    /// <param name="width">Initial framebuffer width in pixels.</param>
    /// <param name="height">Initial framebuffer height in pixels.</param>
    void Load(int width, int height);

    /// <summary>React to a framebuffer resize (width/height in pixels).</summary>
    void Resize(int width, int height);

    /// <summary>Draw a single frame using the supplied transforms.</summary>
    void Render(Matrix4 model, Matrix4 view, Matrix4 projection);
}
