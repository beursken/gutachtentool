using Gutachten2025.Models;
using Gutachten2025.Rohpunkte;
using Gutachten2025.Services;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Windows.System;

namespace Gutachten2025;

/// <summary>
/// Windows-Teile der Korrekturbegleitung: PDF über Word, Wörterbuch aus Sachverhalt/Lösung, Suche und Aktionen
/// in der Seitenleiste, Sprung zur Rohpunktzeile, Schnellerfassung der Rohpunkte, Übersicht.
/// </summary>
public sealed partial class MainWindow
{
    // ---------- PDF über Word ----------

    dynamic? _word;
    DispatcherTimer? _wordBeenden;

    /// <summary>Gutachten mit Word als PDF speichern (Word bleibt für Serien kurz geöffnet und wird danach beendet).</summary>
    public async Task<bool> AlsPdfSpeichernAsync(string docx, string pdf)
    {
        _wordBeenden?.Stop();
        try
        {
            await Task.Run(() =>
            {
                if (_word is null)
                {
                    Type typ = Type.GetTypeFromProgID("Word.Application") ?? throw new InvalidOperationException("Microsoft Word ist nicht installiert.");
                    _word = Activator.CreateInstance(typ) ?? throw new InvalidOperationException("Word ließ sich nicht starten.");
                    _word.Visible = false;
                    _word.DisplayAlerts = 0; // wdAlertsNone
                }
                dynamic dokument = _word.Documents.Open(docx, false, true, false); // FileName, ConfirmConversions, ReadOnly, AddToRecentFiles
                try
                {
                    dokument.ExportAsFixedFormat(pdf, 17); // wdExportFormatPDF
                }
                finally
                {
                    dokument.Close(0); // wdDoNotSaveChanges
                }
            });
            return File.Exists(pdf);
        }
        catch (Exception ex)
        {
            EinstellungsSpeicher.Protokollieren(ex);
            WordBeenden();
            return false;
        }
        finally
        {
            _wordBeenden ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
            _wordBeenden.Tick -= WordBeenden_Tick;
            _wordBeenden.Tick += WordBeenden_Tick;
            _wordBeenden.Start();
        }
    }

    void WordBeenden_Tick(object? sender, object e) => WordBeenden();

    void WordBeenden()
    {
        _wordBeenden?.Stop();
        dynamic? w = _word;
        _word = null;
        if (w is null) return;
        _ = Task.Run(() =>
        {
            try { w.Quit(0); }
            catch (Exception ex) { EinstellungsSpeicher.Protokollieren(ex); }
        });
    }

    // ---------- Wörterbuch für die Rechtschreibprüfung ----------

    /// <summary>
    /// Wörter aus Sachverhalt, Lösung, Erwartungshorizont und Bausteinen (samt Beugungsformen) als Benutzerwörterbuch anmelden;
    /// der Editor prüft danach neu (Prüfung kurz aus- und im nächsten Durchlauf wieder einschalten, damit Windows das Wörterbuch neu lädt).
    /// </summary>
    void WoerterbuchAktualisieren()
    {
        Woerterbuch.Setzen(ViewModel.UnterlagenWoerter);
        if (!Main.IsSpellCheckEnabled) return;
        Main.IsSpellCheckEnabled = false;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => Main.IsSpellCheckEnabled = true);
    }

    // ---------- Tippfehler „GRoßbuchstabe“ ----------

    /// <summary>Letzte Autokorrektur (absolute Position, ursprünglicher und neuer Text) – erkennt „Rückgängig“ (Strg+Z).</summary>
    (int Start, string Alt, string Neu)? _letzteBerichtigung;
    int _textLaenge;

    /// <summary>
    /// Autokorrektur nach einem Wortende bzw. nach einem diktierten Abschnitt (die Spracherkennung fügt mehrere Zeichen auf einmal ein):
    /// „dei“ → „die“, „z.B.“ → „z. B.“, „Paragraph vier dreiunddreißig Absatz eins“ → „§ 433 Abs. 1“, „GRoß“ → „Groß“.
    /// Strg+Z nimmt eine Berichtigung zurück; ein einzelnes Wort kommt dann in die Ausnahmen der Klausur.
    /// </summary>
    void TippfehlerPruefen()
    {
        int laenge = Main.Document.GetRange(0, int.MaxValue).EndPosition;
        int eingefuegt = laenge - _textLaenge;
        _textLaenge = laenge;
        if (_letzteBerichtigung is { } r && eingefuegt == r.Alt.Length - r.Neu.Length && Rueckgaengig(r))
        {
            _letzteBerichtigung = null;
            if (!r.Alt.Any(char.IsWhiteSpace)) ViewModel.AusnahmeHinzufuegen(r.Alt);
            return;
        }
        if (!ViewModel.TippfehlerBerichtigen || Main.FocusState == FocusState.Unfocused || eingefuegt is < 1 or > 400) return;
        ITextSelection auswahl = Main.Document.Selection;
        int pos = auswahl.StartPosition;
        if (auswahl.Length != 0 || pos < 2) return;
        int von = Math.Max(0, pos - 200);
        Main.Document.GetRange(von, pos).GetText(TextGetOptions.None, out string davor);
        if (ViewModel.AutokorrekturVorschlag(davor, Math.Min(eingefuegt, davor.Length)) is not { } e) return;
        int start = von + e.Start;
        _letzteBerichtigung = (start, e.Alt, e.Neu);
        Main.Document.GetRange(start, start + e.Laenge).Text = e.Neu;
        int neuPos = pos + e.Neu.Length - e.Laenge;
        Main.Document.Selection.SetRange(neuPos, neuPos);
        _textLaenge = Main.Document.GetRange(0, int.MaxValue).EndPosition;
    }

    /// <summary>Steht an der Stelle der letzten Berichtigung wieder der ursprüngliche Text?</summary>
    bool Rueckgaengig((int Start, string Alt, string Neu) b)
    {
        Main.Document.GetRange(b.Start, b.Start + b.Alt.Length).GetText(TextGetOptions.None, out string jetzt);
        return jetzt == b.Alt;
    }

    // ---------- Seitenleiste: Suche, Baustein aus Markierung, Sprung zur Rohpunktzeile ----------

    string _unterlagenAuswahl = string.Empty;
    List<Unterlagenansicht.Treffer> _unterlagenTreffer = new();
    int _unterlagenTrefferIndex = -1;

    Unterlagenansicht.Aktionen UnterlagenAktionen => new(
        UeberschriftGewaehlt: UeberschriftGewaehlt,
        AlsBaustein: text => _ = AlsBausteinAsync(text),
        AuswahlGeaendert: text => _unterlagenAuswahl = text,
        Sprungziel: ViewModel.HatSchema && !ViewModel.ZeigtNormen,
        NormGewaehlt: (gesetz, nr) => _ = ViewModel.NormZeigenAsync(gesetz, nr));

    // ---------- Normen und Gliederung im Editor ----------

    readonly DispatcherTimer _normTakt = new() { Interval = TimeSpan.FromMilliseconds(450) };
    Windows.Foundation.Point _normZeiger;

    void NormenEinrichten()
    {
        Main.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(Main_NormZeiger), true);
        Main.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(Main_StrgKlick), true);
        Main.PointerExited += (_, _) => NormHinweisVerbergen();
        _normTakt.Tick += (_, _) =>
        {
            _normTakt.Stop();
            NormHinweisZeigen();
        };
        ViewModel.NormAnspringen += (zitat, nurUeberschrift) => DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            Unterlagenansicht.NormFinden(UnterlagenInhalt, zitat, nurUeberschrift)?
                .StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0.08, AnimationDesired = false }));
        ViewModel.UeberschriftAnspringen += begriff => DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (Unterlagenansicht.UeberschriftFinden(UnterlagenInhalt, begriff) is { } ziel)
                ziel.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0.1, AnimationDesired = true });
            else
                Melden(Meldungsart.Information, "Nicht in der Lösung gefunden", $"„{begriff}“ kommt in der Lösung nicht vor.");
        });
    }

    /// <summary>Text um eine Bildschirmstelle des Editors und die Stelle darin.</summary>
    (string Text, int Stelle)? EditorTextAn(Windows.Foundation.Point punkt)
    {
        try
        {
            ITextRange r = Main.Document.GetRangeFromPoint(punkt, PointOptions.ClientCoordinates);
            int pos = r.StartPosition;
            int von = Math.Max(0, pos - 160);
            Main.Document.GetRange(von, pos + 160).GetText(TextGetOptions.None, out string text);
            return (text, pos - von);
        }
        catch (Exception)
        {
            return null; // außerhalb des Textes
        }
    }

    void Main_NormZeiger(object sender, PointerRoutedEventArgs e)
    {
        _normZeiger = e.GetCurrentPoint(Main).Position;
        if (NormHinweisRahmen.Visibility == Visibility.Visible) NormHinweisVerbergen();
        _normTakt.Stop();
        _normTakt.Start();
    }

    void NormHinweisZeigen()
    {
        if (EditorTextAn(_normZeiger) is not { } t || ViewModel.NormHinweisAn(t.Text, t.Stelle) is not { } hinweis)
        {
            NormHinweisVerbergen();
            return;
        }
        NormHinweisText.Text = hinweis + "\n\nStrg+Klick: Volltext in der Seitenleiste";
        Windows.Foundation.Point p = Main.TransformToVisual(VorschlagEbene).TransformPoint(_normZeiger);
        double breite = Math.Max(240, Math.Min(520, VorschlagEbene.ActualWidth - 24));
        NormHinweisRahmen.MaxWidth = breite;
        Canvas.SetLeft(NormHinweisRahmen, Math.Clamp(p.X + 12, 8, Math.Max(8, VorschlagEbene.ActualWidth - breite - 8)));
        Canvas.SetTop(NormHinweisRahmen, p.Y + 20);
        NormHinweisRahmen.Visibility = Visibility.Visible;
    }

    void NormHinweisVerbergen()
    {
        _normTakt.Stop();
        NormHinweisRahmen.Visibility = Visibility.Collapsed;
    }

    /// <summary>Strg+Klick im Editor: auf einem Zitat den Normtext, auf „Frage 1“ usw. die Stelle der Lösung zeigen.</summary>
    void Main_StrgKlick(object sender, PointerRoutedEventArgs e)
    {
        if (!Gedrueckt(VirtualKey.Control) || EditorTextAn(e.GetCurrentPoint(Main).Position) is not { } t) return;
        if (ViewModel.GliederungsbegriffAn(t.Text, t.Stelle) is { } begriff)
        {
            e.Handled = true;
            NormHinweisVerbergen();
            ViewModel.GliederungZeigen(begriff);
        }
        else if (ViewModel.NormVerweisAn(t.Text, t.Stelle) is { } v)
        {
            e.Handled = true;
            NormHinweisVerbergen();
            _ = ViewModel.NormZeigenAsync(v);
        }
    }

    void NormenSuche_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        e.Handled = true;
        if (ViewModel.NormenSuchenCommand.CanExecute(null)) ViewModel.NormenSuchenCommand.Execute(null);
    }

    void UeberschriftGewaehlt(string ueberschrift)
    {
        if (!ViewModel.HatSchema) return;
        int index = ViewModel.RohpunktzeileZuUeberschrift(ueberschrift);
        if (index < 0)
        {
            Melden(Meldungsart.Information, "Keine passende Rohpunktzeile", $"Zu „{ueberschrift}“ gibt es im Rohpunkteschema keine ähnliche Überschrift.");
            return;
        }
        ReiterSetzen(true, fokussieren: false);
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => RohpunktzeileZeigen(index));
    }

    void RohpunktzeileZeigen(int index)
    {
        if (RohpunkteListe.ContainerFromIndex(index) is not FrameworkElement zeile) return;
        zeile.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0.15, AnimationDesired = true });
        if (Unterlagenansicht.Nachfahre<NumberBox>(zeile) is { Visibility: Visibility.Visible } feld) feld.Focus(FocusState.Programmatic);
    }

    async Task AlsBausteinAsync(string text)
    {
        text = text.Trim();
        if (text.Length == 0)
        {
            Melden(Meldungsart.Information, "Nichts markiert", "Bitte zuerst Text in Sachverhalt oder Lösung markieren.");
            return;
        }
        await ViewModel.NeuerBausteinAsync(text, null);
    }

    void UnterlagenAlsBaustein_Click(object sender, RoutedEventArgs e) => _ = AlsBausteinAsync(_unterlagenAuswahl);

    void UnterlagenSuche_TextChanged(object sender, TextChangedEventArgs e) => UnterlagenSuchen(neu: true);

    void UnterlagenSuche_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            TrefferWechseln(Gedrueckt(VirtualKey.Shift) ? -1 : 1);
        }
        else if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            UnterlagenSuche.Text = string.Empty;
        }
    }

    void UnterlagenSuchen(bool neu)
    {
        _unterlagenTreffer = Unterlagenansicht.Suchen(UnterlagenInhalt, UnterlagenSuche.Text);
        _unterlagenTrefferIndex = -1;
        if (_unterlagenTreffer.Count > 0 && neu) TrefferWechseln(1);
        else TrefferAnzeigen();
    }

    void TrefferWechseln(int richtung)
    {
        if (_unterlagenTreffer.Count == 0) return;
        _unterlagenTrefferIndex = (_unterlagenTrefferIndex + richtung + _unterlagenTreffer.Count) % _unterlagenTreffer.Count;
        Unterlagenansicht.Treffer t = _unterlagenTreffer[_unterlagenTrefferIndex];
        Unterlagenansicht.Markieren(_unterlagenTreffer, _unterlagenTrefferIndex);
        t.Block.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0.3, AnimationDesired = false });
        TrefferAnzeigen();
    }

    void TrefferAnzeigen() =>
        UnterlagenTreffer.Text = UnterlagenSuche.Text.Trim().Length == 0 ? string.Empty
            : _unterlagenTreffer.Count == 0 ? "keine Treffer"
            : $"{Math.Max(1, _unterlagenTrefferIndex + 1)}/{_unterlagenTreffer.Count}";

    // ---------- Rohpunkte-Schnellerfassung ----------

    /// <summary>Eingabe: übernehmen und zur nächsten Station springen (Umschalt+Eingabe: zur vorigen).</summary>
    void Rohpunkt_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter || sender is not FrameworkElement feld || feld.DataContext is not RohpunktZeile z) return;
        e.Handled = true;
        bool zurueck = Gedrueckt(VirtualKey.Shift);
        IList<RohpunktZeile> zeilen = ViewModel.Rohpunktzeilen;
        int i = zeilen.IndexOf(z);
        do i += zurueck ? -1 : 1;
        while (i >= 0 && i < zeilen.Count && !zeilen[i].IstEingabe);
        if (i < 0 || i >= zeilen.Count)
        {
            // Am Ende: Fokus auf die Fußzeile (Wert wird übernommen), Hinweis auf offene Stationen.
            Generate.Focus(FocusState.Programmatic);
            return;
        }
        RohpunktzeileZeigen(i);
    }

    // ---------- Übersicht ----------

    void EintragEntfernen_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is UebersichtEintrag eintrag) ViewModel.TeilnehmerEntfernen(eintrag);
    }

    /// <summary>Hintergrund des Nutzungszählers im Baum (x:Bind-Funktion).</summary>
    public static Brush NutzungFarbe(int stufe)
    {
        string schluessel = stufe switch
        {
            3 => "AccentFillColorDefaultBrush",
            1 => "SystemFillColorCautionBackgroundBrush",
            _ => "SubtleFillColorSecondaryBrush",
        };
        return Application.Current.Resources.TryGetValue(schluessel, out object? b) && b is Brush pinsel
            ? pinsel
            : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }

    public static Brush NutzungSchrift(int stufe) =>
        stufe == 3 && Application.Current.Resources.TryGetValue("TextOnAccentFillColorPrimaryBrush", out object? b) && b is Brush pinsel
            ? pinsel
            : (Application.Current.Resources.TryGetValue("TextFillColorSecondaryBrush", out object? s) && s is Brush p2 ? p2 : new SolidColorBrush(Microsoft.UI.Colors.Gray));
}

/// <summary>
/// Benutzerwörterbuch der Windows-Rechtschreibprüfung (Spell Checking API, IUserDictionariesRegistrar):
/// Wörter aus Sachverhalt und Lösung gelten als richtig geschrieben. Die Datei liegt im Programmdatenordner und wird
/// beim Wechsel der Klausur ersetzt und beim Beenden abgemeldet (die Anmeldung gilt solange auch für andere Programme).
/// </summary>
static class Woerterbuch
{
    const string Sprache = "de-DE";

    [ComImport, Guid("AA176B85-0E12-4844-8E1A-EEF1DA77F586"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IUserDictionariesRegistrar
    {
        void RegisterUserDictionary([MarshalAs(UnmanagedType.LPWStr)] string dictionaryPath, [MarshalAs(UnmanagedType.LPWStr)] string languageTag);
        void UnregisterUserDictionary([MarshalAs(UnmanagedType.LPWStr)] string dictionaryPath, [MarshalAs(UnmanagedType.LPWStr)] string languageTag);
    }

    [ComImport, Guid("7AB36653-1796-484B-BDFA-E74F1DB7C1DC")]
    class SpellCheckerFactory { }

    static string? _angemeldet;

    static string Pfad => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Gutachtentool", "Woerterbuch", "Unterlagen.dic");

    public static void Setzen(IReadOnlyCollection<string> woerter)
    {
        try
        {
            Entfernen();
            if (woerter.Count == 0) return;
            string pfad = Pfad;
            Directory.CreateDirectory(Path.GetDirectoryName(pfad)!);
            // Benutzerwörterbücher: UTF-16 LE mit BOM, ein Wort je Zeile.
            File.WriteAllText(pfad, string.Join("\r\n", woerter) + "\r\n", new UnicodeEncoding(false, true));
            Registrar().RegisterUserDictionary(pfad, Sprache);
            _angemeldet = pfad;
        }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or InvalidCastException)
        {
            EinstellungsSpeicher.Protokollieren(ex); // ohne Wörterbuch bleibt die Prüfung wie bisher
        }
    }

    public static void Entfernen()
    {
        if (_angemeldet is not { } pfad) return;
        _angemeldet = null;
        try
        {
            Registrar().UnregisterUserDictionary(pfad, Sprache);
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            EinstellungsSpeicher.Protokollieren(ex);
        }
    }

    static IUserDictionariesRegistrar Registrar() => (IUserDictionariesRegistrar)new SpellCheckerFactory();
}
