using Gutachten2025.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Gutachten2025.Services;

/// <summary>Kennzahlen der bisher bewerteten Arbeiten.</summary>
public static class Statistik
{
    public const int Bestehensgrenze = 4;
    public const int Vollbefriedigend = 10;

    public static List<Kennzahl> Kennzahlen(IReadOnlyList<int> punkte, int teilnehmende)
    {
        var liste = new List<Kennzahl>();
        int n = punkte.Count;
        liste.Add(new("Bewertet", teilnehmende > 0 ? $"{n} von {teilnehmende}" : n.ToString()));
        if (teilnehmende > 0) liste.Add(new("Offen", Math.Max(0, teilnehmende - n).ToString()));
        if (n == 0) return liste;

        double schnitt = punkte.Average();
        var sortiert = punkte.OrderBy(p => p).ToList();
        double median = n % 2 == 1 ? sortiert[n / 2] : (sortiert[n / 2 - 1] + sortiert[n / 2]) / 2.0;
        double streuung = Math.Sqrt(punkte.Sum(p => (p - schnitt) * (p - schnitt)) / n);
        int durchgefallen = punkte.Count(p => p < Bestehensgrenze);
        int vb = punkte.Count(p => p >= Vollbefriedigend);

        liste.Add(new("Durchschnitt", $"{Zahl.Text(Math.Round(schnitt, 2))} Punkte"));
        liste.Add(new("Median", Zahl.Text(median)));
        liste.Add(new("Standardabweichung", Zahl.Text(Math.Round(streuung, 2))));
        liste.Add(new("Nicht bestanden (0–3)", $"{durchgefallen} ({Zahl.Prozent((double)durchgefallen / n)})"));
        liste.Add(new("Ab vollbefriedigend (≥ 10)", $"{vb} ({Zahl.Prozent((double)vb / n)})"));
        liste.Add(new("Beste / schwächste", $"{sortiert[^1]} / {sortiert[0]}"));
        return liste;
    }

    public static List<VerteilungsBalken> Verteilung(IReadOnlyList<int> punkte, double hoehe)
    {
        int[] anzahl = new int[Notenskala.Maximum + 1];
        foreach (int p in punkte)
            if (Notenskala.IstGueltig(p)) anzahl[p]++;
        int max = Math.Max(1, anzahl.Max());
        return Enumerable.Range(Notenskala.Minimum, Notenskala.Maximum + 1)
            .Select(p => new VerteilungsBalken(p, anzahl[p], anzahl[p] == 0 ? 1 : Math.Max(3, hoehe * anzahl[p] / max)))
            .ToList();
    }
}
