using ManagedCuda;
using ManagedCuda.BasicTypes;
using ManagedCuda.NVRTC;
using ManagedCuda.VectorTypes;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using SomeViewer.GlPrimitives;

namespace SomeViewer.Rendering;

// Shared CUDA-backed renderer base. Owns the CUDA <-> OpenGL display pipeline:
// a CUDA kernel writes RGBA pixels into an OpenGL pixel buffer object (PBO)
// registered for interop, the PBO is copied into a texture and drawn on a
// fullscreen quad. It also compiles the Kernels/VolumeRender.cu module
// once (NVRTC) and exposes the loaded kernels, the launch dimensions, and an
// inverse-MVP upload helper to subclasses.
//
// Subclasses implement OnLoad to create their own resources
// (volume texture, LUT, kernel handles) and Dispatch to launch
// the kernel that fills the mapped PBO for one frame.
public abstract class CudaRendererBase : IRenderer
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

    // The compiled PTX for Kernels/VolumeRender.cu; subclasses pick kernels by name.
    private byte[] _ptx = null!;

    private Shader _displayShader = null!;
    private int _vertexArrayObject;
    private int _vertexBufferObject;

    private int _texture;
    private int _pixelBuffer;
    private CudaOpenGLBufferInteropResource _cudaPixelBuffer = null!;

    // Inverse model-view-projection uploaded so view-dependent kernels can build
    // eye rays. Row-major (m[i*4+j] = M[i,j]) to match the kernel's unproject.
    private readonly float[] _invMvpHost = new float[16];
    private CudaDeviceVariable<float>? _invMvpDevice;

    protected CudaRendererBase(PrimaryContext ctx)
    {
        Context = ctx;
    }

    // The CUDA primary context shared by all renderers.
    protected PrimaryContext Context { get; }

    // Current framebuffer width in pixels.
    protected int Width { get; private set; }

    // Current framebuffer height in pixels.
    protected int Height { get; private set; }

    public void Load(int width, int height)
    {
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);

        CompileModule();
        CreateQuad();
        CreateScreenResources();
        _invMvpDevice = new CudaDeviceVariable<float>(16);

        OnLoad();
    }

    public void Resize(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }

        if (width == Width && height == Height)
        {
            return;
        }

        Width = width;
        Height = height;

        ReleaseScreenResources();
        CreateScreenResources();
    }

    public void Render(Matrix4 model, Matrix4 view, Matrix4 projection)
    {
        // 1. CUDA writes this frame's pixels into the mapped PBO.
        _cudaPixelBuffer.Map();
        CUdeviceptr output = _cudaPixelBuffer.GetMappedPointer();

        DoRender(output, model, view, projection);

        _cudaPixelBuffer.UnMap();

        // 2. Copy the PBO into the texture (GL reads from the bound unpack buffer).
        GL.BindBuffer(BufferTarget.PixelUnpackBuffer, _pixelBuffer);
        GL.BindTexture(TextureTarget.Texture2D, _texture);
        GL.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, Width, Height, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
        GL.BindBuffer(BufferTarget.PixelUnpackBuffer, 0);

        // 3. Blit the texture to the screen with a fullscreen quad.
        GL.Disable(EnableCap.DepthTest);
        _displayShader.Use();
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, _texture);
        GL.BindVertexArray(_vertexArrayObject);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 6);
    }

    // Create subclass-specific resources (volume texture, LUT, kernels).
    protected abstract void OnLoad();

    // Launch the kernel that fills the mapped PBO for one frame.
    protected abstract void DoRender(CUdeviceptr output, Matrix4 model, Matrix4 view, Matrix4 projection);

    // Release subclass-specific resources. Called from Dispose.
    protected virtual void OnDispose()
    {
    }

    // Load a kernel by name from the compiled VolumeRender.cu module.
    protected CudaKernel LoadKernel(string name) => Context.LoadKernelPTX(_ptx, name);

    // Standard 16x16 block with a grid covering the framebuffer.
    protected (dim3 Block, dim3 Grid) LaunchDimensions()
    {
        var block = new dim3(16, 16, 1);
        var grid = new dim3((Width + 15) / 16, (Height + 15) / 16, 1);
        return (block, grid);
    }

    // Device pointer to the uploaded inverse-MVP (16 floats, row-major).
    protected CUdeviceptr InverseMvpPointer => _invMvpDevice!.DevicePointer;

    // Inverts the MVP and uploads it row-major (m[i*4+j] = M[i,j]) so the kernel's
    // unproject can map clip-space pixels into volume-local space. OpenTK stores
    // matrices row-major with the same vec*matrix convention as the shader, so the
    // element order below is a direct copy of the inverted matrix's rows.
    protected void UploadInverseMvp(Matrix4 mvp)
    {
        Matrix4 inv = mvp.Inverted();

        _invMvpHost[0] = inv.M11; _invMvpHost[1] = inv.M12; _invMvpHost[2] = inv.M13; _invMvpHost[3] = inv.M14;
        _invMvpHost[4] = inv.M21; _invMvpHost[5] = inv.M22; _invMvpHost[6] = inv.M23; _invMvpHost[7] = inv.M24;
        _invMvpHost[8] = inv.M31; _invMvpHost[9] = inv.M32; _invMvpHost[10] = inv.M33; _invMvpHost[11] = inv.M34;
        _invMvpHost[12] = inv.M41; _invMvpHost[13] = inv.M42; _invMvpHost[14] = inv.M43; _invMvpHost[15] = inv.M44;

        _invMvpDevice!.CopyToDevice(_invMvpHost);
    }

    private void CompileModule()
    {
        string kernelPath = Path.Combine(AppContext.BaseDirectory, "Kernels", "VolumeRender.cu");
        string kernelSource = File.ReadAllText(kernelPath);

        using var rtc = new CudaRuntimeCompiler(kernelSource, "VolumeRender");
        // GTX 1080 is Pascal -> compute_61 / sm_61
        rtc.Compile(new[] { "--gpu-architecture=compute_61" });
        _ptx = rtc.GetPTX();
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
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, Width, Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        GL.BindTexture(TextureTarget.Texture2D, 0);

        // PBO that CUDA writes into; one RGBA8 texel per pixel.
        _pixelBuffer = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.PixelUnpackBuffer, _pixelBuffer);
        GL.BufferData(BufferTarget.PixelUnpackBuffer, Width * Height * 4, IntPtr.Zero, BufferUsageHint.StreamDraw);
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

        OnDispose();

        _invMvpDevice?.Dispose();
        _invMvpDevice = null;

        GL.DeleteBuffer(_vertexBufferObject);
        GL.DeleteVertexArray(_vertexArrayObject);

        if (_displayShader != null)
        {
            GL.DeleteProgram(_displayShader.Handle);
        }
    }
}
