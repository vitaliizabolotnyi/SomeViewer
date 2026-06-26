using OpenTK.Mathematics;

namespace SomeViewer.Rendering;

// Minimal rendering seam. The Window owns a camera and delegates
// drawing to the active renderer, the CUDA-backed VolumeRenderer.
public interface IRenderer : IDisposable
{
    // Create GPU resources. Must be called once an OpenGL context is current.
    // width: Initial framebuffer width in pixels.
    // height: Initial framebuffer height in pixels.
    void Load(int width, int height);

    // React to a framebuffer resize (width/height in pixels).
    void Resize(int width, int height);

    // Draw a single frame using the supplied transforms.
    void Render(Matrix4 model, Matrix4 view, Matrix4 projection);
}
