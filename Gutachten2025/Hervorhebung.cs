using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using System;

namespace Gutachten2025;

/// <summary>Angehängte Eigenschaft: hebt Suchbegriffe (durch Leerzeichen getrennt) in einem TextBlock hervor.</summary>
public static class Hervorhebung
{
    public static readonly DependencyProperty BegriffeProperty = DependencyProperty.RegisterAttached(
        "Begriffe", typeof(string), typeof(Hervorhebung), new PropertyMetadata(null, (d, _) => Anwenden(d as TextBlock)));

    public static string? GetBegriffe(DependencyObject d) => (string?)d.GetValue(BegriffeProperty);

    public static void SetBegriffe(DependencyObject d, string? wert) => d.SetValue(BegriffeProperty, wert);

    // Merkt sich, ob für den TextBlock bereits auf Textänderungen gehört wird.
    static readonly DependencyProperty BeobachtetProperty = DependencyProperty.RegisterAttached(
        "Beobachtet", typeof(bool), typeof(Hervorhebung), new PropertyMetadata(false));

    static void Anwenden(TextBlock? t)
    {
        if (t is null) return;
        if (!(bool)t.GetValue(BeobachtetProperty))
        {
            t.SetValue(BeobachtetProperty, true);
            t.RegisterPropertyChangedCallback(TextBlock.TextProperty, (d, _) => Anwenden(d as TextBlock));
        }
        t.TextHighlighters.Clear();
        string? begriffe = GetBegriffe(t);
        string text = t.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(begriffe) || text.Length == 0) return;

        var markierung = new TextHighlighter();
        if (Application.Current.Resources.TryGetValue("AccentFillColorSecondaryBrush", out object? b) && b is Brush pinsel)
            markierung.Background = pinsel;
        if (Application.Current.Resources.TryGetValue("TextOnAccentFillColorPrimaryBrush", out object? f) && f is Brush schrift)
            markierung.Foreground = schrift;

        foreach (string wort in begriffe.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            int i = 0;
            while ((i = text.IndexOf(wort, i, StringComparison.CurrentCultureIgnoreCase)) >= 0)
            {
                markierung.Ranges.Add(new TextRange { StartIndex = i, Length = wort.Length });
                i += wort.Length;
            }
        }
        if (markierung.Ranges.Count > 0) t.TextHighlighters.Add(markierung);
    }
}
