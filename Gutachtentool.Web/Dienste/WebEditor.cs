using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Gutachten2025.Models;
using Gutachten2025.Services;
using Microsoft.JSInterop;

namespace Gutachtentool.Web.Dienste;

/// <summary>
/// Gutachtenentwurf im Browser: ein bearbeitbares Element (contenteditable) mit Fett/Kursiv.
/// Entwürfe werden wie in der Windows-Fassung als RTF gespeichert (RtfText), damit sie austauschbar bleiben.
/// </summary>
public sealed class WebEditor : IEntwurfEditor
{
    public const string ElementId = "entwurf";

    static readonly Regex KuerzelVorCursor = new(@"(?<![\p{L}\d_\-])#?([\p{L}\d_\-]{1,24})$", RegexOptions.CultureInvariant);

    readonly IJSInProcessRuntime _js;

    public WebEditor(IJSRuntime js) => _js = (IJSInProcessRuntime)js;

    /// <summary>
    /// Umleitung des Einfügens (Reiter „Rohpunkte“: in die zuletzt bearbeitete Anmerkung);
    /// liefert true, wenn der Text dort eingefügt wurde.
    /// </summary>
    public Func<string, bool>? AnderesZiel { get; set; }

    /// <summary>Kürzelsuche für Kürzel + Tab (vom ViewModel gesetzt).</summary>
    public Func<string, Baustein?>? KuerzelSuche { get; set; }

    /// <summary>Wird bei jeder Eingabe im Entwurf aufgerufen.</summary>
    public Action? Geaendert { get; set; }

    public string TextLesen() => FormatLesen().Text;

    public FormatierterText FormatLesen()
    {
        List<TextLauf>? laeufe = _js.Invoke<List<TextLauf>?>("gutachtentool.editor.laeufe", ElementId);
        return FormatierterText.AusLaeufen(laeufe ?? new());
    }

    public string RtfLesen() => RtfText.Schreiben(FormatLesen());

    public void RtfSetzen(string rtf) => Setzen(RtfText.Lesen(rtf));

    public void TextSetzen(string text) => Setzen(FormatierterText.Unformatiert(text.Replace("\r\n", "\n").Replace('\r', '\n')));

    public void Leeren() => _js.InvokeVoid("gutachtentool.editor.setzen", ElementId, string.Empty);

    public void Einfuegen(string baustein)
    {
        baustein = baustein.Trim();
        if (baustein.Length == 0) return;
        if (AnderesZiel?.Invoke(baustein) == true) return;
        _js.InvokeVoid("gutachtentool.editor.baustein", ElementId, baustein);
    }

    public void Formatieren(string befehl) => _js.InvokeVoid("gutachtentool.editor.formatieren", ElementId, befehl);

    void Setzen(FormatierterText f) => _js.InvokeVoid("gutachtentool.editor.setzen", ElementId, Html(f));

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

    // ---------- Aufrufe aus JavaScript ----------

    /// <summary>Kürzel + Tab: Text vor dem Cursor; liefert Länge des Kürzels und Bausteintext oder null.</summary>
    [JSInvokable]
    public KuerzelTreffer? KuerzelPruefen(string davor)
    {
        Match m = KuerzelVorCursor.Match(davor);
        if (!m.Success || KuerzelSuche?.Invoke(m.Groups[1].Value) is not { } b) return null;
        return new KuerzelTreffer(m.Length, b.Text.Trim());
    }

    [JSInvokable]
    public void EntwurfGeaendert() => Geaendert?.Invoke();

    public sealed record KuerzelTreffer(int Laenge, string Text);
}
