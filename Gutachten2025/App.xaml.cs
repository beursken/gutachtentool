using Gutachten2025.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace Gutachten2025
{
    /// <summary>
    /// Anwendung. Start und Einzelinstanz: siehe <see cref="Program"/>.
    /// </summary>
    public partial class App : Application
    {
        static App? _instanz;
        static DispatcherQueue? _oberflaeche;
        MainWindow? m_window;

        public App()
        {
            _instanz = this;
            _oberflaeche = DispatcherQueue.GetForCurrentThread();
            // Altbestand früherer MSIX-Fassungen (nur in gepackten Apps vorhanden).
            EinstellungsSpeicher.Altbestand = () => Windows.Storage.ApplicationData.Current.LocalSettings.Values;
            // Gewählte Darstellung schon für die App setzen, damit Themenfarben überall passen.
            switch (EinstellungsSpeicher.DarstellungLesen())
            {
                case 1: RequestedTheme = ApplicationTheme.Light; break;
                case 2: RequestedTheme = ApplicationTheme.Dark; break;
            }
            InitializeComponent();
            UnhandledException += (_, e) =>
            {
                EinstellungsSpeicher.Protokollieren(e.Exception);
                if (m_window is MainWindow w)
                {
                    e.Handled = true;
                    w.Melden(Meldungsart.Fehler, "Unerwarteter Fehler",
                        $"{e.Exception.Message} (Details in {EinstellungsSpeicher.Datenordner}\\fehler.log)");
                }
            };
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                EinstellungsSpeicher.Protokollieren(e.Exception);
                e.SetObserved();
            };
        }

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            m_window = new MainWindow();
            m_window.Activate();

            AppActivationArguments? aktivierung = null;
            try { aktivierung = AppInstance.GetCurrent().GetActivatedEventArgs(); }
            catch (System.Exception ex) { EinstellungsSpeicher.Protokollieren(ex); }
            if (Aktivierung.Datei(aktivierung, eigeneKommandozeile: true) is { } datei)
                m_window.DateiOeffnen(datei);
        }

        /// <summary>Ein zweiter Programmstart (z. B. Doppelklick auf ein Paket) wurde an diese Instanz umgeleitet.</summary>
        internal static void Umgeleitet(AppActivationArguments args)
        {
            // Läuft auf einem Hintergrundthread: Fensterzugriffe nur über die Oberflächen-Warteschlange.
            string? datei = Aktivierung.Datei(args, eigeneKommandozeile: false);
            _oberflaeche?.TryEnqueue(() =>
            {
                if (_instanz?.m_window is not MainWindow fenster) return;
                fenster.NachVorne();
                if (datei is not null) fenster.DateiOeffnen(datei);
            });
        }
    }
}
