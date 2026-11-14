namespace UltimateMacro;

public sealed partial class MainForm
{
    private readonly Label[] helpHotkeys = new Label[4];

    private Control BuildHelp()
    {
        var page = UiTheme.Mark(new TableLayoutPanel
        {
            Name = "HelpPage", Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
            Padding = new Padding(5, 7, 6, 5), Margin = Padding.Empty
        }, UiRole.Canvas);
        page.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 183));
        page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var navigation = UiTheme.Mark(new Panel { Dock = DockStyle.Fill, Padding = new Padding(7, 9, 7, 7), Margin = new Padding(0, 0, 9, 0) }, UiRole.Soft);
        var topics = HelpStack();
        topics.Controls.Add(UiTheme.Mark(HelpLabel("Themen", new Font(Font, FontStyle.Bold), new Padding(9, 2, 0, 8)), UiRole.Muted));
        var updates = HelpStack();
        updates.Dock = DockStyle.Bottom;
        updates.Padding = new Padding(2, 10, 2, 2);
        var version = UiTheme.Mark(HelpLabel($"Version {UpdateCheck.InstalledVersion.ToString(3)}", new Font(Font, FontStyle.Bold), new Padding(7, 0, 0, 7)), UiRole.Muted);
        version.Name = "HelpInstalledVersion";
        ConfigureButton(helpUpdateButton, "Nach Updates suchen", async () => await CheckForUpdatesAsync(manual: true));
        helpUpdateButton.Padding = new Padding(4, 1, 4, 1);
        helpUpdateButton.Dock = DockStyle.Fill;
        helpUpdateButton.Margin = Padding.Empty;
        updates.Controls.AddRange([version, helpUpdateButton]);
        navigation.Controls.Add(topics);
        navigation.Controls.Add(updates);
        var scroll = UiTheme.Mark(new Panel { Name = "HelpArticleScroll", Dock = DockStyle.Fill, AutoScroll = true, Margin = Padding.Empty }, UiRole.Surface);
        scroll.Padding = new Padding(0, 0, SystemInformation.VerticalScrollBarWidth, 0);
        page.Controls.Add(navigation, 0, 0);
        page.Controls.Add(scroll, 1, 0);

        (string Title, TableLayoutPanel Content)[] articles =
        [
            ("Schnellstart", BuildQuickStart()),
            ("Aktionen bearbeiten", BuildActionHelp()),
            ("Wiederholen & klicken", BuildClickHelp()),
            ("Bildziele", BuildImageHelp()),
            ("Bibliothek", BuildLibraryHelp()),
            ("Probleme lösen", BuildTroubleshootingHelp())
        ];
        var buttons = new List<Button>();
        var articleLabels = new Dictionary<Control, Label[]>();
        foreach (var article in articles)
        {
            var button = Button(article.Title);
            button.UseMnemonic = false;
            button.Dock = DockStyle.Fill;
            button.TextAlign = ContentAlignment.MiddleLeft;
            button.Padding = new Padding(8, 0, 4, 0);
            button.Margin = new Padding(0, 0, 0, 4);
            topics.Controls.Add(button);
            buttons.Add(button);
            articleLabels.Add(article.Content, AllControls(article.Content).OfType<Label>().ToArray());
            article.Content.Visible = false;
            scroll.Controls.Add(article.Content);
            button.Click += (_, _) => SelectArticle(article.Content, button);
        }
        scroll.SizeChanged += (_, _) =>
        {
            foreach (var article in articles) WrapArticle(article.Content);
        };
        SelectArticle(articles[0].Content, buttons[0]);
        return page;

        void WrapArticle(Control article)
        {
            var width = Math.Max(1, scroll.ClientSize.Width - scroll.Padding.Right - article.Padding.Horizontal - 27);
            foreach (var label in articleLabels[article]) label.MaximumSize = new Size(width, 0);
        }

        void SelectArticle(Control selected, Button active)
        {
            scroll.SuspendLayout();
            WrapArticle(selected);
            foreach (var article in articles) article.Content.Visible = ReferenceEquals(article.Content, selected);
            selected.BringToFront();
            scroll.ResumeLayout(true);
            scroll.AutoScrollPosition = Point.Empty;
            foreach (var button in buttons)
            {
                var selectedTopic = ReferenceEquals(button, active);
                UiTheme.Mark(button, selectedTopic ? UiRole.ActiveNavigation : UiRole.Navigation);
                button.AccessibleDescription = selectedTopic ? "Aktuelles Hilfethema" : "Öffnet das Hilfethema";
                UiTheme.StyleButton(button, Palette);
            }
        }
    }

    private TableLayoutPanel BuildQuickStart()
    {
        var article = HelpArticle("Dein erster Ablauf");
        var start = HelpSection(article, "Aufnehmen und abspielen");
        HelpSteps(start,
            "Starte mit „Aufnehmen“ und führe die gewünschten Maus- und Tastatureingaben aus. Beende die Aufnahme per Tastenkürzel, damit der Stopp-Klick nicht mit aufgezeichnet wird.",
            "Prüfe die Aktionen unter „Ablauf“. Gib dem Makro einen Namen und klicke auf „Speichern“.",
            "Wähle „Abspielen“ und wechsle während der Startverzögerung zum Zielfenster. „Stopp“ beendet die Wiedergabe und gibt gehaltene Tasten frei.");

        var shortcuts = HelpSection(article, "Aktive Tastenkürzel");
        var grid = HelpStack(2);
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        shortcuts.Controls.Add(grid);
        string[] commands = ["Aufnahme starten", "Aufnahme beenden", "Wiedergabe starten", "Alles stoppen"];
        var keyFont = new Font(Font, FontStyle.Bold);
        for (var index = 0; index < commands.Length; index++)
        {
            var label = HelpLabel("—", keyFont, new Padding(0, 0, 8, 5));
            label.AccessibleName = commands[index] + " – Tastenkürzel";
            helpHotkeys[index] = label;
            grid.Controls.Add(label, 0, index);
            grid.Controls.Add(HelpLabel(commands[index], Font, new Padding(0, 0, 0, 5)), 1, index);
        }
        HelpParagraph(shortcuts, "Unter „Einstellungen“ → „Tastenkürzel“ kannst du die Belegungen ändern. Klicke danach auf „Übernehmen“.");
        return article;
    }

    private TableLayoutPanel BuildActionHelp()
    {
        var article = HelpArticle("Aktionen erstellen und sortieren");
        HelpParagraph(HelpSection(article, "Aktionen hinzufügen"), "Unter „Ablauf“ öffnet „Aktion hinzufügen“ den Editor. Wähle die Aktionsart und stelle Dauer, Anzahl und Abstand ein. Doppelklicke auf eine vorhandene Zeile, um sie zu ändern.");
        HelpParagraph(HelpSection(article, "Reihenfolge ändern"), "Ziehe eine Zeile an die gewünschte Stelle. Die Linie zeigt die Einfügeposition. Halte Strg oder Umschalt für eine Mehrfachauswahl gedrückt. Über „Aktionen“ kannst du Zeilen auch duplizieren, verschieben oder löschen.");
        HelpSteps(HelpSection(article, "Beispiel: F und danach E"),
            "Füge „Taste tippen“ für F und danach „Taste tippen“ für E hinzu.",
            "Füge bei Bedarf „Warten“ zwischen den Tasten oder am Ende des Ablaufs ein.",
            "Aktiviere oben „Bis zum Stopp wiederholen“, wenn die gesamte Tastenfolge in einer Schleife laufen soll.");
        return article;
    }

    private TableLayoutPanel BuildClickHelp()
    {
        var article = HelpArticle("Wiederholen und Mausklicks");
        var repeat = HelpSection(article, "Den ganzen Ablauf wiederholen");
        HelpParagraph(repeat, "Aktiviere „Bis zum Stopp wiederholen“. In den Wiedergabe-Einstellungen entspricht das 0 Durchläufen. Für eine feste Anzahl trägst du dort die gewünschten Durchläufe ein.");
        HelpParagraph(repeat, "Setze „Zeitlimit (s, 0 = aus)“ auf 0, wenn du die Wiedergabe selbst stoppen willst.");
        HelpSteps(HelpSection(article, "Dauerklick unter dem Mauszeiger"),
            "Füge „Mausklick“ hinzu oder bearbeite einen vorhandenen Klick. Aktiviere „Aktuelle Mausposition verwenden“.",
            "Setze „Anzahl“ auf 0 für Dauerklicks. „Dauer“ ist die Haltezeit pro Klick, „Abstand“ die Pause zwischen Klicks.",
            "Starte die Wiedergabe. Bewege den Zeiger bei Bedarf, um die nächsten Klicks an eine andere Stelle zu setzen.");
        HelpParagraph(HelpSection(article, "Klick mit gespeicherter Position"), "Deaktiviere „Aktuelle Mausposition verwenden“ und gib X und Y ein oder wähle „Position in 3 s wählen“. Der Zeiger springt zu dieser Position, bevor die Aktion mit den Klicks beginnt.");
        return article;
    }

    private TableLayoutPanel BuildImageHelp()
    {
        var article = HelpArticle("Bewegliche Bildziele anklicken");
        HelpSteps(HelpSection(article, "Bildziel einrichten"),
            "Füge die Aktion „Bildziel klicken“ hinzu und öffne „Bildziel einrichten…“.",
            "Klicke auf „Zielfenster in 3 s erfassen“ und wechsle zum gewünschten Fenster. Ziehe im aufgenommenen Bild einen Rahmen um den Button. Klicke innerhalb des Rahmens auf die gewünschte Klickstelle.",
            "Teste die Erkennung mit der Vorschau ohne Klick. Übernimm anschließend das Bildziel und speichere die Aktion.");
        var search = HelpSection(article, "Wenn die Position wechselt");
        HelpParagraph(search, "Vor jedem Klick sucht die App das markierte Bild im Zielfenster neu. Bei einem fehlenden oder mehrdeutigen Treffer wartet sie. Stelle eine Wartezeit ein, wenn die Wiedergabe nach einer bestimmten Zeit ohne Treffer stoppen soll.");
        HelpParagraph(HelpSection(article, "Im Hintergrund klicken"), "Aktiviere „Im Hintergrund klicken (Maus bleibt frei)“ und prüfe den Treffer in der Hintergrund-Vorschau. Lass das Zielfenster geöffnet und nicht minimiert. Ob es Hintergrundklicks annimmt, hängt vom Zielprogramm ab.");
        return article;
    }

    private TableLayoutPanel BuildLibraryHelp()
    {
        var article = HelpArticle("Gespeicherte Makros verwalten");
        HelpParagraph(HelpSection(article, "Öffnen und speichern"), "Öffne einen Eintrag links per Doppelklick. „Speichern“ übernimmt deine Änderungen. Über „Datei“ → „Kopie speichern…“ kannst du eine zweite Fassung anlegen.");
        HelpParagraph(HelpSection(article, "Importieren und wiederfinden"), "„Importieren…“ legt eine Kopie einer JSON-Makrodatei in der Bibliothek an. Über „Mehr“ öffnest du den Speicherordner oder aktualisierst die Liste, wenn du dort Dateien geändert hast.");
        HelpParagraph(HelpSection(article, "Umbenennen und löschen"), "Klicke mit der rechten Maustaste auf den Eintrag. „Umbenennen…“ ändert den Dateinamen und den Namensvorschlag beim nächsten Import. „Endgültig löschen…“ entfernt die Datei nach deiner Bestätigung.");
        return article;
    }

    private TableLayoutPanel BuildTroubleshootingHelp()
    {
        var article = HelpArticle("Probleme lösen");
        HelpParagraph(HelpSection(article, "Das Zielfenster nimmt keine Tastendrücke an"), "Prüfe zuerst, ob das Zielfenster beim Start aktiv ist. Wenn Tasten in einem Texteditor ankommen, im Zielprogramm aber fehlen, teste „Einstellungen“ → „Wiedergabe“ → „Tasten als Scan-Codes senden“. Speichere das Makro nach einem erfolgreichen Test.");
        HelpParagraph(HelpSection(article, "Der Ablauf läuft nur einmal"), "Aktiviere „Bis zum Stopp wiederholen“ für die ganze Liste. Prüfe auch das Zeitlimit. Eine einzelne Aktion mit „Anzahl 0“ bleibt aktiv, bis du die Wiedergabe stoppst oder das Zeitlimit greift.");
        HelpParagraph(HelpSection(article, "Der Mauszeiger springt beim Start"), "Öffne die Mausklick-Aktion und aktiviere „Aktuelle Mausposition verwenden“. Prüfe auch, ob im Ablauf eine Aktion „Maus bewegen“ steht.");
        HelpParagraph(HelpSection(article, "Das Bildziel wird nicht gefunden"), "Prüfe die Vorschau. Erfasse den Button neu, wenn sich Fenstergröße, Skalierung oder Aussehen geändert haben. Wähle einen markanten Bereich und lass bei der Vordergrundsuche das Ziel sichtbar.");
        HelpParagraph(HelpSection(article, "Ein Tastenkürzel reagiert nicht"), "Prüfe die aktiven Kürzel im Schnellstart. Wähle unter „Einstellungen“ eine freie Tastenkombination und klicke auf „Übernehmen“. Windows reserviert F12 für Debugger.");
        return article;
    }

    private TableLayoutPanel HelpArticle(string title)
    {
        var article = HelpStack();
        article.Padding = new Padding(18, 14, 18, 9);
        article.Controls.Add(HelpLabel(title, new Font(Font.FontFamily, 17, FontStyle.Bold), new Padding(0, 0, 0, 13)));
        return article;
    }

    private TableLayoutPanel HelpSection(TableLayoutPanel article, string title)
    {
        var section = UiTheme.Mark(HelpStack(), UiRole.Soft);
        section.Dock = DockStyle.Fill;
        section.Padding = new Padding(13, 9, 13, 5);
        section.Margin = new Padding(0, 0, 0, 10);
        section.Controls.Add(HelpLabel(title, new Font(Font.FontFamily, 11, FontStyle.Bold), new Padding(0, 0, 0, 5)));
        article.Controls.Add(section);
        return section;
    }

    private static TableLayoutPanel HelpStack(int columns = 1) => new()
    {
        Dock = DockStyle.Top, ColumnCount = columns, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
        GrowStyle = TableLayoutPanelGrowStyle.AddRows, Margin = Padding.Empty
    };

    private static Label HelpLabel(string text, Font font, Padding margin) => new()
    {
        Text = text, Font = font, AutoSize = true, UseMnemonic = false, Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = margin
    };

    private void HelpParagraph(TableLayoutPanel section, string text) => section.Controls.Add(HelpLabel(text, Font, new Padding(0, 0, 0, 7)));

    private void HelpSteps(TableLayoutPanel section, params string[] steps)
    {
        for (var index = 0; index < steps.Length; index++) HelpParagraph(section, $"{index + 1}.  {steps[index]}");
    }
}
