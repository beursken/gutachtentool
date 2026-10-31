using Gutachten2025.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Gutachten2025;

/// <summary>
/// Eigener Einstiegspunkt (DISABLE_XAML_GENERATED_MAIN): Es läuft nur eine Instanz. Ein weiterer Start – etwa per Doppelklick
/// auf eine .gtpaket-Datei – wird an das offene Fenster übergeben, damit nicht zwei Instanzen dieselben Dateien schreiben.
/// </summary>
public static class Program
{
    const string Schluessel = "Gutachtentool";

    [DllImport("Microsoft.ui.xaml.dll")]
    static extern void XamlCheckProcessRequirements();

    [DllImport("user32.dll")]
    static extern bool AllowSetForegroundWindow(uint prozess);

    [STAThread]
    static int Main(string[] args)
    {
        XamlCheckProcessRequirements();
        WinRT.ComWrappersSupport.InitializeComWrappers();

        if (Umleiten()) return 0;

        Application.Start(p =>
        {
            var kontext = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(kontext);
            _ = new App();
        });
        return 0;
    }

    /// <summary>True, wenn bereits eine Instanz läuft und die Aktivierung an sie übergeben wurde.</summary>
    static bool Umleiten()
    {
        try
        {
            AppActivationArguments aktivierung = AppInstance.GetCurrent().GetActivatedEventArgs();
            AppInstance haupt = AppInstance.FindOrRegisterForKey(Schluessel);
            if (haupt.IsCurrent)
            {
                haupt.Activated += (_, e) => App.Umgeleitet(e);
                return false;
            }
            AllowSetForegroundWindow(haupt.ProcessId);
            // Nicht auf dem STA-Thread blockierend warten (Deadlock-Gefahr), sondern auf einem Hintergrundthread.
            Task.Run(() => haupt.RedirectActivationToAsync(aktivierung).AsTask()).Wait(TimeSpan.FromSeconds(10));
            return true;
        }
        catch (Exception ex)
        {
            EinstellungsSpeicher.Protokollieren(ex);
            return false; // im Zweifel normal starten
        }
    }
}
