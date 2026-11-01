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
