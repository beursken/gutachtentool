using Gutachten2025.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Gutachten2025.Services;

/// <summary>Liest und schreibt Klausur.json und wandelt die Konfiguration für den Bearbeitungsdialog in Text um.</summary>
public static class KlausurKonfigDatei
{
    internal static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Lädt die Konfiguration; fehlt die Datei, gilt der Standard. Ungültiges JSON löst eine Ausnahme aus.</summary>
    public static Klausurkonfiguration Laden(string pfad)
    {
        if (!File.Exists(pfad)) return Klausurkonfiguration.Standard();
        var k = JsonSerializer.Deserialize<Klausurkonfiguration>(Formulierungsdatei.TextLesen(pfad), Json)
                ?? Klausurkonfiguration.Standard();
        return Bereinigen(k);
    }

    /// <summary>Entfernt ein UTF-8-BOM (die Byte-Überladung von System.Text.Json überspringt es nicht).</summary>
    public static byte[] OhneBom(byte[] d) =>
        d.Length >= 3 && d[0] == 0xEF && d[1] == 0xBB && d[2] == 0xBF ? d[3..] : d;

    public static Klausurkonfiguration AusBytes(byte[] daten) =>
        Bereinigen(JsonSerializer.Deserialize<Klausurkonfiguration>(OhneBom(daten), Json)
                   ?? throw new InvalidDataException("Klausur.json ist leer."));

    public static void Speichern(string pfad, Klausurkonfiguration k)
    {
        string tmp = pfad + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(Bereinigen(k), Json), new UTF8Encoding(false));
        File.Move(tmp, pfad, overwrite: true);
    }

    static Klausurkonfiguration Bereinigen(Klausurkonfiguration k)
    {
        k.Aufgaben = (k.Aufgaben ?? new()).Where(a => !string.IsNullOrWhiteSpace(a.Name))
            .Select(a => new Aufgabe { Name = a.Name.Trim(), Hoechstpunkte = Math.Max(0, a.Hoechstpunkte) }).ToList();
        k.Formalkriterien = (k.Formalkriterien ?? new()).Where(f => !string.IsNullOrWhiteSpace(f.Name))
            .Select(f => new Formalkriterium
            {
                Name = f.Name.Trim(),
                Vorlage = string.IsNullOrWhiteSpace(f.Vorlage) ? "{subjekt} {wert}." : f.Vorlage.Trim(),
                Stufen = (f.Stufen ?? new()).Select(s => s.Trim()).Where(s => s.Length > 0).ToList(),
            }).ToList();
        if (k.Notenschluessel is { Count: not 18 }) k.Notenschluessel = null;
        return k;
    }

    // ---------- Textdarstellung für den Dialog ----------

    public static string AufgabenText(Klausurkonfiguration k) =>
        string.Join(Environment.NewLine, k.Aufgaben.Select(a => a.Hoechstpunkte > 0 ? $"{a.Name}; {Zahl.Text(a.Hoechstpunkte)}" : a.Name));

    /// <summary>Je Zeile „Bezeichnung; Höchstpunkte“ (Höchstpunkte optional).</summary>
    public static List<Aufgabe> AufgabenLesen(string text)
    {
        var liste = new List<Aufgabe>();
        foreach (string roh in Zeilen(text))
        {
            int i = roh.LastIndexOf(';');
            string name = i >= 0 ? roh[..i].Trim() : roh;
            double max = 0;
            if (i >= 0 && !Zahl.Lesen(roh[(i + 1)..], out max))
                throw new FormatException($"Aufgaben: „{roh}“ – nach dem Semikolon muss eine Zahl stehen.");
            if (name.Length == 0) throw new FormatException($"Aufgaben: „{roh}“ – Bezeichnung fehlt.");
            if (liste.Any(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)))
                throw new FormatException($"Aufgaben: „{name}“ ist doppelt.");
            liste.Add(new Aufgabe { Name = name, Hoechstpunkte = Math.Max(0, max) });
        }
        return liste;
    }

    public static string KriterienText(Klausurkonfiguration k)
    {
        var sb = new StringBuilder();
        foreach (Formalkriterium f in k.Formalkriterien)
        {
            sb.Append('+').Append(f.Name).Append(" | ").AppendLine(f.Vorlage);
            foreach (string s in f.Stufen) sb.AppendLine(s);
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>„+Name | Satzvorlage“, darunter je Zeile eine Stufe.</summary>
    public static List<Formalkriterium> KriterienLesen(string text)
    {
        var liste = new List<Formalkriterium>();
        Formalkriterium? aktuell = null;
        foreach (string zeile in Zeilen(text))
        {
            if (zeile.StartsWith('+'))
            {
                string kopf = zeile[1..];
                int i = kopf.IndexOf('|');
                string name = (i >= 0 ? kopf[..i] : kopf).Trim();
                string vorlage = i >= 0 ? kopf[(i + 1)..].Trim() : "{subjekt} {wert}.";
                if (name.Length == 0) throw new FormatException("Formalkriterien: Nach „+“ fehlt die Bezeichnung.");
                if (!vorlage.Contains("{wert}", StringComparison.Ordinal))
                    throw new FormatException($"Formalkriterien: Die Satzvorlage für „{name}“ muss {{wert}} enthalten.");
                if (liste.Any(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)))
                    throw new FormatException($"Formalkriterien: „{name}“ ist doppelt.");
                aktuell = new Formalkriterium { Name = name, Vorlage = vorlage.Length > 0 ? vorlage : "{subjekt} {wert}." };
                liste.Add(aktuell);
            }
            else if (aktuell is null)
                throw new FormatException($"Formalkriterien: „{zeile}“ steht vor dem ersten „+Kriterium“.");
            else aktuell.Stufen.Add(zeile);
        }
        return liste;
    }

    public static string SchluesselText(Klausurkonfiguration k) =>
        k.Notenschluessel is { Count: 18 } s ? string.Join("; ", s.Select(Zahl.Text)) : string.Empty;

    /// <summary>Leer oder genau 18 aufsteigende Mindest-Rohpunktzahlen für 1 … 18 Punkte.</summary>
    public static List<double>? SchluesselLesen(string text)
    {
        string[] teile = text.Split(new[] { ';', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (teile.Length == 0) return null;
        if (teile.Length != 18) throw new FormatException($"Notenschlüssel: Es werden genau 18 Werte (für 1 bis 18 Punkte) benötigt, eingegeben sind {teile.Length}.");
        var werte = new List<double>();
        foreach (string t in teile)
        {
            if (!Zahl.Lesen(t, out double w)) throw new FormatException($"Notenschlüssel: „{t}“ ist keine Zahl.");
            if (werte.Count > 0 && w < werte[^1]) throw new FormatException("Notenschlüssel: Die Werte müssen aufsteigen.");
            werte.Add(w);
        }
        return werte;
    }

    static IEnumerable<string> Zeilen(string text) =>
        text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Select(z => z.Trim()).Where(z => z.Length > 0);
}
