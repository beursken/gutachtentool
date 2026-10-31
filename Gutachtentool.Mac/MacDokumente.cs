using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Gutachten2025.Services;

namespace Gutachtentool.Mac;

/// <summary>
/// Doppelklick auf eine .gtpaket-Datei im Finder. macOS übergibt die Datei nicht als Startargument, sondern als
/// Nachricht „Dokument öffnen“ an den Delegate der NSApplication (application:openURLs:). Photino implementiert
/// diese Methode nicht; sie wird hier zur Laufzeit über die Objective-C-Laufzeit an die Delegate-Klasse angefügt.
/// Gilt für den Programmstart per Doppelklick wie für ein bereits laufendes Programm.
/// </summary>
static unsafe class MacDokumente
{
    const string ObjC = "/usr/lib/libobjc.A.dylib";

    [DllImport(ObjC)] static extern IntPtr objc_getClass(string name);
    [DllImport(ObjC)] static extern IntPtr sel_registerName(string name);
    [DllImport(ObjC)] static extern IntPtr object_getClass(IntPtr obj);
    [DllImport(ObjC)] static extern IntPtr class_replaceMethod(IntPtr cls, IntPtr sel, IntPtr imp, string types);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern IntPtr Senden(IntPtr empfaenger, IntPtr selektor);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern IntPtr Senden(IntPtr empfaenger, IntPtr selektor, nuint index);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern nuint SendenZahl(IntPtr empfaenger, IntPtr selektor);

    static readonly object Sperre = new();
    static readonly List<string> Wartend = new();
    static readonly HashSet<IntPtr> Eingerichtet = new();
    static Func<string, Task>? _ziel;

    /// <summary>
    /// Fügt application:openURLs: an die Delegate-Klasse an; mehrfach aufrufbar (früh beim Start und erneut,
    /// sobald die Oberfläche bereit ist). Startargumente mit .gtpaket (Start aus dem Terminal) werden übernommen.
    /// </summary>
    public static void Einrichten(IEnumerable<string>? argumente = null)
    {
        foreach (string a in argumente ?? Array.Empty<string>())
            if (a.EndsWith(".gtpaket", StringComparison.OrdinalIgnoreCase) && File.Exists(a)) Melden(a);

        if (!OperatingSystem.IsMacOS()) return;
        try
        {
            IntPtr app = Senden(objc_getClass("NSApplication"), sel_registerName("sharedApplication"));
            IntPtr delegat = app == IntPtr.Zero ? IntPtr.Zero : Senden(app, sel_registerName("delegate"));
            if (delegat == IntPtr.Zero) return; // Photino hat noch keinen Delegate gesetzt – später erneut
            IntPtr klasse = object_getClass(delegat);
            lock (Sperre)
            {
                if (!Eingerichtet.Add(klasse)) return;
            }
            delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr, void> methode = &UrlsOeffnen;
            // v = void, @ = self, : = _cmd, @ = NSApplication, @ = NSArray<NSURL>
            class_replaceMethod(klasse, sel_registerName("application:openURLs:"), (IntPtr)methode, "v@:@@");
        }
        catch (Exception ex)
        {
            EinstellungsSpeicher.Protokollieren(ex);
        }
    }

    /// <summary>Ziel für geöffnete Dateien (die Oberfläche); bis dahin eingegangene Dateien werden nachgereicht.</summary>
    public static void Abonnieren(Func<string, Task> ziel)
    {
        List<string> nachreichen;
        lock (Sperre)
        {
            _ziel = ziel;
            nachreichen = new List<string>(Wartend);
            Wartend.Clear();
        }
        foreach (string pfad in nachreichen) _ = ziel(pfad);
    }

    static void Melden(string pfad)
    {
        Func<string, Task>? ziel;
        lock (Sperre)
        {
            ziel = _ziel;
            if (ziel is null)
            {
                if (!Wartend.Contains(pfad)) Wartend.Add(pfad);
                return;
            }
        }
        _ = ziel(pfad);
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    static void UrlsOeffnen(IntPtr self, IntPtr selektor, IntPtr app, IntPtr urls)
    {
        // Keine Ausnahme darf in den nativen Aufrufer zurücklaufen.
        try
        {
            if (urls == IntPtr.Zero) return;
            nuint anzahl = SendenZahl(urls, sel_registerName("count"));
            for (nuint i = 0; i < anzahl; i++)
            {
                IntPtr url = Senden(urls, sel_registerName("objectAtIndex:"), i);
                IntPtr pfad = url == IntPtr.Zero ? IntPtr.Zero : Senden(url, sel_registerName("path"));
                IntPtr utf8 = pfad == IntPtr.Zero ? IntPtr.Zero : Senden(pfad, sel_registerName("UTF8String"));
                string? text = utf8 == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(utf8);
                if (!string.IsNullOrEmpty(text) && text.EndsWith(".gtpaket", StringComparison.OrdinalIgnoreCase)) Melden(text);
            }
        }
        catch (Exception ex)
        {
            EinstellungsSpeicher.Protokollieren(ex);
        }
    }
}
