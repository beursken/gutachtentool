using System;
using System.IO;
using System.Text;
using Gutachten2025.Services;
using Xunit;

namespace Gutachten2025.Tests;

public class AutokorrekturTests
{
    static readonly string Liste = Encoding.UTF8.GetString(Programmvorlagen.Lesen(Autokorrektur.Datei)).TrimStart('﻿');

    static string? Nach(string davor, int eingefuegt = 1, params string[] ausnahmen)
    {
        var a = new Autokorrektur(Liste, ausnahmen);
        return a.Vorschlag(davor, eingefuegt) is { } e ? davor[..e.Start] + e.Neu + davor[(e.Start + e.Laenge)..] : null;
    }

    [Theory]
    [InlineData("Das ist dei ", "Das ist die ")]
    [InlineData("Dei ", "Die ")]
    [InlineData("etwa z.B.", "etwa z. B.")]
    [InlineData("vgl ", "vgl. ")]
    [InlineData("nach §433 ", "nach § 433 ")]
    [InlineData("Abs.1 ", "Abs. 1 ")]
    [InlineData("Ein GRoßbuchstabe ", "Ein Großbuchstabe ")]
    [InlineData("nach Paragraph 433 ", "nach § 433 ")]
    [InlineData("Paragraph vier dreiunddreißig ", "§ 433 ")]
    [InlineData("§ 4 dreiunddreißig ", "§ 433 ")]
    [InlineData("Satz zwei.", "S. 2.")]
    [InlineData("Paragraph 312 g ", "§ 312g ")]
    [InlineData("Randnummer 12 ", "Rn. 12 ")]
    public void Tippen(string davor, string erwartet) => Assert.Equal(erwartet, Nach(davor));

    [Theory]
    [InlineData("Die Kinder, die die ")]   // doppelte Wörter bleiben
    [InlineData("Die StPO ")]              // Abkürzung bleibt
    [InlineData("bzgl.")]                  // Punkt wird gerade getippt
    [InlineData("der erste Absatz ist ")]  // „Absatz“ ohne Zahl
    [InlineData("eine Alternative ein ")]  // „ein“ ist keine Zahl
    [InlineData("Paragraph 280 281 ")]     // Ziffernfolgen nicht zusammenziehen
    [InlineData("Die ITler ")]
    public void Unveraendert(string davor) => Assert.Null(Nach(davor));

    [Theory]
    [InlineData("aus Paragraph vierhundertdreiunddreißig Absatz eins Satz zwei BGB.", "aus § 433 Abs. 1 S. 2 BGB.")]
    [InlineData("gemäß Paragraphen 280, 281 und 283 BGB", "gemäß §§ 280, 281 und 283 BGB")]
    [InlineData("Artikel sechs Absatz eins Buchstabe a DSGVO", "Art. 6 Abs. 1 lit. a DSGVO")]
    [InlineData("§§ zweihundertachtzig, zweihunderteinundachtzig ", "§§ 280, 281 ")]
    public void Diktat(string davor, string erwartet) => Assert.Equal(erwartet, Nach(davor, davor.Length));

    [Fact]
    public void NurAnDerEinfuegung()
    {
        // Älterer Text (z. B. nach Rückgängig) wird beim Weitertippen nicht erneut berichtigt.
        Assert.Null(Nach("Absatz 1 ganz hinten und weiter "));
    }

    [Fact]
    public void Ausnahmen()
    {
        Assert.Null(Nach("Der Ansrpuch ", 1, "Ansrpuch"));
        Assert.Null(Nach("im Satz 2 ", 1, "Satz"));
        Assert.Equal("Der Anspruch ", Nach("Der Ansrpuch "));
    }

    [Theory]
    [InlineData("vier", 4)]
    [InlineData("eins", 1)]
    [InlineData("dreiunddreißig", 33)]
    [InlineData("einundzwanzig", 21)]
    [InlineData("vierhundertdreiunddreißig", 433)]
    [InlineData("hundert", 100)]
    [InlineData("zweitausendeinhundertfünfundfünfzig", 2155)]
    [InlineData("Siebzehn", 17)]
    public void Zahlwoerter(string wort, int wert) => Assert.Equal(wert, Autokorrektur.Zahlwort(wort));

    [Theory]
    [InlineData("ein")]
    [InlineData("eine")]
    [InlineData("Satz")]
    [InlineData("einsundzwanzig")]
    public void KeineZahl(string wort) => Assert.Null(Autokorrektur.Zahlwort(wort));

    [Fact]
    public void ListeAnpassbar()
    {
        var a = new Autokorrektur("// Anmerkung\nSV => Sachverhalt\nTB # => Tatbestand #\nkaputt\n");
        Assert.Equal("Sachverhalt", a.Berichtigen("SV"));
        Assert.Equal("Tatbestand 2 ", a.Vorschlag("TB zwei ", 1) is { } e ? e.Neu + " " : null);
    }

    [Fact]
    public void AusnahmeDatei()
    {
        string ordner = Path.Combine(Path.GetTempPath(), "gt-ak-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(ordner);
        try
        {
            Assert.True(Autokorrektur.AusnahmeHinzufuegen(ordner, "Ansrpuch"));
            Assert.False(Autokorrektur.AusnahmeHinzufuegen(ordner, "Ansrpuch"));
            var a = Autokorrektur.Laden(ordner, ordner); // Vorlagenordner = Klausurordner: Liste wird angelegt
            Assert.True(File.Exists(Path.Combine(ordner, Autokorrektur.Datei)));
            Assert.True(a.IstAusnahme("Ansrpuch"));
            Assert.Equal("die", a.Berichtigen("dei"));
        }
        finally
        {
            Directory.Delete(ordner, true);
        }
    }
}
