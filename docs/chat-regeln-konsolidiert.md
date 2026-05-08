# Konsolidierte Chatregeln

Diese Datei fasst die im Repository sichtbar definierten Copilot- und Instruktionsregeln zusammen.

Ergaenzt sind auch persistente Nutzerpraeferenzen, sofern sie ausserhalb des Repositories gespeichert wurden.

Nicht enthalten sind interne System-, Plattform- oder Laufzeitregeln, die nicht als Datei im Workspace oder in der User-Memory vorliegen.

## Quellen

- `.github/copilot-instructions.md`
- `.github/instructions/python-files.instructions.md`
- Persistente User-Memory: `preferences.md`
- Repository-Memory: `ui-theme-guidelines.md`

## Persistente Nutzerpraeferenzen

Diese Regeln sind nicht im Repository abgelegt, sondern in der persistenten User-Memory.

- Chat-Antworten sollen auf Deutsch erfolgen.
- Code, Code-Kommentare und nutzerseitige Dialogtexte im Code sollen auf Englisch bleiben.

## Allgemeine Arbeitsregeln

- Wenn die gebundene Python-Helper-API unter `src/Hornetstudio.Host/Python/**` oder `src/Hornetstudio.Editor/Templates/ui_python_client/**` geaendert oder erweitert wird, muessen die Dokumentationen `src/Hornetstudio.Host/Python/Integration/ui-python-client-commands.md` und `src/Hornetstudio.Editor/Templates/ui_python_client/COMMANDS.md` im selben Change mit aktualisiert werden.
- `src/Hornetstudio.Host/Python/Integration/ui-python-client-commands.md` gilt als Source of Truth fuer vordefinierte Python-Client-Kommandos.
- Wenn Python-Bridge-Verhalten, generierte Python-Ordnerinhalte, Template-Workflow oder Python-Interaktionsargumente geaendert werden, muessen `src/Hornetstudio.Host/Python/Integration/python-system-overview.md` und `src/Hornetstudio.Editor/Templates/PYTHON_SYSTEM.md` im selben Change mit aktualisiert werden.
- Bei Arbeiten an Python-Templates, Python-Umgebungen oder generierten Python-Skripten sollen `src/Hornetstudio.Host/Python/Integration/python-system-overview.md` und `src/Hornetstudio.Host/Python/Integration/ui-python-client-commands.md` zuerst konsultiert werden.

## Wartungsregel

- Alle Vorkommen von `FilteredSignals` sollen entfernt werden, da es sich um veralteten Legacy-Code handelt.
- Als Ersatz soll `EnhancedSignal` verwendet werden.

## UI- und Theme-Regeln

- Neue UI-Elemente muessen immer dem Theme-Regelwerk entsprechen.
- Neue Controls, Buttons und Standardzustande sollen theme-abgeleitete Brushes oder Palettenwerte verwenden, damit sie automatisch auf Theme-Wechsel reagieren.
- Statt hart codierter Farben sollen bevorzugt `ThemePalette` oder vorhandene effektive Theme-Farben wie `EffectiveBody*`, `EffectiveHeader*` oder `EffectiveAccent*` verwendet werden.
- Icon-Farben sollen an die Theme-Farben angebunden werden, statt feste Farbwerte direkt im Asset oder Control zu hinterlegen.
- Explizite Farben sollen nur fuer besondere Zustaende wie Error, Running oder Warning eingesetzt werden und sowohl in hellem als auch in dunklem Theme lesbar bleiben.

Kurz erklaert:

- Icon-Farbe anpassen: Icons nicht mit festen Default-Farben einbauen, sondern ihre Foreground-, Fill- oder Brush-Bindung an Theme-Werte koppeln.
- `ThemePalette` verwenden: Farben fuer neue UI-Elemente aus der Theme-Palette oder aus bestehenden effektiven Theme-Properties beziehen, damit Hover-, Idle- und Kontrastverhalten konsistent bleiben.

## Python-spezifische Regeln

Diese Regeln gelten fuer Dateien mit dem Anwendungsbereich `**/*.py`.

- Vor Arbeiten an Python-Dateien soll `src/Hornetstudio.Host/Python/Integration/python-system-overview.md` gelesen werden, um Runtime- und Ordnerstruktur zu verstehen.
- Vor Arbeiten an Python-Dateien soll `src/Hornetstudio.Host/Python/Integration/ui-python-client-commands.md` gelesen werden, um die unterstuetzte Helper-API zu kennen.
- Liegt die Python-Datei in einem generierten Skript- oder Environment-Ordner, sollen zusaetzlich nahegelegene `PYTHON_SYSTEM.md`- und `ui_python_client/COMMANDS.md`-Dateien konsultiert werden, sofern vorhanden.
- Python-Dateien mit Template-Charakter sollen bewusst einfach gehalten werden.
- Fuer einfache Textargumente in `InteractionRules` soll bevorzugt `args.get("value")` verwendet werden, sofern nicht explizit ein umfangreicheres JSON-Payload benoetigt wird.

## Kurzfassung

- Python-bezogene Aenderungen immer mit der passenden Dokumentation koppeln.
- Python-Dokumentation zuerst lesen, bevor Templates oder generierte Skripte angepasst werden.
- Neue oder geaenderte Python-Argumentbehandlung moeglichst einfach halten.
- `FilteredSignals` nicht weiterverwenden, sondern auf `EnhancedSignal` migrieren.