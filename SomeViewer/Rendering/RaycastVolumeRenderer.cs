using ManagedCuda;
using ManagedCuda.BasicTypes;
using OpenTK.Mathematics;
using SomeViewer.GlPrimitives;
using SomeViewer.Model;

namespace SomeViewer.Rendering;

// Direct volume rendering: front-to-back raycasting through the CUDA 3D volume
// texture with a 1D RGBA transfer-function LUT and interactive window/level and
// step-size controls. The model/view/projection passed to
// CudaRendererBase.Render is inverted (after the extent scale) so
// the raycastVolume kernel can build eye rays.
public sealed class RaycastVolumeRenderer : VolumeRenderer, IVolumeControls
{
    private CudaKernel _raycastKernel = null!;

    // Transfer function: density -> RGBA LUT, uploaded once for the kernel to sample.
    private readonly TransferFunction _transferFunction = TransferFunction.CreateDefault();
    private CudaDeviceVariable<float>? _lutDevice;

    // Raycast tuning, adjustable at runtime. stepSize is in volume-local units
    // (the box spans 1.0); densityScale converts a normalized sample into per-step
    // opacity; window center/width remap density in [0,1].
    private float _stepSize = 0.004f;
    private float _densityScale = 100f;
    private float _windowCenter = 0.5f;
    private float _windowWidth = 1.0f;

    public RaycastVolumeRenderer(PrimaryContext ctx, VolumeData volume)
        : base(ctx, volume)
    {
    }

    public float StepSize => _stepSize;

    public float WindowCenter => _windowCenter;

    public float WindowWidth => _windowWidth;

    public void ScaleStepSize(float factor)
    {
        _stepSize = Math.Clamp(_stepSize * factor, 0.0005f, 0.05f);
    }

    public void AdjustWindowCenter(float delta)
    {
        _windowCenter = Math.Clamp(_windowCenter + delta, 0f, 1f);
    }

    public void AdjustWindowWidth(float delta)
    {
        _windowWidth = Math.Clamp(_windowWidth + delta, 0.02f, 2f);
    }

    public void ResetSettings()
    {
        _stepSize = 0.004f;
        _windowCenter = 0.5f;
        _windowWidth = 1.0f;
    }

    protected override void OnLoad()
    {
        base.OnLoad();

        _raycastKernel = LoadKernel("raycastVolume");

        // Upload the transfer-function LUT once (static for now).
        _lutDevice = new CudaDeviceVariable<float>(_transferFunction.Lut.Length);
        _lutDevice.CopyToDevice(_transferFunction.Lut);
    }

    protected override void DoRender(CUdeviceptr output, Matrix4 model, Matrix4 view, Matrix4 projection)
    {
        // Upload inv(extent * model * view * projection) so the kernel can
        // unproject pixels into volume-local space. The extent scale (leftmost,
        // applied first in OpenTK's row-vector convention) gives the volume its
        // real physical proportions instead of a cube.
        UploadInverseMvp(ExtentScale * model * view * projection);

        var (block, grid) = LaunchDimensions();
        _raycastKernel.BlockDimensions = block;
        _raycastKernel.GridDimensions = grid;
        _raycastKernel.Run(
            VolumeTexture,
            InverseMvpPointer,
            _lutDevice!.DevicePointer,
            _transferFunction.Resolution,
            output,
            Width,
            Height,
            _stepSize,
            _densityScale,
            _windowCenter,
            _windowWidth);
    }

    protected override void OnDispose()
    {
        base.OnDispose();

        _lutDevice?.Dispose();
        _lutDevice = null;
    }
}
