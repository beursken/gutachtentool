using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Gutachten2025.Models;

/// <summary>Ein gleich formatierter Textabschnitt (für Fett/Kursiv in Word).</summary>
public sealed class TextLauf
{
    public TextLauf() { }

    public TextLauf(string text, bool fett = false, bool kursiv = false)
    {
        T = text;
        F = fett;
        K = kursiv;
    }

    /// <summary>Text.</summary>
    public string T { get; set; } = string.Empty;

    /// <summary>Fett.</summary>
    public bool F { get; set; }

    /// <summary>Kursiv.</summary>
    public bool K { get; set; }
}

/// <summary>Text mit Formatierung je Zeichen (aus einem RichEditBox gelesen).</summary>
public sealed class FormatierterText
{
    public FormatierterText(string text, bool[] fett, bool[] kursiv)
    {
        Text = text;
        Fett = fett;
        Kursiv = kursiv;
    }

    public string Text { get; }
    public bool[] Fett { get; }
    public bool[] Kursiv { get; }

    public static FormatierterText Unformatiert(string text) => new(text, new bool[text.Length], new bool[text.Length]);

    public static FormatierterText AusLaeufen(IEnumerable<TextLauf> laeufe)
    {
        var sb = new StringBuilder();
        var fett = new List<bool>();
        var kursiv = new List<bool>();
        foreach (TextLauf l in laeufe)
        {
            sb.Append(l.T);
            fett.AddRange(Enumerable.Repeat(l.F, l.T.Length));
            kursiv.AddRange(Enumerable.Repeat(l.K, l.T.Length));
        }
        return new FormatierterText(sb.ToString(), fett.ToArray(), kursiv.ToArray());
    }

    /// <summary>Fasst gleich formatierte Zeichen eines Bereichs zu Läufen zusammen.</summary>
    public List<TextLauf> Laeufe(int start = 0, int laenge = -1)
    {
        if (laenge < 0) laenge = Text.Length - start;
        var liste = new List<TextLauf>();
        var sb = new StringBuilder();
        bool f = false, k = false;
        for (int i = start; i < start + laenge; i++)
        {
            bool fi = i < Fett.Length && Fett[i], ki = i < Kursiv.Length && Kursiv[i];
            if (sb.Length > 0 && (fi != f || ki != k))
            {
                liste.Add(new TextLauf(sb.ToString(), f, k));
                sb.Clear();
            }
            f = fi;
            k = ki;
            sb.Append(Text[i]);
        }
        if (sb.Length > 0) liste.Add(new TextLauf(sb.ToString(), f, k));
        return liste;
    }
}
