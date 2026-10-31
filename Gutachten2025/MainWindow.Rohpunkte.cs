using Gutachten2025.Models;
using Gutachten2025.Rohpunkte;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Windows.Globalization.NumberFormatting;

namespace Gutachten2025;

/// <summary>Reiter „Gutachten | Rohpunkte“, Fett/Kursiv für Editor und Kommentare.</summary>
public sealed partial class MainWindow
{
    bool _rohpunkteReiter;

    /// <summary>Punkteingabe in Schritten von 0,25 (Anzeige ohne überflüssige Nachkommastellen).</summary>
    public static INumberFormatter2 Viertelformat { get; } = new DecimalFormatter
    {
        IntegerDigits = 1,
        FractionDigits = 0,
        NumberRounder = new IncrementNumberRounder { Increment = 0.25, RoundingAlgorithm = RoundingAlgorithm.RoundHalfUp },
    };

    public static Brush ZeilenFarbe(Zeilenart art)
    {
        string? schluessel = art switch
        {
            Zeilenart.Aufgabe => "SubtleFillColorSecondaryBrush",
            Zeilenart.Ueberschrift => "SubtleFillColorTertiaryBrush",
            Zeilenart.Bonus => "SystemFillColorSuccessBackgroundBrush",
            Zeilenart.Minus => "SystemFillColorCriticalBackgroundBrush",
            _ => null,
        };
        return schluessel is not null && Application.Current.Resources.TryGetValue(schluessel, out object? b) && b is Brush pinsel
            ? pinsel
            : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }

    void Reiter_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args) =>
        ReiterSetzen(sender.SelectedItem == ReiterRohpunkte, fokussieren: false);

    /// <summary>Strg+1 / Strg+2 und programmatischer Wechsel.</summary>
    void ReiterSetzen(bool rohpunkte, bool fokussieren = true)
    {
        _rohpunkteReiter = rohpunkte;
        if (Reiter is null || EditorFlaeche is null || RohpunkteFlaeche is null) return; // noch in InitializeComponent
        if ((Reiter.SelectedItem == ReiterRohpunkte) != rohpunkte)
            Reiter.SelectedItem = rohpunkte ? ReiterRohpunkte : ReiterGutachten;
        EditorFlaeche.Visibility = rohpunkte ? Visibility.Collapsed : Visibility.Visible;
        RohpunkteFlaeche.Visibility = rohpunkte ? Visibility.Visible : Visibility.Collapsed;
        if (rohpunkte) VorschlagVerbergen();
        if (!fokussieren) return;
        if (!rohpunkte) Main.Focus(FocusState.Programmatic);
        else DispatcherQueue.TryEnqueue(() =>
        {
            if (Kommentarbindung.ErsteEingabe(RohpunkteListe) is Control c) c.Focus(FocusState.Programmatic);
        });
    }

    public void RohpunkteZeigen() => ReiterSetzen(true);

    void RohpunkteReiter_Click(object sender, RoutedEventArgs e) => ReiterSetzen(true);

    // ---------- Fett / Kursiv ----------

    /// <summary>Ziel der Formatknöpfe: zuletzt fokussierter Kommentar im Rohpunkte-Reiter, sonst der Editor.</summary>
    RichEditBox FormatZiel => _rohpunkteReiter && Kommentarbindung.Zuletzt is { IsLoaded: true } k ? k : Main;

    void Fett_Click(object sender, RoutedEventArgs e) => FormatUmschalten(fett: true);

    void Kursiv_Click(object sender, RoutedEventArgs e) => FormatUmschalten(fett: false);

    void FormatUmschalten(bool fett)
    {
        RichEditBox box = FormatZiel;
        ITextCharacterFormat f = box.Document.Selection.CharacterFormat;
        if (fett) f.Bold = FormatEffect.Toggle;
        else f.Italic = FormatEffect.Toggle;
        if (ReferenceEquals(box, Main)) ViewModel.EntwurfGeaendert();
        else Kommentarbindung.Uebernehmen(box);
        box.Focus(FocusState.Programmatic);
    }

    // ---------- IEntwurfEditor ----------

    public FormatierterText FormatLesen() => FormatierterText.AusLaeufen(RichText.Lesen(Main));
}

/// <summary>Lesen und Schreiben von Text mit Fett/Kursiv in einer RichEditBox.</summary>
public static class RichText
{
    public static List<TextLauf> Lesen(RichEditBox box)
    {
        var laeufe = new List<TextLauf>();
        box.Document.GetText(TextGetOptions.None, out string alles);
        int ende = alles.Length;
        while (ende > 0 && alles[ende - 1] == '\r') ende--; // abschließende Absatzmarke
        ITextRange r = box.Document.GetRange(0, 0);
        int pos = 0;
        while (pos < ende)
        {
            r.SetRange(pos, pos + 1);
            r.Expand(TextRangeUnit.CharacterFormat);
            int bis = Math.Clamp(r.EndPosition, pos + 1, ende);
            ITextCharacterFormat f = r.CharacterFormat;
            laeufe.Add(new TextLauf(alles[pos..bis].Replace('\r', '\n'), f.Bold == FormatEffect.On, f.Italic == FormatEffect.On));
            pos = bis;
        }
        return laeufe;
    }

    public static void Schreiben(RichEditBox box, IReadOnlyList<TextLauf> laeufe)
    {
        var teile = new List<(string T, bool F, bool K)>();
        foreach (TextLauf l in laeufe)
            teile.Add((l.T.Replace("\r\n", "\r").Replace('\n', '\r'), l.F, l.K));
        string text = string.Concat(teile.ConvertAll(t => t.T));
        box.Document.SetText(TextSetOptions.None, text);
        ITextRange alles = box.Document.GetRange(0, text.Length);
        alles.CharacterFormat.Bold = FormatEffect.Off;
        alles.CharacterFormat.Italic = FormatEffect.Off;
        int pos = 0;
        foreach (var (t, f, k) in teile)
        {
            if (t.Length > 0 && (f || k))
            {
                ITextRange r = box.Document.GetRange(pos, pos + t.Length);
                if (f) r.CharacterFormat.Bold = FormatEffect.On;
                if (k) r.CharacterFormat.Italic = FormatEffect.On;
            }
            pos += t.Length;
        }
        box.Document.Selection.SetRange(text.Length, text.Length);
        box.TextDocument.ClearUndoRedoHistory();
    }
}

/// <summary>Verbindet die Kommentar-RichEditBox einer Rohpunktzeile mit der Zeile (beidseitig).</summary>
public static class Kommentarbindung
{
    static readonly ConditionalWeakTable<RichEditBox, Zustand> Zustaende = new();

    sealed class Zustand
    {
        public RohpunktZeile? Zeile;
        public bool Laedt;
        public EventHandler? NeuGeladen;
    }

    /// <summary>Zuletzt fokussierter Kommentar (für Fett/Kursiv).</summary>
    public static RichEditBox? Zuletzt { get; private set; }

    public static readonly DependencyProperty ZeileProperty = DependencyProperty.RegisterAttached(
        "Zeile", typeof(RohpunktZeile), typeof(Kommentarbindung), new PropertyMetadata(null, ZeileGeaendert));

    public static RohpunktZeile? GetZeile(DependencyObject d) => (RohpunktZeile?)d.GetValue(ZeileProperty);

    public static void SetZeile(DependencyObject d, RohpunktZeile? wert) => d.SetValue(ZeileProperty, wert);

    static void ZeileGeaendert(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not RichEditBox box) return;
        bool neu = !Zustaende.TryGetValue(box, out Zustand? z);
        if (neu)
        {
            z = new Zustand();
            Zustaende.Add(box, z);
            box.TextChanged += (_, _) => Uebernehmen(box);
            box.LostFocus += (_, _) => Uebernehmen(box);
            box.GotFocus += (_, _) => Zuletzt = box;
        }
        if (z!.Zeile is { } alt && z.NeuGeladen is not null) alt.KommentarNeuGeladen -= z.NeuGeladen;
        z.Zeile = e.NewValue as RohpunktZeile;
        if (z.Zeile is { } zeile)
        {
            z.NeuGeladen = (_, _) => Laden(box);
            zeile.KommentarNeuGeladen += z.NeuGeladen;
        }
        Laden(box);
    }

    static void Laden(RichEditBox box)
    {
        if (!Zustaende.TryGetValue(box, out Zustand? z)) return;
        z.Laedt = true;
        try { RichText.Schreiben(box, z.Zeile?.Kommentar ?? new List<TextLauf>()); }
        finally { z.Laedt = false; }
    }

    /// <summary>Überträgt den Inhalt der Box in die Zeile (nach Eingaben und Formatwechseln).</summary>
    public static void Uebernehmen(RichEditBox box)
    {
        if (!Zustaende.TryGetValue(box, out Zustand? z) || z.Laedt || z.Zeile is null) return;
        z.Zeile.KommentarVomEditor(RichText.Lesen(box));
    }

    /// <summary>Erste Punkteingabe einer noch offenen Position (für den Wechsel in den Reiter).</summary>
    public static Control? ErsteEingabe(ItemsControl liste)
    {
        for (int i = 0; i < liste.Items.Count; i++)
        {
            if (liste.Items[i] is not RohpunktZeile { Offen: true }) continue;
            if (liste.ContainerFromIndex(i) is DependencyObject c && Suchen<NumberBox>(c) is NumberBox nb) return nb;
        }
        return null;
    }

    static T? Suchen<T>(DependencyObject d) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
        {
            DependencyObject kind = VisualTreeHelper.GetChild(d, i);
            if (kind is T t) return t;
            if (Suchen<T>(kind) is T tiefer) return tiefer;
        }
        return null;
    }
}
