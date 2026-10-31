using System.Globalization;
using Gutachten2025.Services;
using Gutachtentool.Oberflaeche.Dienste;
using Gutachtentool.Web.Dienste;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

// Deutsche Formate für Zahlen (Punkte), Datumsangaben und Sortierung
CultureInfo kultur = CultureInfo.GetCultureInfo("de-DE");
CultureInfo.DefaultThreadCurrentCulture = kultur;
CultureInfo.DefaultThreadCurrentUICulture = kultur;
CultureInfo.CurrentCulture = kultur;
CultureInfo.CurrentUICulture = kultur;

// Virtuelles Dateisystem des Browsers: Ablage und Programmdaten unter /gt (wird in IndexedDB gesichert).
EinstellungsSpeicher.Datenordner = WebPlattform.Datenordner;
KlausurAblage.StandardWurzel = WebPlattform.Klausurordner;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<Gutachtentool.Oberflaeche.App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddSingleton<IPlattform, WebPlattform>();
builder.Services.AddSingleton<UiDienst>();
builder.Services.AddSingleton<EntwurfEditor>();

await builder.Build().RunAsync();
