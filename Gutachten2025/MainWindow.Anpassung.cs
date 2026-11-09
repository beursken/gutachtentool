using Gutachten2025.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.ComponentModel;
using System.Linq;
using Windows.Data.Pdf;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Gutachten2025;

/// <summary>
/// Anpassung an kleine Fenster bzw. hohe Skalierung (Übersicht scrollbar, Formalkriterien nur bei Bedarf aufklappbar),
/// Notenbestätigung ohne Änderung und PDF-Anzeige (Bearbeitung/Erstvotum) in der linken Leiste.
/// </summary>
public sealed partial class MainWindow
{
    bool _einpassenGeplant;

    void AnpassungEinrichten()
    {
        // Oberer Teil der Übersicht höchstens gut die Hälfte der Leiste: Die Liste der Arbeiten bleibt immer sichtbar.
        Seitenleiste.SizeChanged += (_, e) => ObenScroll.MaxHeight = Math.Max(140, e.NewSize.Height * 0.55);
        ArbeitKarte.SizeChanged += (_, _) => KriterienEinpassenSpaeter();
        BewertungKarte.SizeChanged += (_, _) => KriterienEinpassenSpaeter();
        ViewModel.Kriterien.CollectionChanged += (_, _) => KriterienEinpassenSpaeter();
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ViewModels.MainViewModel.KriterienAufgeklappt) or nameof(ViewModels.MainViewModel.HatSchema)
                or nameof(ViewModels.MainViewModel.TeilpunkteSichtbar))
                KriterienEinpassenSpaeter();
        };
    }

    void KriterienEinpassenSpaeter()
    {
        if (_einpassenGeplant) return;
        _einpassenGeplant = true;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            _einpassenGeplant = false;
            KriterienEinpassen();
        });
    }

    /// <summary>
    /// Formalkriterien direkt zeigen, solange die Karte „Bewertung“ damit nicht höher wird als die Karte „Arbeit“;
    /// sonst als aufklappbarer Bereich mit Zusammenfassung.
    /// </summary>
    void KriterienEinpassen()
    {
        if (ArbeitKarte.Child is not FrameworkElement arbeit || BewertungKarte.ActualWidth <= 0) return;
        double breite = Math.Max(100, BewertungKarte.ActualWidth - BewertungKarte.Padding.Left - BewertungKarte.Padding.Right - 2);

        Visibility vorher = KriterienListe.Visibility;
        KriterienListe.Visibility = Visibility.Visible;
        KriterienListe.Measure(new Size(breite, double.PositiveInfinity));
        double liste = KriterienListe.DesiredSize.Height;
        KriterienListe.Visibility = vorher;

        double andere = BewertungInhalt.Children.OfType<FrameworkElement>()
            .Where(c => c != KriterienKopf && c != KriterienListe && c.Visibility == Visibility.Visible)
            .Sum(c => c.DesiredSize.Height + BewertungInhalt.Spacing);
        double verfuegbar = arbeit.DesiredSize.Height;

        bool klappbar = ViewModel.Kriterien.Count > 0 && liste + andere > verfuegbar + 4;
        bool offen = !klappbar || ViewModel.KriterienAufgeklappt;
        KriterienKopf.Visibility = klappbar ? Visibility.Visible : Visibility.Collapsed;
        KriterienListe.Visibility = offen ? Visibility.Visible : Visibility.Collapsed;
        KriterienPfeil.Glyph = ViewModel.KriterienAufgeklappt ? "" : "";
    }

    void KriterienKopf_Click(object sender, RoutedEventArgs e) => ViewModel.KriterienAufgeklappt = !ViewModel.KriterienAufgeklappt;

    /// <summary>Auswahl geschlossen, auch ohne Änderung (z. B. erneuter Klick auf 8 Punkte): Note gilt als gewählt.</summary>
    void Grade_DropDownClosed(object? sender, object e) => ViewModel.NoteBestaetigen();

    // ---------- PDF in der linken Leiste ----------

    int _pdfLauf;

    async void PdfZeichnen(string pfad)
    {
        int lauf = ++_pdfLauf;
        UnterlagenInhalt.Children.Clear();
        try
        {
            StorageFile datei = await StorageFile.GetFileFromPathAsync(pfad);
            PdfDocument dokument = await PdfDocument.LoadFromFileAsync(datei);
            double breite = Math.Max(300, UnterlagenScroller.ActualWidth - 24);
            double skala = (Inhalt.XamlRoot?.RasterizationScale ?? 1) * 1.5; // scharf auch beim Vergrößern
            for (uint i = 0; i < dokument.PageCount; i++)
            {
                if (lauf != _pdfLauf) return;
                using PdfPage seite = dokument.GetPage(i);
                using var strom = new InMemoryRandomAccessStream();
                await seite.RenderToStreamAsync(strom, new PdfPageRenderOptions { DestinationWidth = (uint)Math.Min(4000, breite * skala) });
                var bild = new BitmapImage();
                await bild.SetSourceAsync(strom);
                if (lauf != _pdfLauf) return;
                UnterlagenInhalt.Children.Add(new Image
                {
                    Source = bild, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 0, 8),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                });
            }
        }
        catch (Exception ex)
        {
            EinstellungsSpeicher.Protokollieren(ex);
            if (lauf != _pdfLauf) return;
            UnterlagenInhalt.Children.Add(new TextBlock
            {
                Text = $"Die PDF-Datei lässt sich hier nicht anzeigen ({ex.Message}). „Datei öffnen“ zeigt sie im PDF-Programm.",
                TextWrapping = TextWrapping.Wrap, Opacity = 0.8,
            });
        }
    }
}
