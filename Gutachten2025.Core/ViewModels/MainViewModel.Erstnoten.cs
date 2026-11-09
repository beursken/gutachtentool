using Gutachten2025.Models;
using Gutachten2025.Services;
using System;
using System.Collections.Generic;
using System.IO;

namespace Gutachten2025.ViewModels;

/// <summary>
/// Zweitgutachten: Erstnote zur eingegebenen Kennziffer anzeigen, bei großer Abweichung warnen und in der Notenliste
/// (Spalten „Erstgutachten“, „Differenz“) ausweisen. Quelle: Erstnoten.json im Klausurordner (Paket des KI-Portals).
/// </summary>
public sealed partial class MainViewModel
{
    Dictionary<string, int> _erstnoten = new(StringComparer.OrdinalIgnoreCase);

    public bool HatErstnoten => _erstnoten.Count > 0;

    /// <summary>Erstnote der aktuellen Kennziffer; null = keine.</summary>
    public int? Erstnote => _erstnoten.TryGetValue(Kennung, out int p) ? p : null;

    public bool HatErstnote => Erstnote is not null;

    public bool ErstnoteAbweichend => Erstnote is int e && Math.Abs(NotenIndex - e) >= Erstnoten.Warnschwelle;

    public string ErstnoteText => Erstnote is int e
        ? $"Erstgutachten: {Notenskala.MitPunkten(e)}" + (NotenIndex != e ? $" · Abweichung {NotenIndex - e:+0;−0} Punkte" : " · übereinstimmend")
        : HatErstnoten && Kennung.Length > 0 ? "Keine Erstnote zu dieser Kennziffer" : string.Empty;

    public bool ErstnoteSichtbar => ErstnoteText.Length > 0;

    void ErstnotenLaden()
    {
        _erstnoten = new(StringComparer.OrdinalIgnoreCase);
        if (KlausurName.Length > 0)
        {
            try
            {
                _erstnoten = Erstnoten.Laden(_ablage.KlausurOrdner(KlausurName));
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                Fehler(ex, "Erstnoten nicht lesbar", Erstnoten.Datei);
            }
        }
        OnPropertyChanged(nameof(HatErstnoten));
        ErstnoteAktualisieren();
    }

    void ErstnoteAktualisieren()
    {
        OnPropertyChanged(nameof(Erstnote));
        OnPropertyChanged(nameof(HatErstnote));
        OnPropertyChanged(nameof(ErstnoteAbweichend));
        OnPropertyChanged(nameof(ErstnoteText));
        OnPropertyChanged(nameof(ErstnoteSichtbar));
        ZweitkorrekturAktualisieren();
    }

    /// <summary>Zusatzspalten der Notenliste (nur wenn die Klausur Erstnoten hat; leer, wenn die Kennziffer fehlt).</summary>
    List<(string Kopf, double? Wert)> ErstnotenSpalten(int punkte)
    {
        if (!HatErstnoten) return new();
        int? e = Erstnote;
        return new()
        {
            ("Erstgutachten", e), ("Differenz", e is int x ? punkte - x : null),
            ("Einigungsnote", EinigungVollstaendig ? Einigungsnote : null),
        };
    }
}
