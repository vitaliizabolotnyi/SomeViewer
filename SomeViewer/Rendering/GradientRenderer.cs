using System.Diagnostics;
using ManagedCuda;
using ManagedCuda.BasicTypes;
using OpenTK.Mathematics;

namespace SomeViewer.Rendering;

// Animated fallback renderer: the animatedWaves kernel writes a time-driven
// plasma pattern into the PBO using four overlapping sine-wave fields and a
// three-phase colour palette, so the display loops smoothly without any volume.
// fillGradient (static test pattern) remains compiled in the module for reference.
public sealed class GradientRenderer : CudaRendererBase
{
    private CudaKernel _wavesKernel = null!;

    // Measures wall-clock seconds from the moment the renderer is loaded.
    private readonly Stopwatch _clock = new();

    public GradientRenderer(PrimaryContext ctx)
        : base(ctx)
    {
    }

    protected override void OnLoad()
    {
        _wavesKernel = LoadKernel("animatedWaves");
        _clock.Restart();
    }

    protected override void DoRender(CUdeviceptr output, Matrix4 model, Matrix4 view, Matrix4 projection)
    {
        var (block, grid) = LaunchDimensions();
        _wavesKernel.BlockDimensions = block;
        _wavesKernel.GridDimensions  = grid;
        _wavesKernel.Run(output, Width, Height, (float)_clock.Elapsed.TotalSeconds);
    }
}

