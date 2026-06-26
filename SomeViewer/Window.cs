using ManagedCuda;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using SomeViewer.GlPrimitives;
using SomeViewer.Model;
using SomeViewer.Rendering;
using SomeViewer.Scenes;
using SomeViewer.Services;

namespace SomeViewer
{
    public class Window : GameWindow
    {
        // DICOM series to upload into the CUDA volume texture. If the folder is
        // missing the renderer falls back to the gradient test pattern.
        private const string DicomFolderCT = @"C:\dev\data\manifest-1782357116242";
        private const string DicomFolderMR = @"C:\dev\data\manifest-1782447338589";

        private TrackballCamera _camera = null!;
        private ScenesController _scenes = null!;

        // CPU-side volume, loaded once and cached so scene switches can re-upload
        // it to a CUDA 3D texture without re-reading the DICOM folder from disk.
        private VolumeData? _volume1;
        //private VolumeData? _volume2;

        // Drag-to-rotate state.
        private bool _dragging;
        private Vector2 _lastMousePos;

        // Key-repeat throttle for window/level and step-size adjustments.
        private double _sinceKeyRepeat;

        // The CUDA primary context is created up front and reused by the renderer.
        private PrimaryContext _ctx = null!;

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

            _camera = new TrackballCamera(distance: 3f, aspectRatio: Size.X / (float)Size.Y);

            // Load the DICOM volume up front and cache it so scene switches can
            // re-upload it without re-reading the folder. Fall back to a single
            // gradient scene if the data folder isn't available on this machine.
            _volume1 = TryLoadVolume(DicomFolderCT);
            //_volume2 = TryLoadVolume(DicomFolderMR);

            _scenes = new ScenesController(BuildScenes(_volume1, null));
            _scenes.Activate(0, Size.X, Size.Y);

            UpdateTitle();
        }

        // Builds the switchable scene list shown on keys 1/2/3. With a volume
        // loaded: raycast DVR, the middle-slice sampler, and the gradient pattern
        // (the gradient slot is where a cube/proxy scene can live later). Without
        // a volume, only the gradient scene is registered.
        private IReadOnlyList<Scene> BuildScenes(VolumeData? volume1, VolumeData? volume2)
        {
            if (volume1 == null)// || volume2 == null)
            {
                return new[]
                {
                    new Scene("Gradient", () => new GradientRenderer(_ctx)),
                };
            }

            return new[]
            {
                new Scene("Raycast DVR CT", () => new RaycastVolumeRenderer(_ctx, volume1)),
                //new Scene("Raycast DVR MR", () => new RaycastVolumeRenderer(_ctx, volume2)),
                new Scene("Middle Slice", () => new SliceVolumeRenderer(_ctx, volume1)),
                new Scene("Gradient", () => new GradientRenderer(_ctx)),
            };
        }

        private static VolumeData? TryLoadVolume(string dicomDir)
        {
            if (!Directory.Exists(dicomDir))
            {
                Console.WriteLine($"DICOM folder '{dicomDir}' not found; rendering gradient fallback.");
                return null;
            }

            try
            {
                IVolumeDataService volumeService = new DicomVolumeDataService();
                VolumeData volume = volumeService.Load(dicomDir);
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

            // The trackball camera drives the view now, so the model stays put.
            _scenes.Render(Matrix4.Identity, _camera.GetViewMatrix(), _camera.GetProjectionMatrix());

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

        // Scene switching plus window/level and step-size keys. Held keys repeat
        // on a short timer so adjustments are smooth without flying off after a
        // single press.
        private void HandleSettingKeys(double dt)
        {
            // Scene switching (1/2/3): one-shot. Switching recreates the active
            // renderer (compile/upload on switch); out-of-range keys are ignored.
            if (KeyboardState.IsKeyPressed(Keys.D1))
            {
                SwitchScene(0);
            }
            else if (KeyboardState.IsKeyPressed(Keys.D2))
            {
                SwitchScene(1);
            }
            else if (KeyboardState.IsKeyPressed(Keys.D3))
            {
                SwitchScene(2);
            }

            // Window/level and step-size apply only to scenes that support them.
            var controls = _scenes.ActiveRenderer as IVolumeControls;

            // Reset is a one-shot, not throttled.
            if (KeyboardState.IsKeyPressed(Keys.R))
            {
                _camera.Reset();
                controls?.ResetSettings();
                UpdateTitle();
            }

            // Toggle perspective/orthographic. Orthographic removes depth-based
            // foreshortening, so the far side stops looking scaled when rotated.
            if (KeyboardState.IsKeyPressed(Keys.O))
            {
                _camera.ToggleOrthographic();
                UpdateTitle();
            }

            if (controls == null)
            {
                return;
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
                controls.AdjustWindowCenter(0.01f);
                changed = true;
            }

            if (KeyboardState.IsKeyDown(Keys.Down))
            {
                controls.AdjustWindowCenter(-0.01f);
                changed = true;
            }

            if (KeyboardState.IsKeyDown(Keys.Right))
            {
                controls.AdjustWindowWidth(0.01f);
                changed = true;
            }

            if (KeyboardState.IsKeyDown(Keys.Left))
            {
                controls.AdjustWindowWidth(-0.01f);
                changed = true;
            }

            // Step size (quality vs. speed): '[' finer, ']' coarser.
            if (KeyboardState.IsKeyDown(Keys.LeftBracket))
            {
                controls.ScaleStepSize(0.9f);
                changed = true;
            }

            if (KeyboardState.IsKeyDown(Keys.RightBracket))
            {
                controls.ScaleStepSize(1.1f);
                changed = true;
            }

            if (changed)
            {
                UpdateTitle();
            }
        }

        // Switch to the scene at the given index (no-op if already active or out
        // of range) and refresh the title.
        private void SwitchScene(int index)
        {
            _scenes.Activate(index, Size.X, Size.Y);
            UpdateTitle();
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

            // Pixel drag -> trackball rotation (radians). Pass the raw drag delta
            // (right/down positive); the camera picks signs so the volume follows
            // the cursor and rotates about screen-aligned axes from any angle.
            const float sensitivity = 0.0035f;
            Vector2 delta = MousePosition - _lastMousePos;
            _lastMousePos = MousePosition;

            _camera.Rotate(delta.X * sensitivity, delta.Y * sensitivity);
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
            float rotationDegrees = MathHelper.RadiansToDegrees(
                2f * MathF.Acos(MathHelper.Clamp(_camera.Orientation.W, -1f, 1f)));

            // Window/level/step read-outs only apply to scenes that expose controls.
            string tuning = _scenes.ActiveRenderer is IVolumeControls c
                ? $", center {c.WindowCenter:0.00}, width {c.WindowWidth:0.00}, step {c.StepSize:0.0000}"
                : string.Empty;

            Title = $"SomeViewer — [{_scenes.ActiveName}] | 1/2/3: scene | Drag: trackball | Scroll: zoom | " +
                    $"Arrows: window/level | [ ]: step | O: ortho | R: reset   " +
                    $"{(_camera.Orthographic ? "ortho" : "persp")}{tuning})";
        }

        protected override void OnResize(ResizeEventArgs e)
        {
            base.OnResize(e);

            GL.Viewport(0, 0, Size.X, Size.Y);

            _camera.AspectRatio = Size.X / (float)Size.Y;
            _scenes.Resize(Size.X, Size.Y);
        }

        protected override void OnUnload()
        {
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
            GL.BindVertexArray(0);
            GL.UseProgram(0);

            _scenes?.Dispose();

            // Release CUDA resources.
            _ctx?.Dispose();

            base.OnUnload();
        }
    }
}
