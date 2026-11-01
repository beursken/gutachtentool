using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Gutachten2025.Services;

/// <summary>Bearbeitungszeit einer Arbeit: Beginn, Ende (Erstellen des Gutachtens) und aktive Dauer.</summary>
public sealed class Korrekturzeit
{
    public string Kennung { get; set; } = string.Empty;
    public DateTime Beginn { get; set; }
    public DateTime Ende { get; set; }
    /// <summary>Aktive Bearbeitungszeit in Sekunden (Pausen über drei Minuten zählen nicht).</summary>
    public double Sekunden { get; set; }

    public TimeSpan Dauer => TimeSpan.FromSeconds(Sekunden);
}

/// <summary>&lt;Klausur&gt;\Korrekturzeiten.json (nur lokal; nie Teil eines Pakets).</summary>
public static class Korrekturzeiten
{
    public const string Datei = "Korrekturzeiten.json";

    /// <summary>Längere Pausen ohne Eingabe zählen nicht zur Bearbeitungszeit.</summary>
    public static readonly TimeSpan Leerlauf = TimeSpan.FromMinutes(3);

    public static Dictionary<string, Korrekturzeit> Laden(string klausurOrdner)
    {
        string pfad = Path.Combine(klausurOrdner, Datei);
        var d = new Dictionary<string, Korrekturzeit>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(pfad)) return d;
        try
        {
            foreach (Korrekturzeit z in JsonSerializer.Deserialize<List<Korrekturzeit>>(File.ReadAllText(pfad, Encoding.UTF8), KlausurKonfigDatei.Json) ?? new())
                if (z.Kennung.Length > 0) d[z.Kennung] = z;
        }
        catch (JsonException ex)
        {
            EinstellungsSpeicher.Protokollieren(ex);
        }
        return d;
    }

    /// <summary>Trägt eine Arbeit ein; bei erneutem Erstellen bleibt der erste Beginn, die Dauer wird addiert.</summary>
    public static Korrekturzeit Eintragen(string klausurOrdner, string kennung, DateTime beginn, DateTime ende, double sekunden)
    {
        Dictionary<string, Korrekturzeit> alle = Laden(klausurOrdner);
        if (alle.TryGetValue(kennung, out Korrekturzeit? z))
        {
            z.Beginn = z.Beginn == default || beginn < z.Beginn ? beginn : z.Beginn;
            z.Ende = ende;
            z.Sekunden += Math.Max(0, sekunden);
        }
        else
        {
            z = new Korrekturzeit { Kennung = kennung, Beginn = beginn, Ende = ende, Sekunden = Math.Max(0, sekunden) };
            alle[kennung] = z;
        }
        string pfad = Path.Combine(klausurOrdner, Datei);
        string tmp = pfad + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(alle.Values.OrderBy(x => x.Beginn).ToList(), KlausurKonfigDatei.Json), new UTF8Encoding(false));
        File.Move(tmp, pfad, overwrite: true);
        return z;
    }

    /// <summary>Median der Dauer (robust gegen einzelne lange Arbeiten); null ohne Daten.</summary>
    public static TimeSpan? Median(IEnumerable<Korrekturzeit> zeiten)
    {
        List<double> s = zeiten.Where(z => z.Sekunden >= 30).Select(z => z.Sekunden).OrderBy(x => x).ToList();
        if (s.Count == 0) return null;
        double m = s.Count % 2 == 1 ? s[s.Count / 2] : (s[s.Count / 2 - 1] + s[s.Count / 2]) / 2;
        return TimeSpan.FromSeconds(m);
    }

    public static string Text(TimeSpan t) =>
        t.TotalMinutes < 1 ? "unter 1 Min."
        : t.TotalHours < 1 ? $"{Math.Round(t.TotalMinutes):0} Min."
        : $"{(int)t.TotalHours} Std. {t.Minutes} Min.";
}
