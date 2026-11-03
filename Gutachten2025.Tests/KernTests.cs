using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Gutachten2025.Rohpunkte;
using Gutachten2025.Services;
using Xunit;

namespace Gutachten2025.Tests;

public class MarkdownTests
{
    static XElement Xml(string html) => XElement.Parse("<div>" + html + "</div>");

    [Theory]
    [InlineData("# Titel\n\nText mit **fett** und *kursiv*.")]
    [InlineData("**a *b** c*")]
    [InlineData("- eins\n- zwei\n  - drei\n\n1. a\n2. b")]
    [InlineData("| A | B |\n|---|--:|\n| 1 | 2 |")]
    [InlineData("> Zitat\n\n```\n<code> & \n```\n\n---\n\nZeile  \numbrochen")]
    [InlineData("Seitenvorschub\f und § 433 Abs. 2 BGB, ‚Zitat‘ & <Tag>")]
    public void ErgebnisIstWohlgeformt(string md) => Xml(MarkdownHtml.Umwandeln(md));

    [Fact]
    public void UeberschriftUndAuszeichnung()
    {
        string html = MarkdownHtml.Umwandeln("# Titel\n\n**fett** und *kursiv* und ~~weg~~");
        Assert.Contains("<h1>Titel</h1>", html);
        Assert.Contains("<strong>fett</strong>", html);
        Assert.Contains("<em>kursiv</em>", html);
        Assert.Contains("<del>weg</del>", html);
    }

    [Fact]
    public void HtmlWirdNichtUebernommen()
    {
        string html = MarkdownHtml.Umwandeln("<script>alert(1)</script> <img src=x onerror=y>");
        Assert.DoesNotContain("<script", html);
        Assert.DoesNotContain("<img", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public void NurSichereVerweise()
    {
        string html = MarkdownHtml.Umwandeln("[gut](https://example.org) [böse](javascript:alert(1)) ![Bild](https://example.org/x.png)");
        Assert.Contains("href=\"https://example.org\"", html);
        Assert.DoesNotContain("javascript:", html);
        Assert.DoesNotContain("<img", html);
        Assert.Contains("böse", html);
        Assert.Contains("Bild", html);
    }

    [Fact]
    public void ListenUndTabellen()
    {
        XElement liste = Xml(MarkdownHtml.Umwandeln("- eins\n- zwei\n- drei"));
        Assert.Equal(3, liste.Descendants("li").Count());
        XElement tabelle = Xml(MarkdownHtml.Umwandeln("| A | B |\n|---|---|\n| 1 | 2 |\n| 3 | 4 |"));
        Assert.Equal(2, tabelle.Descendants("th").Count());
        Assert.Equal(4, tabelle.Descendants("td").Count());
    }

    [Fact]
    public void NummerierteListeUnterbrichtAbsatzNurMitEins()
    {
        // Eine umbrochene Zeile „2. Alt.“ darf keine Liste beginnen.
        XElement x = Xml(MarkdownHtml.Umwandeln("Anspruch aus § 823 Abs. 1,\n2. Alt. BGB"));
        Assert.Empty(x.Descendants("ol"));
    }
}

public class GliederungFettTests
{
    [Fact]
    public void BegriffeWerdenFett()
    {
        var absatz = new List<Gutachten2025.Models.TextLauf> { new("Zu Frage 1 und Tatkomplex II sowie Fallfrage 2a; Fragen bleiben.", false, true) };
        var neu = TextNormalizer.GliederungFett(new List<List<Gutachten2025.Models.TextLauf>> { absatz })[0];
        Assert.Equal(absatz[0].T, string.Concat(neu.Select(l => l.T)));
        string fett = string.Join("|", neu.Where(l => l.F).Select(l => l.T));
        Assert.Equal("Frage 1|Tatkomplex II|Fallfrage 2a", fett);
        Assert.All(neu, l => Assert.True(l.K)); // Kursiv bleibt erhalten
    }
}

public class ErstnotenTests
{
    static byte[] Json(object o) => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(o));

    [Fact]
    public void GueltigeListe()
    {
        var liste = Erstnoten.AusBytes(Json(new[] { new { Kennung = "K17", Punkte = 9 }, new { Kennung = "1002", Punkte = 0 } }));
        Assert.Equal(2, liste.Count);
    }

    [Theory]
    [InlineData("Max Mustermann", 9)]
    [InlineData("Müller", 9)]
    [InlineData("K17", 19)]
    [InlineData("K17", -1)]
    public void UngueltigeEintraege(string kennung, int punkte) =>
        Assert.Throws<InvalidDataException>(() => Erstnoten.AusBytes(Json(new[] { new { Kennung = kennung, Punkte = punkte } })));

    [Fact]
    public void WiderspruechlicheDoppelte() =>
        Assert.Throws<InvalidDataException>(() => Erstnoten.AusBytes(Json(new[] { new { Kennung = "K1", Punkte = 5 }, new { Kennung = "k1", Punkte = 6 } })));
}

public class KennziffernTests
{
    [Fact]
    public void BereicheUndEinzelwerte() =>
        Assert.Equal(new[] { "1001", "1002", "1003", "1005" }, Kennziffern.Lesen("1001-1003, 1005"));

    [Fact]
    public void VorsatzUndFuehrendeNullen()
    {
        Assert.Equal(new[] { "K1", "K2", "K3" }, Kennziffern.Lesen("K1 – K3"));
        Assert.Equal(new[] { "008", "009", "010" }, Kennziffern.Lesen("008-010"));
    }

    [Fact]
    public void OhneDoppelteUndOhneNichtVergebene()
    {
        List<string> alle = Kennziffern.Lesen("1-5\n3");
        Assert.Equal(5, alle.Count);
        Assert.Equal(new[] { "1", "2", "5" }, Kennziffern.Ohne(alle, Kennziffern.Lesen("3-4")));
    }

    [Fact]
    public void UmgekehrterBereich() => Assert.Throws<FormatException>(() => Kennziffern.Lesen("10-5"));
}

public class RohpunkteTests
{
    static RohpunktZeile Position(string id, double max) => new(Zeilenart.Position, id, "1", "Station " + id, 2, max, null, string.Empty);

    [Fact]
    public void SummeMitBonusUndAbzug()
    {
        var a = Position("1.1", 10);
        var b = Position("1.2", 20);
        var bonus = new RohpunktZeile(Zeilenart.Bonus, RohpunktZeile.BonusId, string.Empty, "Bonus", 1, 10, null, string.Empty);
        var minus = new RohpunktZeile(Zeilenart.Minus, RohpunktZeile.MinusId, string.Empty, "Abzug", 1, 10, null, string.Empty);
        a.Punkte = 7.5;
        bonus.Punkte = 2;
        minus.Punkte = 1;
        Rohpunkteschema.Summe s = Rohpunkteschema.Berechnen(new[] { a, b, bonus, minus });
        Assert.Equal(7.5, s.Roh);
        Assert.Equal(30, s.Max);
        Assert.Equal(8.5, s.Gesamt);
        Assert.Equal(1, s.Offen);
        Assert.True(s.HatEingaben);
    }

    [Fact]
    public void ViertelpunkteUndHoechstwert()
    {
        var z = Position("1.1", 3);
        double? gemeldet = null;
        z.MaximumUeberschritten += (_, w) => gemeldet = w;
        z.Punkte = 1.3;
        Assert.Equal(1.25, z.Punkte);
        Assert.Null(gemeldet);
        z.Punkte = 5;
        Assert.Equal(3, z.Punkte);
        Assert.Equal(5, gemeldet);
    }

    [Theory]
    [InlineData(49.99, 0)]
    [InlineData(50, 4)]
    [InlineData(89, 4)]
    [InlineData(95, 13)]
    [InlineData(150, 13)]
    public void NotenvorschlagNachUmrechnung(double gesamt, int note)
    {
        var doc = new RpsDoc { Umrechnung = new List<double[]> { new[] { 0d, 0d }, new[] { 50d, 4d }, new[] { 90d, 13d } } };
        Assert.Equal(note, Rohpunkteschema.Note(doc, gesamt));
    }

    [Fact]
    public void StandardumrechnungSteigtMonoton()
    {
        var doc = new RpsDoc();
        int vorher = -1;
        for (double g = 0; g <= 100; g += 0.5)
        {
            int n = Rohpunkteschema.Note(doc, g);
            Assert.InRange(n, 0, 18);
            Assert.True(n >= vorher);
            vorher = n;
        }
    }
}

public class AbgleichUndVersionTests
{
    [Fact]
    public void UeberschriftFindetRohpunktzeile()
    {
        var titel = new[] { "A. Anspruch des K gegen V aus § 433 Abs. 2 BGB", "I. Kaufvertrag", "B. Anspruch des V gegen K aus § 985 BGB" };
        Assert.Equal(0, Gliederungsabgleich.Finden("A. Anspruch K gegen V aus § 433 Abs. 2 BGB", titel));
        Assert.Equal(2, Gliederungsabgleich.Finden("B. Anspruch V gegen K aus § 985 BGB", titel));
        Assert.Equal(-1, Gliederungsabgleich.Finden("Literaturverzeichnis", titel));
    }

    [Theory]
    [InlineData("1.3.0", "1.4.0", "1.4.0")]
    [InlineData("1.3.0", "1.3.0.7", null)]
    [InlineData("1.3.0", "1.2.9", null)]
    [InlineData("1.3.0", "unsinn", null)]
    public void Versionsvergleich(string aktuell, string angeboten, string? erwartet) =>
        Assert.Equal(erwartet, Aktualisierung.Vergleichen(Version.Parse(aktuell), angeboten)?.ToString(3));

    [Fact]
    public void WoerterAusUnterlagen()
    {
        List<string> w = Unterlagenwoerter.Aus("Udo (U) und Alfred (A) wandern mit Gerold.", "Mitverschulden");
        Assert.Contains("Gerold", w);
        Assert.Contains("Mitverschulden", w);
        Assert.DoesNotContain("U", w);
    }
}

public sealed class PaketTests : IDisposable
{
    readonly string _ordner = Path.Combine(Path.GetTempPath(), "gt-test-" + Guid.NewGuid().ToString("N"));

    public PaketTests() => Directory.CreateDirectory(_ordner);

    public void Dispose()
    {
        try { Directory.Delete(_ordner, true); } catch (IOException) { }
    }

    [Fact]
    public void ExportOhneErstnotenUndImportMitSachverhalt()
    {
        string klausur = Path.Combine(_ordner, "K");
        Directory.CreateDirectory(klausur);
        File.WriteAllText(Path.Combine(klausur, KlausurAblage.Formulierungen), "Gruppe\n\tBaustein eins\n");
        File.WriteAllText(Path.Combine(klausur, KlausurAblage.Sachverhalt), "# Sachverhalt\n\nK kauft bei V.");
        File.WriteAllText(Path.Combine(klausur, Erstnoten.Datei), "[{\"Kennung\":\"K1\",\"Punkte\":9}]");
        string paket = Path.Combine(_ordner, "a" + PaketService.Endung);

        PaketService.Exportieren(klausur, new PaketManifest { Titel = "Test" }, paket);

        using (ZipArchive zip = ZipFile.OpenRead(paket))
        {
            Assert.DoesNotContain(zip.Entries, e => e.FullName == Erstnoten.Datei);
            Assert.Contains(zip.Entries, e => e.FullName == KlausurAblage.Sachverhalt);
        }
        PaketInhalt inhalt = PaketService.Pruefen(paket);
        Assert.True(inhalt.Dateien.ContainsKey(KlausurAblage.Sachverhalt));
        Assert.Contains("Sachverhalt", inhalt.Beschreibung());
    }

    [Fact]
    public void PaketMitNamenInErstnotenWirdAbgelehnt()
    {
        string paket = Path.Combine(_ordner, "b" + PaketService.Endung);
        using (ZipArchive zip = ZipFile.Open(paket, ZipArchiveMode.Create))
        {
            Schreiben(zip, PaketService.ManifestName, "{\"Formatversion\":1,\"Titel\":\"X\"}");
            Schreiben(zip, KlausurAblage.Formulierungen, "Baustein\n");
            Schreiben(zip, Erstnoten.Datei, "[{\"Kennung\":\"Max Mustermann\",\"Punkte\":9}]");
        }
        Assert.Throws<PaketFehler>(() => PaketService.Pruefen(paket));
    }

    [Fact]
    public void ManipuliertePruefsummeWirdErkannt()
    {
        string paket = Path.Combine(_ordner, "c" + PaketService.Endung);
        using (ZipArchive zip = ZipFile.Open(paket, ZipArchiveMode.Create))
        {
            Schreiben(zip, PaketService.ManifestName, "{\"Formatversion\":1,\"Titel\":\"X\",\"Pruefsummen\":{\"Texte.txt\":\"00\"}}");
            Schreiben(zip, KlausurAblage.Formulierungen, "Baustein\n");
        }
        Assert.Throws<PaketFehler>(() => PaketService.Pruefen(paket));
    }

    static void Schreiben(ZipArchive zip, string name, string text)
    {
        using Stream s = zip.CreateEntry(name).Open();
        byte[] b = Encoding.UTF8.GetBytes(text);
        s.Write(b, 0, b.Length);
    }
}

public class NormenTests
{
    [Fact]
    public void MehrereParagraphenMitEinemGesetz()
    {
        var v = Normverweise.Finden("Anspruch aus §§ 280 Abs. 1, 281 BGB.");
        Assert.Equal(2, v.Count);
        Assert.All(v, x => Assert.Equal("BGB", x.Gesetz));
        Assert.Equal(new[] { "§ 280", "§ 281" }, v.Select(x => x.Nr).ToArray());
    }

    [Theory]
    [InlineData("Art. 6 Abs. 1 lit. f DSGVO", "DSGVO", "Art 6")]
    [InlineData("nach § 823 I BGB", "BGB", "§ 823")]
    [InlineData("§ 433 Abs. 2 BGB", "BGB", "§ 433")]
    public void EinzelzitatWirdErkannt(string text, string gesetz, string nr)
    {
        var v = Assert.Single(Normverweise.Finden(text));
        Assert.Equal(gesetz, v.Gesetz);
        Assert.Equal(Normverweise.Schluessel(gesetz, nr), Normverweise.Schluessel(v.Gesetz, v.Nr));
    }

    [Fact]
    public void OhneGesetzGiltDasZuletztGenannte()
    {
        var v = Normverweise.Finden("§ 433 BGB; ferner § 434.");
        Assert.Equal(2, v.Count);
        Assert.Equal("BGB", v[1].Gesetz);
    }

    [Fact]
    public void AuszeichnungBleibtWohlgeformt()
    {
        string html = MarkdownHtml.Umwandeln("Prüfe **§ 280 BGB** und Art. 6 DSGVO & mehr.");
        string aus = Normverweise.Auszeichnen(html, _ => "Hinweis <\"&>");
        XElement x = XElement.Parse("<div>" + aus + "</div>");
        Assert.Equal(2, x.Descendants("span").Count(e => (string?)e.Attribute("class") == "norm"));
    }

    [Fact]
    public void GesetzesXmlWirdGelesen()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <dokumente>
              <norm><metadaten><jurabk>BGB</jurabk></metadaten></norm>
              <norm><metadaten><jurabk>BGB</jurabk><enbez>§ 433</enbez><titel>Vertragstypische Pflichten beim Kaufvertrag</titel></metadaten>
                <textdaten><text><Content><P>(1) Durch den Kaufvertrag wird der Verkäufer verpflichtet.</P><P>(2) Der Käufer ist verpflichtet.</P></Content></text></textdaten></norm>
            </dokumente>
            """;
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        var n = Assert.Single(Gesetzesquelle.AusXml(ms, "BGB", "test"));
        Assert.Equal("§ 433", n.Nr);
        Assert.Contains("(2) Der Käufer", n.Text);
        Assert.Equal("Vertragstypische Pflichten beim Kaufvertrag", n.Titel);
    }
}

public class NormstelleTests
{
    static readonly Norm Kauf = new()
    {
        Gesetz = "BGB", Nr = "§ 433", Titel = "Vertragstypische Pflichten beim Kaufvertrag",
        Text = "(1) Durch den Kaufvertrag wird der Verkäufer einer Sache verpflichtet, dem Käufer die Sache zu übergeben. Der Verkäufer hat dem Käufer die Sache frei von Sach- und Rechtsmängeln zu verschaffen.\n(2) Der Käufer ist verpflichtet, dem Verkäufer den vereinbarten Kaufpreis zu zahlen.",
    };

    static readonly Norm Dsgvo = new()
    {
        Gesetz = "DSGVO", Nr = "Art 6", Titel = "Rechtmäßigkeit der Verarbeitung",
        Text = "(1) Die Verarbeitung ist nur rechtmäßig, wenn mindestens eine der nachstehenden Bedingungen erfüllt ist:\na) Einwilligung;\nb) Vertrag;\nf) berechtigte Interessen.\nUnterabsatz 1 Buchstabe f gilt nicht für Behörden.\n(2) Die Mitgliedstaaten können …",
    };

    [Fact]
    public void TeilWirdErkannt()
    {
        NormVerweis v = Assert.Single(Normverweise.Finden("§ 433 Abs. 1 S. 2 BGB"));
        Assert.Equal(new NormTeil("1", "2", null, null), v.Teil);
        Assert.Equal(new NormTeil("1", null, null, "f"), Assert.Single(Normverweise.Finden("Art. 6 Abs. 1 lit. f DSGVO")).Teil);
        Assert.Equal(new NormTeil("1", "1", null, null), Assert.Single(Normverweise.Finden("§ 812 I 1 BGB")).Teil);
        Assert.Equal(new NormTeil("2", null, "3", null), Assert.Single(Normverweise.Finden("§ 573 Abs. 2 Nr. 3 BGB")).Teil);
        Assert.Null(Assert.Single(Normverweise.Finden("§ 433 BGB")).Teil);
    }

    [Fact]
    public void AngabeAusDerAnzeigeWirdZurueckgelesen()
    {
        NormVerweis v = Normverweise.AusAngabe("BGB", "§ 433|Abs. 1 S. 2");
        Assert.Equal("§ 433", v.Nr);
        Assert.Equal(new NormTeil("1", "2", null, null), v.Teil);
    }

    [Fact]
    public void SatzWirdAusgeschnitten()
    {
        Assert.Equal("Der Verkäufer hat dem Käufer die Sache frei von Sach- und Rechtsmängeln zu verschaffen.",
            Normstelle.Ausschnitt(Kauf, new NormTeil("1", "2", null, null)));
        Assert.StartsWith("(2) Der Käufer", Normstelle.Ausschnitt(Kauf, new NormTeil("2", null, null, null)));
        Assert.Equal("f) berechtigte Interessen.", Normstelle.Ausschnitt(Dsgvo, new NormTeil("1", null, null, "f")));
        Assert.Null(Normstelle.Ausschnitt(Kauf, new NormTeil("5", null, null, null)));
    }

    [Fact]
    public void AbkuerzungenBeendenKeinenSatz()
    {
        var s = Normstelle.Saetze("Dies gilt z. B. nach Abs. 2 entsprechend. § 434 bleibt unberührt.");
        Assert.Equal(2, s.Count);
    }

    [Fact]
    public void HervorhebungBleibtWohlgeformt()
    {
        string html = Normstelle.Hervorheben(MarkdownHtml.Umwandeln(Normensammlung.AlsMarkdown(Kauf, new NormTeil("1", "2", null, null))));
        XElement x = XElement.Parse("<div>" + html + "</div>");
        Assert.Contains("Rechtsmängeln", Assert.Single(x.Descendants("mark")).Value);
    }

    [Fact]
    public void EigeneNormenWerdenGelesen()
    {
        List<Norm> n = EigeneNormen.AusMarkdown("// Anmerkung\n# PrüfO\n## § 12 Wiederholung\n(1) Einmal.\n(2) Zweimal.\n## Art. 3a – Frist\nText");
        Assert.Equal(2, n.Count);
        Assert.Equal("§ 12", n[0].Nr);
        Assert.Equal("Wiederholung", n[0].Titel);
        Assert.Equal("(1) Einmal.\n(2) Zweimal.", n[0].Text);
        Assert.Equal("Art 3a", n[1].Nr);
        Assert.Equal("Frist", n[1].Titel);
        Assert.Empty(EigeneNormen.AusMarkdown(EigeneNormen.Vorlage).Where(x => x.Gesetz != "PrüfO"));
    }

    [Fact]
    public void EurLexWirdGelesen()
    {
        const string html = """
            <p class="oj-ti-art">Artikel 6</p><div class="eli-title"><p class="oj-sti-art">Rechtmäßigkeit der Verarbeitung</p></div>
            <div><p class="oj-normal">(1)   Die Verarbeitung ist nur rechtmäßig, wenn:</p>
            <table><tbody><tr><td><p class="oj-normal">a)</p></td><td><p class="oj-normal">Einwilligung;</p></td></tr></tbody></table></div>
            <p class="oj-ti-art">Artikel 7</p><p class="oj-sti-art">Bedingungen für die Einwilligung</p><p class="oj-normal">(1) Text &amp; mehr.</p>
            """;
        List<Norm> n = EurLex.AusHtml(html, "DSGVO", "test");
        Assert.Equal(2, n.Count);
        Assert.Equal("Art 6", n[0].Nr);
        Assert.Equal("Rechtmäßigkeit der Verarbeitung", n[0].Titel);
        Assert.Equal("(1) Die Verarbeitung ist nur rechtmäßig, wenn:\na) Einwilligung;", n[0].Text);
        Assert.Equal("(1) Text & mehr.", n[1].Text);
        Assert.Equal("32016R0679", EurLex.Celex("DS-GVO"));
    }

    [Fact]
    public void KvasirAntwortWirdGelesen()
    {
        const string json = """
            {"exists":true,"object":{"identity":{"title":"Art. 61 BayHIG"},"provenance":{"source":"Bayern.Recht","retrieved_at":"2026-10-01T00:00:00"},
             "content":{"label":"Art. 61 Prüfungen","plain_text":"Art. 61 Prüfungen (1) Erster Absatz. (2) Zweiter Absatz."}}}
            """;
        Norm? n = Kvasir.AusJson(json, "BayHIG", "Art 61");
        Assert.NotNull(n);
        Assert.Equal("Prüfungen", n!.Titel);
        Assert.Contains("(2) Zweiter Absatz.", n.Text);
        Assert.Contains("Stand 01.10.2026", n.Quelle);
        Assert.Null(Kvasir.AusJson("""{"exists":false}""", "X", "§ 1"));
    }
}
