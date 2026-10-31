using System.Globalization;
using Gutachten2025.Services;
using Gutachtentool.Oberflaeche.Dienste;
using Microsoft.Extensions.DependencyInjection;
using Photino.Blazor;

namespace Gutachtentool.Mac;

/// <summary>
/// Mac-Fassung: natives Fenster mit WebKit (Photino); Oberfläche wie die Browser-Fassung,
/// Daten als echte Dateien (Ablage Standard ~/Documents/Klausuren, Einstellungen ~/Library/Application Support/Gutachtentool).
/// </summary>
static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        // Aus dem Finder gestartet ist das Arbeitsverzeichnis „/“; Photino sucht wwwroot relativ dazu.
        Directory.SetCurrentDirectory(AppContext.BaseDirectory);

        CultureInfo kultur = CultureInfo.GetCultureInfo("de-DE");
        CultureInfo.DefaultThreadCurrentCulture = kultur;
        CultureInfo.DefaultThreadCurrentUICulture = kultur;
        CultureInfo.CurrentCulture = kultur;
        CultureInfo.CurrentUICulture = kultur;

        PhotinoBlazorAppBuilder builder = PhotinoBlazorAppBuilder.CreateDefault(args);
        builder.Services.AddLogging();
        builder.Services.AddSingleton<IPlattform, MacPlattform>();
        builder.Services.AddSingleton<UiDienst>();
        builder.Services.AddSingleton<EntwurfEditor>();
        builder.RootComponents.Add<Gutachtentool.Oberflaeche.App>("app");

        PhotinoBlazorApp app = builder.Build();
        app.MainWindow
            .SetTitle("Gutachtentool")
            .SetUseOsDefaultSize(false)
            .SetSize(1440, 920)
            .SetDevToolsEnabled(false)
            .Center();

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) EinstellungsSpeicher.Protokollieren(ex);
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            EinstellungsSpeicher.Protokollieren(e.Exception);
            e.SetObserved();
        };

        app.Run();
    }
}
