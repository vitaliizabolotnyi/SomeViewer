using OpenTK.Graphics.OpenGL4;

namespace SomeViewer;

/// <summary>
/// Compiles and links a GLSL vertex/fragment shader program and exposes
/// uniform-setter helpers.
/// </summary>
internal sealed class Shader : IDisposable
{
    private readonly int _handle;
    private bool _disposed;

    public Shader(string vertexPath, string fragmentPath)
    {
        string vertexSource = File.ReadAllText(vertexPath);
        string fragmentSource = File.ReadAllText(fragmentPath);

        int vertexShader = CompileShader(ShaderType.VertexShader, vertexSource);
        int fragmentShader = CompileShader(ShaderType.FragmentShader, fragmentSource);

        _handle = GL.CreateProgram();
        GL.AttachShader(_handle, vertexShader);
        GL.AttachShader(_handle, fragmentShader);
        GL.LinkProgram(_handle);

        GL.GetProgram(_handle, GetProgramParameterName.LinkStatus, out int linkStatus);
        if (linkStatus == 0)
        {
            string info = GL.GetProgramInfoLog(_handle);
            throw new InvalidOperationException($"Shader program link error: {info}");
        }

        GL.DetachShader(_handle, vertexShader);
        GL.DetachShader(_handle, fragmentShader);
        GL.DeleteShader(vertexShader);
        GL.DeleteShader(fragmentShader);
    }

    public void Use() => GL.UseProgram(_handle);

    public void SetMatrix4(string name, OpenTK.Mathematics.Matrix4 matrix)
    {
        int location = GL.GetUniformLocation(_handle, name);
        GL.UniformMatrix4(location, false, ref matrix);
    }

    public void SetMatrix3(string name, OpenTK.Mathematics.Matrix3 matrix)
    {
        int location = GL.GetUniformLocation(_handle, name);
        GL.UniformMatrix3(location, false, ref matrix);
    }

    public void SetVector3(string name, OpenTK.Mathematics.Vector3 vector)
    {
        int location = GL.GetUniformLocation(_handle, name);
        GL.Uniform3(location, vector);
    }

    private static int CompileShader(ShaderType type, string source)
    {
        int shader = GL.CreateShader(type);
        GL.ShaderSource(shader, source);
        GL.CompileShader(shader);

        GL.GetShader(shader, ShaderParameter.CompileStatus, out int compileStatus);
        if (compileStatus == 0)
        {
            string info = GL.GetShaderInfoLog(shader);
            GL.DeleteShader(shader);
            throw new InvalidOperationException($"{type} compile error: {info}");
        }

        return shader;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            GL.DeleteProgram(_handle);
            _disposed = true;
        }
    }
}
