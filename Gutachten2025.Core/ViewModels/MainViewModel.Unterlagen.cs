using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gutachten2025.Services;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Gutachten2025.ViewModels;

/// <summary>Inhalt der linken Seitenleiste.</summary>
public enum Leistenanzeige { Bausteine = 0, Sachverhalt = 1, Loesung = 2, Beides = 3 }

/// <summary>
/// Sachverhalt und Lösung (Markdown im Klausurordner: Sachverhalt.md, Loesung.md) in der linken Seitenleiste
/// anzeigen, bearbeiten und aus Dateien übernehmen. Liegen die Bausteine über dem Editor, hat die Leiste diese Inhalte allein.
/// </summary>
public sealed partial class MainViewModel
{
    string _sachverhalt = string.Empty, _loesung = string.Empty;
    string? _bearbeitungsPfad;
    Leistenanzeige _letzteUnterlage = Leistenanzeige.Sachverhalt;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ZeigtUnterlagen), nameof(UnterlagenTitel), nameof(KannUnterlageBearbeiten))]
    public partial Leistenanzeige Leiste { get; set; }

    /// <summary>Sachverhalt bzw. Lösung als HTML-Ausschnitt (sicher erzeugt, siehe <see cref="MarkdownHtml"/>).</summary>
    [ObservableProperty] public partial string UnterlagenHtml { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UnterlageAnzeigen), nameof(UnterlageLeerSichtbar))]
    public partial bool UnterlageInBearbeitung { get; set; }

    /// <summary>Markdown während der Bearbeitung (an das Textfeld gebunden).</summary>
    [ObservableProperty] public partial string UnterlagenEntwurf { get; set; }

    public bool ZeigtUnterlagen => Leiste != Leistenanzeige.Bausteine;

    public bool HatSachverhalt => _sachverhalt.Trim().Length > 0;
    public bool HatLoesung => _loesung.Trim().Length > 0;

    public string UnterlagenTitel => Leiste switch
    {
        Leistenanzeige.Sachverhalt => "Sachverhalt",
        Leistenanzeige.Loesung => "Lösung",
        Leistenanzeige.Beides => "Sachverhalt und Lösung",
        _ => "Bausteine",
    };

    /// <summary>Gewählter Inhalt fehlt (Hinweis statt Text anzeigen).</summary>
    public bool UnterlageLeer => Leiste switch
    {
        Leistenanzeige.Sachverhalt => !HatSachverhalt,
        Leistenanzeige.Loesung => !HatLoesung,
        Leistenanzeige.Beides => !HatSachverhalt && !HatLoesung,
        _ => false,
    };

    /// <summary>Formatierte Anzeige (weder leer noch in Bearbeitung).</summary>
    public bool UnterlageAnzeigen => ZeigtUnterlagen && !UnterlageLeer && !UnterlageInBearbeitung;

    /// <summary>Hinweis statt Text (gewählter Inhalt fehlt, keine Bearbeitung).</summary>
    public bool UnterlageLeerSichtbar => ZeigtUnterlagen && UnterlageLeer && !UnterlageInBearbeitung;

    public string UnterlageLeerHinweis
    {
        get
        {
            if (KeineKlausur) return "Bitte zuerst eine Klausur wählen.";
            string was = Leiste switch
            {
                Leistenanzeige.Sachverhalt => "kein Sachverhalt",
                Leistenanzeige.Loesung => "keine Lösung",
                _ => "weder Sachverhalt noch Lösung",
            };
            return $"Für diese Klausur ist {was} hinterlegt. Pakete aus dem KI-Portal enthalten beides; sonst Text über „Bearbeiten“ "
                   + "einfügen (Markdown) oder eine Datei übernehmen (.md, .txt, .docx).";
        }
    }

    public bool KannUnterlageBearbeiten => !KeineKlausur && Leiste is Leistenanzeige.Sachverhalt or Leistenanzeige.Loesung;

    partial void OnLeisteChanged(Leistenanzeige value)
    {
        if (value != Leistenanzeige.Bausteine) _letzteUnterlage = value;
        UnterlageSichern();
        UnterlagenLaden();
    }

    /// <summary>Linke Leiste umschalten; <paramref name="art"/>: Bausteine, Sachverhalt, Loesung oder Beides.</summary>
    [RelayCommand]
    void LeisteZeigen(string? art)
    {
        if (!Enum.TryParse(art, ignoreCase: true, out Leistenanzeige neu)) return;
        Leiste = neu;
        // Links neben dem Editor teilen sich Bausteine und Unterlagen die Leiste: beim Umschalten einblenden.
        if (!BausteineOben) BausteineOffen = true;
    }

    /// <summary>Bausteinliste sichtbar (oben über dem Editor oder links, sofern die Leiste nicht Unterlagen zeigt).</summary>
    public bool BausteineSichtbar => BausteineOffen && (BausteineOben || !ZeigtUnterlagen);

    /// <summary>Sachverhalt/Lösung in der linken Leiste sichtbar.</summary>
    public bool UnterlagenSichtbar => ZeigtUnterlagen && (BausteineOben || BausteineOffen);

    /// <summary>F6 / Schaltfläche „Bausteine“: Links teilen sich Bausteine und Unterlagen die Leiste.</summary>
    public void BausteineUmschalten()
    {
        if (BausteineOben || BausteineSichtbar) BausteineOffen = !BausteineOffen;
        else
        {
            Leiste = Leistenanzeige.Bausteine;
            BausteineOffen = true;
        }
    }

    /// <summary>F7 / Schaltfläche „Sachverhalt“: zuletzt gewählte Unterlage ein- bzw. ausblenden.</summary>
    public void UnterlagenUmschalten()
    {
        if (UnterlagenSichtbar)
        {
            if (BausteineOben) Leiste = Leistenanzeige.Bausteine;
            else BausteineOffen = false;
        }
        else
        {
            Leiste = _letzteUnterlage;
            if (!BausteineOben) BausteineOffen = true;
        }
    }

    /// <summary>Liest Sachverhalt und Lösung der aktuellen Klausur neu (eine laufende Bearbeitung wird zuvor gespeichert).</summary>
    void UnterlagenLaden()
    {
        UnterlageSichern();
        string? ordner = KlausurName.Length > 0 ? _ablage.KlausurOrdner(KlausurName) : null;
        _sachverhalt = ordner is null ? string.Empty : Lesen(Path.Combine(ordner, KlausurAblage.Sachverhalt));
        _loesung = ordner is null ? string.Empty : Lesen(Path.Combine(ordner, KlausurAblage.Loesung));
        UnterlagenHtml = Leiste switch
        {
            Leistenanzeige.Sachverhalt => MarkdownHtml.Umwandeln(_sachverhalt),
            Leistenanzeige.Loesung => MarkdownHtml.Umwandeln(_loesung),
            Leistenanzeige.Beides => Abschnitt("Sachverhalt", _sachverhalt) + Abschnitt("Lösung", _loesung),
            _ => string.Empty,
        };
        UnterlagenWoerter = Unterlagenwoerter.Aus(_sachverhalt, _loesung);
        OnPropertyChanged(nameof(HatSachverhalt));
        OnPropertyChanged(nameof(HatLoesung));
        OnPropertyChanged(nameof(UnterlageLeer));
        OnPropertyChanged(nameof(UnterlageLeerHinweis));
        OnPropertyChanged(nameof(UnterlageAnzeigen));
        OnPropertyChanged(nameof(UnterlageLeerSichtbar));
        OnPropertyChanged(nameof(KannUnterlageBearbeiten));

        static string Abschnitt(string titel, string md) =>
            md.Trim().Length == 0 ? string.Empty : $"<div class=\"unterlagentitel\">{titel}</div>\n{MarkdownHtml.Umwandeln(md)}";
    }

    static string Lesen(string pfad)
    {
        try
        {
            if (!File.Exists(pfad)) return string.Empty;
            using var fs = new FileStream(pfad, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var r = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return r.ReadToEnd();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            EinstellungsSpeicher.Protokollieren(ex);
            return string.Empty;
        }
    }

    string? UnterlagenPfad() => Leiste switch
    {
        Leistenanzeige.Sachverhalt => KlausurAblage.Sachverhalt,
        Leistenanzeige.Loesung => KlausurAblage.Loesung,
        _ => null,
    } is { } name && KlausurName.Length > 0
        ? Path.Combine(_ablage.Wurzel, _ablage.KlausurEinrichten(KlausurName, _notenliste), name)
        : null;

    [RelayCommand]
    void UnterlageBearbeiten()
    {
        if (UnterlageInBearbeitung) return;
        if (KlausurOrdnerOderMeldung() is null) return;
        try
        {
            _bearbeitungsPfad = UnterlagenPfad();
            if (_bearbeitungsPfad is null)
            {
                _ui.Melden(Meldungsart.Information, "Sachverhalt oder Lösung wählen", "Bearbeiten lässt sich nur eines von beiden.");
                return;
            }
            UnterlagenEntwurf = Leiste == Leistenanzeige.Sachverhalt ? _sachverhalt : _loesung;
            UnterlageInBearbeitung = true;
        }
        catch (Exception ex)
        {
            Fehler(ex, "Bearbeiten nicht möglich");
        }
    }

    /// <summary>Bearbeitung übernehmen (Schaltfläche „Fertig“).</summary>
    [RelayCommand]
    void UnterlageFertig()
    {
        UnterlageSichern();
        UnterlagenLaden();
    }

    [RelayCommand]
    void UnterlageVerwerfen()
    {
        _bearbeitungsPfad = null;
        UnterlageInBearbeitung = false;
        UnterlagenLaden();
    }

    /// <summary>
    /// Schreibt eine laufende Bearbeitung in ihre Datei und beendet sie (<paramref name="beenden"/> = false beim automatischen
    /// Sichern: weiter bearbeiten); leerer Text entfernt die Datei.
    /// </summary>
    void UnterlageSichern(bool beenden = true)
    {
        if (!UnterlageInBearbeitung || _bearbeitungsPfad is not { } pfad) return;
        if (beenden)
        {
            _bearbeitungsPfad = null;
            UnterlageInBearbeitung = false;
        }
        try
        {
            string text = (UnterlagenEntwurf ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
            if (text.Trim().Length == 0)
            {
                if (File.Exists(pfad)) File.Delete(pfad);
                return;
            }
            if (Lesen(pfad) == text) return;
            Schreiben(pfad, text.TrimEnd() + "\n");
        }
        catch (Exception ex)
        {
            Fehler(ex, "Text nicht gespeichert", pfad);
        }
    }

    static void Schreiben(string pfad, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(pfad)!);
        string tmp = pfad + ".tmp";
        File.WriteAllText(tmp, text, new UTF8Encoding(false));
        File.Move(tmp, pfad, overwrite: true);
    }

    /// <summary>Sachverhalt bzw. Lösung aus einer Datei übernehmen (Markdown, Text oder Word).</summary>
    [RelayCommand]
    async Task UnterlageImportierenAsync()
    {
        try
        {
            if (KlausurOrdnerOderMeldung() is null) return;
            if (Leiste is not (Leistenanzeige.Sachverhalt or Leistenanzeige.Loesung))
            {
                int wahl = await _ui.AuswahlAsync("Datei übernehmen", "Die Datei enthält …", "den Sachverhalt", "die Lösung", "Abbrechen");
                if (wahl == 0) return;
                Leiste = wahl == 1 ? Leistenanzeige.Sachverhalt : Leistenanzeige.Loesung;
            }
            UnterlageSichern();
            string titel = UnterlagenTitel;
            string ziel = UnterlagenPfad()!;
            string? quelle = await _ui.DateiWaehlenAsync(".md", ".txt", ".docx");
            if (quelle is null || KlausurAblage.GleicherPfad(quelle, ziel)) return;

            string text = Path.GetExtension(quelle).Equals(".docx", StringComparison.OrdinalIgnoreCase)
                ? DocxMarkdown.Umwandeln(quelle)
                : Lesen(quelle);
            if (text.Trim().Length == 0)
            {
                _ui.Melden(Meldungsart.Warnung, $"{titel} nicht übernommen", $"„{Path.GetFileName(quelle)}“ enthält keinen Text.");
                return;
            }
            bool vorhanden = File.Exists(ziel) && Lesen(ziel).Trim().Length > 0;
            if (vorhanden && !await _ui.BestaetigenAsync($"{titel} ersetzen",
                    $"Der bisherige Text wird durch „{Path.GetFileName(quelle)}“ ersetzt und als Sicherung im Klausurordner aufbewahrt.",
                    "Ersetzen", "Abbrechen"))
                return;
            string ordner = Path.GetDirectoryName(ziel)!;
            if (vorhanden) File.Copy(ziel, KlausurAblage.Sicherungsname(ordner, Path.GetFileName(ziel)));
            Schreiben(ziel, text.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd() + "\n");
            UnterlagenLaden();
            _ui.Melden(Meldungsart.Erfolg, $"{titel} übernommen", $"Aus „{Path.GetFileName(quelle)}“; wird mit Paketen weitergegeben.");
        }
        catch (Exception ex)
        {
            Fehler(ex, "Datei nicht übernommen");
        }
    }
}
