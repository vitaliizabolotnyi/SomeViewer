using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace SomeViewer;

/// <summary>
/// Main application window.  Renders a Phong-lit, rotating cube via
/// OpenGL 3.3 Core Profile.
/// </summary>
internal sealed class ViewerWindow : GameWindow
{
    // ── cube geometry ────────────────────────────────────────────────────────
    // 6 faces × 2 triangles × 3 vertices = 36 vertices.
    // Each vertex: position (xyz) + normal (xyz).
    private static readonly float[] CubeVertices =
    {
        // Back face
        -0.5f, -0.5f, -0.5f,  0.0f,  0.0f, -1.0f,
         0.5f,  0.5f, -0.5f,  0.0f,  0.0f, -1.0f,
         0.5f, -0.5f, -0.5f,  0.0f,  0.0f, -1.0f,
         0.5f,  0.5f, -0.5f,  0.0f,  0.0f, -1.0f,
        -0.5f, -0.5f, -0.5f,  0.0f,  0.0f, -1.0f,
        -0.5f,  0.5f, -0.5f,  0.0f,  0.0f, -1.0f,
        // Front face
        -0.5f, -0.5f,  0.5f,  0.0f,  0.0f,  1.0f,
         0.5f, -0.5f,  0.5f,  0.0f,  0.0f,  1.0f,
         0.5f,  0.5f,  0.5f,  0.0f,  0.0f,  1.0f,
         0.5f,  0.5f,  0.5f,  0.0f,  0.0f,  1.0f,
        -0.5f,  0.5f,  0.5f,  0.0f,  0.0f,  1.0f,
        -0.5f, -0.5f,  0.5f,  0.0f,  0.0f,  1.0f,
        // Left face
        -0.5f,  0.5f,  0.5f, -1.0f,  0.0f,  0.0f,
        -0.5f,  0.5f, -0.5f, -1.0f,  0.0f,  0.0f,
        -0.5f, -0.5f, -0.5f, -1.0f,  0.0f,  0.0f,
        -0.5f, -0.5f, -0.5f, -1.0f,  0.0f,  0.0f,
        -0.5f, -0.5f,  0.5f, -1.0f,  0.0f,  0.0f,
        -0.5f,  0.5f,  0.5f, -1.0f,  0.0f,  0.0f,
        // Right face
         0.5f,  0.5f,  0.5f,  1.0f,  0.0f,  0.0f,
         0.5f, -0.5f, -0.5f,  1.0f,  0.0f,  0.0f,
         0.5f,  0.5f, -0.5f,  1.0f,  0.0f,  0.0f,
         0.5f, -0.5f, -0.5f,  1.0f,  0.0f,  0.0f,
         0.5f,  0.5f,  0.5f,  1.0f,  0.0f,  0.0f,
         0.5f, -0.5f,  0.5f,  1.0f,  0.0f,  0.0f,
        // Bottom face
        -0.5f, -0.5f, -0.5f,  0.0f, -1.0f,  0.0f,
         0.5f, -0.5f, -0.5f,  0.0f, -1.0f,  0.0f,
         0.5f, -0.5f,  0.5f,  0.0f, -1.0f,  0.0f,
         0.5f, -0.5f,  0.5f,  0.0f, -1.0f,  0.0f,
        -0.5f, -0.5f,  0.5f,  0.0f, -1.0f,  0.0f,
        -0.5f, -0.5f, -0.5f,  0.0f, -1.0f,  0.0f,
        // Top face
        -0.5f,  0.5f, -0.5f,  0.0f,  1.0f,  0.0f,
         0.5f,  0.5f,  0.5f,  0.0f,  1.0f,  0.0f,
         0.5f,  0.5f, -0.5f,  0.0f,  1.0f,  0.0f,
         0.5f,  0.5f,  0.5f,  0.0f,  1.0f,  0.0f,
        -0.5f,  0.5f, -0.5f,  0.0f,  1.0f,  0.0f,
        -0.5f,  0.5f,  0.5f,  0.0f,  1.0f,  0.0f,
    };

    private int _vao;
    private int _vbo;
    private Shader _shader = null!;
    private float _totalTime;

    public ViewerWindow(GameWindowSettings gameSettings, NativeWindowSettings nativeSettings)
        : base(gameSettings, nativeSettings) { }

    protected override void OnLoad()
    {
        base.OnLoad();

        GL.ClearColor(0.12f, 0.12f, 0.18f, 1.0f);
        GL.Enable(EnableCap.DepthTest);

        // Upload cube geometry
        _vao = GL.GenVertexArray();
        _vbo = GL.GenBuffer();

        GL.BindVertexArray(_vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
        GL.BufferData(BufferTarget.ArrayBuffer,
            CubeVertices.Length * sizeof(float),
            CubeVertices,
            BufferUsageHint.StaticDraw);

        int stride = 6 * sizeof(float);
        // Position (location 0)
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, 0);
        GL.EnableVertexAttribArray(0);
        // Normal (location 1)
        GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, 3 * sizeof(float));
        GL.EnableVertexAttribArray(1);

        GL.BindVertexArray(0);

        // Resolve shader paths relative to the executable
        string baseDir = AppContext.BaseDirectory;
        _shader = new Shader(
            Path.Combine(baseDir, "Shaders", "vertex.glsl"),
            Path.Combine(baseDir, "Shaders", "fragment.glsl"));
    }

    protected override void OnUpdateFrame(FrameEventArgs e)
    {
        base.OnUpdateFrame(e);

        _totalTime += (float)e.Time;

        if (KeyboardState.IsKeyDown(Keys.Escape))
            Close();
    }

    protected override void OnRenderFrame(FrameEventArgs e)
    {
        base.OnRenderFrame(e);

        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        _shader.Use();

        // Rotate the cube over time
        Matrix4 model =
            Matrix4.CreateRotationX(_totalTime * 0.5f) *
            Matrix4.CreateRotationY(_totalTime * 0.8f);

        Vector3 cameraPos = new Vector3(0.0f, 0.5f, 3.0f);
        Matrix4 view = Matrix4.LookAt(cameraPos, Vector3.Zero, Vector3.UnitY);
        Matrix4 projection = Matrix4.CreatePerspectiveFieldOfView(
            MathHelper.DegreesToRadians(45.0f),
            (float)Size.X / Size.Y,
            0.1f,
            100.0f);

        _shader.SetMatrix4("uModel", model);
        _shader.SetMatrix4("uView", view);
        _shader.SetMatrix4("uProjection", projection);

        // Normal matrix: transpose of the inverse of the upper-left 3×3 of model
        Matrix3 normalMatrix = new Matrix3(Matrix4.Transpose(Matrix4.Invert(model)));
        _shader.SetMatrix3("uNormalMatrix", normalMatrix);

        _shader.SetVector3("uLightPos", new Vector3(2.0f, 3.0f, 4.0f));
        _shader.SetVector3("uLightColor", new Vector3(1.0f, 1.0f, 1.0f));
        _shader.SetVector3("uObjectColor", new Vector3(0.2f, 0.6f, 1.0f));
        _shader.SetVector3("uViewPos", cameraPos);

        GL.BindVertexArray(_vao);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 36);
        GL.BindVertexArray(0);

        SwapBuffers();
    }

    protected override void OnResize(ResizeEventArgs e)
    {
        base.OnResize(e);
        GL.Viewport(0, 0, e.Width, e.Height);
    }

    protected override void OnUnload()
    {
        GL.DeleteVertexArray(_vao);
        GL.DeleteBuffer(_vbo);
        _shader.Dispose();
        base.OnUnload();
    }
}
