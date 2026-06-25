using LearnOpenTK.Common;
using ManagedCuda;
using ManagedCuda.BasicTypes;
using ManagedCuda.NVRTC;
using ManagedCuda.VectorTypes;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using SomeViewer.Volumes;

namespace SomeViewer.Rendering;

/// <summary>
/// CUDA-backed renderer. A CUDA kernel writes RGBA pixels into an OpenGL pixel
/// buffer object (PBO) registered for CUDA↔GL interop; the PBO is copied into a
/// texture and drawn on a fullscreen quad.
///
/// When a volume is supplied it is uploaded into a CUDA 3D texture and the
/// <c>raycastVolume</c> kernel performs direct volume rendering: the
/// model/view/projection passed to <see cref="Render"/> is inverted so the kernel
/// can build eye rays, and a 1D RGBA transfer function (<see cref="TransferFunction"/>)
/// uploaded as a LUT maps each sampled density to color and opacity. With no volume
/// it falls back to the <c>fillGradient</c> test pattern; a debug toggle can swap
/// the raycaster for the <c>sampleVolume</c> middle-slice kernel.
/// </summary>
public sealed class VolumeRenderer : IRenderer
{
    // Fullscreen quad: interleaved position (vec2 NDC) + texcoord (vec2).
    private static readonly float[] QuadVertices =
    {
        //  x,    y,    u,   v
        -1f, -1f, 0f, 0f,
         1f, -1f, 1f, 0f,
         1f,  1f, 1f, 1f,
        -1f, -1f, 0f, 0f,
         1f,  1f, 1f, 1f,
        -1f,  1f, 0f, 1f,
    };

    private readonly PrimaryContext _ctx;
    private readonly VolumeData? _volume;

    // Raycast tuning, adjustable at runtime. stepSize is in volume-local units
    // (the box spans 1.0); densityScale converts a normalized sample into per-step
    // opacity; window center/width remap density in [0,1].
    private float _stepSize = 0.004f;
    private float _densityScale = 100f;
    private float _windowCenter = 0.5f;
    private float _windowWidth = 1.0f;

    private CudaKernel _fillKernel = null!;
    private CudaKernel _sampleKernel = null!;
    private CudaKernel _raycastKernel = null!;
    private CudaVolumeTexture? _volumeTexture;
    private Shader _displayShader = null!;

    // Inverse model-view-projection uploaded each frame so the kernel can build
    // eye rays. Row-major (m[i*4+j] = M[i,j]) to match the kernel's unproject.
    private readonly float[] _invMvpHost = new float[16];
    private CudaDeviceVariable<float>? _invMvpDevice;

    // Transfer function: density -> RGBA LUT, uploaded once for the kernel to sample.
    private readonly TransferFunction _transferFunction = TransferFunction.CreateDefault();
    private CudaDeviceVariable<float>? _lutDevice;

    // Scales the unit render box to the volume's real proportions (anisotropic
    // voxel spacing), so the depth axis isn't stretched. Baked into the model.
    private Matrix4 _extentScale = Matrix4.Identity;

    // Debug toggle: false renders the middle-slice sampler instead of the raycaster.
    private bool _useRaycast = true;

    private int _vertexArrayObject;
    private int _vertexBufferObject;

    private int _texture;
    private int _pixelBuffer;
    private CudaOpenGLBufferInteropResource _cudaPixelBuffer = null!;

    private int _width;
    private int _height;

    public VolumeRenderer(PrimaryContext ctx, VolumeData? volume = null)
    {
        _ctx = ctx;
        _volume = volume;
    }

    /// <summary>Current ray step size in volume-local units (smaller = higher quality).</summary>
    public float StepSize => _stepSize;

    /// <summary>Current window center (normalized density in [0,1]).</summary>
    public float WindowCenter => _windowCenter;

    /// <summary>Current window width (normalized density span).</summary>
    public float WindowWidth => _windowWidth;

    /// <summary>Scale the ray step size by a factor, clamped to a sane range.</summary>
    public void ScaleStepSize(float factor)
    {
        _stepSize = Math.Clamp(_stepSize * factor, 0.0005f, 0.05f);
    }

    /// <summary>Shift the window center (normalized), clamped to [0,1].</summary>
    public void AdjustWindowCenter(float delta)
    {
        _windowCenter = Math.Clamp(_windowCenter + delta, 0f, 1f);
    }

    /// <summary>Widen/narrow the window (normalized), clamped to a small positive minimum.</summary>
    public void AdjustWindowWidth(float delta)
    {
        _windowWidth = Math.Clamp(_windowWidth + delta, 0.02f, 2f);
    }

    /// <summary>Reset window/level and step size to their defaults.</summary>
    public void ResetSettings()
    {
        _stepSize = 0.004f;
        _windowCenter = 0.5f;
        _windowWidth = 1.0f;
    }

    public void Load(int width, int height)
    {
        _width = Math.Max(1, width);
        _height = Math.Max(1, height);

        CompileKernel();
        CreateQuad();
        CreateScreenResources();

        // Upload the volume into a CUDA 3D texture once, up front.
        if (_volume != null)
        {
            _volumeTexture = new CudaVolumeTexture(_volume);
            _invMvpDevice = new CudaDeviceVariable<float>(16);

            // Scale the unit render box to the volume's physical proportions so
            // anisotropic spacing (e.g. thick CT slices) isn't stretched in Z.
            _extentScale = Matrix4.CreateScale(_volume.NormalizedExtent);

            // Upload the transfer-function LUT once (static for now).
            _lutDevice = new CudaDeviceVariable<float>(_transferFunction.Lut.Length);
            _lutDevice.CopyToDevice(_transferFunction.Lut);
        }
    }

    public void Resize(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }

        if (width == _width && height == _height)
        {
            return;
        }

        _width = width;
        _height = height;

        ReleaseScreenResources();
        CreateScreenResources();
    }

    public void Render(Matrix4 model, Matrix4 view, Matrix4 projection)
    {
        // 1. CUDA writes this frame's pixels into the mapped PBO.
        _cudaPixelBuffer.Map();
        CUdeviceptr output = _cudaPixelBuffer.GetMappedPointer();

        var blockDim = new dim3(16, 16, 1);
        var gridDim = new dim3((_width + 15) / 16, (_height + 15) / 16, 1);

        if (_volumeTexture != null)
        {
            if (_useRaycast)
            {
                // Colored DVR with window/level. Upload inv(model*view*projection)
                // so the kernel can unproject pixels into volume-local space. The
                // extent scale (leftmost, applied first in OpenTK's row-vector
                // convention) gives the volume its real physical proportions instead of a cube.
                UploadInverseMvp(_extentScale * model * view * projection);

                _raycastKernel.BlockDimensions = blockDim;
                _raycastKernel.GridDimensions = gridDim;
                _raycastKernel.Run(
                    _volumeTexture.TexObject,
                    _invMvpDevice!.DevicePointer,
                    _lutDevice!.DevicePointer,
                    _transferFunction.Resolution,
                    output,
                    _width,
                    _height,
                    _stepSize,
                    _densityScale,
                    _windowCenter,
                    _windowWidth);
            }
            else
            {
                // Render the middle axial slice of the uploaded volume.
                _sampleKernel.BlockDimensions = blockDim;
                _sampleKernel.GridDimensions = gridDim;
                _sampleKernel.Run(_volumeTexture.TexObject, output, _width, _height, 0.5f);
            }
        }
        else
        {
            // Fallback: gradient test pattern.
            _fillKernel.BlockDimensions = blockDim;
            _fillKernel.GridDimensions = gridDim;
            _fillKernel.Run(output, _width, _height);
        }

        _cudaPixelBuffer.UnMap();

        // 2. Copy the PBO into the texture (GL reads from the bound unpack buffer).
        GL.BindBuffer(BufferTarget.PixelUnpackBuffer, _pixelBuffer);
        GL.BindTexture(TextureTarget.Texture2D, _texture);
        GL.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, _width, _height, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
        GL.BindBuffer(BufferTarget.PixelUnpackBuffer, 0);

        // 3. Blit the texture to the screen with a fullscreen quad.
        GL.Disable(EnableCap.DepthTest);
        _displayShader.Use();
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, _texture);
        GL.BindVertexArray(_vertexArrayObject);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 6);
    }

    // Inverts the MVP and uploads it row-major (m[i*4+j] = M[i,j]) so the kernel's
    // unproject can map clip-space pixels into volume-local space. OpenTK stores
    // matrices row-major with the same vec*matrix convention as the shader, so the
    // element order below is a direct copy of the inverted matrix's rows.
    private void UploadInverseMvp(Matrix4 mvp)
    {
        Matrix4 inv = mvp.Inverted();

        _invMvpHost[0] = inv.M11; _invMvpHost[1] = inv.M12; _invMvpHost[2] = inv.M13; _invMvpHost[3] = inv.M14;
        _invMvpHost[4] = inv.M21; _invMvpHost[5] = inv.M22; _invMvpHost[6] = inv.M23; _invMvpHost[7] = inv.M24;
        _invMvpHost[8] = inv.M31; _invMvpHost[9] = inv.M32; _invMvpHost[10] = inv.M33; _invMvpHost[11] = inv.M34;
        _invMvpHost[12] = inv.M41; _invMvpHost[13] = inv.M42; _invMvpHost[14] = inv.M43; _invMvpHost[15] = inv.M44;

        _invMvpDevice!.CopyToDevice(_invMvpHost);
    }

    private void CompileKernel()
    {
        string kernelPath = Path.Combine(AppContext.BaseDirectory, "Kernels", "VolumeRender.cu");
        string kernelSource = File.ReadAllText(kernelPath);

        byte[] ptx;
        using (var rtc = new CudaRuntimeCompiler(kernelSource, "VolumeRender"))
        {
            // GTX 1080 is Pascal -> compute_61 / sm_61
            rtc.Compile(new[] { "--gpu-architecture=compute_61" });
            ptx = rtc.GetPTX();
        }

        _fillKernel = _ctx.LoadKernelPTX(ptx, "fillGradient");
        _sampleKernel = _ctx.LoadKernelPTX(ptx, "sampleVolume");
        _raycastKernel = _ctx.LoadKernelPTX(ptx, "raycastVolume");
    }

    private void CreateQuad()
    {
        _vertexArrayObject = GL.GenVertexArray();
        GL.BindVertexArray(_vertexArrayObject);

        _vertexBufferObject = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vertexBufferObject);
        GL.BufferData(BufferTarget.ArrayBuffer, QuadVertices.Length * sizeof(float), QuadVertices, BufferUsageHint.StaticDraw);

        _displayShader = new Shader("Shaders/display.vert", "Shaders/display.frag");
        _displayShader.Use();

        GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 0);
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 2 * sizeof(float));
        GL.EnableVertexAttribArray(1);

        _displayShader.SetInt("screenTexture", 0);
    }

    private void CreateScreenResources()
    {
        // Texture that receives the CUDA-rendered pixels.
        _texture = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, _texture);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, _width, _height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        GL.BindTexture(TextureTarget.Texture2D, 0);

        // PBO that CUDA writes into; one RGBA8 texel per pixel.
        _pixelBuffer = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.PixelUnpackBuffer, _pixelBuffer);
        GL.BufferData(BufferTarget.PixelUnpackBuffer, _width * _height * 4, IntPtr.Zero, BufferUsageHint.StreamDraw);
        GL.BindBuffer(BufferTarget.PixelUnpackBuffer, 0);

        // Register the PBO with CUDA for zero-copy writes.
        _cudaPixelBuffer = new CudaOpenGLBufferInteropResource((uint)_pixelBuffer, CUGraphicsRegisterFlags.None);
    }

    private void ReleaseScreenResources()
    {
        // Unregister from CUDA before deleting the GL buffer.
        _cudaPixelBuffer?.Dispose();
        _cudaPixelBuffer = null!;

        if (_pixelBuffer != 0)
        {
            GL.DeleteBuffer(_pixelBuffer);
            _pixelBuffer = 0;
        }

        if (_texture != 0)
        {
            GL.DeleteTexture(_texture);
            _texture = 0;
        }
    }

    public void Dispose()
    {
        ReleaseScreenResources();

        _volumeTexture?.Dispose();
        _volumeTexture = null;

        _invMvpDevice?.Dispose();
        _invMvpDevice = null;

        _lutDevice?.Dispose();
        _lutDevice = null;

        GL.DeleteBuffer(_vertexBufferObject);
        GL.DeleteVertexArray(_vertexArrayObject);

        if (_displayShader != null)
        {
            GL.DeleteProgram(_displayShader.Handle);
        }
    }
}
