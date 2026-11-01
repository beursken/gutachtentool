using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Gutachten2025.Services;

/// <summary>Dateisystem-Struktur: &lt;Wurzel&gt;\Vorlagen und &lt;Wurzel&gt;\&lt;Klausur&gt;.</summary>
public sealed class KlausurAblage
{
    public const string VorlagenName = "Vorlagen";
    public const string VorlageListe = "MUSTER.xlsx";
    public const string WordVorlage = "MUSTER.docx";
    public const string Formulierungen = "Texte.txt";
    public const string Konfiguration = "Klausur.json";
    public const string Notenliste = "Liste.xlsx";
    /// <summary>Sachverhalt und Lösung als Markdown (Seitenleiste; Teil der Pakete).</summary>
    public const string Sachverhalt = "Sachverhalt.md";
    public const string Loesung = "Loesung.md";

    static readonly string[] Reserviert =
        { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "LPT1", "LPT2", "LPT3" };

    /// <summary>Vorgabe für die Ablage (Dokumente\Klausuren); vor dem Start änderbar (Browser-Fassung: virtuelles Dateisystem).</summary>
    public static string StandardWurzel { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Klausuren");

    public KlausurAblage(string wurzel) => Wurzel = Path.GetFullPath(wurzel);

    public string Wurzel { get; }

    public string VorlagenOrdner => Path.Combine(Wurzel, VorlagenName);

    /// <summary>Legt Wurzel und Vorlagen an; liefert die fehlenden Programmvorlagen.</summary>
    public IReadOnlyList<string> Einrichten()
    {
        Directory.CreateDirectory(VorlagenOrdner);
        var fehlend = new List<string>();
        foreach (string datei in new[] { Formulierungen, WordVorlage, VorlageListe })
        {
            string ziel = Path.Combine(VorlagenOrdner, datei);
            if (File.Exists(ziel)) continue;
            // Vorrang: Datei neben dem Programm (vom Installer austauschbar), sonst eingebettete Vorlage.
            string quelle = Path.Combine(AppContext.BaseDirectory, datei);
            if (File.Exists(quelle)) File.Copy(quelle, ziel);
            else if (Programmvorlagen.Vorhanden(datei)) Programmvorlagen.Schreiben(datei, ziel);
            else fehlend.Add(datei);
        }
        string konfiguration = Path.Combine(VorlagenOrdner, Konfiguration);
        if (!File.Exists(konfiguration)) KlausurKonfigDatei.Speichern(konfiguration, Models.Klausurkonfiguration.Standard());
        return fehlend;
    }

    public IEnumerable<string> Klausuren() =>
        Directory.Exists(Wurzel)
            ? Directory.GetDirectories(Wurzel)
                .Select(Path.GetFileName)
                .OfType<string>()
                .Where(n => !n.Equals(VorlagenName, StringComparison.OrdinalIgnoreCase))
                .OrderBy(n => n, StringComparer.Create(CultureInfo.GetCultureInfo("de-DE"), true))
            : Enumerable.Empty<string>();

    public bool Existiert(string klausur) => Directory.Exists(KlausurOrdner(klausur));

    /// <summary>„Name_(2)“ usw. – erster noch nicht vergebener Klausurname.</summary>
    public string FreierName(string klausur)
    {
        string basis = SicherName(klausur);
        if (!Existiert(basis)) return basis;
        int n = 2;
        while (Existiert($"{basis}_({n})")) n++;
        return $"{basis}_({n})";
    }

    public string KlausurOrdner(string klausur) => Path.Combine(Wurzel, SicherName(klausur));

    /// <summary>Pfad der Formulierungsdatei; ohne Klausur die Vorlage.</summary>
    public string TextePfad(string? klausur) => DateiPfad(klausur, Formulierungen);

    /// <summary>Pfad der Klausurkonfiguration; ohne Klausur die Vorlage.</summary>
    public string KonfigurationsPfad(string? klausur) => DateiPfad(klausur, Konfiguration);

    string DateiPfad(string? klausur, string datei) =>
        string.IsNullOrWhiteSpace(klausur)
            ? Path.Combine(VorlagenOrdner, datei)
            : Path.Combine(KlausurOrdner(klausur), datei);

    /// <summary>Legt den Klausurordner samt Liste, Vorlage, Formulierungen und Konfiguration an; liefert den Ordnernamen.</summary>
    public string KlausurEinrichten(string anzeigename, NotenlisteService notenliste)
    {
        string name = SicherName(anzeigename);
        string ordner = Path.Combine(Wurzel, name);
        Directory.CreateDirectory(ordner);

        string liste = Path.Combine(ordner, Notenliste);
        if (!File.Exists(liste))
        {
            File.Copy(Vorlage(VorlageListe), liste);
            notenliste.Initialisieren(liste, anzeigename.Trim());
        }
        KopierenFallsFehlt(Vorlage(WordVorlage), Path.Combine(ordner, WordVorlage));
        KopierenFallsFehlt(Vorlage(Formulierungen), Path.Combine(ordner, Formulierungen));

        string konfiguration = Path.Combine(ordner, Konfiguration);
        if (!File.Exists(konfiguration))
        {
            string vorlage = Path.Combine(VorlagenOrdner, Konfiguration);
            if (File.Exists(vorlage)) File.Copy(vorlage, konfiguration);
            else KlausurKonfigDatei.Speichern(konfiguration, Models.Klausurkonfiguration.Standard());
        }
        return name;
    }

    string Vorlage(string datei)
    {
        string pfad = Path.Combine(VorlagenOrdner, datei);
        return File.Exists(pfad)
            ? pfad
            : throw new FileNotFoundException($"Die Vorlage „{datei}“ fehlt im Ordner „{VorlagenOrdner}“. Ggf. das Programm neu installieren.", pfad);
    }

    static void KopierenFallsFehlt(string quelle, string ziel)
    {
        if (!File.Exists(ziel)) File.Copy(quelle, ziel);
    }

    /// <summary>Datei- bzw. Ordnername ohne unzulässige Zeichen und Leerzeichen.</summary>
    public static string SicherName(string? s)
    {
        // Feste Windows-Zeichenliste statt Path.GetInvalidFileNameChars(): gleiche Ordnernamen unter Windows,
        // macOS und im Browser (z. B. bei gemeinsam genutzten OneDrive-Ordnern).
        var ungueltig = UngueltigeZeichen;
        string n = string.Concat((s ?? string.Empty).Trim().Select(c => ungueltig.Contains(c) || char.IsWhiteSpace(c) ? '_' : c))
            .TrimEnd('.');
        if (n.Length == 0) return "_";
        // „Vorlagen“ ist der Ordner der Programmvorlagen und darf keine Klausur sein.
        if (n.Equals(VorlagenName, StringComparison.OrdinalIgnoreCase)) return "_" + n;
        return Reserviert.Contains(n.ToUpperInvariant()) ? "_" + n : n;
    }

    static readonly char[] UngueltigeZeichen =
        "<>:\"/\\|?*".Concat(Enumerable.Range(0, 32).Select(i => (char)i)).ToArray();

    public static bool GleicherPfad(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    [System.Runtime.InteropServices.DllImport("shlwapi.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    static extern int StrCmpLogicalW(string a, string b);

    /// <summary>Sortierung wie im Explorer („K2“ vor „K10“); außerhalb von Windows gleichwertig nachgebildet.</summary>
    public static readonly IComparer<string> NatuerlicheReihenfolge =
        OperatingSystem.IsWindows()
            ? Comparer<string>.Create((a, b) => StrCmpLogicalW(a, b))
            : Comparer<string>.Create(NatuerlichVergleichen);

    static readonly CompareInfo Vergleich = CultureInfo.GetCultureInfo("de-DE").CompareInfo;

    /// <summary>Ziffernfolgen als Zahlen, sonst ohne Groß-/Kleinschreibung (wie StrCmpLogicalW).</summary>
    public static int NatuerlichVergleichen(string? a, string? b)
    {
        if (ReferenceEquals(a, b)) return 0;
        if (a is null) return -1;
        if (b is null) return 1;
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (char.IsAsciiDigit(a[i]) && char.IsAsciiDigit(b[j]))
            {
                int si = i, sj = j;
                while (i < a.Length && char.IsAsciiDigit(a[i])) i++;
                while (j < b.Length && char.IsAsciiDigit(b[j])) j++;
                string za = a[si..i].TrimStart('0'), zb = b[sj..j].TrimStart('0');
                if (za.Length != zb.Length) return za.Length < zb.Length ? -1 : 1;
                int z = string.CompareOrdinal(za, zb);
                if (z != 0) return z;
            }
            else
            {
                int si = i, sj = j;
                while (i < a.Length && !char.IsAsciiDigit(a[i])) i++;
                while (j < b.Length && !char.IsAsciiDigit(b[j])) j++;
                int t = Vergleich.Compare(a, si, i - si, b, sj, j - sj, CompareOptions.IgnoreCase);
                if (t != 0) return t;
            }
        }
        return (a.Length - i).CompareTo(b.Length - j);
    }

    /// <summary>„MUSTER (3).docx“ – erster freier Sicherungsname.</summary>
    public static string Sicherungsname(string ordner, string datei)
    {
        string basis = Path.GetFileNameWithoutExtension(datei), ext = Path.GetExtension(datei);
        int n = 1;
        string pfad;
        do pfad = Path.Combine(ordner, $"{basis} ({n++}){ext}");
        while (File.Exists(pfad));
        return pfad;
    }

    /// <summary>True, wenn die Datei in einem anderen Programm geöffnet oder schreibgeschützt ist.</summary>
    public static bool IstGesperrt(string pfad)
    {
        if (!File.Exists(pfad)) return false;
        try
        {
            using var fs = new FileStream(pfad, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (IOException) { return true; }
        catch (UnauthorizedAccessException) { return true; }
    }

    /// <summary>Erkennt synchronisierte Cloud-Ordner (OneDrive-Umleitung von „Dokumente“ u. ä.).</summary>
    public string? CloudAnbieter()
    {
        foreach (string variable in new[] { "OneDriveCommercial", "OneDriveConsumer", "OneDrive" })
        {
            string? p = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(p) &&
                Wurzel.StartsWith(Path.GetFullPath(p).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
                return "OneDrive";
        }
        string[] teile = Wurzel.Split('\\', '/');
        // macOS: iCloud Drive liegt unter ~/Library/Mobile Documents, Dateianbieter unter ~/Library/CloudStorage.
        if (Wurzel.Contains("/Library/Mobile Documents/", StringComparison.Ordinal)) return "iCloud";
        foreach (string anbieter in new[] { "OneDrive", "Dropbox", "iCloudDrive", "Google Drive", "GoogleDrive", "Nextcloud", "ownCloud", "Seafile" })
            if (teile.Any(t => t.StartsWith(anbieter, StringComparison.OrdinalIgnoreCase)))
                return anbieter;
        return null;
    }
}
