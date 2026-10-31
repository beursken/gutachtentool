// Übernommen aus dem KI-Portal (Models/KIHelpers/RpsBuilder.cs), damit Rohpunkteschema, JSON-Format,
// Excel-Layout und Prüfregeln in beiden Programmen identisch sind. Änderungen bitte in beiden Projekten nachziehen.
// Abweichungen: Namensraum, Vorlagepfad (VorlagePfad), ohne gliederung.txt-Abgleich (PruefeIds),
// zusätzlich NormalisiereUndPruefe und Standardumrechnung.
#nullable disable
// RpsBuilder.cs — .NET Framework 4.8
//
// NuGet:  DocumentFormat.OpenXml 3.0.*   Referenz: WindowsBase
//
// Zwei Richtungen:
//   RpsBuilder.NachExcel(vorlage, "rohpunkte.json", "rps.xlsx")   JSON -> XLSX
//   RpsBuilder.NachJson("rps.xlsx", "rohpunkte.json")             XLSX -> JSON
//
// Format des JSON: Abschnitt 10.3 des Korrekturprompts
//   { "legende": {...}, "aufgaben": { "1": {t,m} }, "positionen": { "1.6": {m,t,h,a,v,k,b,w,x} },
//     "umrechnung": [[0,0],[10,1], ...] }
//
// Darstellung in Spalte C einer Positionszeile:
//   [1.3] aa) Testierwille [Klausurfokus] [a.A. vertretbar]
//   (Rn. 12: Auslegung § 133 BGB, nicht § 157)
// Die Kurzschlüssel des JSON werden dabei in Klartext übersetzt und beim Zurücklesen
// wieder eingesammelt; maßgeblich ist allein die Tabelle Kennzeichen.
//
// Feld a ist die Gewichtsklasse der Bewertungssprache: R Routine, S Standard,
// K Kernproblem, KS Klausurschwerpunkt, A anspruchsvoll; E Ergebniszeile. Frühere
// Werte (Schwerpunkt, Prädikatsposition, Obersatz …) werden beim Lesen umgesetzt.
//
// Das Modell liefert nur die Positionen (Art P). Überschriften mit Unterpunkten,
// Ergebniszeilen, Titel und die Umrechnungstabelle ergänzt ErgaenzeAusGliederung
// bzw. LiesJson aus gliederung.txt und der Standardtabelle.
//
// Die Punkte rechnet RpsPruefung aus den Gewichtsklassen (R 1, S 2, K 3, KS 4,
// A fest 1); das Modell gibt keine Punkte mehr vor. Anteile je Fallfrage gelten
// nur mit Fundstelle (Feld q der Aufgabe) als Vorgabe der Lösung.
//
// Fallfragen: Maßgeblich ist grundsätzlich das Präfix der ID ("2.45" -> Fallfrage 2).
// Ältere gliederung.txt bildeten es aus den Teilen ("Teil I" = 1, "Teil II" = 2)
// statt aus den Fallfragen. OrdneFallfragenZu berichtigt die Zuordnung dann anhand
// der Überschriftenzeilen ("Frage 2:", "Teil II (Abwandlung):"), ohne die IDs zu
// ändern; beim Zurücklesen gilt die Aufgabenzeile, unter der eine Position steht.

using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Gutachten2025.Rohpunkte
{
    #region Datenmodell

    public sealed class RpsAufgabe
    {
        public string Nr { get; set; }
        public string T { get; set; }
        public double M { get; set; }

        /// <summary>
        /// Fundstelle einer Vorgabe der Lösung für den Anteil dieser Fallfrage
        /// ("Rn. 3", "Bearbeitungsvermerk"). Leer: Der Anteil ergibt sich aus den
        /// Positionen und ist keine Vorgabe.
        /// </summary>
        public string Q { get; set; }

        public bool IstVorgabe => !string.IsNullOrWhiteSpace(Q);
    }

    public sealed class RpsPosition
    {
        public string Id { get; set; }
        public string T { get; set; }
        public string H { get; set; }
        public double? M { get; set; }

        /// <summary>Gewichtsklasse: R, S, K, KS, A; E Ergebniszeile; leer bei Überschriften.</summary>
        public string A { get; set; }

        public string V { get; set; }   // ja / nein
        public string K { get; set; }   // ja
        public string B { get; set; }   // ja
        public string W { get; set; }   // ja
        public string X { get; set; }   // ja = Teilsumme

        /// <summary>Stufe aus gliederung.txt (1 = oberste); 0, wenn unbekannt. Nur für die Darstellung.</summary>
        public int Stufe { get; set; }

        /// <summary>
        /// Fallfrage, wenn sie vom Präfix der Kennung abweicht (ältere gliederung.txt zählten
        /// die Teile statt der Fallfragen). Leer: Es gilt das Präfix. Wird nicht in das JSON
        /// geschrieben; die IDs bleiben unverändert.
        /// </summary>
        public string Frage { get; set; }

        public bool IstTeilsumme => string.Equals(X, "ja", StringComparison.OrdinalIgnoreCase);

        public string Aufgabe
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(Frage))
                {
                    return Frage;
                }

                int punkt = (Id ?? string.Empty).IndexOf('.');
                return punkt > 0 ? Id.Substring(0, punkt) : Id;
            }
        }
    }

    public sealed class RpsDoc
    {
        public string Titel { get; set; }
        public List<RpsAufgabe> Aufgaben { get; set; } = new List<RpsAufgabe>();
        public List<RpsPosition> Positionen { get; set; } = new List<RpsPosition>();
        public List<double[]> Umrechnung { get; set; } = new List<double[]>();

        public int? CN { get; set; }
        public double? CS { get; set; }
    }

    #endregion

    public static class RpsBuilder
    {
        /// <summary>Abweichende Excel-Vorlage; ohne Angabe die eingebettete Programmvorlage (im KI-Portal ~/content/muster.xlsx).</summary>
        public static string VorlagePfad { get; set; }

        /// <summary>Höchstwert für Bonus- und Minuspunkte (wie im Portal).</summary>
        public const double ZuschlagMax = 10d;

        /// <summary>Normalisiert und prüft ein gelesenes Schema wie beim Rücklesen aus Excel; liefert die Meldungen.</summary>
        public static List<string> NormalisiereUndPruefe(RpsDoc doc)
        {
            List<string> meldungen = new();
            Normalisiere(doc, meldungen, false);
            Pruefe(doc, meldungen);
            return meldungen;
        }

        /// <summary>Standardumrechnung als Liste (ab-Rohpunkte, Notenpunkte).</summary>
        public static List<double[]> Standardumrechnung()
        {
            List<double[]> liste = new();
            for (int i = 0; i < StandardUmrechnung.GetLength(0); i++)
                liste.Add(new[] { StandardUmrechnung[i, 0], StandardUmrechnung[i, 1] });
            return liste;
        }

        // ---- Vorlage ------------------------------------------------------------------------
        private const string BlattBogen = "Bewertungsbogen";
        private const string BlattUmrechnung = "Umrechnung";
        private const int ZeileKopf = 4;
        private const int ProtoAufgabe = 5;
        private const int ProtoPosition = 6;
        private const int ProtoTeilsumme = 9;
        private const int ProtoSumme = 79;
        private const int ProtoSchwanz = 80;

        private const double PunkteSollSumme = 100d;
        private const double PunkteMax = 12d;
        private const double BonusMax = 10d;
        private const double MinusMax = 10d;

        private const string FarbeText = "FF000000";
        private const string FarbeAufgabe = "FFFFFFFF";
        /// <summary>Bewertungshinweis: dunkleres Grau, auf Weiß und auf den Füllungen lesbar.</summary>
        private const string FarbeHinweis = "FF595959";
        private const string FarbeKennzeichen = "FFC00000";

        /// <summary>Zeilen ohne Rohpunkte (Überschriften, Ergebnisse, Mitglieder einer
        /// Sammelposition): Titel grau und nicht fett, sie treten zurück.</summary>
        private const string FarbeOhnePunkte = "FF7F7F7F";

        /// <summary>Überschriften mit Unterpunkten: dunkles Blau auf hellblauer Füllung.</summary>
        private const string FarbeUeberschrift = "FF1F3864";

        /// <summary>
        /// Farbcode der Kennzeichen: Die Farbe folgt dem Gewicht - von Grau (Ergebnis)
        /// über Orange (Routine) und Blau (Standard) zu Grün (Kernproblem) und
        /// Dunkelgrün (Klausurschwerpunkt); Violett für das Anspruchsvolle und den
        /// Bonus; Rot für alles, was die Korrektur zum Abweichen zwingt
        /// (a.A. vertretbar/unvertretbar, Weichenstellung).
        /// </summary>
        private static readonly Dictionary<string, string> FarbeJeKennzeichen =
            new(StringComparer.Ordinal)
            {
                ["a:E"] = "FF808080",   // Ergebnis: grau
                ["a:R"] = "FFC55A11",   // Routine: orange
                ["a:S"] = "FF2F5597",   // Standard: blau
                ["a:K"] = "FF548235",   // Kernproblem: grün
                ["a:KS"] = "FF375623",  // Klausurschwerpunkt: dunkelgrün
                ["a:A"] = "FF7030A0",   // anspruchsvoll: violett
                ["v:ja"] = "FFC00000",  // a.A. vertretbar: rot
                ["v:nein"] = "FFC00000",
                ["w:ja"] = "FFC00000",  // Weichenstellung: rot
                ["k:ja"] = "FF808080",  // knapp: grau
                ["b:ja"] = "FF7030A0",  // Bonus möglich: violett
            };

        /// <summary>
        /// Füllung der ganzen Zeile (Spalten A bis D) nach Gewichtsklasse. Nur Zeilen
        /// mit Rohpunkten; der Klausurschwerpunkt am kräftigsten.
        /// </summary>
        private static readonly Dictionary<string, string> FuellungJeKlasse =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["KS"] = "FFC6E0B4",    // kräftiges Hellgrün
                ["K"] = "FFEBF1DE",     // zartes Grün
                ["A"] = "FFE4DFEC",     // zartes Violett
            };

        /// <summary>Überschriften mit Unterpunkten (Zeilen ohne Rohpunkte und ohne Klasse): hellblau.</summary>
        private const string FuellungUeberschrift = "FFDDEBF7";

        /// <summary>Formate einer gefüllten Zeile: je Spalte das Grundformat mit Füllung.</summary>
        private sealed class Zeilenformat
        {
            public uint A, C, D;
        }

        /// <summary>Deckel allein für die Skalierung; das Schema selbst bleibt bei PunkteMax.</summary>
        private const double SkalierungsDeckel = 14d;

        /// <summary>
        /// Einheit der Skalierung. Ganze Punkte, wo sie reichen: Bruchteile wirken auf den
        /// Korrektor willkürlich und machen die Gewichtung nicht genauer. Erst wenn mehr
        /// Positionen zu bedenken sind, als ganze Punkte zur Verfügung stehen, wird die
        /// Einheit halbiert und zuletzt geviertelt.
        /// </summary>
        private static double Einheit(int positionen, double ziel)
        {
            return positionen <= 0 ? 1d : positionen <= ziel ? 1d : positionen <= ziel * 2d ? 0.5d : 0.25d;
        }

        /// <summary>Trägt der Wert einen Bruchteil, der in der Einheit nicht aufgeht?</summary>
        private static bool Ungerade(double wert, double einheit)
        {
            return Math.Abs((wert / einheit) - Math.Round(wert / einheit)) > 1e-9;
        }
        /// <summary>Deckel für Obersätze: Ein Routinepunkt bleibt ein Routinepunkt.</summary>
        private const double ObersatzDeckelEins = 1.5d;
        private const double ObersatzDeckelHalb = 1d;

        /// <summary>
        /// Übersetzung zwischen den Kurzschlüsseln des JSON und dem Klartext der Tabelle.
        /// Klartext ist maßgeblich für die Anzeige, der Schlüssel für das JSON; beide
        /// Richtungen benutzen ausschließlich diese Tabelle.
        /// </summary>
        private static readonly Kennzeichen[] Karte =
        {
            // Gewichtsklasse (Feld a). Der erste Eintrag je Schlüssel bestimmt die Anzeige.
            new("a", "R",  "Routine"),
            new("a", "S",  "Standard"),
            new("a", "K",  "Kernproblem"),
            new("a", "KS", "Klausurschwerpunkt"),
            new("a", "A",  "anspruchsvoll"),
            new("a", "E",  "Ergebnis"),

            // Frühere Anzeigen: nur zum Zurücklesen älterer Bögen
            new("a", "K",  "Klausurfokus"),
            new("a", "S",  "Sammelposition"),
            new("a", "S",  "Obersatz"),
            new("a", "E",  "Ergebnissatz"),
            new("a", "E",  "Endergebnis"),
            new("a", "A",  "Kernproblem, anspruchsvoll"),
            new("a", "A",  "Standard, anspruchsvoll"),
            new("a", "A",  "Routine, anspruchsvoll"),

            // Kennzeichen mit festem Wert
            new("v", "ja",   "a.A. vertretbar"),
            new("v", "nein", "a.A. unvertretbar"),
            new("k", "ja",   "knapp"),
            new("b", "ja",   "Bonus möglich"),
            new("w", "ja",   "Weichenstellung"),
        };

        private sealed class Kennzeichen
        {
            public Kennzeichen(string feld, string schluessel, string klartext)
            {
                Feld = feld; Schluessel = schluessel; Klartext = klartext;
            }
            public string Feld { get; }
            public string Schluessel { get; }
            public string Klartext { get; }
        }

        private static readonly IReadOnlyDictionary<string, string> Legende =
            new Dictionary<string, string>
            {
                ["m"] = "Höchstpunkte",
                ["t"] = "Titel",
                ["h"] = "Bewertungshinweis",
                ["a"] = "Gewichtsklasse: R Routine, S Standard, K Kernproblem, KS Klausurschwerpunkt, A anspruchsvoll; E Ergebniszeile",
                ["v"] = "abweichendes Ergebnis vertretbar",
                ["k"] = "knappe Darstellung genügt",
                ["b"] = "Bonus möglich",
                ["w"] = "Weichenstellung",
                ["x"] = "Teilsumme",
            };

        private static readonly Regex IdMarke = new(@"^\s*\[(?<id>\d+(?:\.\d+)?)\]\s*", RegexOptions.CultureInvariant);
        private static readonly Regex Marken = new(@"\[(?<wert>[^\[\]]+)\]\s*$", RegexOptions.CultureInvariant);
        private static readonly Regex IdImTitel = new(@"^\s*(?:-|\d+\.\d+)\|", RegexOptions.CultureInvariant);

        // =====================================================================================
        // JSON -> XLSX
        // =====================================================================================

        public static string NachExcel(string jsonPfad, string zielPfad)
        {
            // gliederung.txt liegt im Klausurordner neben dem JSON.
            string verzeichnis = Path.GetDirectoryName(Path.GetFullPath(jsonPfad));
            return NachExcel(jsonPfad, zielPfad, Path.Combine(verzeichnis ?? string.Empty, "gliederung.txt"));
        }



        /// <summary>
        /// Wie <see cref="NachExcel(string, string)"/>; ergänzt aus
        /// <paramref name="gliederungPfad"/> Titel, Überschriften und Ergebniszeilen.
        /// Neu: Vorher wird geprüft, dass jede ID des JSON in der Spalte ID der
        /// Gliederung steht. ErgaenzeAusGliederung ordnet nur nach der Zahl hinter dem
        /// Punkt zu; „2.53" landete so stillschweigend auf Zeile 53 („4.53").
        /// </summary>
        public static string NachExcel(string jsonPfad, string zielPfad, string gliederungPfad)
        {
            RpsDoc doc;
            List<string> vorab = new();
            try
            {
                doc = LiesJson(File.ReadAllText(jsonPfad, Encoding.UTF8));
                if (!string.IsNullOrWhiteSpace(gliederungPfad) && File.Exists(gliederungPfad))
                {
                    string idFehler = PruefeIds(doc, gliederungPfad);
                    if (idFehler != null)
                    {
                        return "Datei nicht erstellt:" + Environment.NewLine + "  Fehler: " + idFehler;
                    }

                    ErgaenzeAusGliederung(doc, File.ReadAllLines(gliederungPfad, Encoding.UTF8), vorab);
                }
            }
            catch (Exception ex)
            {
                return "Datei nicht erstellt:" + Environment.NewLine +
                       "Fehler: JSON nicht lesbar – " + ex.Message;
            }

            string bericht = NachExcel(doc, zielPfad);
            return vorab.Count == 0
                ? bericht
                : (bericht ?? string.Empty) + Environment.NewLine
                + string.Join(Environment.NewLine, vorab.Select(m => "  " + m).ToArray());
        }

        /// <summary>
        /// IDs des JSON, die nicht in der Spalte ID von gliederung.txt stehen. Ältere
        /// Gliederungen ohne Spalte ID werden nicht geprüft. Rückgabe null, wenn alles passt.
        /// </summary>
        private static string PruefeIds(RpsDoc doc, string gliederungPfad)
        {
            // Im Gutachtentool gibt es keine gliederung.txt-Prüfung (im KI-Portal: RpsPruefung.LiesGliederung).
            return null;
        }


        /// <summary>Standardtabelle der Notenumrechnung (Rohpunkte ab … → Notenpunkte).</summary>
        private static readonly double[,] StandardUmrechnung =
        {
            { 0, 0 }, { 10, 1 }, { 20, 2 }, { 30, 3 }, { 40, 4 }, { 45, 5 }, { 50, 6 },
            { 55, 7 }, { 59, 8 }, { 63, 9 }, { 67, 10 }, { 71, 11 }, { 75, 12 }, { 79, 13 },
            { 83, 14 }, { 87, 15 }, { 91, 16 }, { 94, 17 }, { 98, 18 }
        };

        /// <summary>
        /// Ergänzt die Positionen aus gliederung.txt: Jede Zeile wird genau ein Eintrag, in
        /// der Reihenfolge der Datei. Das Modell liefert nur die Positionen (Art P); die
        /// Überschriften mit Unterpunkten (T) und die Ergebniszeilen (E) entstehen hier, mit
        /// null Punkten und der Fallfrage der benachbarten Position. Fehlt einer Position
        /// der Titel, kommt er aus der Gliederung. Gelesen werden drei Formate:
        /// Nr|Stufe|Art|Überschrift, Nr|Stufe|Art|Klasse|Überschrift und
        /// Nr|Stufe|Art|Klasse|ID|Überschrift. Mit Spalte ID stammt die Fallfrage aus
        /// ihr; eine Ergebniszeile gehört zur Fallfrage der Position davor, eine
        /// Überschrift zu der der Position danach.
        /// </summary>
        public static void ErgaenzeAusGliederung(RpsDoc doc, IEnumerable<string> zeilen, List<string> meldungen)
        {
            if (doc == null || zeilen == null)
            {
                return;
            }

            List<string[]> gliederung = new();   // Nr, Art, Klasse, Titel, ID, Stufe
            bool mitKlasse = false;
            bool mitId = false;
            foreach (string roh in zeilen)
            {
                string z = (roh ?? string.Empty).Trim();
                if (z.Length == 0)
                {
                    continue;
                }

                if (z.StartsWith(";", StringComparison.Ordinal))
                {
                    // Nur die Formatzeile "; Nr|Stufe|Art|..." bestimmt das Format.
                    if (z.IndexOf("Nr|Stufe|Art", StringComparison.Ordinal) >= 0)
                    {
                        mitKlasse = z.IndexOf("|Klasse|", StringComparison.Ordinal) >= 0;
                        mitId = z.IndexOf("|ID|", StringComparison.Ordinal) >= 0;
                    }
                    continue;
                }
                int spalten = mitId ? 6 : mitKlasse ? 5 : 4;
                string[] teile = z.Split(new[] { '|' }, spalten);
                if (teile.Length < spalten)
                {
                    continue;
                }

                gliederung.Add(mitId
                    ? new[] { teile[0].Trim(), teile[2].Trim(), teile[3].Trim(), teile[5].Trim(), teile[4].Trim(), teile[1].Trim() }
                    : mitKlasse
                        ? new[] { teile[0].Trim(), teile[2].Trim(), teile[3].Trim(), teile[4].Trim(), "-", teile[1].Trim() }
                        : new[] { teile[0].Trim(), teile[2].Trim(), "-", teile[3].Trim(), "-", teile[1].Trim() });
            }
            if (gliederung.Count == 0)
            {
                return;
            }

            Dictionary<string, RpsPosition> nachNr = new(StringComparer.Ordinal);
            foreach (RpsPosition p in doc.Positionen)
            {
                string nr = NrAus(p.Id);
                if (nr != null && !nachNr.ContainsKey(nr))
                {
                    nachNr.Add(nr, p);
                }
            }

            List<RpsPosition> neu = new();
            HashSet<RpsPosition> verwendet = new();

            for (int i = 0; i < gliederung.Count; i++)
            {
                string nr = gliederung[i][0], art = gliederung[i][1], klasse = gliederung[i][2], titel = gliederung[i][3];
                if (!int.TryParse(gliederung[i][5], NumberStyles.None, CultureInfo.InvariantCulture, out int stufe))
                {
                    stufe = 0;
                }


                if (nachNr.TryGetValue(nr, out RpsPosition p))
                {
                    _ = verwendet.Add(p);
                    p.Stufe = stufe;
                    if (string.IsNullOrWhiteSpace(p.T))
                    {
                        p.T = titel;
                    }

                    if (art is "T" or "E")
                    {
                        p.M = null; p.X = "ja";
                        if (art == "E")
                        {
                            p.A = "E";
                        }
                    }
                    else if (string.IsNullOrWhiteSpace(p.A) && klasse != "-")
                    {
                        p.A = klasse;
                    }
                    neu.Add(p);
                    continue;
                }

                if (art == "P")
                {
                    meldungen.Add("Warnung: Position " + nr + " (" + titel + ") fehlt im JSON; ohne Punkte eingefügt.");
                }

                string id = gliederung[i][4];
                neu.Add(new RpsPosition
                {
                    Id = id != "-" && id.Contains(".") ? id : Fallfrage(gliederung, i, nachNr, doc) + "." + nr,
                    T = titel,
                    A = art == "E" ? "E" : (art == "P" && klasse != "-" ? klasse : null),
                    X = "ja",
                    Stufe = stufe,
                });
            }

            foreach (RpsPosition p in doc.Positionen.Where(x => !verwendet.Contains(x)))
            {
                meldungen.Add("Warnung: Position " + p.Id + " steht nicht in gliederung.txt; am Ende angefügt.");
                neu.Add(p);
            }

            doc.Positionen = neu;
        }

        /// <summary>Nummer hinter dem Punkt der Kennung ("1.6" -> "6").</summary>
        private static string NrAus(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return null;
            }

            int punkt = id.IndexOf('.');
            return punkt >= 0 ? id.Substring(punkt + 1).Trim() : id.Trim();
        }

        /// <summary>
        /// Fallfrage einer ergänzten Zeile. Eine Ergebniszeile (E) schließt ihre Fallfrage
        /// ab und gehört zur vorangehenden Position, eine Überschrift (T) eröffnet und gehört
        /// zur folgenden. Bisher galt für beide die folgende - „Ergebnis zu Frage 1" landete
        /// so als 2.35 unter Frage 2. Maßgeblich ist die Spalte ID der Gliederung, hilfsweise
        /// die Kennung der Position im JSON.
        /// </summary>
        private static string Fallfrage(List<string[]> gliederung, int i,
            Dictionary<string, RpsPosition> nachNr, RpsDoc doc)
        {
            bool rueckwaerts = gliederung[i][1] == "E";
            string treffer = rueckwaerts
                ? Nachbar(gliederung, i, -1, nachNr) ?? Nachbar(gliederung, i, +1, nachNr)
                : Nachbar(gliederung, i, +1, nachNr) ?? Nachbar(gliederung, i, -1, nachNr);
            return treffer ?? (doc.Aufgaben.Count > 0 ? doc.Aufgaben[0].Nr : "1");
        }

        private static string Nachbar(List<string[]> gliederung, int i, int schritt,
            Dictionary<string, RpsPosition> nachNr)
        {
            for (int k = i + schritt; k >= 0 && k < gliederung.Count; k += schritt)
            {
                string id = gliederung[k][4];
                int punkt = id.IndexOf('.');
                if (punkt > 0)
                {
                    return id.Substring(0, punkt);
                }

                if (nachNr.TryGetValue(gliederung[k][0], out RpsPosition p))
                {
                    return p.Aufgabe;
                }
            }
            return null;
        }

        // =====================================================================================
        // Fallfragen aus den Überschriften
        // =====================================================================================

        /// <summary>
        /// Fallfragenkopf: Begriff, ggf. Nummer, dann Ende, Doppelpunkt, Klammer oder Strich
        /// („Frage 2:", „Teil II (Abwandlung):", „Abwandlung"). Nicht: „1. Frage der
        /// Verjährung"; Komplexe gliedern unter einer Fallfrage und bleiben außer Betracht.
        /// Gleichlautend in KorrekturPDF.Fallfragen.cs.
        /// </summary>
        private static readonly Regex Fallfragenkopf = new(
            @"^(?:\d+\.\s*)?(?:(?:Zu[rm]?|L(?:ö|oe)sung(?:svorschlag|shinweise|sskizze)?\s+zu[rm]?|Hinweise\s+zu[rm]?|Bearbeitung\s+(?:von|der|zu[rm]?))\s+)?"
            + @"(?:(?:Fall|Zusatz|Haupt|Vor)?frage|(?:Teil)?aufgabe|(?:Klausur)?teil"
            + @"|Ausgangsfall|Grundfall|Abwandlung|(?:Fall)?variante|Fall)"
            + @"(?:\s+(?:[IVX]{1,4}|\d{1,2}|[A-H]))?\s*(?:$|[:(–—-])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>Zeile von gliederung.txt (Zahl hinter dem Punkt der Kennung); unbekannt: ans Ende.</summary>
        private static int Gliederungsnummer(RpsPosition p)
        {
            return int.TryParse(NrAus(p.Id), NumberStyles.None, CultureInfo.InvariantCulture, out int nr)
                ? nr
                : int.MaxValue;
        }

        /// <summary>Zeile ohne Punkte, deren Titel eine Fallfrage bezeichnet („Frage 2:", „Teil II (Abwandlung):").</summary>
        private static bool IstFallfragenkopf(RpsPosition p)
        {
            bool ohnePunkte = p.IstTeilsumme || !p.M.HasValue || p.M.Value <= 0d;
            return ohnePunkte && !string.IsNullOrWhiteSpace(p.T)
                && Fallfragenkopf.IsMatch(IdImTitel.Replace(p.T.Trim(), string.Empty));
        }

        /// <summary>
        /// Ordnet die Positionen den Fallfragen nach den Überschriftenzeilen zu, wenn das
        /// Präfix der IDs nicht passt. Ältere gliederung.txt bildeten es aus den Teilen
        /// („Teil I" = 1, „Teil II" = 2) statt aus den Fallfragen; Teil I Frage 2 fiel so
        /// unter Aufgabe 1, Teil II unter Aufgabe 2, und Aufgabe 3 blieb leer.
        ///
        /// Die innersten Fallfragenköpfe - ein Kopf, dem unmittelbar ein weiterer folgt,
        /// gliedert nur („Teil I:" vor „Frage 1:") - werden der Reihe nach den Aufgaben
        /// zugeordnet, jede Zeile dem letzten Kopf davor. Berichtigt wird nur, wenn die Zahl
        /// der Köpfe der Zahl der Aufgaben entspricht und jede Aufgabe Punkte erhält. Die IDs
        /// bleiben unverändert, damit vorhandene Korrekturen gültig bleiben.
        /// </summary>
        public static void OrdneFallfragenZu(RpsDoc doc, List<string> meldungen)
        {
            if (doc == null || doc.Aufgaben.Count < 2 || doc.Positionen.Count == 0)
            {
                return;
            }

            List<RpsPosition> reihe = doc.Positionen
                .Select((p, i) => new { p, i })
                .OrderBy(x => Gliederungsnummer(x.p)).ThenBy(x => x.i)
                .Select(x => x.p)
                .ToList();

            HashSet<int> koepfe = new(
                Enumerable.Range(0, reihe.Count).Where(i => IstFallfragenkopf(reihe[i])));
            HashSet<int> blatt = new(koepfe.Where(i => !koepfe.Contains(i + 1)));
            List<string> fragen = doc.Aufgaben.Select(a => a.Nr).ToList();

            if (blatt.Count != fragen.Count)
            {
                if (koepfe.Count > 0 && doc.Positionen.Select(p => p.Aufgabe).Distinct().Count() != fragen.Count)
                {
                    meldungen.Add(string.Format(CultureInfo.CurrentCulture,
                        "Warnung: {0} Fallfragen, aber {1} Fallfragenköpfe in der Gliederung; "
                        + "Zuordnung nach dem Präfix der IDs - bitte prüfen.", fragen.Count, blatt.Count));
                }
                return;
            }

            string[] zuordnung = new string[reihe.Count];
            int k = -1;
            for (int i = 0; i < reihe.Count; i++)
            {
                if (blatt.Contains(i))
                {
                    k++;
                }

                zuordnung[i] = fragen[Math.Max(k, 0)];
            }

            if (Enumerable.Range(0, reihe.Count).All(i => zuordnung[i] == reihe[i].Aufgabe))
            {
                return;
            }

            Dictionary<string, double> summen = fragen.ToDictionary(f => f, f => Enumerable.Range(0, reihe.Count)
                .Where(i => zuordnung[i] == f && !reihe[i].IstTeilsumme && reihe[i].M.HasValue && reihe[i].M.Value > 0d)
                .Sum(i => reihe[i].M.Value));

            if (summen.Values.Any(s => s <= 0d))
            {
                meldungen.Add("Warnung: Zuordnung der Positionen zu den Fallfragen nicht eindeutig; "
                    + "es bleibt beim Präfix der IDs.");
                return;
            }

            for (int i = 0; i < reihe.Count; i++)
            {
                reihe[i].Frage = zuordnung[i];
            }

            meldungen.Add("Hinweis: Das Präfix der IDs entspricht nicht den Fallfragen (ältere gliederung.txt); "
                + "Zuordnung aus den Überschriften berichtigt ("
                + string.Join(", ", fragen.Select(f => f + ": "
                    + summen[f].ToString("0.##", CultureInfo.CurrentCulture)).ToArray())
                + " Rohpunkte). Die IDs bleiben unverändert.");
        }

        /// <summary>
        /// Erzeugt den Bewertungsbogen. Rückgabe "0 - Datei … erstellt", wenn alles
        /// stimmt; sonst ein mehrzeiliger Bericht. Beginnt er mit "Datei nicht erstellt",
        /// wurde nichts geschrieben.
        ///
        /// Neu: Trägt keine bepunktbare Position ein m, bricht die Methode ab. Bisher
        /// machte Normalisiere jede Position zur Teilsumme, und der Bogen erschien
        /// vollständig - nur ohne Rohpunkte. Die Punkte rechnet RpsPruefung.
        /// </summary>
        public static string NachExcel(RpsDoc doc, string zielPfad)
        {
            List<string> meldungen = new();

            List<RpsPosition> bepunktbar = doc.Positionen
                .Where(p => !p.IstTeilsumme && !string.Equals(p.A, "E", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (bepunktbar.Count > 0 && bepunktbar.All(p => !p.M.HasValue || p.M.Value <= 0d))
            {
                meldungen.Add("Fehler: Keine Position trägt Punkte (Feld m). Die Punkte rechnet "
                    + "RpsPruefung.PruefeUndBerichtige; sie ist nicht oder nicht erfolgreich gelaufen.");
                return Bericht(false, meldungen);
            }

            // Vor Normalisiere: Teilsummen, Skalierung und Aufgabenzeilen folgen der Fallfrage.
            OrdneFallfragenZu(doc, meldungen);
            Normalisiere(doc, meldungen, true);
            Pruefe(doc, meldungen);

            if (meldungen.Any(m => m.StartsWith("Fehler:", StringComparison.Ordinal)))
            {
                return Bericht(false, meldungen);
            }

            try
            {
                Baue(doc, zielPfad);
            }
            catch (Exception ex)
            {
                meldungen.Add("Fehler: " + ex.Message);
                return Bericht(false, meldungen);
            }

            return meldungen.Count == 0
                ? "0 - Datei " + Path.GetFileName(zielPfad) + " erstellt"
                : Bericht(true, meldungen);
        }


        private static string Bericht(bool erstellt, List<string> meldungen)
        {
            string kopf = erstellt ? "Datei erstellt, mit Anmerkungen:" : "Datei nicht erstellt:";
            return kopf + Environment.NewLine +
                   string.Join(Environment.NewLine, meldungen.Select(m => "  " + m).ToArray());
        }

        private static void Baue(RpsDoc doc, string zielPfad)
        {
            if (!string.IsNullOrEmpty(VorlagePfad) && File.Exists(VorlagePfad)) File.Copy(VorlagePfad, zielPfad, true);
            else Gutachten2025.Services.Programmvorlagen.Schreiben(Gutachten2025.Services.Programmvorlagen.Rohpunkte, zielPfad);
            File.SetAttributes(zielPfad, FileAttributes.Normal);

            using SpreadsheetDocument sd = SpreadsheetDocument.Open(zielPfad, true);
            WorkbookPart wbp = sd.WorkbookPart;
            WorksheetPart wsp = HoleBlatt(wbp, BlattBogen);
            Worksheet ws = wsp.Worksheet;
            SheetData sheetData = ws.GetFirstChild<SheetData>();

            Stil stil = Stil.AusVorlage(sheetData);
            Dictionary<string, Zeilenformat> xfHervor = FuellungJeKlasse.ToDictionary(
                kv => kv.Key,
                kv => new Zeilenformat
                {
                    A = ErzeugeFuellungsFormat(wbp.WorkbookStylesPart, stil.PositionA, kv.Value),
                    C = ErzeugeFuellungsFormat(wbp.WorkbookStylesPart, stil.PositionC, kv.Value),
                    D = ErzeugeFuellungsFormat(wbp.WorkbookStylesPart, stil.PositionD, kv.Value),
                },
                StringComparer.OrdinalIgnoreCase);

            // Oberste Prüfungsebene (A., B., C.): die kleinste Stufe unterhalb der
            // Fallfragen. Nur ihre Überschriften werden hellblau hinterlegt.
            int obersteStufe = doc.Positionen
                .Where(p => p.Stufe > 0 && !IstFallfragenzeile(p.T))
                .Select(p => p.Stufe)
                .DefaultIfEmpty(0)
                .Min();

            // Überschriften mit Unterpunkten: hellblau hinterlegt
            Zeilenformat xfUeberschrift = new()
            {
                A = ErzeugeFuellungsFormat(wbp.WorkbookStylesPart, stil.TeilsummeA, FuellungUeberschrift),
                C = ErzeugeFuellungsFormat(wbp.WorkbookStylesPart, stil.TeilsummeC, FuellungUeberschrift),
                D = ErzeugeFuellungsFormat(wbp.WorkbookStylesPart, stil.TeilsummeD, FuellungUeberschrift),
            };

            // Übrige Zeilen ohne Rohpunkte (Ergebnisse, Mitglieder einer Sammelposition):
            // alle Spalten ohne Füllung (transparent)
            stil.TeilsummeA = ErzeugeOhneFuellung(wbp.WorkbookStylesPart, stil.TeilsummeA);
            stil.TeilsummeC = ErzeugeOhneFuellung(wbp.WorkbookStylesPart, stil.TeilsummeC);
            stil.TeilsummeD = ErzeugeOhneFuellung(wbp.WorkbookStylesPart, stil.TeilsummeD);
            double breiteC = SpaltenBreite(ws, 3, 86d);

            List<Row> schwanz = sheetData.Elements<Row>()
                .Where(r => r.RowIndex != null && r.RowIndex.Value >= ProtoSchwanz)
                .Select(r => (Row)r.CloneNode(true))
                .ToList();

            foreach (Row alt in sheetData.Elements<Row>()
                         .Where(r => r.RowIndex != null && r.RowIndex.Value > ZeileKopf)
                         .ToList())
            {
                alt.Remove();
            }

            SetzeText(sheetData, "A1", doc.Titel);

            int zeile = ZeileKopf + 1;
            List<int> aufgabenZeilen = new();

            foreach (RpsAufgabe aufgabe in doc.Aufgaben)
            {
                int kopf = zeile++;
                aufgabenZeilen.Add(kopf);
                int erste = zeile;

                foreach (RpsPosition pos in doc.Positionen.Where(p => p.Aufgabe == aufgabe.Nr))
                {
                    sheetData.Append(BauePositionszeile(zeile, pos, stil, xfHervor, xfUeberschrift,
                        IstOberstePruefungsebene(pos, obersteStufe), breiteC));
                    zeile++;
                }

                sheetData.Append(BaueAufgabenzeile(kopf, aufgabe, stil, erste, zeile - 1, breiteC));
            }

            Sortiere(sheetData);

            int zSumme = zeile++;
            Row rSumme = new() { RowIndex = (uint)zSumme };
            rSumme.Append(FormelZelle("A" + zSumme, stil.SummeA,
                string.Join("+", aufgabenZeilen.Select(i => "A" + i).ToArray())));
            rSumme.Append(FormelZelle("B" + zSumme, stil.SummeA,
                string.Join("+", aufgabenZeilen.Select(i => "B" + i).ToArray())));
            rSumme.Append(TextZelle("C" + zSumme, stil.SummeC,
                "Summe der Positionen (Maximalpunkte müssen exakt 100 ergeben – Kontrolle: Spalte A)"));
            rSumme.Append(LeereZelle("D" + zSumme, stil.SummeD));
            sheetData.Append(rSumme);

            int versatz = zeile - ProtoSchwanz;
            int zBonus = 0, zMinus = 0, zErreicht = 0, zGesamt = 0, zNoten = 0, zNote = 0;

            foreach (Row proto in schwanz)
            {
                int neu = (int)proto.RowIndex.Value + versatz;
                Row r = VerschiebeZeile(proto, neu);
                sheetData.Append(r);

                string label = ZellText(r.Elements<Cell>().FirstOrDefault(), wbp);
                if (label == null)
                {
                    continue;
                }

                if (label.StartsWith("Bonuspunkte", StringComparison.Ordinal))
                {
                    zBonus = neu;
                }
                else if (label.StartsWith("Minuspunkte", StringComparison.Ordinal))
                {
                    zMinus = neu;
                }
                else if (label.StartsWith("Erreichte Rohpunkte", StringComparison.Ordinal))
                {
                    zErreicht = neu;
                }
                else if (label.StartsWith("Gesamt", StringComparison.Ordinal))
                {
                    zGesamt = neu;
                }
                else if (label.StartsWith("Notenpunkte", StringComparison.Ordinal))
                {
                    zNoten = neu;
                }
                else if (label.StartsWith("Juristische Note", StringComparison.Ordinal))
                {
                    zNote = neu;
                }
            }

            if (zBonus == 0 || zMinus == 0 || zErreicht == 0 || zGesamt == 0 || zNoten == 0 || zNote == 0)
            {
                throw new InvalidDataException(
                    "Vorlage passt nicht: Die Zeilen \"Bonuspunkte\", \"Minuspunkte\", \"Erreichte Rohpunkte\", " +
                    "\"Gesamt\", \"Notenpunkte\" und \"Juristische Note\" müssen unterhalb der Summenzeile stehen.");
            }

            SetzeFormel(sheetData, "C" + zErreicht, "B" + zSumme);
            SetzeFormel(sheetData, "C" + zGesamt,
                string.Format(CultureInfo.InvariantCulture, "MAX(0,MIN(100,B{0}+C{1}+C{2}))", zSumme, zBonus, zMinus));
            SetzeFormel(sheetData, "C" + zNoten,
                string.Format(CultureInfo.InvariantCulture,
                    "LOOKUP(C{0},Umrechnung!$A$2:$A$20,Umrechnung!$B$2:$B$20)", zGesamt));
            SetzeFormel(sheetData, "C" + zNote,
                string.Format(CultureInfo.InvariantCulture,
                    "LOOKUP(C{0},Umrechnung!$E$2:$E$20,Umrechnung!$F$2:$F$20)&\" (\"&C{0}&\" Punkte)\"", zNoten));

            // Bedingte Formate und Eingabepruefungen der Vorlage auf die neuen Zeilen
            // ziehen - vor den eigenen Eingabepruefungen, damit diese nicht doppelt stehen.
            VerschiebeBereiche(ws, zSumme, versatz);

            SetzeText(sheetData, "A" + zBonus, "Bonuspunkte (0 bis +" + BonusMax + "):");
            SetzeText(sheetData, "A" + zMinus, "Minuspunkte (0 bis −" + MinusMax + "):");
            SetzeEingabepruefung(ws, "C" + zBonus, 0d, BonusMax);
            SetzeEingabepruefung(ws, "C" + zMinus, -MinusMax, 0d);

            VerschiebeVerbundzellen(ws, ProtoSchwanz, versatz);
            Sortiere(sheetData);

            SchreibeUmrechnung(wbp, doc.Umrechnung);
            EntferneBerechnungskette(wbp);

            ws.Save();
            wbp.Workbook.Save();
        }

        // =====================================================================================
        // XLSX -> JSON
        // =====================================================================================

        /// <summary>
        /// Liest den bearbeiteten Bogen zurueck. Rueckgabe wie bei NachExcel:
        /// "0 - Datei ... erstellt", wenn alles stimmt, sonst ein Bericht. Bisher kam
        /// im Erfolgsfall null zurueck; der Aufrufer (Schritt) meldete daraufhin eine
        /// leere Rueckmeldung, obwohl die Datei geschrieben war.
        /// </summary>
        public static string NachJson(string xlsxPfad, string jsonPfad)
        {
            RpsDoc doc;
            List<string> meldungen = new();

            try
            {
                doc = Lies(xlsxPfad, meldungen);
            }
            catch (Exception ex)
            {
                meldungen.Add("Fehler: " + xlsxPfad + " nicht lesbar – " + ex.Message);
                return Bericht(false, meldungen);
            }

            Normalisiere(doc, meldungen, false);
            Pruefe(doc, meldungen);

            try
            {
                File.WriteAllText(jsonPfad, SchreibeJson(doc), new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                meldungen.Add("Fehler: " + jsonPfad + " nicht schreibbar – " + ex.Message);
                return Bericht(false, meldungen);
            }

            return meldungen.Count == 0
                ? "0 - Datei " + Path.GetFileName(jsonPfad) + " erstellt"
                : Bericht(true, meldungen);
        }

        /// <summary>Liest eine vom Aufgabensteller bearbeitete Datei zurück in das Datenmodell.</summary>
        public static RpsDoc Lies(string xlsxPfad)
        {
            return Lies(xlsxPfad, new List<string>());
        }

        private static RpsDoc Lies(string xlsxPfad, List<string> meldungen)
        {
            RpsDoc doc = new();

            using (SpreadsheetDocument sd = SpreadsheetDocument.Open(xlsxPfad, false))
            {
                WorkbookPart wbp = sd.WorkbookPart;
                SheetData sheetData = HoleBlatt(wbp, BlattBogen).Worksheet.GetFirstChild<SheetData>();

                doc.Titel = ZellText(Zelle(sheetData, "A1"), wbp);
                string aktuelleAufgabe = null;

                foreach (Row row in sheetData.Elements<Row>()
                             .Where(r => r.RowIndex != null && r.RowIndex.Value > ZeileKopf)
                             .OrderBy(r => r.RowIndex.Value))
                {
                    string c = ZellText(Zelle(sheetData, "C" + row.RowIndex.Value), wbp);
                    if (string.IsNullOrWhiteSpace(c))
                    {
                        continue;
                    }

                    Match id = IdMarke.Match(c);
                    if (!id.Success)
                    {
                        continue;          // Summen-, Bonus- und Hinweiszeilen
                    }

                    string kennung = id.Groups["id"].Value;
                    string rest = c.Substring(id.Length);
                    double? punkte = ZellZahl(Zelle(sheetData, "A" + row.RowIndex.Value), wbp);

                    if (!kennung.Contains("."))
                    {
                        // Aufgabenzeile
                        aktuelleAufgabe = kennung;
                        doc.Aufgaben.Add(new RpsAufgabe
                        {
                            Nr = kennung,
                            T = Kopfzeile(rest).Trim(),
                            M = punkte ?? 0d,
                        });
                        continue;
                    }

                    if (aktuelleAufgabe == null)
                    {
                        meldungen.Add("Warnung: Position " + kennung +
                                      " steht vor der ersten Aufgabenzeile; Aufgabe wird ergänzt.");
                    }

                    RpsPosition gelesen = ZerlegePosition(kennung, rest, punkte);
                    gelesen.A = Normiere(gelesen.A);

                    // Maßgeblich ist die Aufgabenzeile, unter der die Position im Bogen steht -
                    // nicht das Präfix der ID (ältere gliederung.txt zählten die Teile).
                    gelesen.Frage = aktuelleAufgabe;
                    doc.Positionen.Add(gelesen);
                }

                doc.Umrechnung = LiesUmrechnung(wbp);
            }

            // Die Teilsummen der Aufgabenzeilen stehen als Formel in der Datei. Hat Excel
            // keinen Wert zwischengespeichert, rechnen wir sie aus den Positionen nach.
            foreach (RpsAufgabe a in doc.Aufgaben)
            {
                double teil = doc.Positionen.Where(p => p.Aufgabe == a.Nr && !p.IstTeilsumme)
                                            .Sum(p => p.M ?? 0d);
                if (a.M <= 0d)
                {
                    a.M = teil;
                }
            }

            return doc;
        }

        /// <summary>Zerlegt den Zelltext einer Positionszeile in Titel, Kennzeichen und Hinweis.</summary>
        private static RpsPosition ZerlegePosition(string id, string text, double? punkte)
        {
            RpsPosition pos = new() { Id = id, M = punkte };

            string kopf = Kopfzeile(text);
            string rumpf = Restzeilen(text);

            // Bogen, die mit der Gliederung im Format ...|Klasse|ID|Überschrift erstellt
            // wurden, tragen die Spalte ID im Titel ("1.3|I. Antrag", "-|A. Entstehung").
            kopf = IdImTitel.Replace(kopf, string.Empty);

            // Kennzeichen stehen am Ende der Kopfzeile, in beliebiger Zahl
            for (Match m = Marken.Match(kopf); m.Success; m = Marken.Match(kopf))
            {
                string wert = m.Groups["wert"].Value.Trim();
                kopf = kopf.Substring(0, m.Index).TrimEnd();

                Kennzeichen treffer = Karte.FirstOrDefault(k =>
                    string.Equals(k.Klartext, wert, StringComparison.OrdinalIgnoreCase));

                if (treffer == null)
                {
                    // Unbekannte Marke: als Teil des Titels behandeln, nichts verwerfen
                    kopf = (kopf + " [" + wert + "]").Trim();
                    break;
                }

                switch (treffer.Feld)
                {
                    case "a": pos.A = treffer.Schluessel; break;
                    case "v": pos.V = treffer.Schluessel; break;
                    case "k": pos.K = treffer.Schluessel; break;
                    case "b": pos.B = treffer.Schluessel; break;
                    case "w": pos.W = treffer.Schluessel; break;
                    case "g": pos.A = treffer.Schluessel; break;   // Gewichtsklasse
                }
            }

            pos.T = kopf.Trim();

            if (rumpf.Length > 0)
            {
                string h = rumpf.Trim();
                if (h.StartsWith("(", StringComparison.Ordinal) && h.EndsWith(")", StringComparison.Ordinal))
                {
                    h = h.Substring(1, h.Length - 2);
                }

                pos.H = h.Trim();
            }

            if (!punkte.HasValue)
            {
                pos.X = "ja";
            }

            return pos;
        }

        /// <summary>Erzeugt das JSON in der Schlüsselreihenfolge des Formats nach 10.3.</summary>
        public static string SchreibeJson(RpsDoc doc)
        {
            JsonWriterOptions optionen = new()
            {
                Indented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            };

            using MemoryStream speicher = new();
            using (Utf8JsonWriter w = new(speicher, optionen))
            {
                w.WriteStartObject();

                if (!string.IsNullOrWhiteSpace(doc.Titel))
                {
                    w.WriteString("titel", doc.Titel);
                }

                w.WriteStartObject("legende");
                foreach (KeyValuePair<string, string> e in Legende)
                {
                    w.WriteString(e.Key, e.Value);
                }

                w.WriteEndObject();

                w.WriteStartObject("aufgaben");
                foreach (RpsAufgabe a in doc.Aufgaben)
                {
                    w.WriteStartObject(a.Nr);
                    w.WriteString("t", a.T);
                    w.WriteNumber("m", a.M);
                    w.WriteEndObject();
                }
                w.WriteEndObject();

                w.WriteStartObject("positionen");
                foreach (RpsPosition p in doc.Positionen)
                {
                    w.WriteStartObject(p.Id);
                    if (p.M.HasValue)
                    {
                        w.WriteNumber("m", p.M.Value);
                    }

                    w.WriteString("t", p.T ?? string.Empty);
                    if (!string.IsNullOrWhiteSpace(p.H))
                    {
                        w.WriteString("h", p.H);
                    }

                    if (!string.IsNullOrWhiteSpace(p.A))
                    {
                        w.WriteString("a", p.A);
                    }

                    if (!string.IsNullOrWhiteSpace(p.V))
                    {
                        w.WriteString("v", p.V);
                    }

                    if (!string.IsNullOrWhiteSpace(p.K))
                    {
                        w.WriteString("k", p.K);
                    }

                    if (!string.IsNullOrWhiteSpace(p.B))
                    {
                        w.WriteString("b", p.B);
                    }

                    if (!string.IsNullOrWhiteSpace(p.W))
                    {
                        w.WriteString("w", p.W);
                    }

                    if (p.IstTeilsumme)
                    {
                        w.WriteString("x", "ja");
                    }

                    w.WriteEndObject();
                }
                w.WriteEndObject();

                w.WriteStartArray("umrechnung");
                foreach (double[] paar in doc.Umrechnung)
                {
                    w.WriteStartArray();
                    w.WriteNumberValue(paar[0]);
                    w.WriteNumberValue(paar[1]);
                    w.WriteEndArray();
                }
                w.WriteEndArray();

                w.WriteEndObject();
            }

            return Encoding.UTF8.GetString(speicher.ToArray());
        }

        /// <summary>
        /// Liest das JSON. Erkannt werden beide Gestalten: die Listenform des
        /// Ausgabeschemas (aufgaben/positionen als Arrays mit nr bzw. id) und die
        /// Objektform nach Abschnitt 10.3 (Schlüssel = Aufgabennummer bzw. Positions-ID).
        /// Die Reihenfolge der Einträge bleibt in beiden Fällen erhalten.
        /// </summary>
        public static RpsDoc LiesJson(string json)
        {
            RpsDoc doc = new();

            using (JsonDocument jd = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            }))
            {
                JsonElement wurzel = jd.RootElement;

                if (wurzel.TryGetProperty("aufgaben", out JsonElement aufgaben))
                {
                    if (aufgaben.ValueKind == JsonValueKind.Array)
                    {
                        foreach (JsonElement e in aufgaben.EnumerateArray())
                        {
                            doc.Aufgaben.Add(new RpsAufgabe
                            {
                                Nr = Text(e, "nr"),
                                T = Text(e, "t"),
                                M = Zahl(e, "m") ?? 0d,
                                Q = Text(e, "q"),
                            });
                        }
                    }
                    else if (aufgaben.ValueKind == JsonValueKind.Object)
                    {
                        foreach (JsonProperty p in aufgaben.EnumerateObject())
                        {
                            doc.Aufgaben.Add(new RpsAufgabe
                            {
                                Nr = p.Name,
                                T = Text(p.Value, "t"),
                                M = Zahl(p.Value, "m") ?? 0d,
                                Q = Text(p.Value, "q"),
                            });
                        }
                    }
                }

                if (wurzel.TryGetProperty("positionen", out JsonElement positionen))
                {
                    if (positionen.ValueKind == JsonValueKind.Array)
                    {
                        foreach (JsonElement e in positionen.EnumerateArray())
                        {
                            doc.Positionen.Add(Position(Text(e, "id"), e));
                        }
                    }
                    else if (positionen.ValueKind == JsonValueKind.Object)
                    {
                        foreach (JsonProperty p in positionen.EnumerateObject())
                        {
                            doc.Positionen.Add(Position(p.Name, p.Value));
                        }
                    }
                }

                if (wurzel.TryGetProperty("umrechnung", out JsonElement umrechnung))
                {
                    foreach (JsonElement e in umrechnung.EnumerateArray())
                    {
                        if (e.ValueKind == JsonValueKind.Array)
                        {
                            double[] werte = e.EnumerateArray().Select(x => x.GetDouble()).ToArray();
                            if (werte.Length >= 2)
                            {
                                doc.Umrechnung.Add(new[] { werte[0], werte[1] });
                            }
                        }
                        else if (e.ValueKind == JsonValueKind.Object)
                        {
                            double? r = Zahl(e, "r");
                            double? n = Zahl(e, "n");
                            if (r.HasValue && n.HasValue)
                            {
                                doc.Umrechnung.Add(new[] { r.Value, n.Value });
                            }
                        }
                    }
                }

                doc.Titel = wurzel.TryGetProperty("titel", out JsonElement titel) && titel.ValueKind == JsonValueKind.String
                    ? titel.GetString()
                    : "Korrekturbogen";

                // Rechnung des Modells, fuer die Pruefung
                if (wurzel.TryGetProperty("c", out JsonElement c) && c.ValueKind == JsonValueKind.Object)
                {
                    double? n = Zahl(c, "n");
                    doc.CN = n.HasValue ? (int?)Math.Round(n.Value) : null;
                    doc.CS = Zahl(c, "s");
                }
            }

            // Das Modell gibt die Umrechnung nicht mehr aus; es gilt die Standardtabelle.
            if (doc.Umrechnung.Count == 0)
            {
                for (int i = 0; i < StandardUmrechnung.GetLength(0); i++)
                {
                    doc.Umrechnung.Add(new[] { StandardUmrechnung[i, 0], StandardUmrechnung[i, 1] });
                }
            }

            return doc;
        }

        private static RpsPosition Position(string id, JsonElement o)
        {
            return new RpsPosition
            {
                Id = id,
                T = Text(o, "t"),
                H = Text(o, "h"),
                M = Zahl(o, "m"),
                A = Normiere(Text(o, "a")),
                V = Text(o, "v"),
                K = Text(o, "k"),
                B = Text(o, "b"),
                W = Text(o, "w"),
                X = Text(o, "x"),
            };
        }

        /// <summary>
        /// Gewichtsklasse als Kürzel (R, S, K, KS, A, E). Frühere Werte des Felds a werden
        /// umgesetzt: Schwerpunkt → K, Prädikatsposition und x-P → A, Sammelposition und
        /// Obersatz → S, Ergebnissatz und Endergebnis → E, SP → KS.
        /// </summary>
        private static string Normiere(string art)
        {
            if (string.IsNullOrWhiteSpace(art))
            {
                return art;
            }

            string a = art.Trim().Trim('[', ']');
            return a.ToUpperInvariant() switch
            {
                "R" or "ROUTINE" => "R",
                "S" or "STANDARD" or "OBERSATZ" or "SAMMELPOSITION" => "S",
                "K" or "KERNPROBLEM" or "SCHWERPUNKT" => "K",
                "KS" or "SP" or "KLAUSURSCHWERPUNKT" => "KS",
                "A" or "ANSPRUCHSVOLL" or "PRAEDIKATSPOSITION" or "PRÄDIKATSPOSITION" or "K-P" or "S-P" or "R-P" => "A",
                "E" or "ERGEBNIS" or "ERGEBNISSATZ" or "ENDERGEBNIS" => "E",
                _ => a,
            };
        }

        private static string Text(JsonElement o, string name)
        {
            return o.TryGetProperty(name, out JsonElement e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
        }

        private static double? Zahl(JsonElement o, string name)
        {
            return o.TryGetProperty(name, out JsonElement e) && e.ValueKind == JsonValueKind.Number ? e.GetDouble() : null;
        }

        // =====================================================================================
        // Prüfung
        // =====================================================================================

        /// <summary>
        /// Behebt, was sich ohne Auslegung beheben laesst, und vermerkt jede Aenderung.
        /// Der haeufigste Fall: Das Modell setzt bei einer bepunkteten Position zusaetzlich
        /// das Kennzeichen x. Wuerde x gelten, fielen deren Punkte aus der Summe heraus.
        /// Maßgeblich ist deshalb m: Wer Punkte traegt, ist Position, keine Teilsumme.
        /// </summary>
        private static void Normalisiere(RpsDoc doc, List<string> meldungen, bool skalieren)
        {
            if (doc == null)
            {
                return;
            }

            foreach (RpsPosition p in doc.Positionen)
            {
                // Das Modell schreibt keine Teilsummenmarke mehr, sondern m = 0.
                if (p.M.HasValue && p.M.Value <= 0d)
                {
                    p.M = null;
                    p.X = "ja";

                    continue;
                }

                if (p.M.HasValue && p.M.Value > 0d && p.IstTeilsumme)
                {
                    p.X = null;
                    meldungen.Add(string.Format(CultureInfo.CurrentCulture,
                        "Warnung: {0} trug zugleich {1} Punkte und das Kennzeichen x; x wurde verworfen.",
                        p.Id, p.M.Value));
                }
                else if (!p.M.HasValue && !p.IstTeilsumme)
                {
                    p.X = "ja";
                    meldungen.Add("Warnung: " + p.Id + " hat keine Punkte; als Teilsumme geführt.");
                }
            }

            EntpunkteErgebniszeilen(doc, meldungen);

            // Positionen ohne zugehoerige Aufgabe wuerden im Bogen stillschweigend fehlen.
            foreach (string nr in doc.Positionen.Select(p => p.Aufgabe).Distinct().ToList())
            {
                if (string.IsNullOrWhiteSpace(nr) || doc.Aufgaben.Any(a => a.Nr == nr))
                {
                    continue;
                }

                doc.Aufgaben.Add(new RpsAufgabe { Nr = nr, T = "Aufgabe " + nr, M = 0d });
                meldungen.Add("Warnung: Aufgabe " + nr + " fehlte im JSON und wurde ergänzt.");
            }

            if (skalieren)
            {
                Skaliere(doc, meldungen);
            }

            foreach (RpsAufgabe a in doc.Aufgaben)
            {
                double teil = Teilsumme(doc, a.Nr);
                if (Math.Abs(teil - a.M) <= 1e-9)
                {
                    continue;
                }

                meldungen.Add(string.Format(CultureInfo.CurrentCulture,
                    "Warnung: Aufgabe {0}: angegebene Teilsumme {1}, tatsächlich {2}; korrigiert.",
                    a.Nr, a.M, teil));
                a.M = teil;
            }
        }

        /// <summary>
        /// Setzt Ergebniszeilen auf null Punkte und führt sie als Teilsumme. Ergebnisse sind
        /// keine Prüfungsleistung; bewertet wird die Begründung in den tragenden Positionen.
        /// Ausgenommen bleiben die strafrechtlichen Konkurrenzen, deren Einordnung eine eigene
        /// Leistung ist.
        /// </summary>
        private static void EntpunkteErgebniszeilen(RpsDoc doc, List<string> meldungen)
        {
            foreach (RpsPosition p in doc.Positionen)
            {
                if (p.IstTeilsumme)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(p.T) && Konkurrenzen.IsMatch(p.T))
                {
                    continue;
                }

                bool nachArt = string.Equals(p.A, "E", StringComparison.OrdinalIgnoreCase);

                if (!nachArt && !IstErgebniszeile(p.T))
                {
                    continue;
                }

                if (p.M.HasValue && p.M.Value > 0d)
                {
                    meldungen.Add(string.Format(CultureInfo.CurrentCulture,
                        "Warnung: Ergebniszeile {0} trug {1} Punkte; auf 0 gesetzt und als Teilsumme geführt.",
                        p.Id, p.M.Value));
                }

                p.M = null;
                p.X = "ja";
            }
        }

        /// <summary>
        /// Höchstpunkte, die eine Position bei der Skalierung erreichen darf. Ein Obersatz
        /// bleibt ein Obersatz: Wer 1 Punkt trug, erhält höchstens 1,5; wer 0,5 trug,
        /// höchstens 1. Was dadurch frei bleibt, fällt den tragenden Positionen zu.
        /// </summary>
        private static double Deckel(RpsPosition p)
        {
            double m = p.M ?? 0d;

            bool routine = string.IsNullOrWhiteSpace(p.A)
                        || string.Equals(p.A, "R", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(p.A, "S", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(p.A, "A", StringComparison.OrdinalIgnoreCase);

            return !routine ? SkalierungsDeckel : m <= 0.5d ? ObersatzDeckelHalb : m <= 1d ? ObersatzDeckelEins : SkalierungsDeckel;
        }

        /// <summary>
        /// Hebt die Höchstpunkte je Aufgabe anteilig auf ihr Soll an. Gerechnet wird in
        /// Vierteln; der Rundungsrest fällt nach dem Verfahren des größten Restes an die
        /// Positionen mit dem größten Rundungsverlust, bei Gleichstand an die gewichtigere.
        /// Wer seinen Deckel überschreitet, wird gekappt; der Überhang wird unter den
        /// übrigen erneut verteilt. Maßgeblich für das Soll je Aufgabe sind die angegebenen
        /// Teilsummen nur, wenn sie zusammen 100 ergeben UND jede eine Fundstelle (q) in
        /// der Lösung hat. Anteile, die das Modell ohne Vorgabe der Lösung gewählt hat,
        /// setzen sich nicht durch; dann folgt das Soll je Aufgabe aus ihren Positionen.
        /// </summary>
        private static void Skaliere(RpsDoc doc, List<string> meldungen)
        {
            List<RpsPosition> alle = doc.Positionen
                .Where(p => !p.IstTeilsumme && p.M.HasValue && p.M.Value > 0d).ToList();

            if (alle.Count == 0)
            {
                return;
            }

            double gesamt = alle.Sum(p => p.M.Value);
            if (gesamt <= 0d)
            {
                return;
            }

            double einheit = Einheit(alle.Count, PunkteSollSumme);

            bool summeFalsch = Math.Abs(gesamt - PunkteSollSumme) > 1e-9;
            bool bruchteile = alle.Any(p => Ungerade(p.M.Value, einheit));

            if (!summeFalsch && !bruchteile)
            {
                return;
            }

            bool teilungGilt = doc.Aufgaben.Count > 0
                && Math.Abs(doc.Aufgaben.Sum(a => a.M) - PunkteSollSumme) <= 1e-9
                && doc.Aufgaben.All(a => a.M > 0d && a.IstVorgabe);

            foreach (RpsAufgabe aufgabe in doc.Aufgaben)
            {
                List<RpsPosition> teil = alle.Where(p => p.Aufgabe == aufgabe.Nr).ToList();
                if (teil.Count == 0)
                {
                    continue;
                }

                double ziel = teilungGilt
                    ? aufgabe.M
                    : PunkteSollSumme * teil.Sum(p => p.M.Value) / gesamt;

                Verteile(teil, ziel, Einheit(teil.Count, ziel), meldungen);
            }

            meldungen.Add(string.Format(CultureInfo.CurrentCulture,
                summeFalsch
                    ? "Warnung: Summe der Höchstpunkte war {0}; auf {1} umgerechnet, in Schritten von {2}."
                    : "Hinweis: Summe war richtig, enthielt aber Bruchteile; auf Schritte von {2} geglättet.",
                gesamt, PunkteSollSumme, einheit));
        }

        private static void Verteile(
            List<RpsPosition> positionen, double ziel, double einheit, List<string> meldungen)
        {
            int rest = (int)Math.Round(ziel / einheit);

            // Die Rohwerte bleiben in Vierteln: Sie tragen das Verhältnis, nicht das Ergebnis.
            Dictionary<RpsPosition, int> roh =
                positionen.ToDictionary(p => p, p => (int)Math.Round(p.M.Value * 4d));

            Dictionary<RpsPosition, int> deckel = positionen.ToDictionary(
                p => p, p => Math.Max(1, (int)Math.Ceiling(Deckel(p) / einheit)));

            if (deckel.Values.Sum() < rest)
            {
                int weit = (int)Math.Ceiling(SkalierungsDeckel / einheit);
                foreach (RpsPosition p in positionen)
                {
                    deckel[p] = weit;
                }

                meldungen.Add("Warnung: Die Deckel der Obersätze reichten nicht aus; sie wurden aufgehoben.");
            }


            List<RpsPosition> offen = new(positionen);
            Dictionary<RpsPosition, int> fest = new();

            while (offen.Count > 0)
            {
                double basis = offen.Sum(p => (double)roh[p]);
                if (basis <= 0d)
                {
                    break;
                }

                Dictionary<RpsPosition, double> genau =
                    offen.ToDictionary(p => p, p => roh[p] * rest / basis);

                List<RpsPosition> ueber = offen.Where(p => genau[p] > deckel[p]).ToList();
                if (ueber.Count > 0)
                {
                    foreach (RpsPosition p in ueber) { fest[p] = deckel[p]; rest -= deckel[p]; _ = offen.Remove(p); }
                    continue;
                }

                Dictionary<RpsPosition, int> ganz = offen.ToDictionary(p => p, p => (int)Math.Floor(genau[p]));
                int luecke = rest - ganz.Values.Sum();

                List<RpsPosition> reihe = offen
                    .OrderByDescending(p => genau[p] - ganz[p])
                    .ThenByDescending(p => roh[p])
                    .ToList();

                while (luecke > 0)
                {
                    bool vergeben = false;
                    foreach (RpsPosition p in reihe)
                    {
                        if (luecke == 0)
                        {
                            break;
                        }

                        if (ganz[p] >= deckel[p])
                        {
                            continue;
                        }

                        ganz[p]++; luecke--; vergeben = true;
                    }
                    if (!vergeben)
                    {
                        break;
                    }
                }

                foreach (KeyValuePair<RpsPosition, int> kv in ganz)
                {
                    fest[kv.Key] = kv.Value;
                }

                break;
            }

            // Neu vor der Rückschreibung: keine Position unter einer Einheit; der Ausgleich
            // kommt von den größten Positionen, damit die Summe stimmt.
            foreach (RpsPosition p in positionen)
            {
                if (fest.TryGetValue(p, out int wert) && wert < 1)
                {
                    fest[p] = 1;
                }
            }

            int ueberhang = fest.Values.Sum() - (int)Math.Round(ziel / einheit);

            foreach (RpsPosition p in fest.Keys.OrderByDescending(k => fest[k]).ToList())
            {
                if (ueberhang <= 0)
                {
                    break;
                }

                if (fest[p] <= 1)
                {
                    continue;
                }

                fest[p]--; ueberhang--;
            }

            foreach (KeyValuePair<RpsPosition, int> kv in fest)
            {
                kv.Key.M = kv.Value * einheit;
            }
        }
        private static double Teilsumme(RpsDoc doc, string aufgabe)
        {
            return doc.Positionen
                .Where(p => p.Aufgabe == aufgabe && !p.IstTeilsumme)
                .Sum(p => p.M ?? 0d);
        }

        /// <summary>
        /// Prueft das Schema. "Fehler:" verhindert die Erstellung der Datei, "Warnung:"
        /// nicht – dort ist die Datei die bessere Grundlage der Nachbesserung als eine
        /// Meldung ohne Datei.
        /// </summary>
        private static void Pruefe(RpsDoc doc, List<string> meldungen)
        {
            if (doc == null) { meldungen.Add("Fehler: JSON leer oder nicht lesbar."); return; }
            if (doc.Aufgaben.Count == 0)
            {
                meldungen.Add("Fehler: Keine Aufgaben im JSON.");
            }

            if (doc.Positionen.Count == 0)
            {
                meldungen.Add("Fehler: Keine Positionen im JSON.");
            }

            if (doc.Positionen.Count == 0)
            {
                return;
            }

            double summe = 0d;
            HashSet<string> gesehen = new(StringComparer.Ordinal);

            foreach (RpsPosition p in doc.Positionen)
            {
                if (!gesehen.Add(p.Id))
                {
                    meldungen.Add("Fehler: Positions-ID doppelt: " + p.Id);
                }

                if (string.IsNullOrWhiteSpace(p.T))
                {
                    meldungen.Add("Warnung: Position ohne Titel: " + p.Id);
                }

                if (p.IstTeilsumme)
                {
                    continue;
                }

                summe += p.M ?? 0d;

                if (p.M.HasValue && p.M.Value > PunkteMax)
                {
                    meldungen.Add(string.Format(CultureInfo.CurrentCulture,
                        "Warnung: {0} trägt {1} Punkte, zulässig sind höchstens {2}.", p.Id, p.M.Value, PunkteMax));
                }

                if (p.M.HasValue && p.M.Value < 0d)
                {
                    meldungen.Add("Fehler: Negative Punkte: " + p.Id);
                }

                if (p.M.HasValue && Math.Abs((p.M.Value * 4d) - Math.Round(p.M.Value * 4d)) > 1e-9)
                {
                    meldungen.Add("Warnung: Punkte nicht in Schritten von 0,25: " + p.Id);
                }

                if (string.IsNullOrWhiteSpace(p.H))
                {
                    meldungen.Add("Warnung: Position ohne Bewertungshinweis: " + p.Id);
                }

                if (IstErgebniszeile(p.T))
                {
                    meldungen.Add("Warnung: Ergebniszeile trägt Punkte: " + p.Id + " – " + p.T);
                }
            }

            if (Math.Abs(summe - PunkteSollSumme) > 1e-9)
            {
                meldungen.Insert(0, string.Format(CultureInfo.CurrentCulture,
                    "Warnung: Summe der Höchstpunkte ist {0}, erwartet {1} – Abweichung {2}.",
                    summe, PunkteSollSumme, summe - PunkteSollSumme));
            }

            if (doc.Umrechnung.Count == 0)
            {
                meldungen.Add("Warnung: Umrechnungstabelle fehlt; die Tabelle der Vorlage bleibt stehen.");
            }

            int bepunktet = doc.Positionen.Count(p => !p.IstTeilsumme);
            if (doc.CN.HasValue && doc.CN.Value != bepunktet)
            {
                meldungen.Add(string.Format(CultureInfo.CurrentCulture,
                    "Warnung: Das Modell nennt {0} Positionen, bepunktet sind {1}.", doc.CN.Value, bepunktet));
            }

            if (doc.CS.HasValue && Math.Abs(doc.CS.Value - summe) > 1e-9)
            {
                meldungen.Add(string.Format(CultureInfo.CurrentCulture,
                    "Warnung: Das Modell gibt als Summe {0} an, tatsächlich sind es {1} – die Selbstkontrolle wurde nicht gerechnet.",
                    doc.CS.Value, summe));
            }
        }

        private static readonly Regex ErgebnisZeile = new(
            @"^(zwischen|end|gesamt|teil)?(ergebnis|resultat|fazit)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex Konkurrenzen = new(
            @"konkurrenz|tateinheit|tatmehrheit|gesetzeskonkurrenz",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex Gliederungskennung = new(
            @"^\s*(\(?[0-9]+\)?|[A-Za-z]{1,3}\)?|[IVXLC]{1,5}\.?)[\.\)]\s*",
            RegexOptions.CultureInvariant);

        private static bool IstErgebniszeile(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            if (Konkurrenzen.IsMatch(text))
            {
                return false;
            }

            string rumpf = Gliederungskennung.Replace(text.Trim(), string.Empty);
            int schnitt = rumpf.IndexOfAny(new[] { '(', '–', '-', ',', ':' });
            if (schnitt > 0)
            {
                rumpf = rumpf.Substring(0, schnitt);
            }

            return ErgebnisZeile.IsMatch(rumpf.Trim());
        }

        // =====================================================================================
        // Zeilenbau
        // =====================================================================================

        private static Row BaueAufgabenzeile(int index, RpsAufgabe aufgabe, Stil stil, int von, int bis, double breiteC)
        {
            Row r = new() { RowIndex = (uint)index };
            r.Append(FormelZelle("A" + index, stil.AufgabeA, string.Format("SUM(A{0}:A{1})", von, bis)));
            r.Append(FormelZelle("B" + index, stil.AufgabeA, string.Format("SUM(B{0}:B{1})", von, bis)));
            r.Append(ReichtextZelle("C" + index, stil.AufgabeC,
                "[" + aufgabe.Nr + "] " + aufgabe.T, null, null, FarbeAufgabe));
            r.Append(LeereZelle("D" + index, stil.AufgabeC));
            r.Height = Hoehe(aufgabe.T, null, breiteC);
            r.CustomHeight = true;
            return r;
        }

        private static readonly Regex Fallfragenzeile = new(
            @"^\s*(?:Frage|Fallfrage|Aufgabe|Teil|Tatkomplex|Abwandlung|Ausgangsfall)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>Oberster Gliederungsbuchstabe ("A. …") - Rückfall ohne Stufenangabe.</summary>
        private static readonly Regex Buchstabenebene = new(
            @"^\s*[A-Z]\.\s", RegexOptions.CultureInvariant);

        private static bool IstFallfragenzeile(string titel)
        {
            return !string.IsNullOrWhiteSpace(titel) && Fallfragenzeile.IsMatch(titel);
        }

        /// <summary>Überschrift der obersten Prüfungsebene unterhalb der Fallfragen ("A.", "B.", "C.").</summary>
        private static bool IstOberstePruefungsebene(RpsPosition pos, int obersteStufe)
        {
            return pos != null && !IstFallfragenzeile(pos.T) && (pos.Stufe > 0 && obersteStufe > 0
                ? pos.Stufe == obersteStufe
                : Buchstabenebene.IsMatch(pos.T ?? string.Empty));
        }

        private static Row BauePositionszeile(int index, RpsPosition pos, Stil stil,
                                              Dictionary<string, Zeilenformat> xfHervor,
                                              Zeilenformat xfUeberschrift, bool obersteEbene, double breiteC)
        {
            bool teilsumme = pos.IstTeilsumme;

            // Überschrift mit Unterpunkten auf der obersten Prüfungsebene (A., B., C.):
            // ohne Punkte und ohne Klasse. Tiefere Überschriften, Ergebniszeilen ("E")
            // und Mitglieder einer Sammelposition ([R]) bleiben ohne Füllung.
            bool ueberschrift = teilsumme && string.IsNullOrWhiteSpace(pos.A) && obersteEbene;

            Zeilenformat fuellung = null;
            if (ueberschrift)
            {
                fuellung = xfUeberschrift;
            }
            else if (!teilsumme && !string.IsNullOrWhiteSpace(pos.A))
            {
                _ = xfHervor.TryGetValue(pos.A, out fuellung);
            }

            // Alle vier Zellen der Zeile erhalten dieselbe Füllung - auch Spalte B
            // (erreichte Punkte) und D (Anmerkung des Korrektors).
            uint xfAB = fuellung != null ? fuellung.A : (teilsumme ? stil.TeilsummeA : stil.PositionA);
            uint xfC = fuellung != null ? fuellung.C : (teilsumme ? stil.TeilsummeC : stil.PositionC);
            uint xfD = fuellung != null ? fuellung.D : (teilsumme ? stil.TeilsummeD : stil.PositionD);

            Row r = new() { RowIndex = (uint)index };

            r.Append(teilsumme
                ? LeereZelle("A" + index, xfAB)
                : ZahlZelle("A" + index, xfAB, pos.M ?? 0d));
            r.Append(LeereZelle("B" + index, xfAB));
            r.Append(ReichtextZelle("C" + index, xfC,
                "[" + pos.Id + "] " + pos.T, Klartextmarken(pos), pos.H,
                ueberschrift ? FarbeUeberschrift : (teilsumme ? FarbeOhnePunkte : FarbeText), !teilsumme));
            r.Append(LeereZelle("D" + index, xfD));

            r.Height = Hoehe(pos.T, pos.H, breiteC);
            r.CustomHeight = true;
            return r;
        }

        /// <summary>Die Kennzeichen der Position als Klartextmarken mit ihrer Farbe, in fester Reihenfolge.</summary>
        private static List<KeyValuePair<string, string>> Klartextmarken(RpsPosition pos)
        {
            List<KeyValuePair<string, string>> marken = new();
            Ergaenze(marken, "a", pos.A);
            Ergaenze(marken, "v", pos.V);
            Ergaenze(marken, "k", pos.K);
            Ergaenze(marken, "b", pos.B);
            Ergaenze(marken, "w", pos.W);
            return marken;
        }

        private static void Ergaenze(List<KeyValuePair<string, string>> marken, string feld, string wert)
        {
            if (string.IsNullOrWhiteSpace(wert))
            {
                return;
            }

            Kennzeichen treffer = Karte.FirstOrDefault(k =>
                k.Feld == feld && string.Equals(k.Schluessel, wert, StringComparison.OrdinalIgnoreCase));

            string schluessel = feld + ":" + (treffer != null ? treffer.Schluessel : wert);
            if (!FarbeJeKennzeichen.TryGetValue(schluessel, out string farbe))
            {
                farbe = FarbeKennzeichen;
            }

            marken.Add(new KeyValuePair<string, string>(
                " [" + (treffer != null ? treffer.Klartext : wert) + "]", farbe));
        }

        // =====================================================================================
        // Zellen
        // =====================================================================================

        private static Cell LeereZelle(string reference, uint xf)
        {
            return new Cell { CellReference = reference, StyleIndex = xf };
        }

        private static Cell ZahlZelle(string reference, uint xf, double wert)
        {
            return new Cell
            {
                CellReference = reference,
                StyleIndex = xf,
                CellValue = new CellValue(wert.ToString("0.####", CultureInfo.InvariantCulture)),
            };
        }

        private static Cell FormelZelle(string reference, uint xf, string formel)
        {
            return new Cell { CellReference = reference, StyleIndex = xf, CellFormula = new CellFormula(formel) };
        }

        private static Cell TextZelle(string reference, uint xf, string text)
        {
            return new Cell
            {
                CellReference = reference,
                StyleIndex = xf,
                DataType = CellValues.InlineString,
                InlineString = new InlineString(new Text(text ?? string.Empty)
                {
                    Space = SpaceProcessingModeValues.Preserve,
                }),
            };
        }

        /// <summary>Aufgabenzeile: nur der Kopf.</summary>
        private static Cell ReichtextZelle(string reference, uint xf, string kopf,
                                           string marken, string hinweis, string farbeKopf)
        {
            List<KeyValuePair<string, string>> liste = string.IsNullOrEmpty(marken)
                ? null
                : new List<KeyValuePair<string, string>> { new(marken, FarbeKennzeichen) };
            return ReichtextZelle(reference, xf, kopf, liste, hinweis, farbeKopf, true);
        }

        /// <summary>
        /// Überschrift (10 pt; fett nur bei Zeilen mit Rohpunkten) + Kennzeichen in ihrer
        /// Farbe + Zeilenumbruch + Hinweis (8 pt, dunkelgrau).
        /// </summary>
        private static Cell ReichtextZelle(string reference, uint xf, string kopf,
                                           List<KeyValuePair<string, string>> marken, string hinweis,
                                           string farbeKopf, bool kopfFett)
        {
            InlineString ist = new();
            ist.Append(Lauf(kopf, 10d, farbeKopf, kopfFett));

            if (marken != null)
            {
                foreach (KeyValuePair<string, string> marke in marken)
                {
                    ist.Append(Lauf(marke.Key, 10d, marke.Value, true));
                }
            }

            if (!string.IsNullOrWhiteSpace(hinweis))
            {
                ist.Append(Lauf("\n(" + hinweis.Trim() + ")", 8d, FarbeHinweis, false));
            }

            return new Cell
            {
                CellReference = reference,
                StyleIndex = xf,
                DataType = CellValues.InlineString,
                InlineString = ist,
            };
        }

        private static Run Lauf(string text, double groesse, string argb, bool fett)
        {
            // Reihenfolge nach CT_RPrElt: rFont, b, color, sz
            RunProperties rp = new();
            rp.Append(new RunFont { Val = "Calibri" });
            if (fett)
            {
                rp.Append(new Bold());
            }

            rp.Append(new Color { Rgb = argb ?? FarbeText });
            rp.Append(new FontSize { Val = groesse });

            return new Run(rp, new Text(text) { Space = SpaceProcessingModeValues.Preserve });
        }

        // =====================================================================================
        // Umrechnungsblatt
        // =====================================================================================

        private static void SchreibeUmrechnung(WorkbookPart wbp, IReadOnlyList<double[]> paare)
        {
            if (paare == null || paare.Count == 0)
            {
                return;
            }

            WorksheetPart wsp = HoleBlatt(wbp, BlattUmrechnung);
            SheetData sd = wsp.Worksheet.GetFirstChild<SheetData>();

            for (int i = 0; i < paare.Count; i++)
            {
                int zeile = i + 2;                       // Zeile 1 ist der Tabellenkopf
                Row r = Zeile(sd, zeile);
                if (r == null)
                {
                    continue;                 // Vorlage kürzer als die Tabelle
                }

                SetzeZahl(sd, "A" + zeile, paare[i][0]);
                SetzeZahl(sd, "B" + zeile, paare[i][1]);

                double bis = i + 1 < paare.Count ? paare[i + 1][0] - 1 : 100d;
                SetzeText(sd, "C" + zeile, string.Format(CultureInfo.GetCultureInfo("de-DE"),
                    "{0} – {1}", paare[i][0], bis));
            }

            wsp.Worksheet.Save();
        }

        private static List<double[]> LiesUmrechnung(WorkbookPart wbp)
        {
            List<double[]> paare = new();
            SheetData sd = HoleBlatt(wbp, BlattUmrechnung).Worksheet.GetFirstChild<SheetData>();

            foreach (Row r in sd.Elements<Row>()
                         .Where(r => r.RowIndex != null && r.RowIndex.Value >= 2)
                         .OrderBy(r => r.RowIndex.Value))
            {
                double? schwelle = ZellZahl(Zelle(sd, "A" + r.RowIndex.Value), wbp);
                double? note = ZellZahl(Zelle(sd, "B" + r.RowIndex.Value), wbp);
                if (schwelle.HasValue && note.HasValue)
                {
                    paare.Add(new[] { schwelle.Value, note.Value });
                }
            }

            return paare;
        }

        // =====================================================================================
        // Hilfsfunktionen
        // =====================================================================================

        private static string Kopfzeile(string text)
        {
            int umbruch = (text ?? string.Empty).IndexOf('\n');
            return umbruch < 0 ? (text ?? string.Empty) : text.Substring(0, umbruch);
        }

        private static string Restzeilen(string text)
        {
            int umbruch = (text ?? string.Empty).IndexOf('\n');
            return umbruch < 0 ? string.Empty : text.Substring(umbruch + 1);
        }

        private static WorksheetPart HoleBlatt(WorkbookPart wbp, string name)
        {
            Sheet sheet = wbp.Workbook.Descendants<Sheet>()
                .FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.Ordinal));
            return sheet == null ? throw new InvalidDataException("Blatt \"" + name + "\" fehlt.") : (WorksheetPart)wbp.GetPartById(sheet.Id);
        }

        private static Row Zeile(SheetData sd, int index)
        {
            return sd.Elements<Row>().FirstOrDefault(r => r.RowIndex != null && r.RowIndex.Value == (uint)index);
        }

        private static Cell Zelle(SheetData sd, string reference)
        {
            return sd.Descendants<Cell>().FirstOrDefault(c =>
                string.Equals(c.CellReference, reference, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Text einer Zelle, gleich ob geteilte Zeichenkette, Reichtext oder einfacher Wert.</summary>
        private static string ZellText(Cell c, WorkbookPart wbp)
        {
            if (c == null)
            {
                return null;
            }

            if (c.DataType != null && c.DataType.Value == CellValues.SharedString)
            {
                SharedStringTablePart ssp = wbp.SharedStringTablePart;
                return ssp == null || c.CellValue == null ||
                    !int.TryParse(c.CellValue.InnerText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i)
                    ? null
                    : ssp.SharedStringTable.ElementAt(i).InnerText;
            }

            return c.InlineString != null ? c.InlineString.InnerText : (c.CellValue?.InnerText);
        }

        private static double? ZellZahl(Cell c, WorkbookPart wbp)
        {
            string t = ZellText(c, wbp);
            return double.TryParse(t, NumberStyles.Any, CultureInfo.InvariantCulture, out double wert)
                ? wert
                : null;
        }

        private static void SetzeText(SheetData sd, string reference, string text)
        {
            Cell c = Zelle(sd, reference);
            if (c == null)
            {
                return;
            }

            c.CellFormula = null;
            c.CellValue = null;
            c.DataType = CellValues.InlineString;
            c.InlineString = new InlineString(new Text(text ?? string.Empty)
            {
                Space = SpaceProcessingModeValues.Preserve,
            });
        }

        private static void SetzeZahl(SheetData sd, string reference, double wert)
        {
            Cell c = Zelle(sd, reference);
            if (c == null)
            {
                return;
            }

            c.CellFormula = null;
            c.InlineString = null;
            c.DataType = null;
            c.CellValue = new CellValue(wert.ToString("0.####", CultureInfo.InvariantCulture));
        }

        private static void SetzeFormel(SheetData sd, string reference, string formel)
        {
            Cell c = Zelle(sd, reference);
            if (c == null)
            {
                return;
            }

            c.DataType = null;
            c.InlineString = null;
            c.CellValue = null;
            c.CellFormula = new CellFormula(formel);
        }

        private static Row VerschiebeZeile(Row proto, int neuerIndex)
        {
            Row r = (Row)proto.CloneNode(true);
            string alt = r.RowIndex.Value.ToString(CultureInfo.InvariantCulture);
            string neu = neuerIndex.ToString(CultureInfo.InvariantCulture);
            r.RowIndex = (uint)neuerIndex;

            foreach (Cell c in r.Elements<Cell>())
            {
                if (c.CellReference == null)
                {
                    continue;
                }

                string s = c.CellReference.Value;
                c.CellReference = s.Substring(0, s.Length - alt.Length) + neu;
                if (c.CellFormula != null)
                {
                    c.CellValue = null;
                }
            }
            return r;
        }

        private static void Sortiere(SheetData sd)
        {
            List<Row> rows = sd.Elements<Row>()
                .OrderBy(r => r.RowIndex == null ? uint.MaxValue : r.RowIndex.Value)
                .ToList();
            foreach (Row r in rows)
            {
                r.Remove();
            }

            foreach (Row r in rows)
            {
                sd.Append(r);
            }
        }

        private static void VerschiebeVerbundzellen(Worksheet ws, int abZeile, int versatz)
        {
            MergeCells mc = ws.GetFirstChild<MergeCells>();
            if (mc == null || versatz == 0)
            {
                return;
            }

            foreach (MergeCell m in mc.Elements<MergeCell>())
            {
                string[] teile = m.Reference.Value.Split(':');
                if (teile.Length != 2)
                {
                    continue;
                }

                int z1 = ZeilenNummer(teile[0]);
                int z2 = ZeilenNummer(teile[1]);
                if (z1 < abZeile)
                {
                    continue;
                }

                m.Reference = SpaltenTeil(teile[0]) + (z1 + versatz) + ":" + SpaltenTeil(teile[1]) + (z2 + versatz);
            }
        }

        private static int ZeilenNummer(string reference)
        {
            return int.Parse(new string(reference.Where(char.IsDigit).ToArray()), CultureInfo.InvariantCulture);
        }

        private static string SpaltenTeil(string reference)
        {
            return new string(reference.TakeWhile(char.IsLetter).ToArray());
        }

        private static double SpaltenBreite(Worksheet ws, uint spalte, double standard)
        {
            Columns cols = ws.GetFirstChild<Columns>();
            if (cols == null)
            {
                return standard;
            }

            Column col = cols.Elements<Column>().FirstOrDefault(c =>
                c.Min != null && c.Max != null && c.Min.Value <= spalte && c.Max.Value >= spalte);

            return (col == null || col.Width == null) ? standard : col.Width.Value;
        }

        private static double Hoehe(string kopf, string hinweis, double breiteC)
        {
            double zeilen = Umbrueche(kopf, breiteC, 10d);
            if (!string.IsNullOrWhiteSpace(hinweis))
            {
                zeilen += Umbrueche("(" + hinweis + ")", breiteC, 8d) * 0.8d;
            }

            return Math.Max(15d, Math.Ceiling(zeilen * 14d) + 2d);
        }

        private static double Umbrueche(string text, double breiteC, double schriftgroesse)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 0d;
            }

            double zeichenProZeile = Math.Max(10d, breiteC * (11d / schriftgroesse));
            double n = 0d;
            foreach (string teil in text.Split('\n'))
            {
                n += Math.Max(1d, Math.Ceiling(teil.Length / zeichenProZeile));
            }

            return n;
        }

        /// <summary>
        /// Zieht die Bereiche der Vorlage (bedingte Formate, Eingabepruefungen) auf die
        /// tatsaechlichen Zeilen. Bisher blieben sie an den Zeilen der Vorlage stehen:
        /// Die Regel "Summe ungleich 100 -> rot" auf A79 faerbte so eine beliebige
        /// Positions- oder Ergebniszeile ein, die Pruefung der erreichten Punkte endete
        /// bei B78, und die Pruefungen fuer Bonus und Minus lagen auf fremden Zeilen.
        ///  * Zeilen ab der Summenzeile der Vorlage wandern um <paramref name="versatz"/>.
        ///  * Ein Bereich, der vor der Summenzeile endet (Positionsbereich "B5:B78"),
        ///    reicht bis zur Zeile vor der neuen Summenzeile.
        /// </summary>
        private static void VerschiebeBereiche(Worksheet ws, int zSumme, int versatz)
        {
            foreach (ConditionalFormatting cf in ws.Elements<ConditionalFormatting>().ToList())
            {
                string neu = VerschiebeSqref(cf.SequenceOfReferences?.InnerText, zSumme, versatz);
                if (neu == null)
                {
                    cf.Remove();
                }
                else
                {
                    cf.SequenceOfReferences = new ListValue<StringValue> { InnerText = neu };
                }
            }

            DataValidations dvs = ws.GetFirstChild<DataValidations>();
            if (dvs == null)
            {
                return;
            }

            foreach (DataValidation dv in dvs.Elements<DataValidation>().ToList())
            {
                string neu = VerschiebeSqref(dv.SequenceOfReferences?.InnerText, zSumme, versatz);
                if (neu == null)
                {
                    dv.Remove();
                }
                else
                {
                    dv.SequenceOfReferences = new ListValue<StringValue> { InnerText = neu };
                }
            }
            dvs.Count = (uint)dvs.ChildElements.Count;
            if (dvs.Count.Value == 0)
            {
                dvs.Remove();
            }
        }

        private static string VerschiebeSqref(string sqref, int zSumme, int versatz)
        {
            if (string.IsNullOrWhiteSpace(sqref))
            {
                return null;
            }

            List<string> teile = new();
            foreach (string bereich in sqref.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] ecken = bereich.Split(':');
                int z1 = ZeilenNummer(ecken[0]);
                int z2 = ecken.Length > 1 ? ZeilenNummer(ecken[1]) : z1;

                if (z1 >= ProtoSumme) { z1 += versatz; z2 += versatz; }
                else if (z2 >= ProtoSumme - 1 && z1 > ZeileKopf) { z2 = zSumme - 1; }
                else if (z2 >= ProtoSumme) { z2 += versatz; }

                teile.Add(ecken.Length > 1
                    ? SpaltenTeil(ecken[0]) + z1 + ":" + SpaltenTeil(ecken[1]) + z2
                    : SpaltenTeil(ecken[0]) + z1);
            }
            return teile.Count == 0 ? null : string.Join(" ", teile.ToArray());
        }

        private static void SetzeEingabepruefung(Worksheet ws, string reference, double min, double max)
        {
            DataValidations dvs = ws.GetFirstChild<DataValidations>();
            if (dvs == null)
            {
                dvs = new DataValidations();
                OpenXmlElement vorgaenger = (OpenXmlElement)ws.GetFirstChild<MergeCells>()
                                            ?? ws.GetFirstChild<SheetData>();
                _ = ws.InsertAfter(dvs, vorgaenger);
            }

            DataValidation dv = new()
            {
                Type = DataValidationValues.Decimal,
                Operator = DataValidationOperatorValues.Between,
                AllowBlank = true,
                ShowInputMessage = true,
                ShowErrorMessage = true,
                ErrorTitle = "Unzulässiger Wert",
                Error = string.Format(CultureInfo.CurrentCulture, "Zulässig sind Werte von {0} bis {1}.", min, max),
                SequenceOfReferences = new ListValue<StringValue> { InnerText = reference },
            };
            dv.Append(new Formula1(min.ToString(CultureInfo.InvariantCulture)));
            dv.Append(new Formula2(max.ToString(CultureInfo.InvariantCulture)));

            // Eine gleichlautende Pruefung der Vorlage (nach VerschiebeBereiche) ersetzen
            foreach (DataValidation alt in dvs.Elements<DataValidation>()
                         .Where(x => string.Equals(x.SequenceOfReferences?.InnerText, reference, StringComparison.OrdinalIgnoreCase))
                         .ToList())
            {
                alt.Remove();
            }
            dvs.Append(dv);
            dvs.Count = (uint)dvs.ChildElements.Count;
        }

        /// <summary>
        /// Grundformat ohne Füllung (Füllung 0 = "none", in jeder Arbeitsmappe vorhanden).
        /// Für Zeilen ohne Rohpunkte: Die Vorlage gibt Teilsummenzeilen je Spalte eine
        /// eigene Füllung, die Spalte Anmerkung wich deshalb farblich ab.
        /// </summary>
        private static uint ErzeugeOhneFuellung(WorkbookStylesPart styles, uint basisXf)
        {
            Stylesheet ss = styles.Stylesheet;
            CellFormats cfs = ss.CellFormats;
            CellFormat basis = (CellFormat)cfs.ElementAt((int)basisXf).CloneNode(true);
            basis.FillId = 0U;
            basis.ApplyFill = true;
            cfs.Append(basis);
            cfs.Count = (uint)cfs.ChildElements.Count;
            ss.Save();
            return (uint)(cfs.ChildElements.Count - 1);
        }

        private static uint ErzeugeFuellungsFormat(WorkbookStylesPart styles, uint basisXf, string argb)
        {
            Stylesheet ss = styles.Stylesheet;

            Fills fills = ss.Fills ??= new Fills();
            fills.Append(new Fill(new PatternFill
            {
                PatternType = PatternValues.Solid,
                ForegroundColor = new ForegroundColor { Rgb = argb },
                BackgroundColor = new BackgroundColor { Indexed = 64U },
            }));
            fills.Count = (uint)fills.ChildElements.Count;

            CellFormats cfs = ss.CellFormats;
            CellFormat basis = (CellFormat)cfs.ElementAt((int)basisXf).CloneNode(true);
            basis.FillId = (uint)(fills.ChildElements.Count - 1);
            basis.ApplyFill = true;
            cfs.Append(basis);
            cfs.Count = (uint)cfs.ChildElements.Count;

            ss.Save();
            return (uint)(cfs.ChildElements.Count - 1);
        }

        private static void EntferneBerechnungskette(WorkbookPart wbp)
        {
            CalculationChainPart cc = wbp.CalculationChainPart;
            if (cc != null)
            {
                _ = wbp.DeletePart(cc);
            }

            if (wbp.Workbook.CalculationProperties == null)
            {
                wbp.Workbook.CalculationProperties = new CalculationProperties();
            }

            wbp.Workbook.CalculationProperties.FullCalculationOnLoad = true;
            wbp.Workbook.CalculationProperties.ForceFullCalculation = true;
        }

        // ---- Stilprototypen ------------------------------------------------------------------

        private sealed class Stil
        {
            public uint AufgabeA, AufgabeC;
            public uint PositionA, PositionC, PositionD;
            public uint TeilsummeA, TeilsummeC, TeilsummeD;
            public uint SummeA, SummeC, SummeD;

            public static Stil AusVorlage(SheetData sd)
            {
                Row aufgabe = Erwarte(sd, ProtoAufgabe, "Aufgabenzeile");
                Row pos = Erwarte(sd, ProtoPosition, "Positionszeile");
                Row teil = Erwarte(sd, ProtoTeilsumme, "Teilsummenzeile");
                Row sum = Erwarte(sd, ProtoSumme, "Summenzeile");

                Stil s = new()
                {
                    AufgabeA = Xf(aufgabe, "A", 0U),
                    AufgabeC = Xf(aufgabe, "C", 0U),
                    PositionA = Xf(pos, "A", 0U),
                    PositionC = Xf(pos, "C", 0U)
                };
                s.PositionD = Xf(pos, "D", s.PositionC);
                s.TeilsummeA = Xf(teil, "A", 0U);
                s.TeilsummeC = Xf(teil, "C", 0U);
                s.TeilsummeD = Xf(teil, "D", s.TeilsummeC);
                s.SummeA = Xf(sum, "A", 0U);
                s.SummeC = Xf(sum, "C", 0U);
                s.SummeD = Xf(sum, "D", s.SummeC);
                return s;
            }

            private static Row Erwarte(SheetData sd, int index, string was)
            {
                Row r = RpsBuilder.Zeile(sd, index);
                return r ?? throw new InvalidDataException(string.Format(CultureInfo.CurrentCulture,
                        "Vorlage passt nicht: Zeile {0} ({1}) fehlt.", index, was));
            }

            private static uint Xf(Row r, string spalte, uint ersatz)
            {
                Cell c = r.Elements<Cell>().FirstOrDefault(x =>
                    x.CellReference != null &&
                    string.Equals(SpaltenTeil(x.CellReference.Value), spalte, StringComparison.OrdinalIgnoreCase));

                return (c == null || c.StyleIndex == null) ? ersatz : c.StyleIndex.Value;
            }
        }
    }
}