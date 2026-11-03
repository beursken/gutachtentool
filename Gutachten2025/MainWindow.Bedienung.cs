using Gutachten2025.Models;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Hosting;
using System.Numerics;
using Microsoft.UI.Xaml.Documents;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.System;

namespace Gutachten2025;

/// <summary>Anordnung, Zoom, Ziehen in den Editor, Kürzel, Diktat, Tastenkürzel und Hilfe.</summary>
public sealed partial class MainWindow
{
    const string BausteinFormat = "Gutachtentool.Baustein";
    const double ZoomStandard = 14, ZoomMin = 9, ZoomMax = 40;

    static readonly Regex KuerzelVorCursor = new(@"(?<![\p{L}\d_\-])#?([\p{L}\d_\-]{1,24})$", RegexOptions.CultureInvariant);

    Baustein? _gezogen;
    bool _layoutWirdGesetzt;

    void BedienungEinrichten()
    {
        Main.AddHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler(Main_PointerWheelChanged), true);
        Main.AddHandler(UIElement.DragOverEvent, new DragEventHandler(Main_DragOver), true);
        Main.AddHandler(UIElement.DropEvent, new DragEventHandler(Main_Drop), true);
        // Die Suchbox verarbeitet Esc/Pfeil selbst; daher auch bereits behandelte Tasten empfangen.
        Suche.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(Suche_KeyDown), true);
        AppWindow.Changed += (_, a) =>
        {
            if (a.DidPresenterChange) VollbildSymbolSetzen();
        };
    }

    // ---------- Globale Tastenkürzel (vor dem Editor ausgewertet) ----------

    async void Wurzel_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        bool strg = Gedrueckt(VirtualKey.Control), alt = Gedrueckt(VirtualKey.Menu), umschalt = Gedrueckt(VirtualKey.Shift);
        // Alt+Pfeil: Verlauf der Normenansicht (nur wenn die Leiste Normen zeigt).
        if (alt && ViewModel.ZeigtNormen && ViewModel.UnterlagenSichtbar && e.Key is VirtualKey.Left or VirtualKey.Right)
        {
            e.Handled = true;
            if (e.Key == VirtualKey.Left) ViewModel.NormenZurueckCommand.Execute(null);
            else ViewModel.NormenVorCommand.Execute(null);
            return;
        }
        if (alt) return;
        switch (e.Key)
        {
            case VirtualKey.F1:
                e.Handled = true;
                await HilfeZeigenAsync(einfuehrung: false);
                break;
            case VirtualKey.F8:
                e.Handled = true;
                await DiktatStartenAsync();
                break;
            case VirtualKey.F6:
                e.Handled = true;
                ViewModel.BausteineUmschalten();
                if (ViewModel.BausteineSichtbar) Suche.Focus(FocusState.Keyboard);
                break;
            case VirtualKey.F7:
                e.Handled = true;
                ViewModel.UnterlagenUmschalten();
                break;
            case VirtualKey.F when strg && umschalt:
                e.Handled = true;
                FokusSetzen(!_fokusmodus);
                break;
            case VirtualKey.F11:
                e.Handled = true;
                VollbildUmschalten();
                break;
            case VirtualKey.Space when strg && !umschalt && VorschlagSichtbar:
                e.Handled = true;
                VorschlagWeiter();
                break;
            case VirtualKey.F9:
                e.Handled = true;
                ViewModel.SeitenleisteOffen = !ViewModel.SeitenleisteOffen;
                break;
            case VirtualKey.Enter when strg && !umschalt:
                e.Handled = true;
                // Fokus wechseln, damit eine gerade bearbeitete Zahl (Seiten, Teil-/Rohpunkte) übernommen wird.
                if (Kommentarbindung.Zuletzt is { } kommentar) Kommentarbindung.Uebernehmen(kommentar);
                Generate.Focus(FocusState.Programmatic);
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (ViewModel.GenerierenCommand.CanExecute(null)) ViewModel.GenerierenCommand.Execute(null);
                });
                break;
            case VirtualKey.N when strg && !umschalt:
                e.Handled = true;
                ViewModel.NeueArbeit();
                break;
            case VirtualKey.F when strg && !umschalt:
                e.Handled = true;
                SucheSichtbar();
                DispatcherQueue.TryEnqueue(() => Suche.Focus(FocusState.Keyboard));
                break;
            case VirtualKey.Add or (VirtualKey)187 when strg && !umschalt:
                e.Handled = true;
                ZoomAendern(+1);
                break;
            case VirtualKey.Subtract or (VirtualKey)189 when strg && !umschalt:
                e.Handled = true;
                ZoomAendern(-1);
                break;
            case VirtualKey.Number1 or VirtualKey.NumberPad1 when strg && !umschalt:
                e.Handled = true;
                ReiterSetzen(false);
                break;
            case VirtualKey.Number2 or VirtualKey.NumberPad2 when strg && !umschalt:
                e.Handled = true;
                ReiterSetzen(true);
                break;
            case VirtualKey.Number0 or VirtualKey.NumberPad0 when strg && !umschalt:
                e.Handled = true;
                ZoomSetzen(ZoomStandard);
                break;
        }
    }

    /// <summary>
    /// Tab: Kürzel vor dem Cursor durch den Baustein ersetzen, sonst einen angezeigten Satzvorschlag übernehmen.
    /// Esc: Satzvorschlag verwerfen bzw. Vollbild verlassen.
    /// </summary>
    void Main_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        bool modifikator = Gedrueckt(VirtualKey.Control) || Gedrueckt(VirtualKey.Shift) || Gedrueckt(VirtualKey.Menu);
        if (e.Key == VirtualKey.Escape && !modifikator)
        {
            if (VorschlagSichtbar)
            {
                e.Handled = true;
                _vorschlagUnterdrueckt = true;
                VorschlagVerbergen();
            }
            else if (IstVollbild)
            {
                e.Handled = true;
                VollbildUmschalten();
            }
            else if (_fokusmodus)
            {
                e.Handled = true;
                FokusSetzen(false);
            }
            return;
        }
        if (e.Key != VirtualKey.Tab || modifikator) return;
        ITextSelection auswahl = Main.Document.Selection;
        if (auswahl.Length != 0) return;
        if (KuerzelErsetzen(auswahl) || VorschlagUebernehmen())
            e.Handled = true;
    }

    bool KuerzelErsetzen(ITextSelection auswahl)
    {
        int ende = auswahl.StartPosition;
        int start = Math.Max(0, ende - 30);
        Main.Document.GetRange(start, ende).GetText(TextGetOptions.None, out string davor);
        Match m = KuerzelVorCursor.Match(davor);
        if (!m.Success || ViewModel.BausteinNachKuerzel(m.Groups[1].Value) is not { } b) return false;

        VorschlagVerbergen();
        int anfang = ende - m.Length;
        Main.Document.GetRange(anfang, ende).SetText(TextSetOptions.None, string.Empty);
        auswahl.SetRange(anfang, anfang);
        ViewModel.BausteinVerwenden(b);
        return true;
    }

    // ---------- Ziehen aus dem Baum ----------

    void Texte_DragItemsStarting(TreeView sender, TreeViewDragItemsStartingEventArgs args)
    {
        ViewModel.VorUmsortieren();
        _gezogen = args.Items.OfType<Baustein>().FirstOrDefault(b => !b.IstGruppe);
        if (_gezogen is null) return;
        // Bewusst kein Textformat: sonst fügte der Editor den Text zusätzlich selbst ein.
        args.Data.SetData(BausteinFormat, _gezogen.Nr.ToString());
        args.Data.RequestedOperation = DataPackageOperation.Move | DataPackageOperation.Copy;
    }

    void Texte_DragItemsCompleted(TreeView sender, TreeViewDragItemsCompletedEventArgs args)
    {
        _gezogen = null;
        ViewModel.NachUmsortieren();
    }

    void Main_DragOver(object sender, DragEventArgs e)
    {
        if (_gezogen is null || !e.DataView.Contains(BausteinFormat)) return;
        e.AcceptedOperation = DataPackageOperation.Copy;
        if (e.DragUIOverride is { } ui)
        {
            ui.Caption = "Baustein einfügen";
            ui.IsGlyphVisible = false;
        }
        e.Handled = true;
    }

    void Main_Drop(object sender, DragEventArgs e)
    {
        if (_gezogen is not { } b || !e.DataView.Contains(BausteinFormat)) return;
        e.Handled = true;
        e.AcceptedOperation = DataPackageOperation.Copy;
        try
        {
            ITextRange ziel = Main.Document.GetRangeFromPoint(e.GetPosition(Main), PointOptions.ClientCoordinates);
            Main.Document.Selection.SetRange(ziel.StartPosition, ziel.StartPosition);
        }
        catch (Exception)
        {
            // Position nicht bestimmbar: an der Schreibmarke einfügen.
        }
        ViewModel.BausteinVerwenden(b);
    }

    // ---------- Anordnung, Responsivität, Fokusmodus ----------

    const double BreiteUebersichtUeberlagert = 1100, BreiteBausteineUeberlagert = 820, BreiteKopfGestapelt = 1000;

    bool _fokusmodus, _uebersichtAutomatischZu, _bausteineAutomatischZu;

    void Koerper_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_geladen) return;
        double alt = e.PreviousSize.Width, neu = e.NewSize.Width;
        // Beim Schmalerwerden überlagernde Leisten einmal automatisch schließen, beim Verbreitern wieder öffnen.
        if (alt >= BreiteUebersichtUeberlagert && neu < BreiteUebersichtUeberlagert && ViewModel.SeitenleisteOffen)
        {
            _uebersichtAutomatischZu = true;
            ViewModel.SeitenleisteOffen = false;
        }
        else if (alt < BreiteUebersichtUeberlagert && neu >= BreiteUebersichtUeberlagert && _uebersichtAutomatischZu)
        {
            _uebersichtAutomatischZu = false;
            ViewModel.SeitenleisteOffen = true;
        }
        if (!ViewModel.BausteineOben)
        {
            if (alt >= BreiteBausteineUeberlagert && neu < BreiteBausteineUeberlagert && ViewModel.BausteineOffen)
            {
                _bausteineAutomatischZu = true;
                ViewModel.BausteineOffen = false;
            }
            else if (alt < BreiteBausteineUeberlagert && neu >= BreiteBausteineUeberlagert && _bausteineAutomatischZu)
            {
                _bausteineAutomatischZu = false;
                ViewModel.BausteineOffen = true;
            }
        }
        LayoutAnwenden();
    }

    void BausteineSchliessen_Click(object sender, RoutedEventArgs e) => ViewModel.BausteineOffen = false;

    void UnterlagenSchliessen_Click(object sender, RoutedEventArgs e) => ViewModel.UnterlagenUmschalten();

    // Die Umschalter zeigen den Zustand der Leisten (LayoutAnwenden), nicht ihren eigenen Klickzustand.
    void BausteineKnopf_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.BausteineUmschalten();
        LayoutAnwenden();
    }

    void UnterlagenKnopf_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.UnterlagenUmschalten();
        LayoutAnwenden();
    }

    void UebersichtSchliessen_Click(object sender, RoutedEventArgs e) => ViewModel.SeitenleisteOffen = false;

    void Fokus_Click(object sender, RoutedEventArgs e) => FokusSetzen(FokusKnopf.IsChecked == true);

    void FokusSetzen(bool an)
    {
        _fokusmodus = an;
        FokusKnopf.IsChecked = an;
        LayoutAnwenden();
        if (an) Main.Focus(FocusState.Programmatic);
    }

    void KlausurAnlegen_Click(object sender, RoutedEventArgs e)
    {
        Klausur.Focus(FocusState.Keyboard);
        Melden(Services.Meldungsart.Information, "Klausur anlegen", "Namen oben eingeben und mit der Eingabetaste bestätigen.");
    }

    static void Abloesen(FrameworkElement e)
    {
        if (e.Parent is Panel p) p.Children.Remove(e);
    }

    /// <summary>Ordnet Bausteine, Kopf, Editor und Übersicht nach Einstellung, Fensterbreite und Fokusmodus an.</summary>
    void LayoutAnwenden()
    {
        _layoutWirdGesetzt = true;
        try
        {
            double breite = Koerper.ActualWidth;
            bool fokus = _fokusmodus;
            bool oben = ViewModel.BausteineOben;
            bool bausteine = ViewModel.BausteineOffen && !fokus;
            bool uebersicht = ViewModel.SeitenleisteOffen && !fokus;
            bool uebersichtUeber = breite > 0 && breite < BreiteUebersichtUeberlagert;
            // Links: Bausteine oder Sachverhalt/Lösung (geteilte Leiste); liegen die Bausteine oben, gehört die Leiste den Unterlagen.
            bool unterlagen = !fokus && ViewModel.UnterlagenSichtbar;
            bool bausteineLinks = !oben && bausteine && !ViewModel.ZeigtUnterlagen;
            bool linksUeber = breite > 0 && breite < BreiteBausteineUeberlagert;
            bool bausteineUeber = bausteineLinks && linksUeber;
            double linksBreite = unterlagen ? ViewModel.UnterlagenBreite : ViewModel.BausteinBreite;
            Brush deckend = Farben.Background;
            BausteineKnopf.IsChecked = !fokus && ViewModel.BausteineSichtbar;
            UnterlagenKnopf.IsChecked = unterlagen;

            // Kopf: zwei Karten nebeneinander oder untereinander
            KopfBereich.Visibility = fokus ? Visibility.Collapsed : Visibility.Visible;
            bool gestapelt = breite > 0 && breite - ((unterlagen || bausteineLinks) && !linksUeber ? linksBreite : 0) < BreiteKopfGestapelt;
            Grid.SetRow(BewertungKarte, gestapelt ? 1 : 0);
            Grid.SetColumn(BewertungKarte, gestapelt ? 0 : 1);
            Grid.SetColumnSpan(ArbeitKarte, gestapelt ? 2 : 1);
            Grid.SetColumnSpan(BewertungKarte, gestapelt ? 2 : 1);

            // Bausteine (nur bei Wechsel der Position umhängen – sonst bliebe der Baum nicht erhalten)
            Panel ziel = oben ? Arbeitsbereich : Koerper;
            if (!ReferenceEquals(BausteinBereich.Parent, ziel))
            {
                Abloesen(BausteinBereich);
                ziel.Children.Add(BausteinBereich);
            }
            SplitterSpalten.Visibility = Visibility.Collapsed;
            SplitterZeilen.Visibility = Visibility.Collapsed;
            SpalteLinks.MinWidth = 0;
            SpalteLinks.Width = new GridLength(0);
            ZeileOben.MinHeight = 0;
            ZeileOben.Height = new GridLength(0);
            BausteinBereich.Visibility = (oben ? bausteine : bausteineLinks) ? Visibility.Visible : Visibility.Collapsed;
            BausteinBereich.Width = double.NaN;
            BausteinBereich.HorizontalAlignment = HorizontalAlignment.Stretch;
            BausteinBereich.Background = null;
            Canvas.SetZIndex(BausteinBereich, 0);
            Grid.SetColumnSpan(BausteinBereich, 1);
            if (oben)
            {
                Grid.SetRow(BausteinBereich, 1);
                Grid.SetColumn(BausteinBereich, 0);
                BausteinBereich.BorderThickness = new Thickness(0, 1, 0, 1);
                if (bausteine)
                {
                    ZeileOben.Height = new GridLength(Math.Clamp(ViewModel.BausteinHoehe, 90, 600));
                    ZeileOben.MinHeight = 90;
                    SplitterZeilen.Visibility = Visibility.Visible;
                }
            }
            else
            {
                Grid.SetRow(BausteinBereich, 0);
                Grid.SetColumn(BausteinBereich, 0);
                BausteinBereich.BorderThickness = new Thickness(0, 1, 1, 0);
                if (bausteineUeber)
                {
                    Grid.SetColumnSpan(BausteinBereich, 2);
                    BausteinBereich.Width = Math.Min(340, breite - 40);
                    BausteinBereich.HorizontalAlignment = HorizontalAlignment.Left;
                    BausteinBereich.Background = deckend;
                    Canvas.SetZIndex(BausteinBereich, 20);
                }
                else if (bausteineLinks)
                {
                    SpalteLinks.Width = new GridLength(Math.Clamp(ViewModel.BausteinBreite, 220, Math.Max(220, breite - 420)));
                    SpalteLinks.MinWidth = 200;
                    SplitterSpalten.Visibility = Visibility.Visible;
                }
            }

            // Sachverhalt und Lösung
            UnterlagenBereich.Visibility = unterlagen ? Visibility.Visible : Visibility.Collapsed;
            UnterlagenBereich.Width = double.NaN;
            UnterlagenBereich.HorizontalAlignment = HorizontalAlignment.Stretch;
            UnterlagenBereich.Background = null;
            Canvas.SetZIndex(UnterlagenBereich, 0);
            Grid.SetColumnSpan(UnterlagenBereich, 1);
            if (unterlagen && linksUeber)
            {
                Grid.SetColumnSpan(UnterlagenBereich, 2);
                UnterlagenBereich.Width = Math.Min(380, breite - 40);
                UnterlagenBereich.HorizontalAlignment = HorizontalAlignment.Left;
                UnterlagenBereich.Background = deckend;
                Canvas.SetZIndex(UnterlagenBereich, 20);
            }
            else if (unterlagen)
            {
                SpalteLinks.Width = new GridLength(Math.Clamp(ViewModel.UnterlagenBreite, 220, Math.Max(220, breite - 420)));
                SpalteLinks.MinWidth = 200;
                SplitterSpalten.Visibility = Visibility.Visible;
            }

            // Übersicht
            Seitenleiste.Visibility = uebersicht ? Visibility.Visible : Visibility.Collapsed;
            if (uebersichtUeber)
            {
                Grid.SetColumn(Seitenleiste, 1);
                Grid.SetColumnSpan(Seitenleiste, 2);
                Seitenleiste.HorizontalAlignment = HorizontalAlignment.Right;
                Seitenleiste.Background = deckend;
                Canvas.SetZIndex(Seitenleiste, 20);
            }
            else
            {
                Grid.SetColumn(Seitenleiste, 2);
                Grid.SetColumnSpan(Seitenleiste, 1);
                Seitenleiste.HorizontalAlignment = HorizontalAlignment.Stretch;
                Seitenleiste.Background = null;
                Canvas.SetZIndex(Seitenleiste, 0);
            }

            ZeilenbreiteAnwenden();
        }
        finally
        {
            _layoutWirdGesetzt = false;
        }
    }

    /// <summary>Begrenzt die Zeilenlänge im Editor auf etwa 85 Zeichen (zentriert).</summary>
    void ZeilenbreiteAnwenden()
    {
        double em = ViewModel.Zoom * 4 / 3;
        Main.MaxWidth = ViewModel.ZeilenbreiteBegrenzt ? em * 44 + Main.Padding.Left + Main.Padding.Right : double.PositiveInfinity;
    }

    /// <summary>Merkt sich die mit dem Trenner eingestellte Größe der Bausteinliste.</summary>
    void BausteinBereich_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_layoutWirdGesetzt || !_geladen || BausteinBereich.Visibility != Visibility.Visible) return;
        if (ViewModel.BausteineOben)
        {
            if (SplitterZeilen.Visibility == Visibility.Visible && e.NewSize.Height > 60) ViewModel.BausteinHoehe = e.NewSize.Height;
        }
        else if (SplitterSpalten.Visibility == Visibility.Visible && e.NewSize.Width > 150)
            ViewModel.BausteinBreite = e.NewSize.Width;
    }

    void UnterlagenBereich_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_layoutWirdGesetzt || !_geladen || UnterlagenBereich.Visibility != Visibility.Visible) return;
        if (SplitterSpalten.Visibility == Visibility.Visible && e.NewSize.Width > 150) ViewModel.UnterlagenBreite = e.NewSize.Width;
    }

    /// <summary>Sachverhalt/Lösung neu aufbauen (nur bei geändertem Inhalt).</summary>
    void UnterlagenZeichnen()
    {
        string html = ViewModel.UnterlagenHtml;
        if (html == _unterlagenGezeichnet) return;
        _unterlagenGezeichnet = html;
        Unterlagenansicht.Fuellen(UnterlagenInhalt, html, 14, UnterlagenAktionen);
        UnterlagenScroller.ChangeView(null, 0, null, disableAnimation: true);
        if (UnterlagenSuche.Text.Trim().Length > 0) DispatcherQueue.TryEnqueue(() => UnterlagenSuchen(neu: false));
    }

    string? _unterlagenGezeichnet;

    // ---------- Animationen ----------

    /// <summary>Weiches Ein- und Ausblenden der Leisten und Kurzmeldungen.</summary>
    void AnimationenEinrichten()
    {
        try
        {
            Microsoft.UI.Composition.Compositor c = ElementCompositionPreview.GetElementVisual(Wurzel).Compositor;
            foreach (var (element, versatz) in new (UIElement, Vector3)[]
                     {
                         (BausteinBereich, new Vector3(-24, 0, 0)),
                         (UnterlagenBereich, new Vector3(-24, 0, 0)),
                         (Seitenleiste, new Vector3(24, 0, 0)),
                         (Toast, new Vector3(0, 16, 0)),
                         (KopfBereich, new Vector3(0, -12, 0)),
                     })
            {
                ElementCompositionPreview.SetIsTranslationEnabled(element, true);

                var einblenden = c.CreateAnimationGroup();
                var deckkraft = c.CreateScalarKeyFrameAnimation();
                deckkraft.Target = "Opacity";
                deckkraft.InsertKeyFrame(0f, 0f);
                deckkraft.InsertKeyFrame(1f, 1f);
                deckkraft.Duration = TimeSpan.FromMilliseconds(180);
                var bewegung = c.CreateVector3KeyFrameAnimation();
                bewegung.Target = "Translation";
                bewegung.InsertKeyFrame(0f, versatz);
                bewegung.InsertKeyFrame(1f, Vector3.Zero);
                bewegung.Duration = TimeSpan.FromMilliseconds(180);
                einblenden.Add(deckkraft);
                einblenden.Add(bewegung);
                ElementCompositionPreview.SetImplicitShowAnimation(element, einblenden);

                var ausblenden = c.CreateScalarKeyFrameAnimation();
                ausblenden.Target = "Opacity";
                ausblenden.InsertKeyFrame(0f, 1f);
                ausblenden.InsertKeyFrame(1f, 0f);
                ausblenden.Duration = TimeSpan.FromMilliseconds(120);
                ElementCompositionPreview.SetImplicitHideAnimation(element, ausblenden);
            }
        }
        catch (Exception ex)
        {
            Services.EinstellungsSpeicher.Protokollieren(ex); // Animationen sind verzichtbar.
        }
    }

    // ---------- Darstellung ----------

    /// <summary>Hell/Dunkel/System und Größe der Oberfläche anwenden.</summary>
    void DarstellungAnwenden()
    {
        ElementTheme thema = ViewModel.Darstellung switch { 1 => ElementTheme.Light, 2 => ElementTheme.Dark, _ => ElementTheme.Default };
        Wurzel.RequestedTheme = thema;
        bool dunkel = thema == ElementTheme.Dark
                      || (thema == ElementTheme.Default && Application.Current.RequestedTheme == ApplicationTheme.Dark);
        var titel = AppWindow.TitleBar;
        titel.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        titel.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        titel.ButtonForegroundColor = dunkel ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
        titel.ButtonHoverForegroundColor = titel.ButtonForegroundColor;

        Skala.ScaleX = Skala.ScaleY = ViewModel.Oberflaechenskala;
        LayoutAnwenden();
        TitelleisteAktualisieren();
    }

    /// <summary>Hintergrundfarbe des Punktebereichs im Baum (x:Bind-Funktion).</summary>
    public static Brush BereichFarbe(int art)
    {
        string schluessel = art switch
        {
            1 => "SystemFillColorCriticalBackgroundBrush",
            2 => "SystemFillColorSuccessBackgroundBrush",
            _ => "SystemFillColorNeutralBackgroundBrush",
        };
        return Application.Current.Resources.TryGetValue(schluessel, out object? b) && b is Brush pinsel
            ? pinsel
            : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }

    // ---------- Fenstergröße und -lage ----------

    void FensterWiederherstellen()
    {
        if (AppWindow.Presenter is OverlappedPresenter op)
        {
            op.PreferredMinimumWidth = 760;
            op.PreferredMinimumHeight = 560;
        }
        var (x, y, b, h, maximiert) = ViewModel.Fenster;
        if (x == int.MinValue || b < 400 || h < 300) return;
        var rechteck = new Windows.Graphics.RectInt32(x, y, b, h);
        // Nur wiederherstellen, wenn das Fenster auf einem vorhandenen Bildschirm liegt.
        if (DisplayArea.GetFromRect(rechteck, DisplayAreaFallback.None) is null) return;
        AppWindow.MoveAndResize(rechteck);
        if (maximiert && AppWindow.Presenter is OverlappedPresenter p) p.Maximize();
    }

    void FensterMerken()
    {
        if (AppWindow.Presenter is not OverlappedPresenter p) return; // z. B. Vollbild: letzte Lage behalten
        bool maximiert = p.State == OverlappedPresenterState.Maximized;
        var alt = ViewModel.Fenster;
        if (maximiert || p.State == OverlappedPresenterState.Minimized)
            ViewModel.Fenster = (alt.X, alt.Y, alt.Breite, alt.Hoehe, maximiert || alt.Maximiert);
        else
            ViewModel.Fenster = (AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height, false);
    }

    // ---------- Zoom ----------

    void Main_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(VirtualKeyModifiers.Control)) return;
        int delta = e.GetCurrentPoint(Main).Properties.MouseWheelDelta;
        if (delta != 0) ZoomAendern(delta > 0 ? +1 : -1);
        e.Handled = true;
    }

    void ZoomPlus_Click(object sender, RoutedEventArgs e) => ZoomAendern(+1);
    void ZoomMinus_Click(object sender, RoutedEventArgs e) => ZoomAendern(-1);
    void ZoomZurueck_Click(object sender, RoutedEventArgs e) => ZoomSetzen(ZoomStandard);

    void ZoomAendern(int richtung) => ZoomSetzen(ViewModel.Zoom + richtung * (ViewModel.Zoom >= 20 ? 2 : 1));

    void ZoomSetzen(double groesse)
    {
        ViewModel.Zoom = Math.Clamp(Math.Round(groesse), ZoomMin, ZoomMax);
        ZoomAnwenden();
    }

    /// <summary>Setzt die Schriftgröße des ganzen Entwurfs (wirkt sich nicht auf das Word-Gutachten aus).</summary>
    void ZoomAnwenden()
    {
        float groesse = (float)Math.Clamp(ViewModel.Zoom, ZoomMin, ZoomMax);
        string schrift = EditorSchriftName;
        ITextCharacterFormat standard = Main.TextDocument.GetDefaultCharacterFormat();
        standard.Size = groesse;
        standard.Name = schrift;
        Main.TextDocument.SetDefaultCharacterFormat(standard);

        ITextSelection auswahl = Main.Document.Selection;
        int start = auswahl.StartPosition, ende = auswahl.EndPosition;
        ITextRange alles = Main.Document.GetRange(0, 0);
        alles.Expand(TextRangeUnit.Story);
        if (alles.Length > 0)
        {
            alles.CharacterFormat.Size = groesse;
            alles.CharacterFormat.Name = schrift;
        }
        auswahl.SetRange(start, ende);

        ZoomAnzeige.Content = $"{Math.Round(groesse / ZoomStandard * 100)} %";
        VorschlagText.FontFamily = new FontFamily(schrift);
        ZeilenbreiteAnwenden();
    }

    string EditorSchriftName => string.IsNullOrWhiteSpace(ViewModel.EditorSchrift) ? "Segoe UI" : ViewModel.EditorSchrift.Trim();

    // ---------- Vollbild ----------

    bool IstVollbild => AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen;

    void Vollbild_Click(object sender, RoutedEventArgs e) => VollbildUmschalten();

    void VollbildUmschalten()
    {
        AppWindow.SetPresenter(IstVollbild ? AppWindowPresenterKind.Overlapped : AppWindowPresenterKind.FullScreen);
        VollbildSymbolSetzen();
    }

    void VollbildSymbolSetzen()
    {
        bool voll = IstVollbild;
        VollbildSymbol.Glyph = voll ? "\uE73F" : "\uE740";
        ToolTipService.SetToolTip(VollbildKnopf, voll ? "Vollbild verlassen (F11 oder Esc im Editor)" : "Vollbild (F11)");
        TitelleisteAktualisieren();
    }

    // ---------- Satzvorschläge ----------

    static readonly Regex Satzanfang = new(@"(?:[.!?:;]\s+|[\r\n\v])", RegexOptions.CultureInvariant);

    IReadOnlyList<Satzvorschlag> _vorschlaege = Array.Empty<Satzvorschlag>();
    int _vorschlagIndex, _vorschlagPosition = -1, _fragmentAnfang;
    bool _vorschlagGeplant, _vorschlagUnterdrueckt;

    bool VorschlagSichtbar => VorschlagRahmen.Visibility == Visibility.Visible;

    /// <summary>Nach einer Texteingabe: Vorschlag berechnen, sobald das Layout aktualisiert ist.</summary>
    void VorschlagPlanen()
    {
        _vorschlagUnterdrueckt = false;
        if (_vorschlagGeplant) return;
        _vorschlagGeplant = true;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            _vorschlagGeplant = false;
            VorschlagAktualisieren();
        });
    }

    /// <summary>Schreibmarke bewegt (ohne Eingabe): Vorschlag ausblenden, wenn er nicht mehr dort steht.</summary>
    void VorschlagPruefen()
    {
        if (VorschlagSichtbar && Main.Document.Selection is { } s && (s.Length != 0 || s.StartPosition != _vorschlagPosition))
            VorschlagVerbergen();
    }

    void VorschlagVerbergen()
    {
        VorschlagRahmen.Visibility = Visibility.Collapsed;
        _vorschlaege = Array.Empty<Satzvorschlag>();
        _vorschlagPosition = -1;
    }

    void VorschlagAktualisieren()
    {
        if (_vorschlagUnterdrueckt || Main.FocusState == FocusState.Unfocused)
        {
            VorschlagVerbergen();
            return;
        }
        ITextSelection auswahl = Main.Document.Selection;
        int pos = auswahl.StartPosition;
        if (auswahl.Length != 0 || pos == 0)
        {
            VorschlagVerbergen();
            return;
        }

        // Nur am Wortende, nicht mitten im Wort.
        Main.Document.GetRange(pos, pos + 1).GetText(TextGetOptions.None, out string danach);
        if (danach.Length > 0 && char.IsLetterOrDigit(danach[0]))
        {
            VorschlagVerbergen();
            return;
        }

        // Kürzel vor der Schreibmarke: den Baustein grau ankündigen (Tab fügt ihn ein) – auch ohne Satzvorschläge.
        Main.Document.GetRange(Math.Max(0, pos - 30), pos).GetText(TextGetOptions.None, out string kurz);
        Match km = KuerzelVorCursor.Match(kurz);
        if (km.Success && ViewModel.BausteinNachKuerzel(km.Groups[1].Value) is { } kb)
        {
            _vorschlaege = new[] { new Satzvorschlag(kb, kb.Text, Fortsetzung: false) };
            _vorschlagIndex = 0;
            _vorschlagPosition = pos;
            _fragmentAnfang = pos - km.Length;
            VorschlagAnzeigen();
            return;
        }
        if (!ViewModel.Satzvorschlaege)
        {
            VorschlagVerbergen();
            return;
        }

        int start = Math.Max(0, pos - 300);
        Main.Document.GetRange(start, pos).GetText(TextGetOptions.None, out string davor);
        int satz = 0;
        foreach (Match m in Satzanfang.Matches(davor)) satz = m.Index + m.Length;
        string fragment = davor[satz..].TrimStart();
        _vorschlaege = ViewModel.SatzvorschlaegeFinden(fragment);
        if (_vorschlaege.Count == 0)
        {
            VorschlagVerbergen();
            return;
        }
        _vorschlagIndex = 0;
        _vorschlagPosition = pos;
        _fragmentAnfang = pos - fragment.Length;
        VorschlagAnzeigen();
    }

    void VorschlagWeiter()
    {
        if (_vorschlaege.Count < 2) return;
        _vorschlagIndex = (_vorschlagIndex + 1) % _vorschlaege.Count;
        VorschlagAnzeigen();
    }

    void VorschlagAnzeigen()
    {
        Satzvorschlag v = _vorschlaege[_vorschlagIndex];
        double schrift = ViewModel.Zoom * 4 / 3; // Punkt → DIP
        VorschlagText.FontSize = schrift;
        VorschlagText.Inlines.Clear();
        VorschlagText.Inlines.Add(new Run { Text = v.Fortsetzung ? v.Text : "→ " + v.Text });
        string taste = _vorschlaege.Count > 1 ? $"  Tab · Strg+Leertaste {_vorschlagIndex + 1}/{_vorschlaege.Count}" : "  Tab";
        VorschlagText.Inlines.Add(new Run { Text = taste, FontSize = Math.Max(10, schrift * 0.65) });

        if (!SchreibmarkeErmitteln(out Rect marke))
        {
            VorschlagVerbergen();
            return;
        }

        // Rest der Zeile frei und genug Platz: grau direkt an der Schreibmarke; sonst als Hinweis unter der Zeile.
        Main.Document.GetRange(_vorschlagPosition, _vorschlagPosition + 200).GetText(TextGetOptions.None, out string rest);
        int zeilenende = rest.IndexOfAny(new[] { '\r', '\n', '\v' });
        bool zeileFrei = (zeilenende < 0 ? rest : rest[..zeilenende]).Trim().Length == 0;
        Rect editor = Main.TransformToVisual(VorschlagEbene).TransformBounds(new Rect(0, 0, Main.ActualWidth, Main.ActualHeight));
        double links = editor.Left + Main.Padding.Left, rechts = editor.Right - Main.Padding.Right;
        double platz = rechts - marke.Right;

        if (v.Fortsetzung && zeileFrei && platz >= 220)
        {
            VorschlagRahmen.Background = null;
            VorschlagRahmen.BorderBrush = null;
            VorschlagRahmen.BorderThickness = new Thickness(0);
            VorschlagRahmen.Padding = new Thickness(0);
            VorschlagRahmen.MaxWidth = platz;
            Canvas.SetLeft(VorschlagRahmen, marke.Right);
            Canvas.SetTop(VorschlagRahmen, marke.Top);
        }
        else
        {
            VorschlagRahmen.Background = Farben.Background;
            VorschlagRahmen.BorderBrush = Farben.BorderBrush;
            VorschlagRahmen.BorderThickness = new Thickness(1);
            VorschlagRahmen.Padding = new Thickness(8, 4, 8, 4);
            VorschlagRahmen.MaxWidth = Math.Max(200, rechts - links);
            Canvas.SetLeft(VorschlagRahmen, Math.Clamp(marke.Left - 8, links, Math.Max(links, rechts - 300)));
            Canvas.SetTop(VorschlagRahmen, marke.Bottom + 4);
        }
        VorschlagRahmen.Visibility = Visibility.Visible;
    }

    /// <summary>Position der Schreibmarke relativ zur Vorschlagsebene (über Bildschirmkoordinaten, daher unabhängig von Rand und Bildlauf).</summary>
    bool SchreibmarkeErmitteln(out Rect marke)
    {
        marke = default;
        try
        {
            Main.Document.Selection.GetRect(PointOptions.None, out Rect bildschirm, out _);
            IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var ursprung = new PUNKT();
            if (!ClientToScreen(hwnd, ref ursprung)) return false;
            double skala = Content.XamlRoot?.RasterizationScale ?? 1.0;
            var oben = new Point((bildschirm.Left - ursprung.X) / skala, (bildschirm.Top - ursprung.Y) / skala);
            var unten = new Point((bildschirm.Right - ursprung.X) / skala, (bildschirm.Bottom - ursprung.Y) / skala);
            GeneralTransform zurEbene = VorschlagEbene.TransformToVisual(null).Inverse;
            Point a = zurEbene.TransformPoint(oben), b = zurEbene.TransformPoint(unten);
            marke = new Rect(a.X, a.Y, Math.Max(1, b.X - a.X), Math.Max(1, b.Y - a.Y));
            // Plausibilität: muss in der Editorfläche liegen, sonst Ersatzberechnung (Client-Koordinaten + Innenabstand).
            Rect editor = Main.TransformToVisual(VorschlagEbene).TransformBounds(new Rect(0, 0, Main.ActualWidth, Main.ActualHeight));
            if (marke.X < editor.Left - 2 || marke.Y < editor.Top - 2 || marke.X > editor.Right + 2 || marke.Y > editor.Bottom + 2)
            {
                Main.Document.Selection.GetRect(PointOptions.ClientCoordinates, out Rect c, out _);
                marke = new Rect(editor.Left + c.X + Main.Padding.Left, editor.Top + c.Y + Main.Padding.Top, Math.Max(1, c.Width), Math.Max(1, c.Height));
            }
            return marke.Height > 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Tab: übernimmt den angezeigten Vorschlag.</summary>
    bool VorschlagUebernehmen()
    {
        if (!VorschlagSichtbar || _vorschlaege.Count == 0) return false;
        ITextSelection auswahl = Main.Document.Selection;
        if (auswahl.StartPosition != _vorschlagPosition) return false;
        Satzvorschlag v = _vorschlaege[_vorschlagIndex];
        int pos = _vorschlagPosition, anfang = _fragmentAnfang;
        VorschlagVerbergen();
        if (v.Fortsetzung)
        {
            string text = v.Text.TrimEnd();
            text += text.Length > 0 && text[^1] is '.' or '!' or '?' or ':' ? " " : ". ";
            auswahl.SetRange(pos, pos);
            auswahl.SetText(TextSetOptions.None, text);
            auswahl.CharacterFormat.Size = (float)ViewModel.Zoom;
            auswahl.StartPosition = auswahl.EndPosition;
        }
        else
        {
            Main.Document.GetRange(anfang, pos).SetText(TextSetOptions.None, string.Empty);
            auswahl.SetRange(anfang, anfang);
            ViewModel.BausteinVerwenden(v.Baustein);
        }
        return true;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PUNKT { public int X, Y; }

    [DllImport("user32.dll")]
    static extern bool ClientToScreen(IntPtr hwnd, ref PUNKT punkt);

    // ---------- Diktat ----------

    async void Diktat_Click(object sender, RoutedEventArgs e) => await DiktatStartenAsync();

    /// <summary>Startet die in Windows enthaltene Spracheingabe (Win+H) im Editor.</summary>
    async Task DiktatStartenAsync()
    {
        Main.Focus(FocusState.Programmatic);
        await Task.Delay(150);
        const byte Win = 0x5B, H = 0x48;
        const uint Loslassen = 0x0002;
        keybd_event(Win, 0, 0, UIntPtr.Zero);
        keybd_event(H, 0, 0, UIntPtr.Zero);
        keybd_event(H, 0, Loslassen, UIntPtr.Zero);
        keybd_event(Win, 0, Loslassen, UIntPtr.Zero);
    }

    [DllImport("user32.dll")]
    static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    void NeueArbeit_Click(object sender, RoutedEventArgs e) => ViewModel.NeueArbeit();

    // ---------- Hilfe ----------

    async void Hilfe_Click(object sender, RoutedEventArgs e) => await HilfeZeigenAsync(einfuehrung: false);

    static readonly (string Taste, string Wirkung)[] Tastenkuerzel =
    {
        ("Strg+Eingabe", "Gutachten generieren und Note eintragen"),
        ("Strg+1 / Strg+2", "Reiter „Gutachten“ / „Rohpunkte“"),
        ("Strg+B / Strg+I", "Fett / kursiv (im Entwurf und in Rohpunkte-Kommentaren; wird in Word übernommen)"),
        ("Strg+N", "Neue Arbeit: Eingaben und Entwurf leeren (über die Meldung zurückholbar)"),
        ("Strg+F", "Bausteinsuche (Text, #Kürzel, Nummer); Eingabetaste fügt den ersten Treffer ein, Esc beendet"),
        ("Kürzel + Tab", "Baustein mit festem Kürzel einfügen, z. B. „subs1“ oder „#subs1“ tippen, dann Tab"),
        ("Alt+Nummer", "Baustein nach Nummer einfügen (obere Ziffernreihe); bei einer Gruppe wird diese ausgewählt"),
        ("Strg+S", "Markierten Text als neuen Baustein speichern (ohne Markierung: alles speichern)"),
        ("Strg+Z", "Im Baum: letzte Änderung an Bausteinen rückgängig · im Editor: Texteingabe rückgängig"),
        ("Entf / F2", "Im Baum: Baustein löschen (rückgängig möglich) / bearbeiten"),
        ("Strg+Plus / Minus / 0", "Editor vergrößern / verkleinern / zurücksetzen (auch Strg+Mausrad)"),
        ("Tab / Strg+Leertaste / Esc", "Grau angezeigten Satzvorschlag übernehmen / nächsten Vorschlag zeigen / verwerfen"),
        ("F8 oder Win+H", "Diktieren mit der Windows-Spracheingabe"),
        ("F11", "Vollbild ein/aus (Esc im Editor verlässt das Vollbild)"),
        ("F6", "Bausteinliste ein-/ausblenden (bei schmalem Fenster überlagernd)"),
        ("F7", "Sachverhalt bzw. Lösung in der linken Leiste ein-/ausblenden"),
        ("Strg+Klick im Text", "Auf einem Normzitat: Volltext in der Seitenleiste · auf „Frage 1“, „Tatkomplex 2“ …: Stelle in der Lösung"),
        ("Alt+Pfeil links/rechts", "Normenansicht: zurück bzw. vor"),
        ("Eingabe (Rohpunkte)", "Punkte übernehmen und zur nächsten Station springen (Umschalt+Eingabe: zurück)"),
        ("F9", "Übersicht (Seitenleiste) ein-/ausblenden"),
        ("Strg+Umschalt+F", "Fokusmodus: nur der Editor (Esc beendet)"),
        ("F1", "Diese Hilfe"),
    };

    async Task HilfeZeigenAsync(bool einfuehrung)
    {
        var inhalt = new StackPanel { Spacing = 10, MaxWidth = 720 };
        void Absatz(string text, bool fett = false) => inhalt.Children.Add(new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            FontWeight = fett ? FontWeights.SemiBold : FontWeights.Normal,
        });

        if (einfuehrung)
            Absatz("Willkommen! Das Gutachtentool erstellt aus Ihrem Entwurf und Textbausteinen ein Word-Gutachten und trägt die Note in die Notenliste der Klausur ein.");
        Absatz("So geht’s", fett: true);
        Absatz("1. Oben eine Klausur wählen oder einen neuen Namen eingeben. 2. Kennziffer (bzw. Name und Matrikelnummer), Note, Seiten und Formalkriterien angeben. " +
               "3. Den Text schreiben, diktieren oder aus Bausteinen zusammensetzen (Klick, Ziehen in den Editor, Alt+Nummer, Kürzel + Tab). 4. Strg+Eingabe.");
        Absatz("Satzvorschläge", fett: true);
        Absatz("Beim Tippen schlägt das Programm passende Sätze aus den Bausteinen vor – grau an der Schreibmarke (Fortsetzung des begonnenen Satzes) " +
               "oder als Hinweis darunter (Baustein, der die getippten Wörter enthält). Tab übernimmt, Strg+Leertaste wechselt, Esc verwirft. Ein-/ausschalten mit der Glühbirne über dem Editor.");
        Absatz("Bausteine", fett: true);
        Absatz("Gruppen lassen sich beliebig verschachteln (Rechtsklick › Neue Gruppe hier, oder Ziehen). Jeder Baustein kann ein festes Kürzel und einen Punktebereich erhalten; " +
               "Bausteine, die nicht zur gewählten Note passen, werden abgeblendet, und vor dem Generieren erscheint ein Hinweis.");
        Absatz("Klausur einrichten", fett: true);
        Absatz("Ordner › Klausureinstellungen: eigene Formalkriterien (Satzvorlage und Stufen) sowie Aufgaben mit Höchstpunkten für Teilpunkte – wahlweise mit Ausgabe im Gutachten und Notenvorschlag. " +
               "Ordner › Teilnehmendenliste importieren: Vorschläge bei der Eingabe, Prüfung beim Generieren und offene Arbeiten in der Übersicht.");
        Absatz("Rohpunkte", fett: true);
        Absatz("Mit einem Rohpunkteschema (rohpunkte.json bzw. rohpunkte.xlsx wie im KI-Portal, auch per Paket) erscheint der Reiter „Rohpunkte“: je Prüfungsstation Punkte in Schritten von 0,25 und ein Kommentar, " +
               "dazu Bonus- und Minuspunkte (je bis 10). Die Note wird aus der Umrechnungstabelle des Schemas vorgeschlagen; eine abweichende Note bleibt möglich und wird markiert. " +
               "Bewertungshinweise (Infosymbol) sieht nur die korrigierende Person. Das Gutachten erhält als „Anhang: Bewertungsbogen“ eine Tabelle mit Gliederungspunkten, Kommentaren und Punkten (ohne Note und Umrechnung); " +
               "die Notenliste zusätzlich Rohpunkte, Bonus, Minus, Gesamt und die Summen je Aufgabe. Schema und Umrechnung lassen sich über Ordner › Rohpunkteschema in Excel bearbeiten ändern. " +
               "Ein Klick auf einen Baustein fügt ihn im Reiter „Rohpunkte“ in den zuletzt bearbeiteten Kommentar ein.");
        Absatz("Übersicht und Ändern", fett: true);
        Absatz("Die Seitenleiste (F9) zeigt bewertete und offene Kennungen, Durchschnitt, Median, Durchfallquote und Notenverteilung. Ein Klick auf eine bewertete Arbeit öffnet deren Entwurf zum Ändern.");
        Absatz("Sachverhalt und Lösung", fett: true);
        Absatz("Die linke Leiste zeigt statt der Bausteine auch Sachverhalt, Lösung oder beides (Klick auf die Überschrift der Leiste oder F7). " +
               "Stehen die Bausteine über dem Editor, bleibt die Leiste diesen Inhalten vorbehalten. Der Text kommt mit Paketen aus dem KI-Portal, " +
               "lässt sich als Markdown bearbeiten (Stiftsymbol) oder aus einer Datei übernehmen (.md, .txt, .docx) und wird mit exportierten Paketen weitergegeben. " +
               "Strg+Mausrad vergrößert die Anzeige.");
        Absatz("Seitenleiste Sachverhalt/Lösung", fett: true);
        Absatz("Das Suchfeld hebt Fundstellen hervor (Eingabe: nächste, Umschalt+Eingabe: vorige). Markierter Text lässt sich per Rechtsklick oder Schaltfläche als Baustein speichern. " +
               "Ein Klick auf eine Überschrift der Lösung springt zur passenden Zeile im Reiter „Rohpunkte“. Wörter aus Sachverhalt und Lösung (etwa Namen) streicht die Rechtschreibprüfung nicht an.");
        Absatz("Normen", fett: true);
        Absatz("Die Seitenleiste „Normen“ zeigt die Normensammlung des Pakets und sucht nach Zitaten („§ 433 Abs. 2 BGB“, „Art. 6 Abs. 1 lit. f DSGVO“), Gesetzen („GmbHG“) oder Begriffen. " +
               "Fehlende Bundesgesetze lädt das Programm von gesetze-im-internet.de, EU-Rechtsakte von EUR-Lex (jeweils ganz, zum Durchblättern), übrige Normen wie Landesrecht einzeln über kvasir; alles bleibt für alle Klausuren gespeichert. " +
               "Zitate in Sachverhalt, Lösung und im eigenen Text zeigen beim Überfahren mit der Maus den zitierten Absatz, Satz, die Nummer oder den Buchstaben; Klick (in der Leiste) bzw. Strg+Klick (im Text) öffnet die Norm im Gesetz und hebt die Stelle hervor. " +
               "„▲/▼“ blendet weitere Vorschriften ein, Zurück/Vor (Alt+Pfeil) führt durch den Verlauf. Über „Bearbeiten“ lassen sich eigene Normen (z. B. Prüfungsordnung) als Markdown ergänzen; sie gehören zur Klausur und reisen im Paket mit.");
        Absatz("Korrekturfortschritt", fett: true);
        Absatz("Ordner › Kennziffern eingeben… legt Kennziffern als Bereich („1001-1060“) oder Liste an; nicht vergebene lassen sich gleich mit angeben oder später in der Übersicht entfernen (Rechtsklick). " +
               "Das Programm misst die Bearbeitungszeit je Arbeit (Pausen über drei Minuten zählen nicht) und zeigt in der Übersicht Beginn, Ende, Dauer, den Median und eine Restzeitprognose.");
        Absatz("Bausteinstatistik", fett: true);
        Absatz("Beim Erstellen zählt das Programm, welche Bausteine vorkommen: häufig genutzte sind im Baum farbig markiert, bisher nie genutzte (ab fünf Gutachten) gelb. " +
               "Bei Paketen aus dem KI-Portal lässt sich die Statistik ohne Kennziffern oder Gutachtentexte an das Portal senden (Ordner › Bausteinstatistik senden).");
        Absatz("Zweitgutachten", fett: true);
        Absatz("Enthält ein Paket des KI-Portals Erstnoten (nur Kennziffern und Punkte), zeigt das Programm zur eingegebenen Kennziffer die Erstnote, " +
               "warnt vor dem Erstellen bei einer Abweichung ab 4 Punkten und trägt Erstnote und Differenz in die Notenliste ein. " +
               "Die Kennziffern ergänzen die Teilnehmendenliste. „Einverstanden“ übernimmt die Erstnote und fügt einen Zustimmungssatz ein; die Übersicht zeigt die mittlere Abweichung. " +
               "Beim Export eigener Pakete werden Erstnoten nie mitgegeben.");
        Absatz("Prüfungsportal", fett: true);
        Absatz("Ordner › Export für das Prüfungsportal erstellt ein ZIP mit der Notenliste und den Gutachten als PDF (Umwandlung mit Word) für „Noten/Gutachten hochladen“.");
        Absatz("Pakete", fett: true);
        Absatz("Ordner › Klausur als Paket exportieren erzeugt eine .gtpaket-Datei mit Formulierungen, Word-Vorlage, Klausureinstellungen, Rohpunkteschema, Sachverhalt und Lösung (ohne Noten und personenbezogene Daten). " +
               "Ein Doppelklick auf ein Paket öffnet es im Gutachtentool.");
        Absatz("Diktat", fett: true);
        Absatz("Die Windows-Spracheingabe verarbeitet Sprache in der Regel online bei Microsoft. Bitte keine Namen oder Matrikelnummern diktieren.");
        Absatz("Tastenkürzel", fett: true);

        var tabelle = new Grid { ColumnSpacing = 16, RowSpacing = 4 };
        tabelle.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        tabelle.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int i = 0; i < Tastenkuerzel.Length; i++)
        {
            tabelle.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var taste = new TextBlock { Text = Tastenkuerzel[i].Taste, FontWeight = FontWeights.SemiBold };
            var wirkung = new TextBlock { Text = Tastenkuerzel[i].Wirkung, TextWrapping = TextWrapping.Wrap };
            Grid.SetRow(taste, i);
            Grid.SetRow(wirkung, i);
            Grid.SetColumn(wirkung, 1);
            tabelle.Children.Add(taste);
            tabelle.Children.Add(wirkung);
        }
        inhalt.Children.Add(tabelle);
        Absatz("Darstellung, Oberflächengröße, Editorschrift, Zeilenbreite und Verhalten (Word öffnen, Satzvorschläge) stellen Sie über das Zahnrad oben rechts ein.");
        Absatz("Diese Hilfe ist jederzeit über das Fragezeichen oben rechts oder F1 erreichbar. Alle Schaltflächen zeigen beim Darüberfahren einen Hinweis.");

        var dialog = new ContentDialog
        {
            Title = einfuehrung ? "Willkommen im Gutachtentool" : "Hilfe und Tastenkürzel",
            Content = new ScrollViewer { Content = inhalt, MaxHeight = 560, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            CloseButtonText = einfuehrung ? "Los geht’s" : "Schließen",
            DefaultButton = ContentDialogButton.Close,
        };
        dialog.Resources["ContentDialogMaxWidth"] = 800.0;
        await DialogZeigenAsync(dialog);
    }
}
