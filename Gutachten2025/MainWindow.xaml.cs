using Gutachten2025.Models;
using Gutachten2025.Services;
using Gutachten2025.ViewModels;
using Microsoft.UI.Input;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.System;
using Windows.UI.Core;

namespace Gutachten2025;

/// <summary>
/// View: Darstellung, Tastatur, Dialoge und Editorzugriff. Die Logik liegt in <see cref="MainViewModel"/>.
/// Weitere Teile: MainWindow.Bedienung.cs (Layout, Zoom, Ziehen, Diktat, Tastenkürzel, Hilfe) und MainWindow.Dialoge.cs.
/// </summary>
public sealed partial class MainWindow : Window, IUiService, IEntwurfEditor
{
    readonly DispatcherTimer _autosave = new() { Interval = TimeSpan.FromSeconds(2) };
    readonly DispatcherTimer _meldungSchliessen = new() { Interval = TimeSpan.FromSeconds(8) };
    readonly SemaphoreSlim _dialogSperre = new(1, 1);
    string _nummer = string.Empty;
    string? _ausstehendeDatei;
    bool _bereit, _geladen, _auswahlWirdGesetzt;

    public MainViewModel ViewModel { get; }

    public MainWindow()
    {
        ViewModel = new MainViewModel(this, this);
        InitializeComponent();
        Title = "Gutachtentool";

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        SymbolSetzen();
        AppTitleBar.Loaded += (_, _) => TitelleisteAktualisieren();
        AppTitleBar.SizeChanged += (_, _) => TitelleisteAktualisieren();

        // Unbehandelte Fehler protokolliert und meldet App.xaml.cs.

        EditorFormatieren();
        BedienungEinrichten();
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        ViewModel.Initialisieren();
        KlausurAuswahlSetzen();
        FensterWiederherstellen();
        _bereit = true;

        _autosave.Tick += (_, _) =>
        {
            ViewModel.Autosave();
            StatusAktualisieren(); // laufende Bearbeitungszeit
        };
        _autosave.Start();
        _meldungSchliessen.Tick += (_, _) => { _meldungSchliessen.Stop(); Toast.Visibility = Visibility.Collapsed; };
    }

    // ---------- Fenster ----------

    async void Wurzel_Loaded(object sender, RoutedEventArgs e)
    {
        if (_geladen) return;
        _geladen = true;
        AnimationenEinrichten();
        DarstellungAnwenden();
        UnterlagenZeichnen();
        WoerterbuchAktualisieren();
        ZoomAnwenden();
        WoerterZaehlen();
        StatusAktualisieren();
        TitelleisteAktualisieren();

        if (!ViewModel.EinfuehrungGezeigt)
        {
            ViewModel.EinfuehrungGezeigt = true;
            await HilfeZeigenAsync(einfuehrung: true);
        }
        if (_ausstehendeDatei is { } datei)
        {
            _ausstehendeDatei = null;
            await ViewModel.PaketImportierenAsync(datei);
        }
    }

    /// <summary>Öffnet eine per Doppelklick übergebene Datei (vor dem Laden des Fensters wird sie vorgemerkt).</summary>
    public void DateiOeffnen(string pfad)
    {
        if (!_geladen || Content?.XamlRoot is null)
        {
            _ausstehendeDatei = pfad;
            return;
        }
        _ = ViewModel.PaketImportierenAsync(pfad);
    }

    /// <summary>Holt das Fenster in den Vordergrund (bei einem zweiten Programmstart).</summary>
    public void NachVorne()
    {
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } p) p.Restore();
        Activate();
        SetForegroundWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr hwnd);

    void EditorFormatieren()
    {
        ITextParagraphFormat absatz = Main.TextDocument.GetDefaultParagraphFormat();
        absatz.SpaceAfter = 8;
        absatz.SetLineSpacing(LineSpacingRule.Multiple, 1.1f);
        Main.TextDocument.SetDefaultParagraphFormat(absatz);
        ITextCharacterFormat zeichen = Main.TextDocument.GetDefaultCharacterFormat();
        zeichen.Size = (float)ViewModel.Zoom;
        zeichen.LanguageTag = "de-DE";
        Main.TextDocument.SetDefaultCharacterFormat(zeichen);
    }

    void TitelleisteAktualisieren()
    {
        if (AppTitleBar.XamlRoot is null || AppWindow is null) return;
        double skala = AppTitleBar.XamlRoot.RasterizationScale;
        RightPaddingColumn.Width = new GridLength(AppWindow.TitleBar.RightInset / skala);
        LeftPaddingColumn.Width = new GridLength(AppWindow.TitleBar.LeftInset / skala);

        var bereiche = new[] { Rechteck(Klausur, skala), Rechteck(TitelKnoepfe, skala) };
        InputNonClientPointerSource.GetForWindowId(AppWindow.Id)
            .SetRegionRects(NonClientRegionKind.Passthrough, bereiche);
    }

    static Windows.Graphics.RectInt32 Rechteck(FrameworkElement e, double skala)
    {
        Rect r = e.TransformToVisual(null).TransformBounds(new Rect(0, 0, e.ActualWidth, e.ActualHeight));
        return new Windows.Graphics.RectInt32(
            (int)Math.Round(r.X * skala), (int)Math.Round(r.Y * skala),
            (int)Math.Round(r.Width * skala), (int)Math.Round(r.Height * skala));
    }

    void Window_Activated(object sender, Microsoft.UI.Xaml.WindowActivatedEventArgs args)
    {
        if (!_bereit || args.WindowActivationState == WindowActivationState.Deactivated) return;
        ViewModel.ExterneAenderungenPruefen();
        if (ViewModel.SeitenleisteOffen) ViewModel.UebersichtAktualisieren();
    }

    void Window_Closed(object sender, WindowEventArgs args)
    {
        _autosave.Stop();
        _meldungSchliessen.Stop();
        Woerterbuch.Entfernen();
        WordBeenden();
        FensterMerken();
        ViewModel.Autosave(erzwingen: true);
    }

    /// <summary>
    /// Fenstersymbol für Taskleiste, Vorschau und Alt+Tab (ohne SetIcon zeigt die Vorschau kein Symbol)
    /// sowie das Symbol in der eigenen Titelleiste.
    /// </summary>
    void SymbolSetzen()
    {
        try
        {
            string ico = System.IO.Path.Combine(AppContext.BaseDirectory, "gutachten.ico");
            if (System.IO.File.Exists(ico)) AppWindow.SetIcon(ico);
            string png = System.IO.Path.Combine(AppContext.BaseDirectory, "Images", "Titelleiste.png");
            if (System.IO.File.Exists(png)) TitleBarIcon.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(png));
        }
        catch (Exception ex)
        {
            EinstellungsSpeicher.Protokollieren(ex);
        }
    }

    // ---------- Klausurauswahl ----------

    void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.KlausurName):
                KlausurAuswahlSetzen();
                StatusAktualisieren();
                break;
            case nameof(MainViewModel.Satzvorschlaege) when !ViewModel.Satzvorschlaege:
                VorschlagVerbergen();
                break;
            case nameof(MainViewModel.UnterlagenHtml):
                if (_geladen) UnterlagenZeichnen();
                break;
            case nameof(MainViewModel.HatSchema):
                _unterlagenGezeichnet = null; // Überschriften werden erst mit Schema zu Sprungzielen
                if (_geladen) UnterlagenZeichnen();
                break;
            case nameof(MainViewModel.UnterlagenWoerter):
                if (_geladen) WoerterbuchAktualisieren();
                break;
            case nameof(MainViewModel.BausteineOffen) or nameof(MainViewModel.BausteineOben) or nameof(MainViewModel.SeitenleisteOffen)
                or nameof(MainViewModel.Leiste):
                if (e.PropertyName == nameof(MainViewModel.SeitenleisteOffen)) _uebersichtAutomatischZu = false;
                if (e.PropertyName == nameof(MainViewModel.BausteineOffen)) _bausteineAutomatischZu = false;
                if (_geladen) LayoutAnwenden();
                break;
            case nameof(MainViewModel.Student) or nameof(MainViewModel.Matrikel) or nameof(MainViewModel.NotenIndex)
                or nameof(MainViewModel.Anonym) or nameof(MainViewModel.BearbeiteteKennung) or nameof(MainViewModel.RohpunkteKurz):
                StatusAktualisieren();
                break;
        }
    }

    void KlausurAuswahlSetzen()
    {
        _auswahlWirdGesetzt = true;
        try
        {
            string name = ViewModel.KlausurName;
            if (ViewModel.Klausuren.Contains(name))
                Klausur.SelectedItem = name;
            else
            {
                Klausur.SelectedItem = null;
                Klausur.Text = name;
            }
        }
        finally
        {
            _auswahlWirdGesetzt = false;
        }
    }

    void Klausur_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_bereit || _auswahlWirdGesetzt) return;
        if (Klausur.SelectedItem is string name) ViewModel.KlausurWechseln(name);
    }

    void Klausur_TextSubmitted(ComboBox sender, ComboBoxTextSubmittedEventArgs args)
    {
        args.Handled = true;
        ViewModel.KlausurWechseln(args.Text);
        KlausurAuswahlSetzen();
    }

    // ---------- Kennung mit Vorschlägen aus der Teilnehmendenliste ----------

    void Student_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
        var vorschlaege = ViewModel.TeilnehmerVorschlaege(sender.Text);
        sender.ItemsSource = vorschlaege.Count > 0 ? vorschlaege : null;
    }

    void Student_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is Teilnehmer t)
        {
            ViewModel.TeilnehmerWaehlen(t);
            sender.ItemsSource = null;
        }
    }

    // ---------- Übersicht ----------

    async void Eintraege_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is UebersichtEintrag eintrag) await ViewModel.EintragOeffnenAsync(eintrag);
    }

    // ---------- Bausteine ----------

    void Texte_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is not Baustein b) return;
        ViewModel.BausteinVerwenden(b);
        if (!b.IstGruppe)
        {
            Texte.SelectedItem = null;
            Main.Focus(FocusState.Programmatic);
        }
    }

    async void Texte_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        bool strg = Gedrueckt(VirtualKey.Control);
        if (strg && e.Key == VirtualKey.Z)
        {
            e.Handled = true;
            ViewModel.BausteinRueckgaengigCommand.Execute(null);
            return;
        }
        if (Texte.SelectedItem is not Baustein b) return;
        if (e.Key == VirtualKey.Delete)
        {
            e.Handled = true;
            await ViewModel.BausteinLoeschenAsync(b);
        }
        else if (e.Key == VirtualKey.F2)
        {
            e.Handled = true;
            await ViewModel.BausteinBearbeitenAsync(b);
        }
    }

    static Baustein? AusMenue(object sender) => (sender as FrameworkElement)?.Tag as Baustein;

    async void BausteinBearbeiten_Click(object sender, RoutedEventArgs e)
    {
        if (AusMenue(sender) is { } b) await ViewModel.BausteinBearbeitenAsync(b);
    }

    async void BausteinLoeschen_Click(object sender, RoutedEventArgs e)
    {
        if (AusMenue(sender) is { } b) await ViewModel.BausteinLoeschenAsync(b);
    }

    async void BausteinNeu_Click(object sender, RoutedEventArgs e) =>
        await ViewModel.NeuerBausteinAsync(string.Empty, AusMenue(sender));

    async void GruppeNeu_Click(object sender, RoutedEventArgs e) =>
        await ViewModel.NeueGruppeAsync(AusMenue(sender));

    // ---------- Bausteinsuche ----------

    void Suche_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (ViewModel.ErsterTreffer is { } b)
        {
            ViewModel.BausteinVerwenden(b);
            Main.Focus(FocusState.Programmatic);
        }
    }

    void Suche_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            ViewModel.Suchtext = string.Empty;
            Main.Focus(FocusState.Programmatic);
        }
        else if (e.Key == VirtualKey.Down && ViewModel.Treffer.Count > 0)
        {
            e.Handled = true;
            Texte.Focus(FocusState.Keyboard);
        }
    }

    // ---------- Editor-Tastatur ----------

    static bool Gedrueckt(VirtualKey taste) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(taste).HasFlag(CoreVirtualKeyStates.Down);

    async void Main_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        // Nur die obere Ziffernreihe: Alt+Ziffernblock erzeugt Windows-Alt-Codes (Sonderzeichen im Text).
        if (Gedrueckt(VirtualKey.Menu) && e.Key >= VirtualKey.Number0 && e.Key <= VirtualKey.Number9)
        {
            _nummer += (char)('0' + (e.Key - VirtualKey.Number0));
            e.Handled = true;
            return;
        }

        if (Gedrueckt(VirtualKey.Control) && e.Key == VirtualKey.S)
        {
            e.Handled = true;
            string markierung = Main.Document.Selection.Text?.Trim() ?? string.Empty;
            if (markierung.Length > 0) await ViewModel.NeuerBausteinAsync(markierung, Texte.SelectedItem as Baustein);
            else
            {
                ViewModel.Autosave(erzwingen: true);
                Melden(Meldungsart.Information, "Gespeichert", "Entwurf und Bausteine wurden gespeichert.");
            }
        }
    }

    void Main_KeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Menu || _nummer.Length == 0) return;
        string nummer = _nummer;
        _nummer = string.Empty;
        if (!int.TryParse(nummer, out int nr)) return;

        Baustein? gruppe = ViewModel.BausteinNachNummer(nr);
        if (gruppe is not null)
        {
            Texte.SelectedItem = gruppe;
            Texte.Focus(FocusState.Programmatic);
        }
    }

    void Main_LostFocus(object sender, RoutedEventArgs e)
    {
        _nummer = string.Empty;
        VorschlagVerbergen();
    }

    void Main_TextChanged(object sender, RoutedEventArgs e)
    {
        ViewModel.EntwurfGeaendert();
        WoerterZaehlen();
        VorschlagPlanen();
    }

    void Main_SelectionChanged(object sender, RoutedEventArgs e) => VorschlagPruefen();

    void WoerterZaehlen()
    {
        string text = TextLesen();
        int woerter = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        WortZahl.Text = woerter == 1 ? "1 Wort" : $"{woerter} Wörter";
    }

    /// <summary>Statuszeile: Klausur · Kennung · Note.</summary>
    void StatusAktualisieren()
    {
        string kennung = (ViewModel.Anonym ? ViewModel.Student : ViewModel.Matrikel).Trim();
        var teile = new System.Collections.Generic.List<string>();
        if (ViewModel.KlausurName.Length > 0) teile.Add(ViewModel.KlausurName.Replace('_', ' '));
        teile.Add(kennung.Length > 0 ? kennung : "keine Kennung");
        if (Notenskala.IstGueltig(ViewModel.NotenIndex)) teile.Add(Notenskala.MitPunkten(ViewModel.NotenIndex));
        if (ViewModel.RohpunkteKurz is { } roh) teile.Add(roh);
        if (ViewModel.InBearbeitung) teile.Add("Änderung eines bestehenden Gutachtens");
        if (ViewModel.LaufendeZeit is { Length: > 0 } zeit) teile.Add(zeit);
        string status = string.Join(" · ", teile);
        if (StatusText.Text != status) StatusText.Text = status;
    }

    void EditorRueckgaengig_Click(object sender, RoutedEventArgs e)
    {
        Main.Document.Undo();
        Main.Focus(FocusState.Programmatic);
    }

    void EditorWiederholen_Click(object sender, RoutedEventArgs e)
    {
        Main.Document.Redo();
        Main.Focus(FocusState.Programmatic);
    }

    void Filter_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        int i = sender.Items.IndexOf(sender.SelectedItem);
        ViewModel.UebersichtFilter = i < 0 ? 0 : i;
    }

    async void Einstellungen_Click(object sender, RoutedEventArgs e) => await EinstellungenZeigenAsync();

    // ---------- Kurzmeldung ----------

    Action? _toastAktion;

    void ToastAktion_Click(object sender, RoutedEventArgs e)
    {
        Action? aktion = _toastAktion;
        Toast.Visibility = Visibility.Collapsed;
        _meldungSchliessen.Stop();
        aktion?.Invoke();
    }

    void ToastSchliessen_Click(object sender, RoutedEventArgs e)
    {
        Toast.Visibility = Visibility.Collapsed;
        _meldungSchliessen.Stop();
    }

    // ---------- IEntwurfEditor ----------

    public string TextLesen()
    {
        Main.Document.GetText(TextGetOptions.None, out string text);
        return text;
    }

    public string RtfLesen()
    {
        Main.Document.GetText(TextGetOptions.FormatRtf, out string rtf);
        return rtf;
    }

    public void RtfSetzen(string rtf)
    {
        Main.Document.SetText(TextSetOptions.FormatRtf, rtf);
        ZoomAnwenden();
    }

    public void TextSetzen(string text)
    {
        Main.Document.SetText(TextSetOptions.None, text);
        ZoomAnwenden();
    }

    public void Einfuegen(string baustein)
    {
        baustein = baustein.Trim();
        if (baustein.Length == 0) return;

        // Im Reiter „Rohpunkte“ in den zuletzt bearbeiteten Kommentar, sonst in den Entwurf
        RichEditBox ziel = FormatZiel;
        ITextSelection auswahl = ziel.Document.Selection;
        auswahl.StartPosition = auswahl.EndPosition;

        string davor = string.Empty;
        if (auswahl.StartPosition > 0)
            ziel.Document.GetRange(auswahl.StartPosition - 1, auswahl.StartPosition).GetText(TextGetOptions.None, out davor);
        string praefix = davor.Length > 0 && !char.IsWhiteSpace(davor[0]) ? " " : string.Empty;
        string suffix = baustein[^1] is '.' or '!' or '?' or ':' ? " " : ". ";

        auswahl.SetText(TextSetOptions.None, praefix + baustein + suffix);
        auswahl.CharacterFormat.Bold = FormatEffect.Off;
        auswahl.CharacterFormat.Italic = FormatEffect.Off;
        if (ReferenceEquals(ziel, Main))
        {
            auswahl.CharacterFormat.Size = (float)ViewModel.Zoom;
            auswahl.CharacterFormat.Name = EditorSchriftName;
        }
        auswahl.StartPosition = auswahl.EndPosition;
        if (!ReferenceEquals(ziel, Main)) Kommentarbindung.Uebernehmen(ziel);
        ziel.Focus(FocusState.Programmatic);
    }

    public void Leeren() => Main.Document.SetText(TextSetOptions.None, string.Empty);

    // ---------- IUiService (einfache Teile; Dialoge in MainWindow.Dialoge.cs) ----------

    public void Melden(Meldungsart art, string titel, string text, string? aktion = null, Action? beiAktion = null)
    {
        _meldungSchliessen.Stop();
        if (art is Meldungsart.Information or Meldungsart.Erfolg)
        {
            // Erfolg und Hinweise: kurz unten rechts; Warnungen und Fehler: Meldungsleiste oben.
            ToastTitel.Text = titel;
            ToastText.Text = text;
            ToastSymbol.Glyph = art == Meldungsart.Erfolg ? "\uE73E" : "\uE946";
            ToastSymbol.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[
                art == Meldungsart.Erfolg ? "SystemFillColorSuccessBrush" : "AccentFillColorDefaultBrush"];
            _toastAktion = aktion is not null ? beiAktion : null;
            ToastAktion.Content = aktion;
            ToastAktion.Visibility = _toastAktion is null ? Visibility.Collapsed : Visibility.Visible;
            Toast.Visibility = Visibility.Visible;
            _meldungSchliessen.Interval = TimeSpan.FromSeconds(_toastAktion is null ? 5 : 15);
            _meldungSchliessen.Start();
            return;
        }
        Meldung.Severity = art switch
        {
            Meldungsart.Erfolg => InfoBarSeverity.Success,
            Meldungsart.Warnung => InfoBarSeverity.Warning,
            Meldungsart.Fehler => InfoBarSeverity.Error,
            _ => InfoBarSeverity.Informational,
        };
        Meldung.Title = titel;
        Meldung.Message = text;
        if (aktion is not null && beiAktion is not null)
        {
            var knopf = new Button { Content = aktion };
            knopf.Click += (_, _) =>
            {
                Meldung.IsOpen = false;
                beiAktion();
            };
            Meldung.ActionButton = knopf;
        }
        else
        {
            Meldung.ActionButton = null;
        }
        Meldung.IsOpen = true;
    }

    public void Oeffnen(string pfad)
    {
        try
        {
            Process.Start(new ProcessStartInfo(pfad) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            EinstellungsSpeicher.Protokollieren(ex);
            Melden(Meldungsart.Fehler, "Öffnen nicht möglich", $"„{pfad}“: {ex.Message}");
        }
    }

    public bool Drucken(string pfad)
    {
        try
        {
            Process.Start(new ProcessStartInfo(pfad)
            {
                UseShellExecute = true,
                Verb = "print",
                WindowStyle = ProcessWindowStyle.Minimized,
            });
            return true;
        }
        catch (Exception ex)
        {
            EinstellungsSpeicher.Protokollieren(ex);
            return false;
        }
    }

    Control GutachtenReiter()
    {
        if (_rohpunkteReiter) ReiterSetzen(false, fokussieren: false);
        return Main;
    }

    Control SucheSichtbar()
    {
        if (_fokusmodus) FokusSetzen(false);
        if (!ViewModel.BausteineSichtbar) ViewModel.BausteineUmschalten();
        return Suche;
    }

    public void Fokussieren(Eingabefeld feld)
    {
        Control ziel = feld switch
        {
            Eingabefeld.Klausur => Klausur,
            Eingabefeld.Matrikel => Matrikel,
            Eingabefeld.Note => Grade,
            Eingabefeld.Editor => GutachtenReiter(),
            Eingabefeld.Suche => SucheSichtbar(),
            _ => Student,
        };
        ziel.Focus(FocusState.Programmatic);
    }
}
