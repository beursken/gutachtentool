using System.Globalization;
using Gutachten2025.Models;
using Gutachten2025.Services;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;

namespace Gutachtentool.Oberflaeche.Dienste;

/// <summary>Meldung am unteren Rand (wie die InfoBar der Windows-Fassung).</summary>
public sealed record Meldung(Meldungsart Art, string Titel, string Text, string? Aktion, Action? BeiAktion)
{
    public Guid Id { get; } = Guid.NewGuid();
}

/// <summary>Offener Dialog; die Oberfläche (Dialoge.razor) zeigt ihn an und schließt ihn über <see cref="Ende"/>.</summary>
public abstract class Dialog
{
    public string Titel { get; init; } = string.Empty;
}

/// <summary>Frage mit bis zu drei Schaltflächen; Ergebnis = Index der Schaltfläche, -1 = Abbrechen.</summary>
public sealed class FrageDialog : Dialog
{
    public string Text { get; init; } = string.Empty;
    public IReadOnlyList<string> Knoepfe { get; init; } = Array.Empty<string>();
    public TaskCompletionSource<int> Ende { get; } = new();
}

public sealed class EingabeDialog : Dialog
{
    public string Kopf { get; init; } = string.Empty;
    public string Wert { get; set; } = string.Empty;
    public TaskCompletionSource<string?> Ende { get; } = new();
}

public sealed class BausteinDialog : Dialog
{
    public string Text { get; set; } = string.Empty;
    public string Kuerzel { get; set; } = string.Empty;
    public string Von { get; set; } = string.Empty;
    public string Bis { get; set; } = string.Empty;
    public string? Fehler { get; set; }
    public Func<BausteinDaten, string?> Pruefen { get; init; } = _ => null;
    public TaskCompletionSource<BausteinDaten?> Ende { get; } = new();
}

public sealed class KonfigurationDialog : Dialog
{
    public bool TeilpunkteAusgeben { get; set; }
    public bool BogenPunkteAusgeben { get; set; }
    public string Aufgaben { get; set; } = string.Empty;
    public string Schluessel { get; set; } = string.Empty;
    public string Kriterien { get; set; } = string.Empty;
    public string? Fehler { get; set; }
    public TaskCompletionSource<Klausurkonfiguration?> Ende { get; } = new();
}

public sealed class DateiDialog : Dialog
{
    public IReadOnlyList<string> Endungen { get; init; } = Array.Empty<string>();
    public string? Fehler { get; set; }
    public bool Laedt { get; set; }
    public TaskCompletionSource<string?> Ende { get; } = new();
}

/// <summary>
/// IUiService der Blazor-Oberfläche (Browser und Mac): Dialoge und Meldungen in der Seite;
/// Dateiauswahl, Speichern und Öffnen über die <see cref="IPlattform"/>.
/// </summary>
public sealed class UiDienst : IUiService
{
    /// <summary>Übergabeordner für hochgeladene Dateien (Browser).</summary>
    public const string Eingang = "/tmp/gt-eingang";

    readonly IJSRuntime _js;
    readonly IPlattform _plattform;

    public UiDienst(IJSRuntime js, IPlattform plattform)
    {
        _js = js;
        _plattform = plattform;
    }

    /// <summary>Löst ein Neuzeichnen der Seite aus.</summary>
    public event Action? Geaendert;

    /// <summary>Wechsel zum Reiter „Rohpunkte“.</summary>
    public event Action? RohpunkteGewuenscht;

    public List<Meldung> Meldungen { get; } = new();

    public Dialog? Offen { get; private set; }

    void Melde() => Geaendert?.Invoke();

    // ---------- Meldungen ----------

    public void Melden(Meldungsart art, string titel, string text, string? aktion = null, Action? beiAktion = null)
    {
        var m = new Meldung(art, titel, text, aktion, beiAktion);
        Meldungen.RemoveAll(x => x.Titel == titel && x.Text == text);
        Meldungen.Add(m);
        if (Meldungen.Count > 4) Meldungen.RemoveAt(0);
        Melde();
        if (art is Meldungsart.Information or Meldungsart.Erfolg)
            _ = SpaeterSchliessenAsync(m, aktion is null ? 6000 : 12000);
    }

    async Task SpaeterSchliessenAsync(Meldung m, int ms)
    {
        await Task.Delay(ms);
        if (Meldungen.Remove(m)) Melde();
    }

    public void MeldungSchliessen(Meldung m)
    {
        if (Meldungen.Remove(m)) Melde();
    }

    // ---------- Dialoge ----------

    async Task<T> ZeigenAsync<T>(Dialog d, Task<T> ende)
    {
        // Ein Dialog zur Zeit; ein noch offener wird abgebrochen.
        if (Offen is not null) Abbrechen();
        Offen = d;
        Melde();
        try
        {
            return await ende;
        }
        finally
        {
            if (ReferenceEquals(Offen, d)) Offen = null;
            Melde();
        }
    }

    /// <summary>Schließt den offenen Dialog ohne Ergebnis.</summary>
    public void Abbrechen()
    {
        switch (Offen)
        {
            case FrageDialog f: f.Ende.TrySetResult(-1); break;
            case EingabeDialog e: e.Ende.TrySetResult(null); break;
            case BausteinDialog b: b.Ende.TrySetResult(null); break;
            case KonfigurationDialog k: k.Ende.TrySetResult(null); break;
            case DateiDialog d: d.Ende.TrySetResult(null); break;
        }
    }

    public async Task<bool> BestaetigenAsync(string titel, string text, string ja, string nein)
    {
        var d = new FrageDialog { Titel = titel, Text = text, Knoepfe = new[] { ja, nein } };
        return await ZeigenAsync(d, d.Ende.Task) == 0;
    }

    public async Task<int> AuswahlAsync(string titel, string text, string erste, string zweite, string abbrechen)
    {
        var d = new FrageDialog { Titel = titel, Text = text, Knoepfe = new[] { erste, zweite, abbrechen } };
        return await ZeigenAsync(d, d.Ende.Task) switch { 0 => 1, 1 => 2, _ => 0 };
    }

    public Task<string?> TextEingebenAsync(string titel, string vorgabe, string kopf = "Textbaustein")
    {
        var d = new EingabeDialog { Titel = titel, Kopf = kopf, Wert = vorgabe };
        return ZeigenAsync(d, d.Ende.Task);
    }

    public Task<BausteinDaten?> BausteinBearbeitenAsync(string titel, BausteinDaten vorgabe, Func<BausteinDaten, string?> pruefen)
    {
        var d = new BausteinDialog
        {
            Titel = titel,
            Text = vorgabe.Text,
            Kuerzel = vorgabe.Kuerzel,
            Von = vorgabe.Von?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            Bis = vorgabe.Bis?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            Pruefen = pruefen,
        };
        return ZeigenAsync(d, d.Ende.Task);
    }

    /// <summary>Übernimmt die Eingaben des Bausteindialogs; false bei Fehlern (Meldung im Dialog).</summary>
    public bool BausteinUebernehmen(BausteinDialog d)
    {
        static bool Punkte(string s, out int? wert)
        {
            wert = null;
            if (string.IsNullOrWhiteSpace(s)) return true;
            if (int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int w) && Notenskala.IstGueltig(w))
            {
                wert = w;
                return true;
            }
            return false;
        }
        if (!Punkte(d.Von, out int? von) || !Punkte(d.Bis, out int? bis))
        {
            d.Fehler = "Punkte bitte als ganze Zahl von 0 bis 18 angeben.";
            return false;
        }
        var daten = new BausteinDaten(d.Text.Trim(), d.Kuerzel.Trim().TrimStart('#'), von, bis);
        d.Fehler = d.Pruefen(daten);
        if (d.Fehler is not null) return false;
        d.Ende.TrySetResult(daten);
        return true;
    }

    public Task<Klausurkonfiguration?> KonfigurationBearbeitenAsync(string klausur, Klausurkonfiguration vorgabe)
    {
        var d = new KonfigurationDialog
        {
            Titel = $"Klausureinstellungen: {klausur}",
            TeilpunkteAusgeben = vorgabe.TeilpunkteAusgeben,
            BogenPunkteAusgeben = vorgabe.BogenPunkteAusgeben,
            Aufgaben = KlausurKonfigDatei.AufgabenText(vorgabe),
            Schluessel = KlausurKonfigDatei.SchluesselText(vorgabe),
            Kriterien = KlausurKonfigDatei.KriterienText(vorgabe),
        };
        return ZeigenAsync(d, d.Ende.Task);
    }

    /// <summary>Übernimmt die Eingaben des Konfigurationsdialogs; false bei Formatfehlern.</summary>
    public bool KonfigurationUebernehmen(KonfigurationDialog d)
    {
        try
        {
            d.Ende.TrySetResult(new Klausurkonfiguration
            {
                TeilpunkteAusgeben = d.TeilpunkteAusgeben,
                BogenPunkteAusgeben = d.BogenPunkteAusgeben,
                Aufgaben = KlausurKonfigDatei.AufgabenLesen(d.Aufgaben),
                Notenschluessel = KlausurKonfigDatei.SchluesselLesen(d.Schluessel),
                Formalkriterien = KlausurKonfigDatei.KriterienLesen(d.Kriterien),
            });
            return true;
        }
        catch (FormatException ex)
        {
            d.Fehler = ex.Message;
            return false;
        }
    }

    // ---------- Dateien ----------

    public Task<string?> DateiWaehlenAsync(params string[] endungen)
    {
        if (!_plattform.ImBrowser) return _plattform.DateiWaehlenAsync(endungen);
        var d = new DateiDialog { Titel = "Datei hochladen", Endungen = endungen };
        return ZeigenAsync(d, d.Ende.Task);
    }

    /// <summary>Legt eine hochgeladene Datei im Übergabeordner ab und schließt den Dateidialog.</summary>
    public async Task DateiAnnehmenAsync(DateiDialog d, IBrowserFile datei)
    {
        string endung = Path.GetExtension(datei.Name);
        if (d.Endungen.Count > 0 && !d.Endungen.Any(e => e.Equals(endung, StringComparison.OrdinalIgnoreCase)))
        {
            d.Fehler = $"Erwartet wird eine Datei mit der Endung {string.Join(", ", d.Endungen)}.";
            Melde();
            return;
        }
        d.Laedt = true;
        d.Fehler = null;
        Melde();
        try
        {
            Directory.CreateDirectory(Eingang);
            string ziel = Path.Combine(Eingang, KlausurAblage.SicherName(Path.GetFileNameWithoutExtension(datei.Name)) + endung);
            await using (Stream quelle = datei.OpenReadStream(maxAllowedSize: 200L * 1024 * 1024))
            await using (FileStream f = File.Create(ziel))
                await quelle.CopyToAsync(f);
            d.Ende.TrySetResult(ziel);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            d.Laedt = false;
            d.Fehler = ex.Message;
            Melde();
        }
    }

    public Task<string?> SpeicherortWaehlenAsync(string vorschlag, string endung, string bezeichnung) =>
        _plattform.SpeicherortWaehlenAsync(vorschlag, endung, bezeichnung);

    public Task<string?> OrdnerWaehlenAsync() => _plattform.OrdnerWaehlenAsync();

    public void Oeffnen(string pfad) => _ = OeffnenAsync(pfad);

    async Task OeffnenAsync(string pfad)
    {
        try
        {
            await _plattform.OeffnenAsync(pfad);
        }
        catch (Exception ex)
        {
            Melden(Meldungsart.Fehler, "Nicht geöffnet", ex.Message);
        }
    }

    /// <summary>Daten als Datei ausgeben (Browser: herunterladen; Mac: speichern).</summary>
    public async Task AusgebenAsync(string name, byte[] daten)
    {
        try
        {
            await _plattform.AusgebenAsync(name, daten);
        }
        catch (Exception ex)
        {
            Melden(Meldungsart.Fehler, "Nicht gespeichert", ex.Message);
        }
    }

    /// <summary>Drucken über Word gibt es nur unter Windows (die Schaltfläche fehlt hier).</summary>
    public bool Drucken(string pfad) => false;

    public void Fokussieren(Eingabefeld feld)
    {
        string id = feld switch
        {
            Eingabefeld.Klausur => "f-klausur",
            Eingabefeld.Student => "f-student",
            Eingabefeld.Matrikel => "f-matrikel",
            Eingabefeld.Note => "f-note",
            Eingabefeld.Suche => "f-suche",
            _ => EntwurfEditor.ElementId,
        };
        _ = FokusAsync(id);
    }

    async Task FokusAsync(string id)
    {
        try { await _js.InvokeVoidAsync("gutachtentool.fokus", id); }
        catch (JSException) { }
    }

    public void RohpunkteZeigen() => RohpunkteGewuenscht?.Invoke();
}
