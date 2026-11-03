using Microsoft.UI.Text;
using Microsoft.UI.Xaml;

namespace Gutachten2025;

/// <summary>Umrechnung plattformneutraler Werte aus Gutachten2025.Core für x:Bind-Funktionsbindungen.</summary>
public static class Darstellung
{
    public static Windows.UI.Text.FontWeight Gewicht(bool fett) => fett ? FontWeights.SemiBold : FontWeights.Normal;

    public static Thickness Links(double einzug) => new(einzug, 0, 0, 0);

    /// <summary>Sichtbar, wenn der Wert falsch ist.</summary>
    public static Visibility Nicht(bool wert) => wert ? Visibility.Collapsed : Visibility.Visible;

    public static bool Umgekehrt(bool wert) => !wert;

    static Microsoft.UI.Xaml.Media.Brush? _warnung;
    static readonly Microsoft.UI.Xaml.Media.SolidColorBrush Durchsichtig = new(Microsoft.UI.Colors.Transparent);

    /// <summary>Rahmen um Pflichtangaben ohne Eingabe (Warnfarbe des Themas), sonst unsichtbar.</summary>
    public static Microsoft.UI.Xaml.Media.Brush Hinweisrahmen(bool offen) =>
        offen
            ? _warnung ??= (Application.Current.Resources.TryGetValue("SystemFillColorCautionBrush", out object? b) && b is Microsoft.UI.Xaml.Media.Brush pinsel
                ? pinsel
                : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Orange))
            : Durchsichtig;
}
