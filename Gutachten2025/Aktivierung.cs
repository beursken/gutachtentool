using Microsoft.Windows.AppLifecycle;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Windows.ApplicationModel.Activation;

namespace Gutachten2025.Services;

/// <summary>Ermittelt die per Doppelklick bzw. Kommandozeile übergebene Paketdatei.</summary>
public static class Aktivierung
{
    static readonly Regex Token = new("\"([^\"]*)\"|(\\S+)", RegexOptions.CultureInvariant);

    /// <param name="eigeneKommandozeile">Nur beim eigenen Start true – bei umgeleiteten Aktivierungen gilt allein deren Inhalt.</param>
    public static string? Datei(AppActivationArguments? args, bool eigeneKommandozeile)
    {
        try
        {
            if (args?.Kind == ExtendedActivationKind.File && args.Data is IFileActivatedEventArgs datei)
            {
                foreach (var item in datei.Files)
                    if (Passend(item.Path)) return item.Path;
            }
            if (args?.Kind == ExtendedActivationKind.Launch && args.Data is ILaunchActivatedEventArgs start
                && AusKommandozeile(start.Arguments) is { } pfad)
                return pfad;
        }
        catch (Exception ex)
        {
            EinstellungsSpeicher.Protokollieren(ex);
        }
        return eigeneKommandozeile ? Erste(Environment.GetCommandLineArgs().Skip(1)) : null;
    }

    static string? AusKommandozeile(string? zeile) =>
        string.IsNullOrWhiteSpace(zeile)
            ? null
            : Erste(Token.Matches(zeile).Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value));

    static string? Erste(IEnumerable<string> kandidaten) => kandidaten.FirstOrDefault(Passend);

    static bool Passend(string? pfad) =>
        !string.IsNullOrWhiteSpace(pfad)
        && pfad.EndsWith(PaketService.Endung, StringComparison.OrdinalIgnoreCase)
        && File.Exists(pfad);
}
