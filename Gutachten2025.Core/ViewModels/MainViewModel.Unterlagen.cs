using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gutachten2025.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Gutachten2025.ViewModels;

/// <summary>Inhalt der linken Seitenleiste.</summary>
public enum Leistenanzeige { Bausteine = 0, Sachverhalt = 1, Loesung = 2, Beides = 3, Normen = 4, Bearbeitung = 5, Erstvotum = 6 }

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
    [NotifyPropertyChangedFor(nameof(ZeigtUnterlagen), nameof(UnterlagenTitel), nameof(KannUnterlageBearbeiten), nameof(ZeigtNormen), nameof(ZeigtDokument))]
    public partial Leistenanzeige Leiste { get; set; }

    /// <summary>Bearbeitung bzw. Erstvotum als PDF: Pfad der Datei (die Oberfläche zeigt die Seiten); sonst null.</summary>
    [ObservableProperty] public partial string? UnterlagenPdf { get; set; }

    /// <summary>Gezeigte Datei (Bearbeitung/Erstvotum) – für „Öffnen“; sonst null.</summary>
    [ObservableProperty] public partial string? UnterlagenDatei { get; set; }

    public bool ZeigtDokument => Leiste is Leistenanzeige.Bearbeitung or Leistenanzeige.Erstvotum;

    /// <summary>Sachverhalt bzw. Lösung als HTML-Ausschnitt (sicher erzeugt, siehe <see cref="MarkdownHtml"/>).</summary>
    [ObservableProperty] public partial string UnterlagenHtml { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UnterlageAnzeigen), nameof(UnterlageLeerSichtbar))]
    public partial bool UnterlageInBearbeitung { get; set; }

    /// <summary>Markdown während der Bearbeitung (an das Textfeld gebunden).</summary>
    [ObservableProperty] public partial string UnterlagenEntwurf { get; set; }

    public bool ZeigtUnterlagen => Leiste != Leistenanzeige.Bausteine;

    public bool ZeigtNormen => Leiste == Leistenanzeige.Normen;

    public bool HatSachverhalt => _sachverhalt.Trim().Length > 0;
    public bool HatLoesung => _loesung.Trim().Length > 0;

    public string UnterlagenTitel => Leiste switch
    {
        Leistenanzeige.Sachverhalt => "Sachverhalt",
        Leistenanzeige.Loesung => "Lösung",
        Leistenanzeige.Beides => "Sachverhalt und Lösung",
        Leistenanzeige.Normen => "Normen",
        Leistenanzeige.Bearbeitung => Kennung.Length > 0 ? $"Bearbeitung {Kennung}" : "Bearbeitung",
        Leistenanzeige.Erstvotum => Kennung.Length > 0 ? $"Erstvotum {Kennung}" : "Erstvotum",
        _ => "Bausteine",
    };

    /// <summary>Gewählter Inhalt fehlt (Hinweis statt Text anzeigen).</summary>
    public bool UnterlageLeer => Leiste switch
    {
        Leistenanzeige.Sachverhalt => !HatSachverhalt,
        Leistenanzeige.Loesung => !HatLoesung,
        Leistenanzeige.Beides => !HatSachverhalt && !HatLoesung,
        Leistenanzeige.Bearbeitung or Leistenanzeige.Erstvotum => UnterlagenDatei is null,
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
            if (ZeigtDokument)
            {
                string art = Leiste == Leistenanzeige.Bearbeitung ? "Bearbeitung" : "Erstvotum";
                string ordnername = Leiste == Leistenanzeige.Bearbeitung ? Arbeitsdokumente.Bearbeitungen : Arbeitsdokumente.Erstvoten;
                return Kennung.Length == 0
                    ? $"Bitte eine Kennziffer eingeben – angezeigt wird dann das {art} dazu (PDF oder DOCX)."
                    : $"Kein {art} zu „{Kennung}“. Dateien (PDF oder DOCX, Dateiname beginnt mit der Kennziffer) über „Aus Datei übernehmen“ " +
                      $"importieren oder in den Unterordner „{ordnername}“ des Klausurordners legen.";
            }
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

    public bool KannUnterlageBearbeiten => !KeineKlausur && Leiste is Leistenanzeige.Sachverhalt or Leistenanzeige.Loesung or Leistenanzeige.Normen;

    partial void OnLeisteChanged(Leistenanzeige value)
    {
        if (value is not (Leistenanzeige.Bausteine or Leistenanzeige.Normen or Leistenanzeige.Bearbeitung or Leistenanzeige.Erstvotum)) _letzteUnterlage = value;
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
        string? datei = null, pdf = null, dokumentHtml = string.Empty;
        if (ZeigtDokument && ordner is not null && Kennung.Length > 0)
        {
            datei = Arbeitsdokumente.Finden(ordner, Leiste == Leistenanzeige.Bearbeitung ? Arbeitsdokumente.Bearbeitungen : Arbeitsdokumente.Erstvoten, Kennung);
            if (datei is not null && Path.GetExtension(datei).Equals(".pdf", StringComparison.OrdinalIgnoreCase)) pdf = datei;
            else if (datei is not null)
            {
                try
                {
                    dokumentHtml = MarkdownHtml.Umwandeln(DocxMarkdown.Umwandeln(datei));
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Xml.XmlException)
                {
                    EinstellungsSpeicher.Protokollieren(ex);
                    dokumentHtml = MarkdownHtml.Umwandeln(MarkdownHtml.Maskieren($"„{Path.GetFileName(datei)}“ ist nicht lesbar: {ex.Message}"));
                }
            }
        }
        UnterlagenDatei = datei;
        UnterlagenPdf = pdf;
        OnPropertyChanged(nameof(UnterlagenTitel));
        string html = Leiste switch
        {
            Leistenanzeige.Bearbeitung or Leistenanzeige.Erstvotum => dokumentHtml,
            Leistenanzeige.Sachverhalt => MarkdownHtml.Umwandeln(_sachverhalt),
            Leistenanzeige.Loesung => MarkdownHtml.Umwandeln(_loesung),
            Leistenanzeige.Beides => Abschnitt("Sachverhalt", _sachverhalt) + Abschnitt("Lösung", _loesung),
            Leistenanzeige.Normen => NormenHtml(),
            _ => string.Empty,
        };
        // Normzitate: Hinweis beim Überfahren, Klick bzw. Strg+Klick zeigt den Volltext.
        UnterlagenHtml = Leiste == Leistenanzeige.Bausteine ? html : Normverweise.Auszeichnen(html, NormHinweis);
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
        Leistenanzeige.Normen => Normensammlung.EigeneDatei,
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
            UnterlagenEntwurf = Leiste switch
            {
                Leistenanzeige.Sachverhalt => _sachverhalt,
                Leistenanzeige.Normen => Lesen(_bearbeitungsPfad) is { Length: > 0 } eigene ? eigene : EigeneNormen.Vorlage,
                _ => _loesung,
            };
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
            bool eigeneNormen = Path.GetFileName(pfad).Equals(Normensammlung.EigeneDatei, StringComparison.OrdinalIgnoreCase);
            // Unveränderte Vorlage der eigenen Normen nicht speichern.
            if (eigeneNormen && text.Trim() == EigeneNormen.Vorlage.Trim()) text = string.Empty;
            if (text.Trim().Length == 0)
            {
                if (File.Exists(pfad))
                {
                    File.Delete(pfad);
                    if (eigeneNormen && beenden) NormensammlungLaden();
                }
                return;
            }
            if (Lesen(pfad) == text) return;
            Schreiben(pfad, text.TrimEnd() + "\n");
            if (eigeneNormen && beenden) NormensammlungLaden();
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
            if (ZeigtDokument)
            {
                await DokumenteImportierenAsync(Leiste);
                return;
            }
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

    [RelayCommand]
    void UnterlageDateiOeffnen()
    {
        if (UnterlagenDatei is string d && File.Exists(d)) _ui.Oeffnen(d);
    }

    /// <summary>Bearbeitungen und/oder Erstvoten importieren (Ordner-Menü, Übersicht): Art wählen, dann ZIP, Ordner oder Datei.</summary>
    [RelayCommand]
    async Task ArbeitsdokumenteImportierenAsync()
    {
        if (KlausurOrdnerOderMeldung() is null) return;
        int art = await _ui.AuswahlAsync("Bearbeitungen und Erstvoten importieren",
            "PDF- oder DOCX-Dateien, deren Name mit der Kennziffer beginnt (z. B. „1017.pdf“, „1017_Erstvotum.docx“) – auch als ZIP. " +
            "Ein ZIP darf beides enthalten: Pfade mit „Erstvotum“, „Erstgutachten“ oder „Votum“ gelten als Erstvoten, mit „Bearbeitung“ als Bearbeitungen; " +
            "für die übrigen Dateien bitte wählen:",
            "Bearbeitungen", "Erstvoten", "Abbrechen");
        if (art == 0) return;
        await DokumenteImportierenAsync(art == 1 ? Leistenanzeige.Bearbeitung : Leistenanzeige.Erstvotum);
    }

    /// <summary>Bearbeitungen bzw. Erstvoten importieren: ZIP oder Einzeldatei, alternativ ganzer Ordner.</summary>
    async Task DokumenteImportierenAsync(Leistenanzeige art)
    {
        try
        {
            string unterordner = art == Leistenanzeige.Erstvotum ? Arbeitsdokumente.Erstvoten : Arbeitsdokumente.Bearbeitungen;
            string was = art == Leistenanzeige.Erstvotum ? "Erstvoten" : "Bearbeitungen";
            int wahl = await _ui.AuswahlAsync($"{was} importieren",
                $"ZIP-Datei oder einzelne PDF/DOCX-Datei wählen – oder einen ganzen Ordner. Dateinamen beginnen mit der Kennziffer; " +
                $"die Dateien werden in den Unterordner „{unterordner}“ der Klausur kopiert.",
                "ZIP oder Datei …", "Ganzer Ordner …", "Abbrechen");
            if (wahl == 0) return;
            string ordner = Path.Combine(_ablage.Wurzel, _ablage.KlausurEinrichten(KlausurName, _notenliste));
            string bericht;
            if (wahl == 1)
            {
                if (await _ui.DateiWaehlenAsync(".zip", ".pdf", ".docx") is not string datei) return;
                if (Path.GetExtension(datei).Equals(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    var (b, e, u) = Arbeitsdokumente.AusZip(ordner, datei, unterordner);
                    bericht = $"{b} Bearbeitungen, {e} Erstvoten" + (u > 0 ? $"; {u} andere Dateien übergangen" : string.Empty) + ".";
                }
                else if (Kennung.Length > 0 && !Path.GetFileNameWithoutExtension(datei).StartsWith(Kennung, StringComparison.OrdinalIgnoreCase)
                         && await _ui.BestaetigenAsync("Datei zuordnen", $"„{Path.GetFileName(datei)}“ als {(art == Leistenanzeige.Erstvotum ? "Erstvotum" : "Bearbeitung")} zu „{Kennung}“ übernehmen?",
                             $"Zu „{Kennung}“", "Name beibehalten"))
                {
                    Arbeitsdokumente.FuerKennung(ordner, unterordner, Kennung, datei);
                    bericht = $"Zugeordnet zu „{Kennung}“.";
                }
                else
                {
                    var (neu, ersetzt) = Arbeitsdokumente.Importieren(ordner, unterordner, new[] { datei });
                    bericht = neu + ersetzt > 0 ? "1 Datei übernommen." : "Nichts übernommen.";
                }
            }
            else
            {
                if (await _ui.OrdnerWaehlenAsync() is not string quelle) return;
                var dateien = Directory.EnumerateFiles(quelle, "*", SearchOption.AllDirectories).Where(Arbeitsdokumente.IstDokument).ToList();
                var (neu, ersetzt) = Arbeitsdokumente.Importieren(ordner, unterordner, dateien);
                bericht = $"{neu} neu{(ersetzt > 0 ? $", {ersetzt} ersetzt" : string.Empty)}.";
            }
            Leiste = art;
            UnterlagenLaden();
            _ui.Melden(Meldungsart.Erfolg, $"{was} importiert", bericht + " Angezeigt wird jeweils die Datei zur eingegebenen Kennziffer.");
        }
        catch (Exception ex)
        {
            Fehler(ex, "Dateien nicht importiert");
        }
    }

    /// <summary>Datei zur eingegebenen Kennziffer wählen (einzeln je Arbeit).</summary>
    [RelayCommand]
    async Task DokumentFuerKennungAsync()
    {
        try
        {
            if (KlausurOrdnerOderMeldung() is null || !ZeigtDokument) return;
            if (Kennung.Length == 0)
            {
                _ui.Melden(Meldungsart.Information, "Keine Kennziffer", "Bitte zuerst die Kennziffer der Arbeit eingeben.");
                _ui.Fokussieren(Eingabefeld.Student);
                return;
            }
            if (await _ui.DateiWaehlenAsync(".pdf", ".docx") is not string datei) return;
            string ordner = Path.Combine(_ablage.Wurzel, _ablage.KlausurEinrichten(KlausurName, _notenliste));
            Arbeitsdokumente.FuerKennung(ordner, Leiste == Leistenanzeige.Erstvotum ? Arbeitsdokumente.Erstvoten : Arbeitsdokumente.Bearbeitungen, Kennung, datei);
            UnterlagenLaden();
        }
        catch (Exception ex)
        {
            Fehler(ex, "Datei nicht übernommen");
        }
    }

    /// <summary>Scannen: Scan-App öffnen und den Zielordner zeigen (Scans als „Kennziffer.pdf“ dort speichern).</summary>
    [RelayCommand]
    void Scannen()
    {
        if (KlausurOrdnerOderMeldung() is null) return;
        string unterordner = Leiste == Leistenanzeige.Erstvotum ? Arbeitsdokumente.Erstvoten : Arbeitsdokumente.Bearbeitungen;
        string ziel = Path.Combine(_ablage.Wurzel, _ablage.KlausurEinrichten(KlausurName, _notenliste), unterordner);
        Directory.CreateDirectory(ziel);
        if (!_ui.ScannenStarten())
        {
            _ui.Melden(Meldungsart.Information, "Scannen", "Hier ist keine Scan-App verfügbar. Bitte mit dem Scanprogramm des Geräts scannen.");
        }
        _ui.Oeffnen(ziel);
        _ui.Melden(Meldungsart.Information, "Scannen",
            $"Scans bitte als PDF mit der Kennziffer als Namen (z. B. „{(Kennung.Length > 0 ? Kennung : "1017")}.pdf“) im geöffneten Ordner „{unterordner}“ speichern; sie erscheinen dann hier.");
    }
}
