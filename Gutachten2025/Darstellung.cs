using Microsoft.UI.Text;
using Microsoft.UI.Xaml;

namespace Gutachten2025;

/// <summary>Umrechnung plattformneutraler Werte aus Gutachten2025.Core für x:Bind-Funktionsbindungen.</summary>
public static class Darstellung
{
    public static Windows.UI.Text.FontWeight Gewicht(bool fett) => fett ? FontWeights.SemiBold : FontWeights.Normal;

    public static Thickness Links(double einzug) => new(einzug, 0, 0, 0);
}
