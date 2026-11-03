using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Gutachten2025;

/// <summary>
/// Überträgt den von <see cref="Services.MarkdownHtml"/> erzeugten HTML-Ausschnitt (wohlgeformt) in WinUI-Steuerelemente:
/// Absätze und Überschriften als RichTextBlock (Text markier- und kopierbar), Listen, Zitate, Code, Tabellen.
/// Kein Browser-Steuerelement: Darstellung, Dunkelmodus und Skalierung wie der Rest der Oberfläche.
/// </summary>
static class Unterlagenansicht
{
    static readonly Regex Leerraum = new(@"\s+", RegexOptions.CultureInvariant);

    /// <summary>Rückrufe der Seitenleiste: Klick auf eine Überschrift, Markierung als Baustein, geänderte Markierung.</summary>
    public sealed record Aktionen(Action<string> UeberschriftGewaehlt, Action<string> AlsBaustein, Action<string> AuswahlGeaendert, bool Sprungziel,
        Action<string, string>? NormGewaehlt = null);

    /// <summary>Fundstelle der Suche.</summary>
    public sealed record Treffer(RichTextBlock Block, int Start, int Laenge);

    static Aktionen? _aktionen;

    public static void Fuellen(StackPanel ziel, string html, double schrift, Aktionen? aktionen = null)
    {
        _aktionen = aktionen;
        HervorLaeufe.Clear();
        ziel.Children.Clear();
        if (string.IsNullOrWhiteSpace(html)) return;
        XElement wurzel;
        try
        {
            wurzel = XElement.Parse("<div>" + html + "</div>", LoadOptions.PreserveWhitespace);
        }
        catch (XmlException)
        {
            // Sollte nicht vorkommen; dann wenigstens den Text zeigen.
            ziel.Children.Add(new TextBlock { Text = Regex.Replace(html, "<[^>]+>", " "), TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
            return;
        }
        Bloecke(wurzel, ziel, schrift);
    }

    static void Bloecke(XElement eltern, Panel ziel, double schrift)
    {
        // Text und Auszeichnungen direkt im Element (enge Listenpunkte) bilden einen Absatz.
        Paragraph? lose = null;
        foreach (XNode n in eltern.Nodes())
        {
            if (n is XElement e && IstBlock(e.Name.LocalName))
            {
                if (lose is not null) { Absatz(ziel, lose, schrift); lose = null; }
                Block(e, ziel, schrift);
            }
            else
            {
                if (n is XText t && t.Value.Trim().Length == 0 && lose is null) continue;
                lose ??= new Paragraph();
                Inline(n, lose.Inlines, schrift);
            }
        }
        if (lose is not null) Absatz(ziel, lose, schrift);
    }

    static bool IstBlock(string name) => name is "p" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6" or "ul" or "ol" or "li"
        or "blockquote" or "pre" or "table" or "hr" or "div";

    static void Block(XElement e, Panel ziel, double schrift)
    {
        string name = e.Name.LocalName;
        switch (name)
        {
            case "p":
            {
                var p = new Paragraph();
                Inlines(e, p.Inlines, schrift);
                Absatz(ziel, p, schrift);
                break;
            }
            case "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
            {
                int ebene = name[1] - '0';
                double groesse = schrift * (ebene switch { 1 => 1.35, 2 => 1.2, 3 => 1.08, _ => 1.0 });
                var p = new Paragraph { FontSize = groesse, FontWeight = FontWeights.SemiBold };
                Inlines(e, p.Inlines, schrift);
                RichTextBlock r = Absatz(ziel, p, schrift);
                r.Margin = new Thickness(0, schrift * (ebene <= 2 ? 1.0 : 0.7), 0, schrift * 0.25);
                r.Tag = Ueberschriftsmarke;
                if (_aktionen is { Sprungziel: true } a)
                {
                    string titel = Leerraum.Replace(e.Value, " ").Trim();
                    ToolTipService.SetToolTip(r, "Klick: zur passenden Zeile im Reiter „Rohpunkte“");
                    r.Tapped += (_, _) =>
                    {
                        if (r.SelectedText.Length == 0) a.UeberschriftGewaehlt(titel);
                    };
                }
                break;
            }
            case "div" when (string?)e.Attribute("class") == "unterlagentitel":
                ziel.Children.Add(new TextBlock
                {
                    Text = e.Value.ToUpper(CultureInfo.CurrentCulture),
                    FontSize = schrift * 0.8,
                    FontWeight = FontWeights.SemiBold,
                    CharacterSpacing = 60,
                    Opacity = 0.65,
                    Margin = new Thickness(0, ziel.Children.Count == 0 ? 0 : schrift * 1.4, 0, schrift * 0.2),
                });
                ziel.Children.Add(Linie());
                break;
            case "ul" or "ol":
                Liste(e, ziel, schrift);
                break;
            case "blockquote":
            {
                var innen = new StackPanel();
                Bloecke(e, innen, schrift);
                ziel.Children.Add(new Border
                {
                    BorderThickness = new Thickness(3, 0, 0, 0),
                    BorderBrush = Grau(0x45),
                    Padding = new Thickness(10, 0, 0, 0),
                    Margin = new Thickness(0, schrift * 0.3, 0, schrift * 0.3),
                    Child = innen,
                    Opacity = 0.9,
                });
                break;
            }
            case "pre":
                ziel.Children.Add(new Border
                {
                    Background = Grau(0x1C),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(8, 6, 8, 6),
                    Margin = new Thickness(0, schrift * 0.3, 0, schrift * 0.3),
                    Child = new TextBlock
                    {
                        Text = e.Value,
                        FontFamily = new FontFamily("Consolas"),
                        FontSize = schrift * 0.92,
                        TextWrapping = TextWrapping.Wrap,
                        IsTextSelectionEnabled = true,
                    },
                });
                break;
            case "table":
                Tabelle(e, ziel, schrift);
                break;
            case "hr":
                ziel.Children.Add(Linie(schrift * 0.8));
                break;
            default: // li außerhalb einer Liste, div
                Bloecke(e, ziel, schrift);
                break;
        }
    }

    static RichTextBlock Absatz(Panel ziel, Paragraph p, double schrift)
    {
        var r = new RichTextBlock
        {
            FontSize = schrift,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
            Margin = new Thickness(0, schrift * 0.2, 0, schrift * 0.2),
        };
        Trimmen(p.Inlines);
        r.Blocks.Add(p);
        Hervorheben(r, p);
        Ausstatten(r);
        ziel.Children.Add(r);
        return r;
    }

    static void Liste(XElement liste, Panel ziel, double schrift)
    {
        bool nummeriert = liste.Name.LocalName == "ol";
        int nr = int.TryParse((string?)liste.Attribute("start"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int s) ? s : 1;
        var panel = new StackPanel { Margin = new Thickness(0, schrift * 0.1, 0, schrift * 0.1) };
        foreach (XElement li in liste.Elements("li"))
        {
            var zeile = new Grid { ColumnSpacing = 4 };
            zeile.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(nummeriert ? schrift * 2 : schrift * 1.2) });
            zeile.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var marke = new TextBlock
            {
                Text = nummeriert ? nr.ToString(CultureInfo.InvariantCulture) + "." : "•",
                FontSize = schrift,
                HorizontalAlignment = nummeriert ? HorizontalAlignment.Right : HorizontalAlignment.Center,
                Margin = new Thickness(0, schrift * 0.2, 0, 0),
                Opacity = 0.8,
            };
            var inhalt = new StackPanel();
            Bloecke(li, inhalt, schrift);
            Grid.SetColumn(inhalt, 1);
            zeile.Children.Add(marke);
            zeile.Children.Add(inhalt);
            panel.Children.Add(zeile);
            nr++;
        }
        ziel.Children.Add(panel);
    }

    static void Tabelle(XElement tabelle, Panel ziel, double schrift)
    {
        var zeilen = tabelle.Descendants("tr").ToList();
        int spalten = zeilen.Count == 0 ? 0 : zeilen.Max(z => z.Elements().Count());
        if (spalten == 0) return;
        var grid = new Grid();
        for (int k = 0; k < spalten; k++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MaxWidth = 420 });
        Brush linie = Grau(0x45), kopf = Grau(0x1C);
        for (int z = 0; z < zeilen.Count; z++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            int k = 0;
            foreach (XElement zelle in zeilen[z].Elements())
            {
                bool th = zelle.Name.LocalName == "th";
                var p = new Paragraph();
                if (th) p.FontWeight = FontWeights.SemiBold;
                Inlines(zelle, p.Inlines, schrift);
                var text = new RichTextBlock { FontSize = schrift * 0.95, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
                text.Blocks.Add(p);
                Ausstatten(text);
                string? stil = (string?)zelle.Attribute("style");
                if (stil?.Contains("right", StringComparison.Ordinal) == true) text.TextAlignment = TextAlignment.Right;
                else if (stil?.Contains("center", StringComparison.Ordinal) == true) text.TextAlignment = TextAlignment.Center;
                var rand = new Border
                {
                    BorderBrush = linie,
                    BorderThickness = new Thickness(k == 0 ? 1 : 0, z == 0 ? 1 : 0, 1, 1),
                    Padding = new Thickness(6, 3, 6, 3),
                    Background = th ? kopf : null,
                    Child = text,
                };
                Grid.SetRow(rand, z);
                Grid.SetColumn(rand, k++);
                grid.Children.Add(rand);
            }
        }
        ziel.Children.Add(new ScrollViewer
        {
            Content = grid,
            HorizontalScrollMode = ScrollMode.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollMode = ScrollMode.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new Thickness(0, schrift * 0.4, 0, schrift * 0.4),
        });
    }

    static void Inlines(XElement e, InlineCollection ziel, double schrift)
    {
        foreach (XNode n in e.Nodes()) Inline(n, ziel, schrift);
        Trimmen(ziel);
    }

    /// <summary>Leerraum am Anfang und Ende eines Absatzes entfernen.</summary>
    static void Trimmen(InlineCollection ziel)
    {
        if (ziel.FirstOrDefault() is Run erster) erster.Text = erster.Text.TrimStart();
        if (ziel.LastOrDefault() is Run letzter) letzter.Text = letzter.Text.TrimEnd();
    }

    static void Inline(XNode n, InlineCollection ziel, double schrift)
    {
        switch (n)
        {
            case XText t:
                ziel.Add(new Run { Text = Leerraum.Replace(t.Value, " ") });
                break;
            case XElement e:
                switch (e.Name.LocalName)
                {
                    case "br":
                        ziel.Add(new LineBreak());
                        break;
                    case "strong":
                    {
                        var b = new Bold();
                        foreach (XNode k in e.Nodes()) Inline(k, b.Inlines, schrift);
                        ziel.Add(b);
                        break;
                    }
                    case "em":
                    {
                        var i = new Italic();
                        foreach (XNode k in e.Nodes()) Inline(k, i.Inlines, schrift);
                        ziel.Add(i);
                        break;
                    }
                    case "del":
                    {
                        var d = new Span { TextDecorations = Windows.UI.Text.TextDecorations.Strikethrough };
                        foreach (XNode k in e.Nodes()) Inline(k, d.Inlines, schrift);
                        ziel.Add(d);
                        break;
                    }
                    case "mark":
                    {
                        // Zitierte Stelle einer Norm: Hintergrund per TextHighlighter (siehe Hervorheben), Läufe tragen den Namen.
                        var m = new Span();
                        foreach (XNode k in e.Nodes()) Inline(k, m.Inlines, schrift);
                        Benennen(m.Inlines);
                        ziel.Add(m);
                        break;
                    }
                    case "code":
                        ziel.Add(new Run { Text = e.Value, FontFamily = new FontFamily("Consolas"), FontSize = schrift * 0.92 });
                        break;
                    case "span" when (string?)e.Attribute("class") == "norm":
                    {
                        // Normzitat: Hinweis beim Überfahren (Text der Norm), Klick zeigt den Volltext in der Leiste.
                        string gesetz = (string?)e.Attribute("data-gesetz") ?? string.Empty, nr = (string?)e.Attribute("data-nr") ?? string.Empty;
                        var h = new Hyperlink { UnderlineStyle = UnderlineStyle.None };
                        h.Inlines.Add(new Run { Text = Leerraum.Replace(e.Value, " ") });
                        if ((string?)e.Attribute("title") is { Length: > 0 } hinweis)
                            ToolTipService.SetToolTip(h, new TextBlock { Text = hinweis, TextWrapping = TextWrapping.Wrap, MaxWidth = 480 });
                        Action<string, string>? wahl = _aktionen?.NormGewaehlt;
                        if (wahl is not null) h.Click += (_, _) => wahl(gesetz, nr);
                        ziel.Add(h);
                        break;
                    }
                    case "a" when Uri.TryCreate((string?)e.Attribute("href"), UriKind.Absolute, out Uri? ziel2)
                                  && ziel2.Scheme is "http" or "https" or "mailto":
                    {
                        // Hyperlink öffnet den Standardbrowser bzw. das Mailprogramm selbst.
                        var h = new Hyperlink { NavigateUri = ziel2 };
                        h.Inlines.Add(new Run { Text = Leerraum.Replace(e.Value, " ") });
                        ToolTipService.SetToolTip(h, ziel2.ToString());
                        ziel.Add(h);
                        break;
                    }
                    default:
                        foreach (XNode k in e.Nodes()) Inline(k, ziel, schrift);
                        break;
                }
                break;
        }
    }

    static Border Linie(double abstand = 0) => new()
    {
        Height = 1,
        Background = Grau(0x45),
        Margin = new Thickness(0, abstand, 0, abstand),
    };

    /// <summary>Halbtransparentes Grau: passt zu hell und dunkel (die Leiste kann vom Windows-Thema abweichen).</summary>
    static SolidColorBrush Grau(byte deckung) => new(Windows.UI.Color.FromArgb(deckung, 0x80, 0x80, 0x80));

    /// <summary>Markierung melden; Kontextmenü „Kopieren“ und „Als Baustein speichern…“.</summary>
    static void Ausstatten(RichTextBlock r)
    {
        if (_aktionen is not { } a) return;
        r.SelectionChanged += (_, _) => a.AuswahlGeaendert(r.SelectedText);
        var kopieren = new MenuFlyoutItem { Text = "Kopieren", Icon = new FontIcon { Glyph = "\uE8C8" } };
        kopieren.Click += (_, _) => r.CopySelectionToClipboard();
        var baustein = new MenuFlyoutItem { Text = "Als Baustein speichern…", Icon = new FontIcon { Glyph = "\uE710" } };
        baustein.Click += (_, _) => a.AlsBaustein(r.SelectedText.Length > 0 ? r.SelectedText : string.Empty);
        var menue = new MenuFlyout();
        menue.Items.Add(kopieren);
        menue.Items.Add(baustein);
        menue.Opening += (_, _) => kopieren.IsEnabled = baustein.IsEnabled = r.SelectedText.Length > 0;
        r.ContextFlyout = menue;
    }

    const string Ueberschriftsmarke = "ueberschrift";
    const string Hervormarke = "hervor";
    static SolidColorBrush? _hervorFarbe;
    static SolidColorBrush HervorFarbe => _hervorFarbe ??= new SolidColorBrush(Windows.UI.Color.FromArgb(0x70, 0xFF, 0xC1, 0x07));

    /// <summary>Such- und Sprungmarkierungen entfernen; die Hervorhebung der zitierten Normstelle bleibt.</summary>
    static void Leeren(RichTextBlock b)
    {
        for (int i = b.TextHighlighters.Count - 1; i >= 0; i--)
            if (!ReferenceEquals(b.TextHighlighters[i].Background, _hervorFarbe)) b.TextHighlighters.RemoveAt(i);
    }

    /// <summary>Läufe innerhalb von &lt;mark&gt; (bis der Absatz fertig ist).</summary>
    static readonly HashSet<Run> HervorLaeufe = new(ReferenceEqualityComparer.Instance);

    static void Benennen(InlineCollection inlines)
    {
        foreach (Inline i in inlines)
        {
            if (i is Run r) HervorLaeufe.Add(r);
            else if (i is Span s) Benennen(s.Inlines);
        }
    }

    /// <summary>Hervorgehobene Läufe (zitierte Stelle) mit Hintergrund versehen; der Block wird als Sprungziel markiert.</summary>
    static void Hervorheben(RichTextBlock r, Paragraph p)
    {
        var bereich = new TextHighlighter { Background = HervorFarbe };
        int pos = 0;
        void Gehe(IEnumerable<Inline> inlines)
        {
            foreach (Inline i in inlines)
            {
                switch (i)
                {
                    case Run run:
                        if (HervorLaeufe.Remove(run) && run.Text.Length > 0) bereich.Ranges.Add(new TextRange { StartIndex = pos, Length = run.Text.Length });
                        pos += run.Text.Length;
                        break;
                    case LineBreak: pos++; break;
                    case Span sp: Gehe(sp.Inlines); break;
                }
            }
        }
        Gehe(p.Inlines);
        if (bereich.Ranges.Count == 0) return;
        r.TextHighlighters.Add(bereich);
        if (r.Tag is null) r.Tag = Hervormarke;
    }

    /// <summary>Gezeigte Norm: zuerst die hervorgehobene Stelle, sonst die Überschrift, die mit dem Zitat beginnt; null = keine.</summary>
    public static FrameworkElement? NormFinden(Panel wurzel, string zitat, bool nurUeberschrift)
    {
        List<RichTextBlock> bloecke = Nachfahren<RichTextBlock>(wurzel).ToList();
        if (!nurUeberschrift && bloecke.FirstOrDefault(b => Hervormarke.Equals(b.Tag)) is { } hier) return hier;
        return bloecke.FirstOrDefault(b => Ueberschriftsmarke.Equals(b.Tag) && Klartext(b) is var t
                                           && (t == zitat || t.StartsWith(zitat + " ", StringComparison.Ordinal)));
    }

    /// <summary>Gliederungsnavigation: erste Überschrift (ersatzweise erster Absatz), die den Begriff enthält, hervorheben; null = keine.</summary>
    public static RichTextBlock? UeberschriftFinden(Panel wurzel, string begriff)
    {
        List<RichTextBlock> bloecke = Nachfahren<RichTextBlock>(wurzel).ToList();
        RichTextBlock? treffer = bloecke.FirstOrDefault(b => Ueberschriftsmarke.Equals(b.Tag) && Klartext(b).Contains(begriff, StringComparison.CurrentCultureIgnoreCase))
                                 ?? bloecke.FirstOrDefault(b => Klartext(b).Contains(begriff, StringComparison.CurrentCultureIgnoreCase));
        if (treffer is null) return null;
        foreach (RichTextBlock b in bloecke) Leeren(b);
        string text = Klartext(treffer);
        int k = text.IndexOf(begriff, StringComparison.CurrentCultureIgnoreCase);
        var hier = new TextHighlighter { Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xFF, 0x9F, 0x1A)) };
        hier.Ranges.Add(new TextRange { StartIndex = Math.Max(0, k), Length = begriff.Length });
        treffer.TextHighlighters.Add(hier);
        return treffer;
    }

    // ---------- Suche ----------

    /// <summary>Alle Fundstellen (ohne Groß-/Kleinschreibung) in Reihenfolge der Anzeige; leerer Begriff löscht die Hervorhebung.</summary>
    public static List<Treffer> Suchen(Panel wurzel, string? begriff)
    {
        var treffer = new List<Treffer>();
        List<RichTextBlock> bloecke = Nachfahren<RichTextBlock>(wurzel).ToList();
        foreach (RichTextBlock b in bloecke) Leeren(b);
        string s = (begriff ?? string.Empty).Trim();
        if (s.Length < 2) return treffer;
        foreach (RichTextBlock b in bloecke)
        {
            string text = Klartext(b);
            int k = 0;
            while ((k = text.IndexOf(s, k, StringComparison.CurrentCultureIgnoreCase)) >= 0)
            {
                treffer.Add(new Treffer(b, k, s.Length));
                k += s.Length;
            }
        }
        return treffer;
    }

    /// <summary>Alle Fundstellen hell, die aktuelle kräftig hervorheben.</summary>
    public static void Markieren(List<Treffer> treffer, int aktuell)
    {
        foreach (var gruppe in treffer.Select((t, i) => (t, i)).GroupBy(x => x.t.Block))
        {
            RichTextBlock b = gruppe.Key;
            Leeren(b);
            var alle = new TextHighlighter { Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x66, 0xFF, 0xD5, 0x4F)) };
            foreach (var (t, i) in gruppe)
                if (i != aktuell) alle.Ranges.Add(new TextRange { StartIndex = t.Start, Length = t.Laenge });
            if (alle.Ranges.Count > 0) b.TextHighlighters.Add(alle);
            if (gruppe.FirstOrDefault(x => x.i == aktuell) is { t: not null } akt)
            {
                var hier = new TextHighlighter { Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xFF, 0x9F, 0x1A)) };
                hier.Ranges.Add(new TextRange { StartIndex = akt.t.Start, Length = akt.t.Laenge });
                b.TextHighlighters.Add(hier);
            }
        }
    }

    /// <summary>Text eines Absatzes in der Zählweise der Hervorhebung (Zeilenumbruch = ein Zeichen).</summary>
    static string Klartext(RichTextBlock b)
    {
        var sb = new System.Text.StringBuilder();
        void Sammeln(IEnumerable<Inline> inlines)
        {
            foreach (Inline i in inlines)
            {
                switch (i)
                {
                    case Run r: sb.Append(r.Text); break;
                    case LineBreak: sb.Append('\n'); break;
                    case Span sp: Sammeln(sp.Inlines); break;
                }
            }
        }
        foreach (var blk in b.Blocks)
            if (blk is Paragraph p) Sammeln(p.Inlines);
        return sb.ToString();
    }

    public static IEnumerable<T> Nachfahren<T>(DependencyObject wurzel) where T : DependencyObject
    {
        int n = VisualTreeHelper.GetChildrenCount(wurzel);
        for (int i = 0; i < n; i++)
        {
            DependencyObject kind = VisualTreeHelper.GetChild(wurzel, i);
            if (kind is T t) yield return t;
            foreach (T x in Nachfahren<T>(kind)) yield return x;
        }
    }

    public static T? Nachfahre<T>(DependencyObject wurzel) where T : DependencyObject => Nachfahren<T>(wurzel).FirstOrDefault();
}
