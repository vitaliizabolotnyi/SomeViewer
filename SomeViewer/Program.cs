using FellowOakDicom;
using Microsoft.Extensions.Hosting;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace SomeViewer;

public static class Program
{
    private static void Main(string[] args)
    {
        var host = Host
            .CreateDefaultBuilder(args)
            .ConfigureServices(services =>
            {
                services.AddFellowOakDicom();
            })
            .Build();

        // This is still necessary for now until fo-dicom has first-class AspNetCore integration
        DicomSetupBuilder.UseServiceProvider(host.Services);

        const int Scale = 2;

        var nativeWindowSettings = new NativeWindowSettings()
        {
            ClientSize = new Vector2i(1024 * Scale, 768 * Scale),
            Title = "SomeViewer",
            // This is needed to run on macos
            Flags = ContextFlags.ForwardCompatible,
        };

        using (var window = new Window(GameWindowSettings.Default, nativeWindowSettings))
        {
            window.Run();
        }
    }
}
