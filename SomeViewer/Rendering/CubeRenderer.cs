using LearnOpenTK.Common;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace SomeViewer.Rendering;

/// <summary>
/// Milestone 1 proxy geometry: a unit cube centered on the origin, colored by
/// local position so each face is distinguishable. Validates the camera, the
/// model/view/projection pipeline, and depth testing before the CUDA raycaster
/// replaces it on a fullscreen quad.
/// </summary>
public sealed class CubeRenderer : IRenderer
{
    // 8 corners of a unit cube spanning [-0.5, 0.5] on each axis.
    private static readonly float[] Vertices =
    {
        -0.5f, -0.5f, -0.5f,
         0.5f, -0.5f, -0.5f,
         0.5f,  0.5f, -0.5f,
        -0.5f,  0.5f, -0.5f,
        -0.5f, -0.5f,  0.5f,
         0.5f, -0.5f,  0.5f,
         0.5f,  0.5f,  0.5f,
        -0.5f,  0.5f,  0.5f,
    };

    // 12 triangles (two per face).
    private static readonly uint[] Indices =
    {
        0, 1, 2, 2, 3, 0, // back  (z = -0.5)
        4, 5, 6, 6, 7, 4, // front (z =  0.5)
        0, 3, 7, 7, 4, 0, // left  (x = -0.5)
        1, 5, 6, 6, 2, 1, // right (x =  0.5)
        0, 1, 5, 5, 4, 0, // bottom(y = -0.5)
        3, 2, 6, 6, 7, 3, // top   (y =  0.5)
    };

    private int _vertexArrayObject;
    private int _vertexBufferObject;
    private int _elementBufferObject;
    private Shader _shader = null!;

    public void Load(int width, int height)
    {
        _vertexArrayObject = GL.GenVertexArray();
        GL.BindVertexArray(_vertexArrayObject);

        _vertexBufferObject = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vertexBufferObject);
        GL.BufferData(BufferTarget.ArrayBuffer, Vertices.Length * sizeof(float), Vertices, BufferUsageHint.StaticDraw);

        _elementBufferObject = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, _elementBufferObject);
        GL.BufferData(BufferTarget.ElementArrayBuffer, Indices.Length * sizeof(uint), Indices, BufferUsageHint.StaticDraw);

        _shader = new Shader("Shaders/shader.vert", "Shaders/shader.frag");
        _shader.Use();

        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);
        GL.EnableVertexAttribArray(0);
    }

    public void Resize(int width, int height)
    {
        // Projection is supplied per-frame via Render; nothing cached here yet.
    }

    public void Render(Matrix4 model, Matrix4 view, Matrix4 projection)
    {
        _shader.Use();
        _shader.SetMatrix4("model", model);
        _shader.SetMatrix4("view", view);
        _shader.SetMatrix4("projection", projection);

        GL.BindVertexArray(_vertexArrayObject);
        GL.DrawElements(PrimitiveType.Triangles, Indices.Length, DrawElementsType.UnsignedInt, 0);
    }

    public void Dispose()
    {
        GL.DeleteBuffer(_vertexBufferObject);
        GL.DeleteBuffer(_elementBufferObject);
        GL.DeleteVertexArray(_vertexArrayObject);

        if (_shader != null)
        {
            GL.DeleteProgram(_shader.Handle);
        }
    }
}
