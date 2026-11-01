using Gutachten2025.Models;
using Gutachten2025.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Windows.Storage.Pickers;

namespace Gutachten2025;

/// <summary>Dialoge und Datei-/Ordnerauswahl (IUiService).</summary>
public sealed partial class MainWindow
{
    async Task<ContentDialogResult> DialogZeigenAsync(ContentDialog dialog)
    {
        // Es darf immer nur ein ContentDialog offen sein.
        await _dialogSperre.WaitAsync();
        try
        {
            dialog.XamlRoot = Content.XamlRoot;
            return await dialog.ShowAsync();
        }
        finally
        {
            _dialogSperre.Release();
        }
    }

    static TextBlock Fehlerzeile() => new()
    {
        Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"],
        TextWrapping = TextWrapping.Wrap,
        Visibility = Visibility.Collapsed,
    };

    static TextBlock Hinweiszeile(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        Opacity = 0.75,
        Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
    };

    // ---------- Einstellungen ----------

    static readonly string[] Schriften =
    {
        "Segoe UI", "Aptos", "Calibri", "Cambria", "Arial", "Times New Roman", "Georgia",
        "Garamond", "Palatino Linotype", "Book Antiqua", "Constantia", "Verdana",
    };

    async Task EinstellungenZeigenAsync()
    {
        var darstellung = new RadioButtons { Header = "Darstellung", MaxColumns = 3 };
        foreach (string t in new[] { "Wie Windows", "Hell", "Dunkel" }) darstellung.Items.Add(t);
        darstellung.SelectedIndex = Math.Clamp(ViewModel.Darstellung, 0, 2);

        var skala = new Slider
        {
            Header = "Größe der Oberfläche",
            Minimum = 80, Maximum = 150, StepFrequency = 5, TickFrequency = 10,
            Value = Math.Round(ViewModel.Oberflaechenskala * 100),
            Width = 320, HorizontalAlignment = HorizontalAlignment.Left,
        };

        string? vorlage = ViewModel.VorlagenSchrift();
        var schrift = new ComboBox
        {
            Header = "Schrift im Editor",
            IsEditable = true,
            Width = 320,
            PlaceholderText = "Segoe UI",
        };
        if (vorlage is not null) schrift.Items.Add($"{vorlage} (Word-Vorlage)");
        foreach (string s in Schriften) schrift.Items.Add(s);
        schrift.Text = ViewModel.EditorSchrift;

        var zeile = new CheckBox { Content = "Zeilenlänge im Editor begrenzen (ca. 85 Zeichen, zentriert)", IsChecked = ViewModel.ZeilenbreiteBegrenzt };
        var word = new CheckBox { Content = "Gutachten nach dem Erstellen in Word öffnen", IsChecked = ViewModel.WordOeffnen };
        var vorschlaege = new CheckBox { Content = "Satzvorschläge beim Tippen", IsChecked = ViewModel.Satzvorschlaege };
        var ablage = new TextBlock { Text = ViewModel.AblageordnerText, TextWrapping = TextWrapping.Wrap, Opacity = 0.8 };
        var ablageKnopf = new Button { Content = "Ablageordner wählen…" };

        var panel = new StackPanel { Spacing = 14, MinWidth = 460 };
        panel.Children.Add(darstellung);
        panel.Children.Add(skala);
        panel.Children.Add(schrift);
        panel.Children.Add(zeile);
        panel.Children.Add(new TextBlock { Text = "Verhalten", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        panel.Children.Add(word);
        panel.Children.Add(vorschlaege);
        panel.Children.Add(new TextBlock { Text = "Ablageordner (Klausuren, Gutachten, Notenlisten)", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        panel.Children.Add(ablage);
        panel.Children.Add(ablageKnopf);

        var dialog = new ContentDialog
        {
            Title = "Einstellungen",
            Content = new ScrollViewer { Content = panel, MaxHeight = 600, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            PrimaryButtonText = "Übernehmen",
            CloseButtonText = "Abbrechen",
            DefaultButton = ContentDialogButton.Primary,
        };
        bool ablageWaehlen = false;
        ablageKnopf.Click += (_, _) =>
        {
            ablageWaehlen = true;
            dialog.Hide();
        };

        ContentDialogResult ergebnis = await DialogZeigenAsync(dialog);
        if (ablageWaehlen)
        {
            if (ViewModel.AblageordnerWaehlenCommand.CanExecute(null)) await ViewModel.AblageordnerWaehlenCommand.ExecuteAsync(null);
            return;
        }
        if (ergebnis != ContentDialogResult.Primary) return;

        ViewModel.Darstellung = darstellung.SelectedIndex < 0 ? 0 : darstellung.SelectedIndex;
        ViewModel.Oberflaechenskala = Math.Clamp(skala.Value / 100.0, 0.8, 1.5);
        string gewaehlt = (schrift.SelectedItem as string ?? schrift.Text ?? string.Empty).Replace(" (Word-Vorlage)", string.Empty).Trim();
        ViewModel.EditorSchrift = gewaehlt == "Segoe UI" ? string.Empty : gewaehlt;
        ViewModel.ZeilenbreiteBegrenzt = zeile.IsChecked == true;
        ViewModel.WordOeffnen = word.IsChecked == true;
        ViewModel.Satzvorschlaege = vorschlaege.IsChecked == true;
        ViewModel.Autosave(erzwingen: true);

        DarstellungAnwenden();
        ZoomAnwenden();
        Melden(Meldungsart.Erfolg, "Einstellungen übernommen", "Darstellung und Verhalten wurden gespeichert.");
    }

    public async Task<bool> BestaetigenAsync(string titel, string text, string ja, string nein)
    {
        var dialog = new ContentDialog
        {
            Title = titel,
            Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = ja,
            CloseButtonText = nein,
            DefaultButton = ContentDialogButton.Close,
        };
        return await DialogZeigenAsync(dialog) == ContentDialogResult.Primary;
    }

    public async Task<int> AuswahlAsync(string titel, string text, string erste, string zweite, string abbrechen)
    {
        var dialog = new ContentDialog
        {
            Title = titel,
            Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = erste,
            SecondaryButtonText = zweite,
            CloseButtonText = abbrechen,
            DefaultButton = ContentDialogButton.Close,
        };
        return await DialogZeigenAsync(dialog) switch
        {
            ContentDialogResult.Primary => 1,
            ContentDialogResult.Secondary => 2,
            _ => 0,
        };
    }

    public async Task<string?> TextEingebenAsync(string titel, string vorgabe, string kopf = "Textbaustein")
    {
        var eingabe = new TextBox
        {
            Text = vorgabe,
            AcceptsReturn = false,
            TextWrapping = TextWrapping.Wrap,
            MinWidth = 480,
            PlaceholderText = "Bitte eingeben",
            Header = kopf,
            IsSpellCheckEnabled = true,
            Language = "de-DE",
        };
        var dialog = new ContentDialog
        {
            Title = titel,
            Content = eingabe,
            PrimaryButtonText = "Speichern",
            CloseButtonText = "Abbrechen",
            DefaultButton = ContentDialogButton.Primary,
        };
        return await DialogZeigenAsync(dialog) == ContentDialogResult.Primary ? eingabe.Text : null;
    }

    public async Task<BausteinDaten?> BausteinBearbeitenAsync(string titel, BausteinDaten vorgabe, Func<BausteinDaten, string?> pruefen)
    {
        var text = new TextBox
        {
            Header = "Formulierung",
            Text = vorgabe.Text,
            AcceptsReturn = false,
            TextWrapping = TextWrapping.Wrap,
            MinWidth = 560,
            IsSpellCheckEnabled = true,
            Language = "de-DE",
            PlaceholderText = "Bitte die Formulierung eingeben",
        };
        var kuerzel = new TextBox
        {
            Header = "Kürzel (optional)",
            Text = vorgabe.Kuerzel,
            MaxLength = 25,
            Width = 180,
            PlaceholderText = "z. B. subs1",
        };
        NumberBox Punkte(string kopf, int? wert) => new()
        {
            Header = kopf,
            Minimum = Notenskala.Minimum,
            Maximum = Notenskala.Maximum,
            Value = wert ?? double.NaN,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            PlaceholderText = "–",
            Width = 120,
        };
        NumberBox von = Punkte("ab Punkte", vorgabe.Von), bis = Punkte("bis Punkte", vorgabe.Bis);
        TextBlock fehler = Fehlerzeile();

        var zeile = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        zeile.Children.Add(kuerzel);
        zeile.Children.Add(von);
        zeile.Children.Add(bis);

        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(text);
        panel.Children.Add(zeile);
        panel.Children.Add(Hinweiszeile(
            "Kürzel: im Editor „kürzel“ tippen und Tab drücken. Punktebereich: Bei Noten außerhalb des Bereichs wird der Baustein abgeblendet " +
            "und vor dem Generieren erscheint ein Hinweis. Leer = für alle Noten."));
        panel.Children.Add(fehler);

        var dialog = new ContentDialog
        {
            Title = titel,
            Content = panel,
            PrimaryButtonText = "Speichern",
            CloseButtonText = "Abbrechen",
            DefaultButton = ContentDialogButton.Primary,
        };
        dialog.Resources["ContentDialogMaxWidth"] = 760.0;

        BausteinDaten? ergebnis = null;
        dialog.PrimaryButtonClick += (_, a) =>
        {
            static int? Wert(NumberBox n) => double.IsNaN(n.Value) ? null : (int)Math.Round(n.Value);
            var d = new BausteinDaten(text.Text.Trim(), kuerzel.Text.Trim(), Wert(von), Wert(bis));
            string? meldung = d.Text.Length == 0 ? "Bitte eine Formulierung eingeben." : pruefen(d);
            if (meldung is not null)
            {
                a.Cancel = true;
                fehler.Text = meldung;
                fehler.Visibility = Visibility.Visible;
                return;
            }
            ergebnis = d;
        };
        return await DialogZeigenAsync(dialog) == ContentDialogResult.Primary ? ergebnis : null;
    }

    public async Task<Klausurkonfiguration?> KonfigurationBearbeitenAsync(string klausur, Klausurkonfiguration vorgabe)
    {
        var ausgeben = new CheckBox
        {
            Content = "Teilpunkte im Gutachten ausgeben",
            IsChecked = vorgabe.TeilpunkteAusgeben,
        };
        var bogenPunkte = new CheckBox
        {
            Content = "Rohpunkteschema: Punkte im Bewertungsbogen ausgeben (sonst nur die Anmerkungen, ohne Punkte und Summen)",
            IsChecked = vorgabe.BogenPunkteAusgeben,
        };
        var gliederungFett = new CheckBox
        {
            Content = "„Frage 1“, „Aufgabe 2“, „Tatkomplex 3“, „Fallfrage 4“ im Gutachten fett setzen",
            IsChecked = vorgabe.GliederungFett,
        };
        TextBox Mehrzeilig(string kopf, string inhalt, double hoehe, string platzhalter) => new()
        {
            Header = kopf,
            Text = inhalt,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            Height = hoehe,
            PlaceholderText = platzhalter,
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            IsSpellCheckEnabled = false,
        };
        TextBox aufgaben = Mehrzeilig("Aufgaben – je Zeile „Bezeichnung; Höchstpunkte“", KlausurKonfigDatei.AufgabenText(vorgabe), 120,
            "Aufgabe 1; 20\nAufgabe 2; 15\nZusatzfrage; 5");
        var schluessel = new TextBox
        {
            Header = "Notenschlüssel (optional) – Mindest-Rohpunkte für 1, 2, … 18 Punkte, durch „;“ getrennt",
            Text = KlausurKonfigDatei.SchluesselText(vorgabe),
            PlaceholderText = "leer = linear nach Anteil an der Höchstpunktzahl",
            TextWrapping = TextWrapping.Wrap,
        };
        TextBox kriterien = Mehrzeilig("Formalkriterien – „+Bezeichnung | Satzvorlage“, darunter je Zeile eine Stufe", KlausurKonfigDatei.KriterienText(vorgabe), 260,
            "+Gliederung | {subjekt} {wert}.\nist klar gegliedert\nhätte von einer Gliederung profitiert");
        TextBlock fehler = Fehlerzeile();

        var panel = new StackPanel { Spacing = 12, MinWidth = 640 };
        panel.Children.Add(Hinweiszeile(
            "Gilt nur für diese Klausur (Klausur.json im Klausurordner; wird mit Paketen weitergegeben). " +
            "Teilpunkte erscheinen im Gutachten am Platzhalter ###TEILPUNKTE### der Word-Vorlage, sonst als Absatz nach dem Gutachtentext; ###ROHPUNKTE### liefert die Summe. Mit Rohpunkteschema entfallen die Teilpunkte; der Bewertungsbogen erscheint am Platzhalter ###BEWERTUNGSBOGEN### oder am Ende."));
        panel.Children.Add(ausgeben);
        panel.Children.Add(bogenPunkte);
        panel.Children.Add(gliederungFett);
        panel.Children.Add(aufgaben);
        panel.Children.Add(schluessel);
        panel.Children.Add(kriterien);
        panel.Children.Add(Hinweiszeile(
            "Satzvorlage: {wert} = gewählte Stufe, {subjekt} = „Die 12-seitige Arbeit“ beim ersten und „Sie“ bei weiteren Sätzen, {seiten} = Seitenzahl. " +
            "Beispiel: „+Sprache | Die Ausdrucksweise ist {wert}.“"));
        panel.Children.Add(fehler);

        var dialog = new ContentDialog
        {
            Title = $"Klausureinstellungen: {klausur}",
            Content = new ScrollViewer { Content = panel, MaxHeight = 620, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            PrimaryButtonText = "Speichern",
            CloseButtonText = "Abbrechen",
            DefaultButton = ContentDialogButton.Primary,
        };
        dialog.Resources["ContentDialogMaxWidth"] = 900.0;

        Klausurkonfiguration? ergebnis = null;
        dialog.PrimaryButtonClick += (_, a) =>
        {
            try
            {
                ergebnis = new Klausurkonfiguration
                {
                    TeilpunkteAusgeben = ausgeben.IsChecked == true,
                    BogenPunkteAusgeben = bogenPunkte.IsChecked == true,
                    GliederungFett = gliederungFett.IsChecked == true,
                    Aufgaben = KlausurKonfigDatei.AufgabenLesen(aufgaben.Text),
                    Notenschluessel = KlausurKonfigDatei.SchluesselLesen(schluessel.Text),
                    Formalkriterien = KlausurKonfigDatei.KriterienLesen(kriterien.Text),
                };
            }
            catch (FormatException ex)
            {
                a.Cancel = true;
                fehler.Text = ex.Message;
                fehler.Visibility = Visibility.Visible;
            }
        };
        return await DialogZeigenAsync(dialog) == ContentDialogResult.Primary ? ergebnis : null;
    }

    public async Task<string?> DateiWaehlenAsync(params string[] endungen)
    {
        var picker = new FileOpenPicker
        {
            ViewMode = PickerViewMode.List,
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            CommitButtonText = "Importieren",
        };
        foreach (string endung in endungen) picker.FileTypeFilter.Add(endung);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        var datei = await picker.PickSingleFileAsync();
        return datei?.Path is { Length: > 0 } pfad && File.Exists(pfad) ? pfad : null;
    }

    public async Task<string?> SpeicherortWaehlenAsync(string vorschlag, string endung, string bezeichnung)
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = vorschlag,
            CommitButtonText = "Speichern",
        };
        picker.FileTypeChoices.Add(bezeichnung, new List<string> { endung });
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        var datei = await picker.PickSaveFileAsync();
        return datei?.Path is { Length: > 0 } pfad ? pfad : null;
    }

    public async Task<string?> OrdnerWaehlenAsync()
    {
        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            CommitButtonText = "Als Ablageordner verwenden",
        };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        var ordner = await picker.PickSingleFolderAsync();
        return ordner?.Path is { Length: > 0 } pfad ? pfad : null;
    }
}
