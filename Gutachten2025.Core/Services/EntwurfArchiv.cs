using Gutachten2025.Models;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Gutachten2025.Services;

/// <summary>Speichert je Kennung den Stand des Gutachtens (&lt;Klausur&gt;\Entwürfe\&lt;Kennung&gt;.json), damit es erneut geöffnet werden kann.</summary>
public static class EntwurfArchiv
{
    public const string Ordnername = "Entwürfe";

    static string Pfad(string klausurOrdner, string kennung) =>
        Path.Combine(klausurOrdner, Ordnername, KlausurAblage.SicherName(kennung) + ".json");

    public static bool Existiert(string klausurOrdner, string kennung) => File.Exists(Pfad(klausurOrdner, kennung));

    public static void Speichern(string klausurOrdner, GutachtenEntwurf entwurf)
    {
        string pfad = Pfad(klausurOrdner, entwurf.Kennung);
        Directory.CreateDirectory(Path.GetDirectoryName(pfad)!);
        string tmp = pfad + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(entwurf, KlausurKonfigDatei.Json), new UTF8Encoding(false));
        File.Move(tmp, pfad, overwrite: true);
    }

    public static GutachtenEntwurf? Laden(string klausurOrdner, string kennung)
    {
        string pfad = Pfad(klausurOrdner, kennung);
        return File.Exists(pfad)
            ? JsonSerializer.Deserialize<GutachtenEntwurf>(File.ReadAllText(pfad, Encoding.UTF8), KlausurKonfigDatei.Json)
            : null;
    }

    static readonly ConcurrentDictionary<string, (DateTime Stand, string? Schema)> SchemaCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Fingerabdruck des Rohpunkteschemas, mit dem die Arbeit bewertet wurde (zwischengespeichert nach Dateistand).</summary>
    public static string? Schema(string klausurOrdner, string kennung)
    {
        string pfad = Pfad(klausurOrdner, kennung);
        if (!File.Exists(pfad)) return null;
        DateTime stand = File.GetLastWriteTimeUtc(pfad);
        if (SchemaCache.TryGetValue(pfad, out var c) && c.Stand == stand) return c.Schema;
        string? schema = null;
        try
        {
            using FileStream fs = File.OpenRead(pfad);
            using JsonDocument doc = JsonDocument.Parse(fs);
            foreach (JsonProperty p in doc.RootElement.EnumerateObject())
                if (p.Name.Equals(nameof(GutachtenEntwurf.Schema), StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.String)
                    schema = p.Value.GetString();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            schema = null;
        }
        SchemaCache[pfad] = (stand, schema);
        return schema;
    }
}
