# Ultimate Macro

A portable Windows desktop macro recorder with a graphical action editor.

## Run

1. Download [UltimateMacro-Windows-x64.zip](https://github.com/adrianmue/UltimateMacro/releases/latest/download/UltimateMacro-Windows-x64.zip) from the [latest release](https://github.com/adrianmue/UltimateMacro/releases/latest).
2. Extract the ZIP into a writable folder, then open **UltimateMacro.exe**. The app runs on Windows 10/11 x64 without an installer, Visual Studio, or a separate .NET installation.

Keep `Examples` beside the executable. When updating an existing copy, close the app, then extract the new ZIP into the same folder. Keep your `Recordings` folder and settings files to carry over your saved macros and preferences.

Ultimate Macro checks GitHub for a newer Windows package when it starts. The **Hilfe** tab shows the installed version and offers **Nach Updates suchen**. Confirm the update to download and verify the ZIP, replace the executable, and restart. Your recordings and settings remain in place. For a manual download, choose **Hilfe → GitHub-Releases öffnen**.

Choose **Wie Windows**, **Hell**, or **Dunkel** under **Einstellungen → Darstellung**. **Wie Windows** is the default and follows the Windows app theme even while Ultimate Macro is running. Explicit light/dark choices, including those saved in earlier releases, override Windows. The choice is saved separately from your macros in `appearance.json` beside the executable.

## Default controls

| Control | Default hotkey |
| --- | --- |
| Start recording | F8 |
| Stop recording | F9 |
| Start playback | F10 |
| Stop everything and release held inputs | F11 |

These hotkeys work while another application is focused. Change them in **Einstellungen → Tastenkürzel**, then click **Übernehmen**. Combinations such as `Ctrl+Alt+F8` are supported. Windows reserves F12 for debuggers, so it cannot be selected as a control hotkey. Windows must be able to register all four hotkeys; conflicts produce a message and preserve the previous working configuration when it can be restored.

## Record a macro

1. Choose which inputs to capture in **Einstellungen → Aufnahme**: keyboard, mouse buttons, movement, and wheel.
2. Start with F8, perform your actions, and finish with F9.
3. Review the Action list. Double-click a row to edit it.
4. Enter a **Makroname** and click **Speichern**. If no name is entered, a name dialog opens. The `.macro.json` file goes into **Recordings**, beside the executable, and includes both actions and configuration.
5. Press F10 and focus the target window during the countdown.

Recording replaces the current list by default. Enable **Append recording to current list** to add to it. **Undo recording** restores the list from before the most recent recording. Use the stop hotkey to avoid adding clicks on the app's controls to your recording.

Mouse movement is sampled at the configured interval (default 15 ms). Actual elapsed delays, key down/up events, key repeat events, buttons, dragging, vertical/horizontal wheel input, and absolute cursor coordinates are recorded. Keys/buttons already held when recording starts are ignored until released. The app's own playback input and the four control hotkey keys are excluded. Recording stops automatically at approximately 200,000 list entries.

## Saved recordings and lists

The **Bibliothek** on the left shows the saved macros. Choose one and click **Öffnen**, double-click it, or press Enter while the list is focused. Loading opens its actions and settings for review and editing. Unsaved changes are protected by a save prompt.

- **Speichern** saves the current named macro into `Recordings`. Saving the loaded name updates that file. Changing the name creates another named file.
- **Datei → Kopie speichern…** saves a separate copy.
- **Importieren…** copies an existing `.macro.json` or `.json` file into the library under a name of your choice. Ctrl+O also imports.
- **Mehr → Bibliothek aktualisieren** finds files added or changed outside the app. The list also refreshes after saving or importing.
- **Mehr → Speicherordner öffnen** opens `Recordings` in File Explorer.
- **Umbenennen…** in a library row's context menu changes the filename and the name inside the macro, so importing it again suggests the new name. The file stays in its current folder, the macro stays selected, and unsaved edits remain open.
- **Endgültig löschen…** is available from **Mehr**, the library row's context menu, or the Delete key. A confirmation shows the selected file. Deletion is permanent and does not use the Recycle Bin. If that macro has unsaved edits open, they remain in the editor as a draft.

Choosing an existing name asks before replacing a different macro. Names can contain spaces and Unicode characters, but must be valid Windows filenames of up to 120 characters. Files previously saved directly beside the executable with the `.macro.json` extension also appear in the selector, marked **(beside app)**. Save them to add a library copy; import older files saved elsewhere.

## Build a manual list

In **Ablauf**, each row shows the action, its target, and the relevant details. Drag rows to reorder them; the line shows where they will be inserted. Select multiple rows with Ctrl or Shift first to move them together. The list scrolls when you drag near its top or bottom edge. Click **Speichern** to keep the new order. Click **Aktion hinzufügen** to open the editor. Double-click a row to edit it. Use **Aktionen** or right-click a row to duplicate, move, or delete it. **Position in 3 s wählen** captures the cursor position after a short countdown.

All action times are in milliseconds. 1000 ms = 1 second. Keys accept names such as `A`, `1`, `Space`, `Enter`, `Tab`, `Ctrl`, `Shift`, `Alt`, `Win`, `Escape`, `Left`, `PageUp`, `F1`, `LControlKey` or `RControlKey`. Modifier shortcuts use separate KeyDown / KeyPress / KeyUp actions.

| Action | Fields and behavior |
| --- | --- |
| Delay | Wait for Duration ms. |
| KeyPress | Tap Key Count times. Duration is the hold time per tap; Interval is the gap between taps. |
| KeyDown / KeyUp | Explicitly press/release Key. Supports overlapping holds and shortcuts. |
| KeyHold | Hold Key for Duration ms. Duration 0 holds until stopped. |
| KeyRepeat | Repeat Key using the count/duration rules below. |
| MouseMove | Move to absolute X/Y. Duration 0 is instant; positive Duration smoothly interpolates from the current cursor position. |
| MouseClick | Click Left/Right/Middle/X1/X2 at the current pointer position by default for new actions. Clear **Aktuelle Mausposition verwenden** to move to X/Y before the first click. Count 0 repeats until stopped. Duration is the hold time per click, Interval is the gap between clicks. |
| Bildziel klicken | Find a marked button or image in the selected window before each click. Optional background mode leaves the system mouse alone. Count 0 repeats until stopped. |
| MouseDown / MouseUp | Move to X/Y and press/release the mouse button. Insert movement actions between them to drag. |
| MouseScroll | Key = Vertical or Horizontal. Y = signed wheel delta; +120 is one up/right notch, -120 is one down/left notch. Count multiplies the delta. Scrolls at the current cursor position. |
| Text | Type the text as Unicode, with Interval ms between UTF-16 characters. |

### F, dann E, bis zum Stopp

Lege **Taste tippen → F** und danach **Taste tippen → E** als eigene Zeilen an, jeweils mit **Anzahl 1**. Setze bei Bedarf eine **Warten**-Zeile dazwischen. Aktiviere **Bis zum Stopp wiederholen** neben **Abspielen**. Das wiederholt den ganzen Ablauf, auch bei Aufzeichnungen. Beende ihn mit **F11** oder deinem Stopptaster. Für einen ausschließlich manuellen Stopp muss **Einstellungen → Zeitlimit** auf **0** stehen. Das fertige Beispiel findest du unter **Bibliothek → Mehr → Beispiele öffnen** und kannst es über **Importieren…** laden.

Für **Moon Sweepers** und andere Apps, die virtuelle Tastencodes ignorieren, aktiviere **Einstellungen → Wiedergabe → Tasten als Scan-Codes senden** und speichere das Makro. Das F→E-Beispiel hat die Option bereits aktiviert und übernimmt das Timing von Test B. Die Option gilt für manuell angelegte Tastenaktionen; aufgezeichnete Tasten behalten ihre eigenen Scan-Codes, und **Text eingeben** sendet weiterhin Text. Die Umrechnung nutzt das Tastaturlayout des Vordergrundfensters. Bei Sondertasten ohne unterstützten Scan-Code bleibt die virtuelle Eingabe erhalten.

### Bildziel klicken: einem beweglichen Button folgen

1. Wähle **Aktion hinzufügen → Bildziel klicken → Bildziel einrichten**.
2. Klicke auf **Zielfenster in 3 s erfassen** und wechsle während des Countdowns zum gewünschten Fenster. Lasse es im Vordergrund. Falls der Dialog nicht automatisch wieder nach vorne kommt, wechsle mit Alt+Tab zurück.
3. Ziehe auf dem Standbild einen engen Rahmen um den Button (12 × 12 bis 480 × 320 Pixel). Klicke dann innerhalb des Rahmens auf den gewünschten Klickpunkt. Nimm markante Schrift oder ein Symbol mit auf und vermeide viel wechselnden Hintergrund.
4. Klicke auf **Vorschau in 3 s (ohne Klick)** und wechsle erneut zum Zielfenster. Die Vorschau zeigt auf einem frischen Standbild den Treffer und das Fadenkreuz. Sie bewegt die Maus nicht und löst keinen Klick aus.
5. Wähle **Bildziel übernehmen** und **Aktion speichern**. Stelle bei Bedarf Maustaste, Anzahl, Dauer und Abstand ein. Der markierte Ausschnitt und der relative Klickpunkt werden mit dem Makro gespeichert.

Das Makro sucht vor jedem einzelnen Klick neu, auch bei **Count 0**. Wenn der Treffer im Fensterbild fehlt oder mehrdeutig ist, wartet es ohne Klick. Standardmäßig wartet es unbegrenzt, bis ein Treffer gefunden oder das Makro gestoppt wird. Mit einer positiven **Wartezeit in s** bricht es nach dieser Zeit ohne Treffer ab. **F11** bzw. der konfigurierte Stopptaster wirkt auch während der Suche. Die Wartezeit gilt je Klick und wird nicht durch den Geschwindigkeitsfaktor verändert.

Bei ähnlichen Buttons hilft ein stabiles Unterscheidungsmerkmal im Rahmen, etwa die Beschriftung oder ein benachbartes Symbol. Bei kleinen Farbabweichungen kannst du **Ähnlichkeit %** schrittweise senken und den Klickpunkt in der Vorschau prüfen. Bei **100 %** müssen die Pixel exakt übereinstimmen.

Im normalen Modus muss das ausgewählte Fenster im Vordergrund sein. Nach dem Laden einer gespeicherten Liste muss es anhand von Fenstertitel und Anwendung eindeutig sein. Falls sich der Titel dauerhaft geändert hat, erfasse es erneut. Die Vorlage sucht in Originalgröße: Positionen dürfen sich ändern, aber Zoom, DPI, starke Farbwechsel oder sehr schnelle Animationen können einen Treffer verhindern. Im Makro wird nur der markierte Ausschnitt gespeichert, nicht das gesamte Fensterbild.

### Bildziel im Hintergrund klicken

Aktiviere im Bildziel-Dialog **Im Hintergrund klicken (Maus bleibt frei)**. Nutze dann **Hintergrund-Vorschau (ohne Klick)**. Du kannst das Zielfenster mit einem anderen Fenster verdecken und musst für die Vorschau oder Wiedergabe nicht dorthin wechseln. Das Ziel muss geöffnet und darf nicht minimiert sein. Die Option wird pro Bildziel gespeichert.

Das Tool sendet Hintergrundklicks an das erkannte Fenster und bewegt den Systemmauszeiger dabei nicht. Es aktiviert das Zielfenster nicht. Das funktioniert mit Anwendungen, die ein Hintergrundbild liefern und Klicknachrichten akzeptieren. Manche Anwendungen, insbesondere Spiele oder hardwarebeschleunigte Oberflächen, liefern kein brauchbares Bild, ignorieren diese Klicks oder reagieren mit eigener Fokusänderung. Ein Treffer in der Vorschau bestätigt nur die Bildsuche; teste zunächst einen einzelnen Klick. Es gibt keinen automatischen Wechsel auf normale Mausklicks. Normale Maus- oder Tastaturaktionen in derselben Liste verwenden weiterhin den Vordergrund.

### Repeat a key

| Count | Duration | Behavior |
| --- | --- | --- |
| Positive | Positive | Spread Count presses over Duration ms; this determines the press interval. |
| Positive | 0 | Perform Count presses at Interval ms between press starts. |
| 0 | Positive | Repeat at Interval ms until Duration expires. |
| 0 | 0 | Repeat at Interval ms until stopped. |

For example, KeyRepeat / A / Count 20 / Duration 5000 produces 20 presses over 5 seconds. KeyRepeat / Space / Count 0 / Duration 0 / Interval 100 repeats until F11. Repeat taps are held for at most 30 ms (or half the interval). Windows scheduling and target application responsiveness limit very fast timings.

An indefinite KeyHold, KeyRepeat, or MouseClick occupies its row until stopped, so subsequent rows do not run. For continuous clicks, choose MouseClick, set Count to 0, and set Duration/Interval to your desired hold time/gap; F11 (or your configured stop hotkey) stops it. The stop-after-seconds limit also stops continuous clicks. KeyDown and MouseDown let you keep inputs held while proceeding to later rows. Any remaining held inputs are released at the end of each playback loop, on cancellation, and on errors.

## Playback settings

- **Durchläufe:** number of passes through the whole list; 0 repeats until stopped. **Bis zum Stopp wiederholen** switches this to 0.
- **Between loops:** delay after each pass.
- **Start countdown:** time to select the target application before any input is sent.
- **Stop after seconds:** optional total runtime cap, starting after the countdown. 0 disables it.
- **Speed multiplier:** 0.1× to 10×. Scales action timings and loop delays. Countdown and runtime cap use real time.

Pending settings are applied when starting recording/playback or saving. Files are saved through a temporary file before replacement. The app asks to save unsaved changes before opening another list, clearing the list, or closing.

## Examples and JSON

**Bibliothek → Mehr → Beispiele öffnen** opens editable examples, including the F-then-E sequence that repeats until stopped. Set suitable coordinates and keys before using them in your target app.

If a manually built keyboard sequence works in a text editor but not in the target app, import **06 - Input test A - Virtual keys** and **07 - Input test B - Scan codes** separately. Both run F → E once, after a four-second countdown. Test both with the target app focused in the same state, and confirm that pressing the keys by hand works there. A uses the normal manual-action input; B uses the scan-code path used by recorded keys. Hold and delay times are identical in both files so you can compare the two input paths. A successful test in a text editor alone does not establish that the target app accepts the input.

You can also create a manual list in a text editor:

```json
{
  "Version": 1,
  "Actions": [
    { "Action": "KeyHold", "Key": "W", "DurationMs": 2000 },
    { "Action": "Delay", "DurationMs": 500 },
    { "Action": "KeyRepeat", "Key": "Space", "Count": 10, "DurationMs": 5000 }
  ],
  "Playback": {
    "Loops": 1,
    "LoopDelayMs": 100,
    "StartDelayMs": 3000,
    "MaximumRunSeconds": 0,
    "Speed": 1
  }
}
```

Omitted Recording and Hotkeys sections use defaults. Recorded key rows also store ScanCode and Extended to preserve the physical keyboard key. Editing the key clears that recorded scan code. Macro files are local JSON; they do not execute scripts or shell commands.

## Windows behavior

Input goes to whichever application currently has focus. Mouse coordinates span the entire virtual desktop, including monitors to the left/above the primary display. Keep the target window position and monitor layout consistent for recordings. Release physical keys/buttons before playback.

For an administrator-level target, run Ultimate Macro as administrator too. Windows secure desktops, some protected apps, and some games reject simulated input. The tool uses standard Windows input APIs; it does not bypass those restrictions.

Recording is enabled only when explicitly started. Recorded keystrokes may contain sensitive information; save and share macro files accordingly. The app does not upload recordings or require network access during normal use. The executable is unsigned, so Windows may show an unknown-publisher prompt.

## Build from source

To build your own copy on Windows, install Git and the .NET 10 SDK. In PowerShell, run:

```powershell
git clone https://github.com/adrianmue/UltimateMacro.git
cd UltimateMacro
powershell -NoProfile -ExecutionPolicy Bypass -File .\Build.ps1
.\dist\UltimateMacro.exe
```

The build creates `dist` in the cloned folder. To also create the portable ZIP in `release`, pass `-Package` to `Build.ps1`.

To run the built-in verification suite, use `UltimateMacro.exe --self-test`. It verifies action validation, named library save/load, image matching and repeated clicks, file round trips, count/timing modes, cancellation and input cleanup. Results are saved as `self-test-results.txt` beside the executable. Run while the desktop is unlocked and do not change focus during the brief native-input test. Add `--skip-native` to exclude global keyboard and mouse input. Add `--test-capture` to also verify capture, preview and window-local clicks in dedicated foreground and covered test windows. Library/UI tests use isolated temporary files and preserve your recordings and preferences.
