# Plan für Python-Scripting auf Basis der Signalschicht

Dieses Dokument beschreibt, wie die bestehende Signalschicht (`ISignal`, `ISignalRegistry`) als Grundlage für eine zukünftige Python-Scripting-Integration genutzt werden soll.

Ziel ist, dass ein Python-Skript **genau dieselben Signale** sieht wie RealtimeChart und CsvLogger – inklusive Metadaten (Unit, Format, SourcePath) – und nur über diese abstrahierte Oberfläche mit der Runtime interagiert.

## 1. Ziele und Leitlinien

- **What You See Is What You Get (WYSIWYG)**
  - Alles, was im UI (Signals, Charts, Logger) konfiguriert ist, steht dem Skript 1:1 als Signal zur Verfügung.
  - Keine „versteckten“ Objekte, keine Sonderpfade.

- **Gemeinsame Signalschicht für alle Engines**
  - C#-Scripting und Python-Scripting nutzen dasselbe Modell:
    - `ISignal`
    - `ISignalRegistry`
    - `SignalDescriptor` (mit `Unit`, `Format`, `SourcePath`).

- **Strikte Entkopplung vom UI**
  - Skripte kennen nur Signale, keine UI-Controls.
  - Der Editor erzeugt lediglich eine Liste der freigegebenen Signale und übergibt diese an den ScriptContext.

- **Sicherheits- und Scope-Kontrolle**
  - Ein Skript sieht nur die Signale, die explizit für seine Seite/sein Widget freigegeben sind.
  - Keine globale Freigabe des kompletten Host-Objektmodells.

## 2. ScriptContext-Design

Für jede Script-Engine (C#, Python, …) soll es einen gemeinsamen `ScriptContext` geben, der vom Host aufgebaut wird.

### 2.1. ScriptContext-Inhalt

- `IReadOnlyDictionary<string, ISignal> Signals`
  - Key: stabiler Name im Skript (z.B. `"speed"`, `"temperature"`).
  - Value: `ISignal` aus `ISignalRegistry`, inklusive Descriptor.

- Optionale Services (später):
  - `ILogger` oder einfacher Logging-Callback.
  - `IDialogService` für Nutzer-Interaktion (MessageBox, Input).
  - Timer-/Delay-Funktionen.

### 2.2. Befüllung des ScriptContext

Der Host baut den Context in etwa so:

1. UI-Seite (Layout) definiert, welche Signale für ein Skript sichtbar sind:
   - Analog zu `ChartSeriesDefinitions` oder `CsvSignalPaths`.
   - Z.B. eine Liste von `TargetPath`s und optionalen Alias-Namen.
2. Für jeden Eintrag:
   - Auflösung des `TargetPath` zu einem `Item` (wie im Datenfluss-Dokument beschrieben).
   - Bestimmen eines `SourcePath = item.Path ?? targetPath`.
   - `HostRegistries.Signals.TryGetBySourcePath(sourcePath, out var signal)`.
3. Eintrag im Context:
   - Key: Anzeigename oder explizit konfigurierter Alias.
   - Value: das gefundene `ISignal`.

Beispiel-Schema (Pseudo-C#):

```csharp
var contextSignals = new Dictionary<string, ISignal>(StringComparer.OrdinalIgnoreCase);
foreach (var (alias, targetPath) in configuredScriptSignals)
{
    if (!TryResolveDataItem(targetPath, pageName, out var item) || item is null)
        continue;

    var sourcePath = item.Path ?? targetPath;
    if (!HostRegistries.Signals.TryGetBySourcePath(sourcePath, out var signal) || signal is null)
        continue;

    contextSignals[alias] = signal;
}

var context = new ScriptContext(contextSignals, services: ...);
```

## 3. ScriptEngine-Schnittstelle

Um verschiedene Script-Sprachen zu unterstützen, bietet der Host eine generische Engine-Schnittstelle an.

### 3.1. Interface-Idee

```csharp
public interface IScriptEngine
{
    string Language { get; } // z.B. "csharp", "python"

    Task ExecuteAsync(
        string code,
        ScriptContext context,
        CancellationToken cancellationToken = default);
}
```

- `code` – Quelltext des Skripts.
- `context` – Signale und Services, die dem Skript zur Verfügung stehen.
- `Language` – Kennung der Engine.

### 3.2. Python-Implementierung (High-Level)

- Eine `PythonScriptEngine` implementiert `IScriptEngine`.
- Intern könnte sie z.B. CPython einbetten oder einen externen Python-Prozess starten.
- Wichtiger Punkt: Die Engine übersetzt `ISignal` in Python-Objekte.

## 4. Python-Sicht auf Signale

Das Python-Skript soll eine sehr einfache, klare API sehen.

### 4.1. Mapping zu Python-Objekten

Beispiel: Jedes `ISignal` wird in Python als `Signal`-Objekt repräsentiert:

```python
class Signal:
    def __init__(self, descriptor, get_value, set_value):
        self.id = descriptor.Id
        self.name = descriptor.Name
        self.unit = descriptor.Unit
        self.format = descriptor.Format
        self.source_path = descriptor.SourcePath
        self.is_writable = descriptor.IsWritable
        self.category = descriptor.Category
        self._get_value = get_value
        self._set_value = set_value

    @property
    def value(self):
        return self._get_value()

    @value.setter
    def value(self, v):
        if not self.is_writable:
            raise RuntimeError(f"Signal '{self.name}' is read-only")
        self._set_value(v)
```

- `get_value`/`set_value` sind Delegates/Funktionsobjekte, die intern auf `ISignal.Value` zugreifen.
- Metadaten (Unit, Format, SourcePath) sind als Felder verfügbar.

### 4.2. Bereitstellung im Skript

Aus dem `ScriptContext` wird in der Python-Umgebung z.B. ein Dictionary gebaut:

```python
signals = {
    "speed": Signal(...),
    "temperature": Signal(...),
}
```

Dann kann ein Python-Script so aussehen:

```python
speed = signals["speed"]
temperature = signals["temperature"]

if temperature.value > 80.0:
    speed.value = 0  # Not-Aus
```

### 4.3. WYSIWYG-Kopplung

- Die UI definiert, welche Signale (TargetPaths) für ein Skript relevant sind.
- Diese werden auf SourcePaths/Signale gemappt und erhalten einen Aliasnamen.
- Das Skript kennt nur diese Aliasnamen und optional die Metadaten.

Beispiel:

- In der UI wird konfiguriert:
  - `speed|UdlBook/Page1/udl1/m310/set/request`
  - `temperature|UdlBook/Page1/udl1/m310/Read/Value`
- Im `ScriptContext` entsteht:
  - `Signals["speed"]` → `ISignal` mit `SourcePath` des Set-Requests.
  - `Signals["temperature"]` → `ISignal` mit `SourcePath` des Read-Werts.
- In Python greift man nur auf `signals["speed"]` und `signals["temperature"]` zu.

## 5. Laufzeit- und Fehlerbehandlung

### 5.1. Ausführung

- Host startet einen Script-Job (z.B. durch ein UI-Event oder beim Seitenwechsel).
- `IScriptEngine.ExecuteAsync(code, context, token)` wird aufgerufen.
- Signale im Context sind live mit dem Host verbunden – Lese- und Schreibzugriffe laufen über `ISignal` → `DataRegistry`.

### 5.2. Logging und Diagnose

- Python-Scripts sollen über einen einfachen Logging-Kanal verfügen (z.B. `log.info(...)`).
- Fehler/Exceptions werden vom Host gefangen und dem Nutzer angezeigt:
  - Fehlermeldung
  - ggf. Stacktrace/Zeilennummer.

## 6. Zusammenspiel mit RealtimeChart und CsvLogger

Durch die gemeinsame Signalschicht ergibt sich mittelfristig folgendes Bild:

- **CsvLogger**
  - Nutzt heute bereits `ISignal` (mit Fallback auf `Item`).
  - Python-Scripts können dieselben Signale verwenden wie der Logger.

- **RealtimeChart**
  - Nutzt heute `Item` + Timer-Sampling über `Item.Value`.
  - Perspektivisch kann er auf `ISignal` umgestellt werden:
    - Statt `TryResolveSeriesItem(...)` → `ISignalRegistry.TryGetBySourcePath(...)`.
    - Event-basiertes Sampling über `Signal.ValueChanged` wäre möglich.

- **Python-Scripts**
  - Arbeiten ausschließlich auf `ISignal`.
  - Sehen Metadaten (Unit, Format, SourcePath) identisch zu Chart und Logger.

Damit ist sichergestellt, dass ein einmal konfiguriertes Signal (inkl. Pfad, Unit, Format) in **allen drei Welten** konsistent verwendet werden kann:

1. Visualisierung (RealtimeChart)
2. Aufzeichnung (CsvLogger)
3. Logik/Automation (Python-Scripts)
