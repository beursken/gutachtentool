using Gutachten2025.Services;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace Gutachten2025;

/// <summary>
/// Abfrage der Windows-Druckwarteschlange des Standarddruckers (winspool): Zahl der Aufträge, Druckerstörungen
/// (offline, Papierstau, Papier leer …) und fehlerhafte Aufträge – für „Alle Gutachten drucken“.
/// </summary>
static class Druckwarteschlange
{
    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool GetDefaultPrinter(StringBuilder? name, ref int laenge);

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool OpenPrinter(string name, out IntPtr drucker, IntPtr vorgaben);

    [DllImport("winspool.drv", SetLastError = true)]
    static extern bool ClosePrinter(IntPtr drucker);

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool GetPrinter(IntPtr drucker, int stufe, IntPtr puffer, int groesse, out int benoetigt);

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool EnumJobs(IntPtr drucker, int erster, int anzahl, int stufe, IntPtr puffer, int groesse, out int benoetigt, out int geliefert);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct JobInfo1
    {
        public int JobId;
        public string? PrinterName, MachineName, UserName, Document, Datatype, Status;
        public int StatusFlags, Priority, Position, TotalPages, PagesPrinted;
        public long Submitted1, Submitted2; // SYSTEMTIME (16 Byte)
    }

    // PRINTER_STATUS_* bzw. JOB_STATUS_*
    static readonly (int Bit, string Text)[] Druckerstoerungen =
    {
        (0x1, "Der Drucker ist angehalten."), (0x2, "Der Drucker meldet einen Fehler."), (0x8, "Papierstau."), (0x10, "Kein Papier."),
        (0x20, "Manuelle Zufuhr erforderlich."), (0x40, "Papierproblem."), (0x80, "Der Drucker ist offline."), (0x400000, "Druckertür offen."),
        (0x100000, "Eingreifen am Drucker erforderlich."), (0x40000, "Toner leer."), (0x8000, "Ausgabefach voll."),
    };
    const int JobFehler = 0x2 | 0x20 | 0x40 | 0x200 | 0x400; // ERROR, OFFLINE, PAPEROUT, BLOCKED_DEVQ, USER_INTERVENTION

    public static Druckstand? Abfragen()
    {
        try
        {
            int laenge = 0;
            GetDefaultPrinter(null, ref laenge);
            if (laenge == 0) return new Druckstand(0, "Es ist kein Standarddrucker eingerichtet.", Array.Empty<string>());
            var name = new StringBuilder(laenge);
            if (!GetDefaultPrinter(name, ref laenge)) return null;
            if (!OpenPrinter(name.ToString(), out IntPtr drucker, IntPtr.Zero)) return null;
            try
            {
                string? problem = null;
                // PRINTER_INFO_6: nur der Status
                GetPrinter(drucker, 6, IntPtr.Zero, 0, out int noetig);
                if (noetig > 0)
                {
                    IntPtr p = Marshal.AllocHGlobal(noetig);
                    try
                    {
                        if (GetPrinter(drucker, 6, p, noetig, out _))
                        {
                            int status = Marshal.ReadInt32(p);
                            foreach (var (bit, text) in Druckerstoerungen)
                                if ((status & bit) != 0) { problem = $"{text} ({name})"; break; }
                        }
                    }
                    finally { Marshal.FreeHGlobal(p); }
                }

                var fehlerhaft = new List<string>();
                int auftraege = 0;
                EnumJobs(drucker, 0, 999, 1, IntPtr.Zero, 0, out int groesse, out _);
                if (groesse > 0)
                {
                    IntPtr puffer = Marshal.AllocHGlobal(groesse);
                    try
                    {
                        if (EnumJobs(drucker, 0, 999, 1, puffer, groesse, out _, out auftraege))
                        {
                            int schritt = Marshal.SizeOf<JobInfo1>();
                            for (int i = 0; i < auftraege; i++)
                            {
                                JobInfo1 j = Marshal.PtrToStructure<JobInfo1>(puffer + i * schritt);
                                if ((j.StatusFlags & JobFehler) != 0) fehlerhaft.Add(j.Document ?? string.Empty);
                            }
                        }
                    }
                    finally { Marshal.FreeHGlobal(puffer); }
                }
                return new Druckstand(auftraege, problem, fehlerhaft);
            }
            finally
            {
                ClosePrinter(drucker);
            }
        }
        catch (Exception ex) when (ex is Win32Exception or DllNotFoundException or EntryPointNotFoundException or ExternalException)
        {
            EinstellungsSpeicher.Protokollieren(ex);
            return null;
        }
    }
}
