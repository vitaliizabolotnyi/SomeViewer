using OpenTK.Graphics.OpenGL4;

namespace SomeViewer.Rendering;

/// <summary>
/// Minimal GLSL shader-program helper: compiles a vertex+fragment pair, links
/// them, and caches uniform locations. Replaces the former
/// <c>LearnOpenTK.Common.Shader</c> so the viewer no longer depends on the
/// Common project.
/// </summary>
public sealed class Shader
{
    /// <summary>The linked GL program handle.</summary>
    public readonly int Handle;

    private readonly Dictionary<string, int> _uniformLocations = new();

    public Shader(string vertPath, string fragPath)
    {
        int vertexShader = CompileShader(ShaderType.VertexShader, File.ReadAllText(vertPath));
        int fragmentShader = CompileShader(ShaderType.FragmentShader, File.ReadAllText(fragPath));

        Handle = GL.CreateProgram();
        GL.AttachShader(Handle, vertexShader);
        GL.AttachShader(Handle, fragmentShader);
        LinkProgram(Handle);

        // The individual shaders are baked into the program once linked.
        GL.DetachShader(Handle, vertexShader);
        GL.DetachShader(Handle, fragmentShader);
        GL.DeleteShader(fragmentShader);
        GL.DeleteShader(vertexShader);

        // Cache uniform locations up front; querying per-set call is slow.
        GL.GetProgram(Handle, GetProgramParameterName.ActiveUniforms, out int uniformCount);
        for (int i = 0; i < uniformCount; i++)
        {
            string key = GL.GetActiveUniform(Handle, i, out _, out _);
            _uniformLocations[key] = GL.GetUniformLocation(Handle, key);
        }
    }

    /// <summary>Bind this shader program for subsequent draws.</summary>
    public void Use()
    {
        GL.UseProgram(Handle);
    }

    /// <summary>Set an int uniform (e.g. a sampler texture unit) by name.</summary>
    public void SetInt(string name, int data)
    {
        GL.UseProgram(Handle);
        GL.Uniform1(_uniformLocations[name], data);
    }

    private static int CompileShader(ShaderType type, string source)
    {
        int shader = GL.CreateShader(type);
        GL.ShaderSource(shader, source);
        GL.CompileShader(shader);

        GL.GetShader(shader, ShaderParameter.CompileStatus, out int status);
        if (status != (int)All.True)
        {
            string infoLog = GL.GetShaderInfoLog(shader);
            throw new Exception($"Error compiling {type} ({shader}).\n\n{infoLog}");
        }

        return shader;
    }

    private static void LinkProgram(int program)
    {
        GL.LinkProgram(program);

        GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int status);
        if (status != (int)All.True)
        {
            string infoLog = GL.GetProgramInfoLog(program);
            throw new Exception($"Error linking program ({program}).\n\n{infoLog}");
        }
    }
}
