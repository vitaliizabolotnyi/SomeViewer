using ManagedCuda;
using ManagedCuda.NVRTC;
using ManagedCuda.VectorTypes;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using SomeViewer.Rendering;
using SomeViewer.Volumes;

namespace SomeViewer
{
    public class Window : GameWindow
    {
        // DICOM series to upload into the CUDA volume texture. If the folder is
        // missing the renderer falls back to the gradient test pattern.
        private const string DicomFolder = @"C:\dev\data\manifest-1782357116242";

        private IRenderer _renderer = null!;
        private OrbitCamera _camera = null!;
        private VolumeRenderer? _volumeRenderer;

        // Drag-to-orbit state.
        private bool _dragging;
        private Vector2 _lastMousePos;

        // Key-repeat throttle for window/level and step-size adjustments.
        private double _sinceKeyRepeat;

        // The CUDA primary context is created up front and reused by the renderer.
        private PrimaryContext _ctx = null!;

        // Kept for the preserved WindowLevel example only (see WindowLevelExample); not used by the renderer.
        private CudaKernel? _kernel;
        private CudaDeviceVariable<short>? _dInput;
        private CudaDeviceVariable<byte>? _dOutput;

        public Window(GameWindowSettings gameWindowSettings, NativeWindowSettings nativeWindowSettings)
            : base(gameWindowSettings, nativeWindowSettings)
        {
        }

        protected override void OnLoad()
        {
            base.OnLoad();

            // Create the CUDA primary context up front; the CUDA raycaster reuses
            // it. SetCurrent is mandatory for a PrimaryContext.
            const int deviceID = 0;
            _ctx = new PrimaryContext(deviceID);
            _ctx.SetCurrent();

            GL.ClearColor(0.1f, 0.1f, 0.12f, 1.0f);
            GL.Enable(EnableCap.DepthTest);

            _camera = new OrbitCamera(distance: 3f, aspectRatio: Size.X / (float)Size.Y);

            // Load the DICOM volume up front so the renderer can upload it to a
            // CUDA 3D texture. Fall back to the gradient if the data folder isn't
            // available on this machine.
            VolumeData? volume = TryLoadVolume();

            var volumeRenderer = new VolumeRenderer(_ctx, volume);
            _volumeRenderer = volumeRenderer;
            _renderer = volumeRenderer;
            _renderer.Load(Size.X, Size.Y);

            UpdateTitle();
        }

        private static VolumeData? TryLoadVolume()
        {
            if (!Directory.Exists(DicomFolder))
            {
                Console.WriteLine($"DICOM folder '{DicomFolder}' not found; rendering gradient fallback.");
                return null;
            }

            try
            {
                IVolumeDataService volumeService = new DicomVolumeDataService();
                VolumeData volume = volumeService.Load(DicomFolder);
                Console.WriteLine($"Loaded volume {volume.Width}x{volume.Height}x{volume.Depth} " +
                                  $"(intensity {volume.MinValue}..{volume.MaxValue}).");
                return volume;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load DICOM volume: {ex.Message}; rendering gradient fallback.");
                return null;
            }
        }

        // Now that initialization is done, let's create our render loop.
        protected override void OnRenderFrame(FrameEventArgs e)
        {
            base.OnRenderFrame(e);

            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            // The orbit camera drives the view now, so the model stays put.
            _renderer.Render(Matrix4.Identity, _camera.GetViewMatrix(), _camera.GetProjectionMatrix());

            SwapBuffers();
        }

        protected override void OnUpdateFrame(FrameEventArgs e)
        {
            base.OnUpdateFrame(e);

            if (KeyboardState.IsKeyDown(Keys.Escape))
            {
                Close();
            }

            HandleSettingKeys(e.Time);
        }

        // Window/level and step-size keys. Held keys repeat on a short timer so
        // adjustments are smooth without flying off after a single press.
        private void HandleSettingKeys(double dt)
        {
            if (_volumeRenderer == null)
            {
                return;
            }

            // Reset is a one-shot, not throttled.
            if (KeyboardState.IsKeyPressed(Keys.R))
            {
                _camera.Reset();
                _volumeRenderer.ResetSettings();
                UpdateTitle();
            }

            // Toggle perspective/orthographic. Orthographic removes depth-based
            // foreshortening, so the far side stops looking scaled when rotated.
            if (KeyboardState.IsKeyPressed(Keys.O))
            {
                _camera.ToggleOrthographic();
                UpdateTitle();
            }

            _sinceKeyRepeat += dt;
            if (_sinceKeyRepeat < 0.03)
            {
                return;
            }

            _sinceKeyRepeat = 0;
            bool changed = false;

            // Window level (center): Up/Down. Window width: Left/Right.
            if (KeyboardState.IsKeyDown(Keys.Up))
            {
                _volumeRenderer.AdjustWindowCenter(0.01f);
                changed = true;
            }

            if (KeyboardState.IsKeyDown(Keys.Down))
            {
                _volumeRenderer.AdjustWindowCenter(-0.01f);
                changed = true;
            }

            if (KeyboardState.IsKeyDown(Keys.Right))
            {
                _volumeRenderer.AdjustWindowWidth(0.01f);
                changed = true;
            }

            if (KeyboardState.IsKeyDown(Keys.Left))
            {
                _volumeRenderer.AdjustWindowWidth(-0.01f);
                changed = true;
            }

            // Step size (quality vs. speed): '[' finer, ']' coarser.
            if (KeyboardState.IsKeyDown(Keys.LeftBracket))
            {
                _volumeRenderer.ScaleStepSize(0.9f);
                changed = true;
            }

            if (KeyboardState.IsKeyDown(Keys.RightBracket))
            {
                _volumeRenderer.ScaleStepSize(1.1f);
                changed = true;
            }

            if (changed)
            {
                UpdateTitle();
            }
        }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            base.OnMouseDown(e);

            if (e.Button == MouseButton.Left)
            {
                _dragging = true;
                _lastMousePos = MousePosition;
            }
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            base.OnMouseUp(e);

            if (e.Button == MouseButton.Left)
            {
                _dragging = false;
            }
        }

        protected override void OnMouseMove(MouseMoveEventArgs e)
        {
            base.OnMouseMove(e);

            if (!_dragging)
            {
                return;
            }

            // Pixel drag -> orbit radians. Negate so the volume follows the cursor.
            const float sensitivity = 0.0035f;
            Vector2 delta = MousePosition - _lastMousePos;
            _lastMousePos = MousePosition;

            _camera.Orbit(-delta.X * sensitivity, -delta.Y * sensitivity);
            UpdateTitle();
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);

            // Scroll up = zoom in.
            _camera.Zoom(e.OffsetY * 0.1f);
            UpdateTitle();
        }

        private void UpdateTitle()
        {
            if (_volumeRenderer == null)
            {
                Title = "SomeViewer";
                return;
            }

            Title = $"SomeViewer — Drag: orbit | Scroll: zoom | Arrows: window/level | [ ]: step | O: ortho | R: reset   " +
                    $"(yaw {MathHelper.RadiansToDegrees(_camera.Yaw):0.0}°, pitch {MathHelper.RadiansToDegrees(_camera.Pitch):0.0}°, " +
                    $"zoom {_camera.ZoomFactor:0.00}x, dist {_camera.Distance:0.00}, " +
                    $"{(_camera.Orthographic ? "ortho" : "persp")}, " +
                    $"center {_volumeRenderer.WindowCenter:0.00}, width {_volumeRenderer.WindowWidth:0.00}, " +
                    $"step {_volumeRenderer.StepSize:0.0000})";
        }

        protected override void OnResize(ResizeEventArgs e)
        {
            base.OnResize(e);

            GL.Viewport(0, 0, Size.X, Size.Y);

            _camera.AspectRatio = Size.X / (float)Size.Y;
            _renderer.Resize(Size.X, Size.Y);
        }

        protected override void OnUnload()
        {
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
            GL.BindVertexArray(0);
            GL.UseProgram(0);

            _renderer?.Dispose();

            // Release CUDA resources.
            _dInput?.Dispose();
            _dOutput?.Dispose();
            _ctx?.Dispose();

            base.OnUnload();
        }

        // Preserved example only — the original window/level pass over the DICOM
        // volume. Intentionally NOT called; kept for reference per project notes.
        // The CUDA raycaster (see docs/VolumeRaycastingPlan.md) replaces this.
        private void WindowLevelExample()
        {
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
        }
    }
}
