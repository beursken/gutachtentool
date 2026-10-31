using System.Globalization;
using Gutachten2025.Services;
using Gutachtentool.Web;
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
EinstellungsSpeicher.Datenordner = Ablagespeicher.Datenordner;
KlausurAblage.StandardWurzel = Ablagespeicher.Klausurordner;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddSingleton<Ablagespeicher>();
builder.Services.AddSingleton<WebUi>();
builder.Services.AddSingleton<WebEditor>();

await builder.Build().RunAsync();
