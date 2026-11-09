using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Gutachten2025.Services;
using Xunit;

namespace Gutachten2025.Tests;

public class ZweitkorrekturTests
{
    [Theory]
    [InlineData(14, 13, "Insgesamt sehe ich die Arbeit übereinstimmend als eine erheblich")]
    [InlineData(9, 11, "Leider handelt es sich aus meiner Sicht nur um eine Leistung, die in jeder Hinsicht")]
    [InlineData(11, 9, "Es handelt sich aus meiner Sicht bereits um eine über den durchschnittlichen")]
    public void SchlusssatzNachNotenstufe(int zweit, int erst, string anfang) =>
        Assert.StartsWith(anfang, Zweitkorrektur.Schlusssatz(zweit, erst));

    [Fact]
    public void OhneErstnoteBleibtDerUeblicheSchlusssatz() =>
        Assert.Equal("Insgesamt handelt es sich daher um eine besonders hervorragende Leistung.", Zweitkorrektur.Schlusssatz(17, null));

    [Theory]
    [InlineData("12", 12)]
    [InlineData("12,0", 12)]
    [InlineData(" 7 P.", 7)]
    [InlineData("4 Punkte", 4)]
    [InlineData("19", null)]
    [InlineData("gut", null)]
    public void Punktwerte(string text, int? erwartet) => Assert.Equal(erwartet, Teilnehmerliste.Punktwert(text));

    static string Datei(string inhalt)
    {
        string pfad = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".csv");
        File.WriteAllText(pfad, inhalt, new System.Text.UTF8Encoding(false));
        return pfad;
    }

    [Fact]
    public void ImportMitNotenspalte()
    {
        string pfad = Datei("Kennziffer;Name;Note\n1001;Anna;12\n1002;Ben;7 P.\n1003;Cem;\n");
        try
        {
            Teilnehmerimport t = Teilnehmerliste.ImportierenMitNoten(pfad);
            Assert.Equal(3, t.Teilnehmende.Count);
            Assert.Equal("Anna", t.Teilnehmende[0].Name);
            Assert.Equal(2, t.Noten.Count);
            Assert.Equal(7, t.Noten["1002"]);
        }
        finally { File.Delete(pfad); }
    }

    [Fact]
    public void ImportOhneUeberschriftErkenntNotenspalte()
    {
        string pfad = Datei("1001;Anna Muster;12\n1002;Ben Beispiel;3\n");
        try
        {
            Teilnehmerimport t = Teilnehmerliste.ImportierenMitNoten(pfad);
            Assert.Equal("Anna Muster", t.Teilnehmende[0].Name);
            Assert.Equal(3, t.Noten["1002"]);
        }
        finally { File.Delete(pfad); }
    }

    [Fact]
    public void ImportOhneNotenBleibtWieBisher()
    {
        string pfad = Datei("Kennziffer\n1001\n1002\n");
        try
        {
            Assert.Empty(Teilnehmerliste.ImportierenMitNoten(pfad).Noten);
        }
        finally { File.Delete(pfad); }
    }

    [Fact]
    public void VorlageWirdUeberRunsHinwegUmgestellt()
    {
        string pfad = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".docx");
        using (WordprocessingDocument doc = WordprocessingDocument.Create(pfad, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            MainDocumentPart main = doc.AddMainDocumentPart();
            main.Document = new Document(new Body(
                new Paragraph(new Run(new Text("Erst")), new Run(new Text("gutachten zur Klausur"))),
                new Paragraph(new Run(new Text("Insgesamt handelt es sich daher um ###DEFI")), new Run(new Text("NITION### Die Note …"))),
                new Paragraph(new Run(new Text("Erstvotum")))));
        }
        try
        {
            Dictionary<string, int> n = new GutachtenService().TexteErsetzen(pfad, Zweitkorrektur.Vorlagenersetzungen);
            Assert.Equal(1, n["Erstgutachten"]);
            Assert.Equal(1, n["Erstvotum"]);
            Assert.Equal(1, n["Insgesamt handelt es sich daher um ###DEFINITION###"]);
            using WordprocessingDocument doc = WordprocessingDocument.Open(pfad, false);
            string text = string.Join("|", doc.MainDocumentPart!.Document!.Body!.Elements<Paragraph>().Select(p => p.InnerText));
            Assert.Equal("Zweitgutachten zur Klausur|###SCHLUSS### Die Note …|Zweitvotum", text);
        }
        finally { File.Delete(pfad); }
    }
}

public class ArbeitsdokumenteTests
{
    static string Ordner()
    {
        string o = Path.Combine(Path.GetTempPath(), "gt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(o);
        return o;
    }

    [Fact]
    public void DateiZurKennungWirdGefunden()
    {
        string o = Ordner();
        try
        {
            string b = Path.Combine(o, Arbeitsdokumente.Bearbeitungen);
            Directory.CreateDirectory(b);
            File.WriteAllText(Path.Combine(b, "10170.pdf"), "x");
            File.WriteAllText(Path.Combine(b, "1017_Bearbeitung.docx"), "x");
            File.WriteAllText(Path.Combine(b, "1017.pdf"), "x");
            Assert.Equal("1017.pdf", Path.GetFileName(Arbeitsdokumente.Finden(o, Arbeitsdokumente.Bearbeitungen, "1017")));
            File.Delete(Path.Combine(b, "1017.pdf"));
            Assert.Equal("1017_Bearbeitung.docx", Path.GetFileName(Arbeitsdokumente.Finden(o, Arbeitsdokumente.Bearbeitungen, "1017")));
            Assert.Null(Arbeitsdokumente.Finden(o, Arbeitsdokumente.Bearbeitungen, "101"));
        }
        finally { Directory.Delete(o, true); }
    }

    [Theory]
    [InlineData("Erstvoten/1017.pdf", Arbeitsdokumente.Erstvoten)]
    [InlineData("1017_Erstgutachten.docx", Arbeitsdokumente.Erstvoten)]
    [InlineData("Bearbeitungen/1017.pdf", Arbeitsdokumente.Bearbeitungen)]
    [InlineData("1017.pdf", "Standard")]
    public void ZipEintraegeWerdenZugeordnet(string pfad, string erwartet) =>
        Assert.Equal(erwartet, Arbeitsdokumente.Art(pfad, "Standard"));

    [Fact]
    public void ZipWirdEntpackt()
    {
        string o = Ordner();
        string zip = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            using (var a = System.IO.Compression.ZipFile.Open(zip, System.IO.Compression.ZipArchiveMode.Create))
            {
                foreach (string n in new[] { "Erstvoten/1017.pdf", "Bearbeitungen/1017.pdf", "1018.docx", "liesmich.txt", "__MACOSX/._1018.docx" })
                    using (var w = new StreamWriter(a.CreateEntry(n).Open())) w.Write("x");
            }
            var (b, e, u) = Arbeitsdokumente.AusZip(o, zip, Arbeitsdokumente.Bearbeitungen);
            Assert.Equal((2, 1, 2), (b, e, u));
            Assert.True(File.Exists(Path.Combine(o, Arbeitsdokumente.Erstvoten, "1017.pdf")));
            Assert.True(File.Exists(Path.Combine(o, Arbeitsdokumente.Bearbeitungen, "1018.docx")));
        }
        finally
        {
            Directory.Delete(o, true);
            File.Delete(zip);
        }
    }

    [Fact]
    public void PflichtkriterienImText()
    {
        var k = KlausurKonfigDatei.KriterienLesen("+!Gliederung | {subjekt} {wert}.\nklar\n+Schrift | {subjekt} {wert}.\ngut lesbar");
        Assert.True(k[0].IstPflicht);
        Assert.False(k[1].IstPflicht);
        Assert.StartsWith("+!Gliederung", KlausurKonfigDatei.KriterienText(new Gutachten2025.Models.Klausurkonfiguration { Formalkriterien = k }));
        Assert.True(new Gutachten2025.Models.Formalkriterium { Name = "Gliederung" }.IstPflicht); // ältere Klausur.json
    }

    [Fact]
    public void BeispielbausteineWerdenErkannt()
    {
        var beispiele = Formulierungsdatei.Parsen(System.Text.Encoding.UTF8.GetString(Programmvorlagen.Lesen(Beispielbausteine.Datei)).TrimStart('﻿'));
        Assert.True(Beispielbausteine.SindBeispiele(beispiele));
        Assert.Empty(Formulierungsdatei.Parsen(System.Text.Encoding.UTF8.GetString(Programmvorlagen.Lesen("Texte.txt")).TrimStart('﻿')));
    }

    [Fact]
    public void DruckauftraegeWerdenZugeordnet()
    {
        var staemme = new[] { "101", "1017" };
        Assert.Equal("1017", Druckprotokoll.Zuordnen("Microsoft Word - 1017.docx", staemme));
        Assert.Null(Druckprotokoll.Zuordnen("Brief.docx", staemme));
    }
}
