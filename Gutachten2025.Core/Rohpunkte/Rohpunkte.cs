using CommunityToolkit.Mvvm.ComponentModel;
using Gutachten2025.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Gutachten2025.Rohpunkte;

public enum Zeilenart { Aufgabe, Ueberschrift, Ergebnis, Position, Bonus, Minus }

/// <summary>Eine Zeile des Rohpunkte-Tabs: Aufgabe, Zwischenüberschrift, Position oder Bonus/Minus.</summary>
public sealed partial class RohpunktZeile : ObservableObject
{
    public const string BonusId = "+";
    public const string MinusId = "-";

    public RohpunktZeile(Zeilenart art, string id, string aufgabe, string titel, int stufe, double max, string? hinweis, string etiketten)
    {
        Art = art;
        Id = id;
        Aufgabe = aufgabe;
        Titel = titel;
        Stufe = stufe;
        Max = max;
        Hinweis = hinweis ?? string.Empty;
        Etiketten = etiketten;
        Punkte = double.NaN;
        Zwischensumme = string.Empty;
    }

    public Zeilenart Art { get; }
    public string Id { get; }
    public string Aufgabe { get; }
    public string Titel { get; }
    /// <summary>Gliederungsebene (0 = Aufgabe, 1 = A./B., 2 = I./II., 3 = 1./2., …).</summary>
    public int Stufe { get; }
    public double Max { get; }
    /// <summary>Bewertungshinweis – nur in der Oberfläche, nie in der Ausgabe.</summary>
    public string Hinweis { get; }
    /// <summary>Gewichtsklasse und Merker als Klartext (nur Oberfläche).</summary>
    public string Etiketten { get; }

    /// <summary>Erreichte Punkte; NaN = nicht eingetragen. Schritte 0,25, begrenzt auf 0 … Max.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Offen))]
    public partial double Punkte { get; set; }

    /// <summary>„x / y“ für Aufgaben und Zwischenüberschriften.</summary>
    [ObservableProperty]
    public partial string Zwischensumme { get; set; }

    partial void OnPunkteChanged(double value)
    {
        if (double.IsNaN(value)) return;
        double gerundet = Math.Clamp(Math.Round(value * 4, MidpointRounding.AwayFromZero) / 4, 0, Max);
        if (gerundet != value) Punkte = gerundet;
    }

    public double? Wert => double.IsNaN(Punkte) ? null : Punkte;

    /// <summary>Kommentar mit Fett/Kursiv.</summary>
    public List<TextLauf> Kommentar { get; private set; } = new();

    /// <summary>Vom Editor gemeldete Änderung.</summary>
    public event EventHandler? KommentarGeaendert;

    /// <summary>Vom Programm gesetzter Kommentar (z. B. beim Öffnen einer Arbeit): Editor muss neu laden.</summary>
    public event EventHandler? KommentarNeuGeladen;

    public void KommentarVomEditor(List<TextLauf> laeufe)
    {
        Kommentar = laeufe;
        KommentarGeaendert?.Invoke(this, EventArgs.Empty);
    }

    public void KommentarLaden(List<TextLauf>? laeufe)
    {
        Kommentar = laeufe ?? new();
        KommentarNeuGeladen?.Invoke(this, EventArgs.Empty);
    }

    public bool HatKommentar => Kommentar.Any(l => !string.IsNullOrWhiteSpace(l.T));

    // ---------- Darstellung ----------

    public bool IstEingabe => Art is Zeilenart.Position or Zeilenart.Bonus or Zeilenart.Minus;
    public bool IstKopf => Art is Zeilenart.Aufgabe or Zeilenart.Ueberschrift;
    public bool IstAufgabe => Art == Zeilenart.Aufgabe;
    public bool Offen => Art == Zeilenart.Position && double.IsNaN(Punkte);
    public string MaxText => Art switch
    {
        Zeilenart.Bonus => "von bis zu 10 (Bonus)",
        Zeilenart.Minus => "von bis zu 10 (Abzug)",
        _ => $"von {Zahl.Text(Max)}",
    };
    /// <summary>Linker Einzug in Pixeln (Oberfläche).</summary>
    public double EinrueckungLinks => Math.Max(0, Stufe - 1) * 16;
    /// <summary>Halbfett darstellen (Überschriften, Bonus, Abzug).</summary>
    public bool Fett => IstKopf || Art is Zeilenart.Bonus or Zeilenart.Minus;
    public bool HatHinweis => Hinweis.Length > 0;
    public bool HatEtiketten => Etiketten.Length > 0;
    public string Tooltip => HatHinweis ? $"{Titel}\n\nBewertungshinweis (nur für Sie sichtbar):\n{Hinweis}" : Titel;
}

/// <summary>Aufbau der Zeilen aus dem Schema, Summen, Note und Fingerabdruck.</summary>
public static class Rohpunkteschema
{
    public const string Dateiname = "rohpunkte.json";
    public const string Exceldatei = "rohpunkte.xlsx";

    static readonly (Regex Muster, int Stufe)[] Ebenen =
    {
        (new Regex(@"^[A-Z]\.\s", RegexOptions.CultureInvariant), 1),
        (new Regex(@"^[IVXLC]+\.\s", RegexOptions.CultureInvariant), 2),
        (new Regex(@"^\d+\.\s", RegexOptions.CultureInvariant), 3),
        (new Regex(@"^[a-z]\)\s", RegexOptions.CultureInvariant), 4),
        (new Regex(@"^[a-z]{2}\)\s", RegexOptions.CultureInvariant), 5),
        (new Regex(@"^\(\d+\)\s", RegexOptions.CultureInvariant), 6),
        (new Regex(@"^\([a-z]\)\s", RegexOptions.CultureInvariant), 7),
        (new Regex(@"^\([a-z]{2}\)\s", RegexOptions.CultureInvariant), 8),
    };

    static readonly Dictionary<string, string> Klassen = new(StringComparer.OrdinalIgnoreCase)
    {
        ["R"] = "Routine", ["S"] = "Standard", ["K"] = "Kernproblem", ["KS"] = "Klausurschwerpunkt", ["A"] = "anspruchsvoll", ["E"] = "Ergebnis",
    };

    /// <summary>Gliederungsebene aus dem Gliederungszeichen am Anfang des Titels; 0 = unbekannt.</summary>
    static int StufeAusTitel(string titel, bool buchstabenGesehen)
    {
        string t = titel.TrimStart();
        // „I.“, „V.“, „X.“ sind römisch, sobald auf dieser Aufgabe schon Buchstabenebenen (A., B.) vorkamen
        // oder kein Buchstabe davor in Frage kommt.
        if (Regex.IsMatch(t, @"^[IVX]\.\s") && buchstabenGesehen) return 2;
        foreach (var (muster, stufe) in Ebenen)
            if (muster.IsMatch(t)) return stufe;
        return 0;
    }

    static string EtikettenText(RpsPosition p)
    {
        var teile = new List<string>();
        if (!string.IsNullOrWhiteSpace(p.A) && Klassen.TryGetValue(p.A.Trim(), out string? k)) teile.Add(k);
        if (Ja(p.V)) teile.Add("a.A. vertretbar");
        else if (string.Equals(p.V, "nein", StringComparison.OrdinalIgnoreCase)) teile.Add("a.A. unvertretbar");
        if (Ja(p.K)) teile.Add("knapp genügt");
        if (Ja(p.B)) teile.Add("Bonus möglich");
        if (Ja(p.W)) teile.Add("Weichenstellung");
        return string.Join(" · ", teile);
    }

    static bool Ja(string? s) => string.Equals(s?.Trim(), "ja", StringComparison.OrdinalIgnoreCase);

    /// <summary>Sortierschlüssel „1.2“ &lt; „1.10“ &lt; „2.1“.</summary>
    static (int, int) Kennungsfolge(string? id)
    {
        string[] t = (id ?? string.Empty).Split('.');
        int a = t.Length > 0 && int.TryParse(t[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int x) ? x : int.MaxValue;
        int b = t.Length > 1 && int.TryParse(t[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int y) ? y : 0;
        return (a, b);
    }

    /// <summary>Baut die Zeilen in der Reihenfolge des Bewertungsbogens; am Ende Bonus und Minus.</summary>
    public static List<RohpunktZeile> Zeilen(RpsDoc doc)
    {
        var zeilen = new List<RohpunktZeile>();
        var aufgaben = doc.Aufgaben.Select(a => a.Nr).ToList();
        foreach (string fehlt in doc.Positionen.Select(p => p.Aufgabe).Where(a => a is not null && !aufgaben.Contains(a)).Distinct())
            aufgaben.Add(fehlt);

        foreach (string nr in aufgaben)
        {
            RpsAufgabe? a = doc.Aufgaben.FirstOrDefault(x => x.Nr == nr);
            var positionen = doc.Positionen.Where(p => p.Aufgabe == nr).OrderBy(p => Kennungsfolge(p.Id)).ToList();
            double max = positionen.Where(p => !p.IstTeilsumme).Sum(p => p.M ?? 0);
            zeilen.Add(new RohpunktZeile(Zeilenart.Aufgabe, nr, nr, a?.T is { Length: > 0 } t ? t : $"Aufgabe {nr}", 0, max, null, string.Empty));

            bool buchstaben = false;
            int letzteStufe = 1;
            foreach (RpsPosition p in positionen)
            {
                string titel = (p.T ?? string.Empty).Trim();
                int stufe = StufeAusTitel(titel, buchstaben);
                if (stufe == 1) buchstaben = true;
                bool ergebnis = string.Equals(p.A, "E", StringComparison.OrdinalIgnoreCase) && !(p.M > 0);
                Zeilenart art = p.IstTeilsumme || !p.M.HasValue ? (ergebnis ? Zeilenart.Ergebnis : Zeilenart.Ueberschrift)
                              : ergebnis ? Zeilenart.Ergebnis : Zeilenart.Position;
                if (stufe == 0) stufe = art == Zeilenart.Position || art == Zeilenart.Ergebnis ? letzteStufe + 1 : letzteStufe;
                if (art == Zeilenart.Ueberschrift) letzteStufe = stufe;
                else if (stufe <= letzteStufe) letzteStufe = stufe;
                zeilen.Add(new RohpunktZeile(art, p.Id, nr, titel, stufe, art == Zeilenart.Position ? p.M ?? 0 : 0, p.H, EtikettenText(p)));
            }
        }
        zeilen.Add(new RohpunktZeile(Zeilenart.Bonus, RohpunktZeile.BonusId, string.Empty, "Bonuspunkte", 1, RpsBuilder.ZuschlagMax, null, string.Empty));
        zeilen.Add(new RohpunktZeile(Zeilenart.Minus, RohpunktZeile.MinusId, string.Empty, "Minuspunkte", 1, RpsBuilder.ZuschlagMax, null, string.Empty));
        return zeilen;
    }

    /// <summary>Positionen unter einer Kopfzeile (bis zur nächsten Kopfzeile gleicher oder höherer Ebene bzw. Aufgabe).</summary>
    public static IEnumerable<RohpunktZeile> Unterpositionen(IList<RohpunktZeile> zeilen, int index)
    {
        RohpunktZeile kopf = zeilen[index];
        for (int i = index + 1; i < zeilen.Count; i++)
        {
            RohpunktZeile z = zeilen[i];
            if (z.Art is Zeilenart.Bonus or Zeilenart.Minus or Zeilenart.Aufgabe) yield break;
            if (kopf.Art == Zeilenart.Ueberschrift && z.Art == Zeilenart.Ueberschrift && z.Stufe <= kopf.Stufe) yield break;
            if (kopf.Art == Zeilenart.Ueberschrift && z.Art == Zeilenart.Position && z.Stufe <= kopf.Stufe) yield break;
            if (z.Art == Zeilenart.Position) yield return z;
        }
    }

    /// <summary>Setzt „x / y“ bei Aufgaben und Zwischenüberschriften (nur solche mit Positionen).</summary>
    public static void ZwischensummenSetzen(IList<RohpunktZeile> zeilen)
    {
        for (int i = 0; i < zeilen.Count; i++)
        {
            if (!zeilen[i].IstKopf) continue;
            var unter = Unterpositionen(zeilen, i).ToList();
            zeilen[i].Zwischensumme = unter.Count == 0
                ? string.Empty
                : $"{Zahl.Text(unter.Sum(z => z.Wert ?? 0))} / {Zahl.Text(unter.Sum(z => z.Max))}";
        }
    }

    public sealed record Summe(double Roh, double Max, double Bonus, double Minus, double Gesamt, bool HatEingaben, int Offen);

    public static Summe Berechnen(IEnumerable<RohpunktZeile> zeilen)
    {
        var liste = zeilen.ToList();
        var positionen = liste.Where(z => z.Art == Zeilenart.Position).ToList();
        double roh = positionen.Sum(z => z.Wert ?? 0);
        double max = positionen.Sum(z => z.Max);
        double bonus = liste.Where(z => z.Art == Zeilenart.Bonus).Sum(z => z.Wert ?? 0);
        double minus = liste.Where(z => z.Art == Zeilenart.Minus).Sum(z => z.Wert ?? 0);
        bool eingaben = liste.Any(z => z.IstEingabe && z.Wert is not null);
        return new Summe(roh, max, bonus, minus, roh + bonus - minus, eingaben, positionen.Count(z => z.Wert is null));
    }

    /// <summary>Notenpunkte nach der Umrechnung des Schemas (Schwelle „ab“, einschließlich); Gesamt unbegrenzt.</summary>
    public static int Note(RpsDoc doc, double gesamt)
    {
        var tabelle = (doc.Umrechnung is { Count: > 0 } u ? u : RpsBuilder.Standardumrechnung())
            .Where(p => p is { Length: >= 2 }).OrderBy(p => p[0]).ToList();
        int note = 0;
        foreach (double[] p in tabelle)
            if (gesamt + 1e-9 >= p[0]) note = (int)Math.Round(p[1]);
        return Math.Clamp(note, Notenskala.Minimum, Notenskala.Maximum);
    }

    /// <summary>Nächste Schwelle oberhalb der Note (für „bis x fehlen y“); null bei 18.</summary>
    public static double? NaechsteSchwelle(RpsDoc doc, double gesamt)
    {
        var tabelle = (doc.Umrechnung is { Count: > 0 } u ? u : RpsBuilder.Standardumrechnung())
            .Where(p => p is { Length: >= 2 }).OrderBy(p => p[0]).ToList();
        foreach (double[] p in tabelle)
            if (p[0] > gesamt + 1e-9) return p[0];
        return null;
    }

    /// <summary>Kennung von Positionen und Höchstpunkten – ändert sich, wenn das Schema geändert wurde.</summary>
    public static string Fingerabdruck(RpsDoc doc)
    {
        var sb = new StringBuilder();
        foreach (RpsPosition p in doc.Positionen.OrderBy(p => Kennungsfolge(p.Id)))
            sb.Append(p.Id).Append('=').Append((p.M ?? -1).ToString(CultureInfo.InvariantCulture)).Append(';');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())))[..16];
    }
}
