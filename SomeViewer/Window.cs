using LearnOpenTK.Common;
using ManagedCuda;
using ManagedCuda.NVRTC;
using ManagedCuda.VectorTypes;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace SomeViewer
{
    public class Window : GameWindow
    {
        private readonly float[] _vertices =
        {
            -0.5f, -0.5f, 0.0f, // Bottom-left vertex
             0.5f, -0.5f, 0.0f, // Bottom-right vertex
             0.0f,  0.5f, 0.0f  // Top vertex
        };

        private int _vertexBufferObject;
        private int _vertexArrayObject;

        private Shader _shader;

        private PrimaryContext _ctx;
        private CudaKernel _kernel;
        private CudaDeviceVariable<short> _dInput;
        private CudaDeviceVariable<byte> _dOutput;

        public Window(GameWindowSettings gameWindowSettings, NativeWindowSettings nativeWindowSettings)
            : base(gameWindowSettings, nativeWindowSettings)
        {
        }

        protected override void OnLoad()
        {
            base.OnLoad();

            // create CUDA primary context
            int deviceID = 0;
            _ctx = new PrimaryContext(deviceID);
            // Set current to CPU thread, mandatory for a PrimaryContext
            _ctx.SetCurrent();

            // load the DICOM series from the manifest folder
            const string DicomFolder = @"C:\dev\data\manifest-1782357116242";

            var allSeries = DicomFolderLoader.LoadFolder(DicomFolder);
            var series = allSeries[0]; // pick the first series to view

            short[] volume = DicomFolderLoader.LoadVolumeInt16(series);
            // volume is [Columns * Rows * Depth], ready to upload to a CudaDeviceVariable<short>

            // compile Kernels/SomeKernel.cu at runtime (NVRTC) AFTER the volume is loaded
            string kernelPath = Path.Combine(AppContext.BaseDirectory, "Kernels", "SomeKernel.cu");
            string kernelSource = File.ReadAllText(kernelPath);

            byte[] ptx;
            using (var rtc = new CudaRuntimeCompiler(kernelSource, "SomeKernel"))
            {
                // GTX 1080 is Pascal -> compute_61 / sm_61
                rtc.Compile(new[] { "--gpu-architecture=compute_61" });
                ptx = rtc.GetPTX();
            }

            _kernel = _ctx.LoadKernelPTX(ptx, "WindowLevel");

            // upload the volume and run the kernel on the GPU
            _dInput = volume;                                   // host -> device copy
            _dOutput = new CudaDeviceVariable<byte>(volume.Length);

            int threads = 256;
            _kernel.BlockDimensions = new dim3(threads, 1, 1);
            _kernel.GridDimensions = new dim3((volume.Length + threads - 1) / threads, 1, 1);

            // arguments must match WindowLevel(short*, uchar*, int, float, float)
            _kernel.Run(_dInput.DevicePointer, _dOutput.DevicePointer,
                        volume.Length, 40.0f, 400.0f);

            // _dOutput now holds the windowed 8-bit volume on the GPU,
            // ready to upload into an OpenGL texture for rendering.
            // To copy back to the host: byte[] windowed = _dOutput;

            GL.ClearColor(0.2f, 0.3f, 0.3f, 1.0f);

            // VBO
            _vertexBufferObject = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, _vertexBufferObject);
            GL.BufferData(BufferTarget.ArrayBuffer, _vertices.Length * sizeof(float), _vertices, BufferUsageHint.StaticDraw);

            // VAO
            _vertexArrayObject = GL.GenVertexArray();
            GL.BindVertexArray(_vertexArrayObject);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);

            // Enable variable 0 in the shader.
            GL.EnableVertexAttribArray(0);

            _shader = new Shader("Shaders/shader.vert", "Shaders/shader.frag");
            _shader.Use();
        }

        // Now that initialization is done, let's create our render loop.
        protected override void OnRenderFrame(FrameEventArgs e)
        {
            base.OnRenderFrame(e);

            GL.Clear(ClearBufferMask.ColorBufferBit);

            _shader.Use();

            GL.BindVertexArray(_vertexArrayObject);
            GL.DrawArrays(PrimitiveType.Triangles, 0, 3);

            SwapBuffers();
        }

        protected override void OnUpdateFrame(FrameEventArgs e)
        {
            base.OnUpdateFrame(e);

            var input = KeyboardState;

            if (input.IsKeyDown(Keys.Escape))
            {
                Close();
            }
        }

        protected override void OnResize(ResizeEventArgs e)
        {
            base.OnResize(e);

            GL.Viewport(0, 0, Size.X, Size.Y);
        }

        protected override void OnUnload()
        {
            // Unbind all the resources by binding the targets to 0/null.
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
            GL.BindVertexArray(0);
            GL.UseProgram(0);

            // Delete all the resources.
            GL.DeleteBuffer(_vertexBufferObject);
            GL.DeleteVertexArray(_vertexArrayObject);

            GL.DeleteProgram(_shader.Handle);

            // Release CUDA resources.
            _dInput?.Dispose();
            _dOutput?.Dispose();
            _ctx?.Dispose();

            base.OnUnload();
        }
    }
}
