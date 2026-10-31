using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gutachten2025.Models;
using Gutachten2025.Rohpunkte;
using Gutachten2025.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Gutachten2025.ViewModels;

/// <summary>
/// Rohpunkteschema wie im KI-Portal (rohpunkte.json/rohpunkte.xlsx im Klausurordner): Eingabe je Position, Bonus/Minus,
/// errechnete Note (überschreibbar), Anhang „Bewertungsbogen“ und Zusatzspalten der Notenliste.
/// </summary>
public sealed partial class MainViewModel
{
    RpsDoc? _schema;
    string? _schemaKennung;
    DateTime _schemaJsonStand, _schemaXlsxStand;
    bool _noteManuell, _noteWirdGesetzt, _rohpunkteWerdenGeladen;

    public ObservableCollection<RohpunktZeile> Rohpunktzeilen { get; } = new();

    [ObservableProperty] public partial bool HatSchema { get; set; }

    /// <summary>„63,5 / 100 · Bonus 2 · Minus 0 · Gesamt 65,5 → vollbefriedigend (10 Punkte) · bis 11 Punkte fehlen 5,5“.</summary>
    [ObservableProperty] public partial string? RohpunkteStatus { get; set; }

    /// <summary>Kurzform für die Statuszeile.</summary>
    [ObservableProperty] public partial string? RohpunkteKurz { get; set; }

    [ObservableProperty] public partial string? NotenHinweis { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ErrechneteNoteUebernehmenCommand))]
    public partial bool NoteAbweichend { get; set; }

    public int? ErrechneteNote { get; private set; }

    /// <summary>Teilpunkte je Aufgabe nur ohne Rohpunkteschema.</summary>
    public bool TeilpunkteSichtbar => HatAufgaben && !HatSchema;

    public bool KeinSchema => !HatSchema;

    partial void OnHatSchemaChanged(bool value)
    {
        OnPropertyChanged(nameof(TeilpunkteSichtbar));
        OnPropertyChanged(nameof(KeinSchema));
    }

    string SchemaPfad(string datei) => Path.Combine(_ablage.KlausurOrdner(KlausurName), datei);

    // ---------- Laden ----------

    /// <summary>Lädt das Schema der Klausur; eine neuere rohpunkte.xlsx wird zuvor übernommen.</summary>
    void SchemaLaden(bool eingabenBehalten = false)
    {
        var punkte = eingabenBehalten ? AktuelleRohpunkte() : null;
        var kommentare = eingabenBehalten ? AktuelleRohkommentare() : null;

        _schema = null;
        _schemaKennung = null;
        if (KlausurName.Length > 0)
        {
            string json = SchemaPfad(Rohpunkteschema.Dateiname), xlsx = SchemaPfad(Rohpunkteschema.Exceldatei);
            try
            {
                if (File.Exists(xlsx) && (!File.Exists(json) || File.GetLastWriteTimeUtc(xlsx) > File.GetLastWriteTimeUtc(json)))
                    AusExcelUebernehmen(xlsx, json);
                if (File.Exists(json))
                {
                    _schema = RpsBuilder.LiesJson(Formulierungsdatei.TextLesen(json));
                    _schemaKennung = Rohpunkteschema.Fingerabdruck(_schema);
                }
            }
            catch (Exception ex)
            {
                _schema = null;
                Fehler(ex, "Rohpunkteschema nicht lesbar", json);
            }
            finally
            {
                // auch bei Fehlern: erst eine erneute Änderung der Dateien führt zum Neuladen
                _schemaJsonStand = File.Exists(json) ? File.GetLastWriteTimeUtc(json) : DateTime.MinValue;
                _schemaXlsxStand = File.Exists(xlsx) ? File.GetLastWriteTimeUtc(xlsx) : DateTime.MinValue;
            }
        }
        RohpunkteAufbauen();
        if (eingabenBehalten) RohpunkteWiederherstellen(punkte, kommentare, melden: true);
    }

    /// <summary>Liest rohpunkte.xlsx (Layout wie im KI-Portal) und schreibt rohpunkte.json; meldet Prüfhinweise.</summary>
    void AusExcelUebernehmen(string xlsx, string json)
    {
        // Kopie lesen: Excel hält die Datei ggf. noch geöffnet.
        string tmp = Path.Combine(Path.GetTempPath(), $"gutachtentool-{Guid.NewGuid():N}.xlsx");
        try
        {
            using (var quelle = new FileStream(xlsx, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var ziel = File.Create(tmp))
                quelle.CopyTo(ziel);
            string bericht = RpsBuilder.NachJson(tmp, json);
            if (!bericht.StartsWith("0 ", StringComparison.Ordinal))
                _ui.Melden(bericht.Contains("Fehler:", StringComparison.Ordinal) ? Meldungsart.Fehler : Meldungsart.Warnung,
                    "Rohpunkteschema aus Excel übernommen – bitte prüfen", Kuerzen(bericht.Replace(Environment.NewLine, " "), 700));
        }
        finally
        {
            try { File.Delete(tmp); } catch (Exception) { /* temporäre Datei */ }
        }
    }

    static string Kuerzen(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    void RohpunkteAufbauen()
    {
        foreach (RohpunktZeile z in Rohpunktzeilen)
        {
            z.PropertyChanged -= Rohpunkt_PropertyChanged;
            z.KommentarGeaendert -= Rohpunkt_KommentarGeaendert;
        }
        Rohpunktzeilen.Clear();
        if (_schema is not null)
            foreach (RohpunktZeile z in Rohpunkteschema.Zeilen(_schema))
            {
                z.PropertyChanged += Rohpunkt_PropertyChanged;
                z.KommentarGeaendert += Rohpunkt_KommentarGeaendert;
                Rohpunktzeilen.Add(z);
            }
        HatSchema = _schema is not null;
        RohpunkteAktualisieren();
    }

    /// <summary>Beim Zurückkehren ins Programm: in Excel geänderte Schemadatei übernehmen.</summary>
    void SchemaExternPruefen()
    {
        if (KlausurName.Length == 0) return;
        string json = SchemaPfad(Rohpunkteschema.Dateiname), xlsx = SchemaPfad(Rohpunkteschema.Exceldatei);
        bool xlsxNeu = File.Exists(xlsx) && File.GetLastWriteTimeUtc(xlsx) > _schemaXlsxStand;
        bool jsonNeu = File.Exists(json) && File.GetLastWriteTimeUtc(json) > _schemaJsonStand;
        if (!xlsxNeu && !jsonNeu) return;
        string? vorher = _schemaKennung;
        SchemaLaden(eingabenBehalten: true);
        if (_schemaKennung != vorher)
            _ui.Melden(Meldungsart.Information, "Rohpunkteschema aktualisiert",
                _schema is null ? "Das Schema konnte nicht gelesen werden." : $"{Rohpunktzeilen.Count(z => z.Art == Zeilenart.Position)} Positionen, Höchstpunkte {Zahl.Text(Rohpunkteschema.Berechnen(Rohpunktzeilen).Max)}.");
    }

    // ---------- Eingaben ----------

    void Rohpunkt_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RohpunktZeile.Punkte)) RohpunkteAktualisieren();
    }

    void Rohpunkt_KommentarGeaendert(object? sender, EventArgs e)
    {
        if (_initialisiert && !_rohpunkteWerdenGeladen) _einstellungenGeaendert = true;
    }

    void RohpunkteAktualisieren()
    {
        if (_initialisiert && !_rohpunkteWerdenGeladen) _einstellungenGeaendert = true;
        if (_schema is null || Rohpunktzeilen.Count == 0)
        {
            ErrechneteNote = null;
            RohpunkteStatus = RohpunkteKurz = null;
            NotenHinweisAktualisieren();
            return;
        }
        Rohpunkteschema.ZwischensummenSetzen(Rohpunktzeilen);
        Rohpunkteschema.Summe s = Rohpunkteschema.Berechnen(Rohpunktzeilen);
        if (!s.HatEingaben)
        {
            ErrechneteNote = null;
            RohpunkteStatus = $"Noch keine Punkte eingetragen · Höchstpunkte {Zahl.Text(s.Max)}";
            RohpunkteKurz = null;
            NotenHinweisAktualisieren();
            return;
        }

        int note = Rohpunkteschema.Note(_schema, s.Gesamt);
        ErrechneteNote = note;
        var teile = new List<string> { $"Rohpunkte {Zahl.Text(s.Roh)} / {Zahl.Text(s.Max)}" };
        if (s.Bonus > 0) teile.Add($"Bonus + {Zahl.Text(s.Bonus)}");
        if (s.Minus > 0) teile.Add($"Minus − {Zahl.Text(s.Minus)}");
        teile.Add($"Gesamt {Zahl.Text(s.Gesamt)} → {Notenskala.MitPunkten(note)}");
        if (Rohpunkteschema.NaechsteSchwelle(_schema, s.Gesamt) is double schwelle && note < Notenskala.Maximum)
            teile.Add($"bis {note + 1} Punkte fehlen {Zahl.Text(schwelle - s.Gesamt)}");
        if (s.Offen > 0) teile.Add($"{s.Offen} Positionen offen");
        RohpunkteStatus = string.Join(" · ", teile);
        RohpunkteKurz = $"Rohpunkte {Zahl.Text(s.Gesamt)} / {Zahl.Text(s.Max)}";

        if (!_noteManuell && !_rohpunkteWerdenGeladen && NotenIndex != note)
        {
            _noteWirdGesetzt = true;
            try { NotenIndex = note; }
            finally { _noteWirdGesetzt = false; }
        }
        NotenHinweisAktualisieren();
    }

    /// <summary>Aus OnNotenIndexChanged: eine von Hand gewählte Note gilt als Überschreibung.</summary>
    void NoteGeaendert(int wert)
    {
        if (_noteWirdGesetzt || _rohpunkteWerdenGeladen) return;
        if (ErrechneteNote is int e) _noteManuell = wert != e;
        NotenHinweisAktualisieren();
    }

    void NotenHinweisAktualisieren()
    {
        NoteAbweichend = ErrechneteNote is int e && NotenIndex != e;
        NotenHinweis = NoteAbweichend && ErrechneteNote is int n
            ? $"Note von Hand gesetzt – nach Rohpunkten wären es {Notenskala.MitPunkten(n)}."
            : null;
    }

    [RelayCommand(CanExecute = nameof(NoteAbweichend))]
    void ErrechneteNoteUebernehmen()
    {
        if (ErrechneteNote is not int n) return;
        _noteManuell = false;
        _noteWirdGesetzt = true;
        try { NotenIndex = n; }
        finally { _noteWirdGesetzt = false; }
        NotenHinweisAktualisieren();
    }

    Dictionary<string, double> AktuelleRohpunkte()
    {
        var d = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (RohpunktZeile z in Rohpunktzeilen)
            if (z.IstEingabe && z.Wert is double w) d[z.Id] = w;
        return d;
    }

    Dictionary<string, List<TextLauf>> AktuelleRohkommentare()
    {
        var d = new Dictionary<string, List<TextLauf>>(StringComparer.Ordinal);
        foreach (RohpunktZeile z in Rohpunktzeilen)
            if (z.IstEingabe && z.HatKommentar) d[z.Id] = z.Kommentar.ToList();
        return d;
    }

    /// <summary>Setzt gespeicherte Eingaben; entfallene Positionen und gesunkene Höchstpunkte werden gemeldet.</summary>
    void RohpunkteWiederherstellen(IReadOnlyDictionary<string, double>? punkte, IReadOnlyDictionary<string, List<TextLauf>>? kommentare, bool melden)
    {
        _rohpunkteWerdenGeladen = true;
        var hinweise = new List<string>();
        try
        {
            foreach (RohpunktZeile z in Rohpunktzeilen.Where(z => z.IstEingabe))
            {
                if (punkte is not null && punkte.TryGetValue(z.Id, out double w))
                {
                    if (w > z.Max + 1e-9) hinweise.Add($"{z.Titel}: {Zahl.Text(w)} auf {Zahl.Text(z.Max)} gekürzt");
                    z.Punkte = w;
                }
                else z.Punkte = double.NaN;
                z.KommentarLaden(kommentare is not null && kommentare.TryGetValue(z.Id, out var k) ? k : null);
            }
            var bekannt = Rohpunktzeilen.Select(z => z.Id).ToHashSet(StringComparer.Ordinal);
            var entfallen = (punkte?.Keys ?? Enumerable.Empty<string>())
                .Concat(kommentare?.Keys ?? Enumerable.Empty<string>())
                .Where(id => !bekannt.Contains(id)).Distinct().ToList();
            if (entfallen.Count > 0) hinweise.Add($"{entfallen.Count} Position(en) gibt es im Schema nicht mehr ({string.Join(", ", entfallen.Take(6))})");
        }
        finally
        {
            _rohpunkteWerdenGeladen = false;
        }
        RohpunkteAktualisieren();
        if (melden && hinweise.Count > 0)
            _ui.Melden(Meldungsart.Warnung, "Rohpunkteschema geändert", Kuerzen(string.Join("; ", hinweise) + ".", 600));
    }

    void RohpunkteLeeren()
    {
        _rohpunkteWerdenGeladen = true;
        try
        {
            foreach (RohpunktZeile z in Rohpunktzeilen.Where(z => z.IstEingabe))
            {
                z.Punkte = double.NaN;
                z.KommentarLaden(null);
            }
        }
        finally
        {
            _rohpunkteWerdenGeladen = false;
        }
        _noteManuell = false;
        RohpunkteAktualisieren();
    }

    // ---------- Ausgabe ----------

    /// <summary>Anhang „Bewertungsbogen“; null, wenn kein Schema oder keine Zeile einen Wert enthält.</summary>
    IReadOnlyList<BogenZeile>? Bewertungsbogen()
    {
        if (_schema is null) return null;
        Rohpunkteschema.Summe s = Rohpunkteschema.Berechnen(Rohpunktzeilen);
        if (!s.HatEingaben) return null;

        static IReadOnlyList<IReadOnlyList<TextLauf>> Kommentar(RohpunktZeile z) =>
            z.HatKommentar
                ? TextNormalizer.FormatierteAbsaetze(FormatierterText.AusLaeufen(z.Kommentar)).Cast<IReadOnlyList<TextLauf>>().ToList()
                : Array.Empty<IReadOnlyList<TextLauf>>();

        var bogen = new List<BogenZeile>();
        foreach (RohpunktZeile z in Rohpunktzeilen)
        {
            switch (z.Art)
            {
                case Zeilenart.Aufgabe:
                    bogen.Add(new BogenZeile(BogenArt.Aufgabe, z.Titel, 0, z.Zwischensumme, Array.Empty<IReadOnlyList<TextLauf>>()));
                    break;
                case Zeilenart.Ueberschrift:
                    bogen.Add(new BogenZeile(BogenArt.Ueberschrift, z.Titel, z.Stufe, z.Zwischensumme, Array.Empty<IReadOnlyList<TextLauf>>()));
                    break;
                case Zeilenart.Ergebnis:
                    bogen.Add(new BogenZeile(BogenArt.Position, z.Titel, z.Stufe, string.Empty, Array.Empty<IReadOnlyList<TextLauf>>()));
                    break;
                case Zeilenart.Position:
                    bogen.Add(new BogenZeile(BogenArt.Position, z.Titel, z.Stufe, $"{Zahl.Text(z.Wert ?? 0)} / {Zahl.Text(z.Max)}", Kommentar(z)));
                    break;
                case Zeilenart.Bonus when (z.Wert ?? 0) > 0 || z.HatKommentar:
                    bogen.Add(new BogenZeile(BogenArt.Zuschlag, "Bonuspunkte", 1, $"+ {Zahl.Text(z.Wert ?? 0)}", Kommentar(z)));
                    break;
                case Zeilenart.Minus when (z.Wert ?? 0) > 0 || z.HatKommentar:
                    bogen.Add(new BogenZeile(BogenArt.Zuschlag, "Minuspunkte", 1, $"− {Zahl.Text(z.Wert ?? 0)}", Kommentar(z)));
                    break;
            }
        }
        string summe = s.Gesamt > s.Max + 1e-9
            ? $"{Zahl.Text(s.Max)} / {Zahl.Text(s.Max)} (+ {Zahl.Text(s.Gesamt - s.Max)})"
            : $"{Zahl.Text(s.Gesamt)} / {Zahl.Text(s.Max)}";
        bogen.Add(new BogenZeile(BogenArt.Summe, "Summe", 0, summe, Array.Empty<IReadOnlyList<TextLauf>>()));
        return bogen;
    }

    /// <summary>Zusatzspalten der Notenliste: Rohpunkte, Bonus, Minus, Gesamt und je Aufgabe die Summe.</summary>
    List<(string Kopf, double? Wert)> RohpunkteSpalten()
    {
        Rohpunkteschema.Summe s = Rohpunkteschema.Berechnen(Rohpunktzeilen);
        var liste = new List<(string, double?)> { ("Rohpunkte", s.Roh), ("Bonus", s.Bonus), ("Minus", s.Minus), ("Gesamt", s.Gesamt) };
        foreach (RohpunktZeile a in Rohpunktzeilen.Where(z => z.Art == Zeilenart.Aufgabe))
            liste.Add(($"Aufgabe {a.Id}", Rohpunktzeilen.Where(z => z.Art == Zeilenart.Position && z.Aufgabe == a.Id).Sum(z => z.Wert ?? 0)));
        return liste;
    }

    // ---------- Schema verwalten ----------

    [RelayCommand]
    async Task RohpunkteImportierenAsync()
    {
        try
        {
            if (KlausurOrdnerOderMeldung() is null) return;
            string ordner = Path.Combine(_ablage.Wurzel, _ablage.KlausurEinrichten(KlausurName, _notenliste));
            string? quelle = await _ui.DateiWaehlenAsync(".json", ".xlsx");
            if (quelle is null) return;
            if (_schema is not null && !await _ui.BestaetigenAsync("Rohpunkteschema ersetzen?",
                    "Das bisherige Schema wird gesichert und ersetzt. Eingetragene Punkte bleiben erhalten, soweit die Positionen weiter bestehen.",
                    "Ersetzen", "Abbrechen"))
                return;

            string json = Path.Combine(ordner, Rohpunkteschema.Dateiname), xlsx = Path.Combine(ordner, Rohpunkteschema.Exceldatei);
            foreach (string vorhanden in new[] { json, xlsx }.Where(File.Exists))
                File.Move(vorhanden, KlausurAblage.Sicherungsname(ordner, Path.GetFileName(vorhanden)));

            if (Path.GetExtension(quelle).Equals(".json", StringComparison.OrdinalIgnoreCase))
            {
                RpsDoc doc = RpsBuilder.LiesJson(Formulierungsdatei.TextLesen(quelle));
                List<string> meldungen = RpsBuilder.NormalisiereUndPruefe(doc);
                File.WriteAllText(json, RpsBuilder.SchreibeJson(doc), new UTF8Encoding(false));
                if (meldungen.Count > 0)
                    _ui.Melden(meldungen.Any(m => m.StartsWith("Fehler:", StringComparison.Ordinal)) ? Meldungsart.Fehler : Meldungsart.Warnung,
                        "Rohpunkteschema importiert – bitte prüfen", Kuerzen(string.Join(" ", meldungen), 700));
            }
            else
            {
                File.Copy(quelle, xlsx, overwrite: true);
            }
            SchemaLaden(eingabenBehalten: true);
            if (_schema is not null)
                _ui.Melden(Meldungsart.Erfolg, "Rohpunkteschema übernommen",
                    $"{Rohpunktzeilen.Count(z => z.Art == Zeilenart.Position)} Positionen, Höchstpunkte {Zahl.Text(Rohpunkteschema.Berechnen(Rohpunktzeilen).Max)}.");
        }
        catch (Exception ex)
        {
            Fehler(ex, "Rohpunkteschema nicht importiert");
        }
    }

    /// <summary>Öffnet rohpunkte.xlsx in Excel (legt sie bei Bedarf aus dem Schema bzw. als Beispiel an).</summary>
    [RelayCommand]
    void RohpunkteInExcelBearbeiten() => MitKlausur(ordner =>
    {
        string json = Path.Combine(ordner, Rohpunkteschema.Dateiname), xlsx = Path.Combine(ordner, Rohpunkteschema.Exceldatei);
        if (!File.Exists(xlsx))
        {
            RpsDoc doc = File.Exists(json) ? RpsBuilder.LiesJson(Formulierungsdatei.TextLesen(json)) : Beispielschema();
            string bericht = RpsBuilder.NachExcel(doc, xlsx);
            if (!File.Exists(xlsx))
            {
                _ui.Melden(Meldungsart.Fehler, "Excel-Datei nicht erstellt", Kuerzen(bericht.Replace(Environment.NewLine, " "), 700));
                return;
            }
            if (!File.Exists(json)) AusExcelUebernehmen(xlsx, json);
            SchemaLaden(eingabenBehalten: true);
        }
        _ui.Oeffnen(xlsx);
        _ui.Melden(Meldungsart.Information, "Rohpunkteschema in Excel",
            "Blatt „Bewertungsbogen“: Höchstpunkte, Titel und Bewertungshinweise (Kennungen in eckigen Klammern nicht ändern); Blatt „Umrechnung“: Notenschwellen. Nach dem Speichern hierher zurückkehren – das Schema wird übernommen.");
    });

    RpsDoc Beispielschema()
    {
        var doc = new RpsDoc { Titel = KlausurName.Replace('_', ' '), Umrechnung = RpsBuilder.Standardumrechnung() };
        doc.Aufgaben.Add(new RpsAufgabe { Nr = "1", T = "Aufgabe 1", M = 100, Q = string.Empty });
        string[] titel =
        {
            "A. Erster Prüfungspunkt", "I. Voraussetzung", "II. Voraussetzung", "III. Ergebnis zu A.",
            "B. Zweiter Prüfungspunkt", "I. Voraussetzung", "II. Voraussetzung", "III. Ergebnis zu B.",
            "C. Dritter Prüfungspunkt", "D. Gesamtergebnis",
        };
        for (int i = 0; i < titel.Length; i++)
            doc.Positionen.Add(new RpsPosition { Id = $"1.{i + 1}", T = titel[i], M = 10, A = "S", H = "Bewertungshinweis (nur für Korrigierende sichtbar)" });
        return doc;
    }
}
