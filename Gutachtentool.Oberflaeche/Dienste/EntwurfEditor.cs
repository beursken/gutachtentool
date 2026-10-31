using System.Net;
using System.Text;
using Gutachten2025.Models;
using Gutachten2025.Services;
using Microsoft.JSInterop;

namespace Gutachtentool.Oberflaeche.Dienste;

/// <summary>
/// Gutachtenentwurf in der Blazor-Oberfläche: ein bearbeitbares Element (contenteditable) mit Fett/Kursiv.
/// Der Inhalt wird bei jeder Eingabe an .NET gemeldet und hier gespiegelt, damit das ViewModel ihn synchron
/// lesen kann (im Mac-Fenster gibt es keine synchronen JavaScript-Aufrufe). Gespeichert wird wie unter Windows
/// als RTF (RtfText), damit Entwürfe zwischen allen Fassungen austauschbar bleiben.
/// </summary>
public sealed class EntwurfEditor : IEntwurfEditor
{
    public const string ElementId = "entwurf";

    readonly IJSRuntime _js;
    FormatierterText _stand = FormatierterText.Unformatiert(string.Empty);

    public EntwurfEditor(IJSRuntime js) => _js = js;

    /// <summary>
    /// Umleitung des Einfügens (Reiter „Rohpunkte“: in die zuletzt bearbeitete Anmerkung);
    /// liefert true, wenn der Text dort eingefügt wurde.
    /// </summary>
    public Func<string, bool>? AnderesZiel { get; set; }

    /// <summary>Wird bei jeder Eingabe im Entwurf aufgerufen.</summary>
    public Action? Geaendert { get; set; }

    public string TextLesen() => _stand.Text;

    public FormatierterText FormatLesen() => _stand;

    public string RtfLesen() => RtfText.Schreiben(_stand);

    public void RtfSetzen(string rtf) => Setzen(RtfText.Lesen(rtf));

    public void TextSetzen(string text) => Setzen(FormatierterText.Unformatiert(text.Replace("\r\n", "\n").Replace('\r', '\n')));

    public void Leeren() => Setzen(FormatierterText.Unformatiert(string.Empty));

    public void Einfuegen(string baustein)
    {
        baustein = baustein.Trim();
        if (baustein.Length == 0) return;
        if (AnderesZiel?.Invoke(baustein) == true) return;
        // Das Element meldet den neuen Inhalt anschließend über EntwurfEingabe.
        _ = AufrufAsync("gutachtentool.editor.baustein", ElementId, baustein);
    }

    public Task FormatierenAsync(string befehl) => AufrufAsync("gutachtentool.editor.formatieren", ElementId, befehl);

    /// <summary>Meldet die festen Kürzel (Kürzel + Tab) an das Element; Schlüssel ohne „#“, klein geschrieben.</summary>
    public Task KuerzelMeldenAsync(IEnumerable<Baustein> bausteine)
    {
        var liste = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Baustein b in Formulierungsdatei.Alle(bausteine))
        {
            if (b.IstGruppe || b.Kuerzel.Trim().Length == 0) continue;
            liste.TryAdd(b.Kuerzel.Trim().ToLowerInvariant(), b.Text.Trim());
        }
        return AufrufAsync("gutachtentool.editor.kuerzel", liste);
    }

    void Setzen(FormatierterText f)
    {
        _stand = f;
        _ = AufrufAsync("gutachtentool.editor.setzen", ElementId, Html(f));
    }

    async Task AufrufAsync(string funktion, params object?[] argumente)
    {
        try
        {
            await _js.InvokeVoidAsync(funktion, argumente);
        }
        catch (JSException ex)
        {
            EinstellungsSpeicher.Protokollieren(ex);
        }
        catch (TaskCanceledException)
        {
            // Fenster wird geschlossen
        }
    }

    /// <summary>HTML aus formatiertem Text: nur &lt;b&gt;, &lt;i&gt; und &lt;br&gt;.</summary>
    public static string Html(FormatierterText f)
    {
        var sb = new StringBuilder();
        foreach (TextLauf l in f.Laeufe())
        {
            string t = WebUtility.HtmlEncode(l.T).Replace("\n", "<br>");
            if (l.K) t = "<i>" + t + "</i>";
            if (l.F) t = "<b>" + t + "</b>";
            sb.Append(t);
        }
        // Ein abschließender Zeilenumbruch braucht ein weiteres <br>, um sichtbar zu sein.
        if (f.Text.EndsWith('\n')) sb.Append("<br>");
        return sb.ToString();
    }

    // ---------- Aufruf aus JavaScript ----------

    /// <summary>Neuer Inhalt nach einer Eingabe (Tastatur, Einfügen, Fett/Kursiv).</summary>
    [JSInvokable]
    public void EntwurfEingabe(List<TextLauf>? laeufe)
    {
        _stand = FormatierterText.AusLaeufen(laeufe ?? new());
        Geaendert?.Invoke();
    }
}
