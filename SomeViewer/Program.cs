using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using SomeViewer;

var nativeSettings = new NativeWindowSettings
{
    ClientSize = new Vector2i(1280, 720),
    Title = "SomeViewer – OpenGL",
    Profile = ContextProfile.Core,
    APIVersion = new Version(3, 3),
};

using var window = new ViewerWindow(GameWindowSettings.Default, nativeSettings);
window.Run();
