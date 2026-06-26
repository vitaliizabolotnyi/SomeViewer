using ManagedCuda;
using ManagedCuda.BasicTypes;
using OpenTK.Mathematics;
using SomeViewer.Model;

namespace SomeViewer.Rendering;

// samples the middle axial slice of the uploaded volume with the sampleVolume kernel.
// View-independent (no raycasting), so it ignores the camera transforms and exposes no window/level controls.
public sealed class SliceVolumeRenderer : VolumeRenderer
{
    private CudaKernel _sampleKernel = null!;

    public SliceVolumeRenderer(PrimaryContext ctx, VolumeData volume)
        : base(ctx, volume)
    {
    }

    protected override void OnLoad()
    {
        base.OnLoad();

        _sampleKernel = LoadKernel("sampleVolume");
    }

    protected override void DoRender(CUdeviceptr output, Matrix4 model, Matrix4 view, Matrix4 projection)
    {
        // Render the middle axial slice (normalized z = 0.5) of the volume.
        var (block, grid) = LaunchDimensions();
        _sampleKernel.BlockDimensions = block;
        _sampleKernel.GridDimensions = grid;
        _sampleKernel.Run(VolumeTexture, output, Width, Height, 0.5f);
    }
}
