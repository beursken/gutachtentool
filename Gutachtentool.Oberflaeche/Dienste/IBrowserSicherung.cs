namespace Gutachtentool.Oberflaeche.Dienste;

/// <summary>Nur Browser: Gesamtsicherung der im Browser gespeicherten Daten (Menü „Daten“).</summary>
public interface IBrowserSicherung
{
    /// <summary>Alle Daten als ZIP.</summary>
    byte[] SicherungErstellen();

    /// <summary>Ersetzt alle Daten durch die Sicherung (ZIP); danach muss die Seite neu geladen werden.</summary>
    Task SicherungEinspielenAsync(string zipPfad);

    /// <summary>Belegter und verfügbarer Speicher in Bytes; null = unbekannt.</summary>
    Task<(double Belegt, double Verfuegbar)?> BelegungAsync();

    /// <summary>Lädt die Seite neu (nach dem Einspielen einer Sicherung).</summary>
    Task NeuLadenAsync();
}
