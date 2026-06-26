using ManagedCuda;
using ManagedCuda.BasicTypes;
using OpenTK.Mathematics;
using SomeViewer.GlPrimitives;
using SomeViewer.Model;

namespace SomeViewer.Rendering;

// Abstract base for renderers that sample a single VolumeData.
// On load it uploads the volume into a CUDA 3D texture and derives the render
// box's extent scale (so anisotropic voxel spacing isn't stretched). Concrete
// subclasses (RaycastVolumeRenderer, SliceVolumeRenderer)
// add their kernel and per-frame DoRender.
public abstract class VolumeRenderer : CudaRendererBase
{
    private readonly VolumeData _volume;
    private CudaVolumeTexture? _volumeTexture;

    protected VolumeRenderer(PrimaryContext ctx, VolumeData volume)
        : base(ctx)
    {
        _volume = volume;
    }

    // The uploaded volume's bindless CUDA texture handle for kernels.
    protected CUtexObject VolumeTexture => _volumeTexture!.TexObject;

    // Scales the unit render box to the volume's real proportions (anisotropic
    // voxel spacing), so the depth axis isn't stretched. Baked into the model.
    protected Matrix4 ExtentScale { get; private set; } = Matrix4.Identity;

    protected override void OnLoad()
    {
        // Upload the volume into a CUDA 3D texture once, up front.
        _volumeTexture = new CudaVolumeTexture(_volume);

        // Scale the unit render box to the volume's physical proportions so
        // anisotropic spacing (e.g. thick CT slices) isn't stretched in Z.
        ExtentScale = Matrix4.CreateScale(_volume.NormalizedExtent);
    }

    protected override void OnDispose()
    {
        _volumeTexture?.Dispose();
        _volumeTexture = null;
    }
}
