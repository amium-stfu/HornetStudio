# Amium Item Communication Structure

Dieses Dokument beschreibt die Zielstruktur fuer die gemeinsame Kommunikation ueber `Amium.Item`.

## Ziel

`Amium.Item` ist die zentrale Kommunikationsstruktur zwischen Runtime, Host, UI, Widgets, Skripten und zukuenftigen externen Clients.

Transportwege wie UDL, CAN, MQTT, Python oder lokale Plugins duerfen eigene Protokolle besitzen. Fachlich werden Daten aber immer in dieselbe Struktur uebersetzt:

```text
Item path + parameter + value
```

Damit gibt es fuer den Host und die UI ein gemeinsames Modell, unabhaengig davon, ob ein Wert von einem UDL-Client, einem ESP32, einer Simulation, einem Python-Skript oder einem internen Widget stammt.

## Grundmodell

Ein `Item` ist ein adressierbarer Knoten in einem Baum.

```text
Device01
├─ Read
├─ Set
└─ Raw
```

Jeder Knoten besitzt Parameter.

```text
Name
Path
Value
Unit
Format
Kind
Writable
WritePath
WriteMode
```

Wichtig:

- `Read`, `Set` und `Raw` sind Child-Items.
- `Value`, `Unit`, `Format` und aehnliche Metadaten sind Parameter.
- `Value` ist kein Child-Item, sondern `Item.Params["Value"]`.

Beispiel:

```text
Runtime.Mqtt.Device01.Read
  Params["Value"] = 23
  Params["Unit"] = "ABC"

Runtime.Mqtt.Device01.Set
  Params["Value"] = 24
  Params["Unit"] = "ABC"

Runtime.Mqtt.Device01.Raw
  Params["Value"] = 0.23
  Params["Unit"] = "mA"
```

## Verantwortlichkeiten

### Amium.Item

`Amium.Item` definiert die gemeinsame Datenform:

- Item-Baum
- Child-Items
- Parameter
- Pfade
- Werte
- Metadaten

`Amium.Item` enthaelt keine Transportlogik.

### Runtime Client

Ein Runtime Client ist jede Komponente, die Werte erzeugt, verarbeitet oder konsumiert.

Beispiele:

- UDL client
- MQTT client
- ESP32 device
- CAN adapter
- Python client
- Simulation
- Plugin runtime

Ein Runtime Client kann intern eigene Objekte und Sammlungen besitzen. Fuer die Kommunikation mit Host und UI erzeugt oder aktualisiert er `Item`s.

### Host DataRegistry

`HostRegistries.Data` ist die zentrale Live-Registry im Host.

Sie enthaelt publizierte Root-Items und muss Root- und Descendant-Items eindeutig aufloesen koennen.

Grundregel:

```text
TryGet(...)     = direkter Root-Key-Zugriff
TryResolve(...) = finaler Lookup fuer Root- und Descendant-Items
```

Alle finalen Host-Item-Lookups sollen `TryResolve(...)` verwenden.

### Amium.ItemBroker

`Amium.ItemBroker` ist als verteilte Kommunikations- und Routing-Schicht um `Amium.Item` geplant. Der Broker ist getrennt von `HornetStudio.Host.DataRegistry`; die `DataRegistry` bleibt die lokale Live-Registry innerhalb von HornetStudio.

Details zu Broker-Contracts, Adressierung, Retained State, Subscriptions, Service Host und zukuenftigen Transport-Adaptern stehen in:

```text
docs/amium-itembroker-architecture.md
```

### SignalRegistry

`HostRegistries.Signals` ist die signalorientierte Sicht auf `DataRegistry`.

Ein Signal verweist auf ein aufgeloestes `Item` und nutzt dessen Parameter fuer Descriptor-Daten:

- `SourcePath`
- `Name`
- `Unit`
- `Format`
- `DataType`
- `IsWritable`
- `Category`

Widgets und Logger koennen dadurch mit `ISignal` arbeiten, ohne die konkrete Item-Baumstruktur selbst zu kennen.

### UI und Widgets

Widgets speichern konfigurierte `TargetPath`s.

Der typische Zugriff ist:

```text
Widget.TargetPath
  -> TargetPathHelper.EnumerateResolutionCandidates(...)
  -> HostRegistries.Data.TryResolve(...)
  -> Item oder ISignal
```

Widgets sollen nach Moeglichkeit beim Binding oder bei `TargetPath`-Aenderung aufloesen und danach mit einer `Item`- oder `ISignal`-Referenz arbeiten.

## Pfadregeln

### Separatoren

Diese Separatoren sind fuer Hierarchiepfade gleichwertig:

```text
.
/
\
```

Diese Pfade meinen dasselbe Item:

```text
Device01.Read
Device01/Read
Device01\Read
```

Intern wird fuer Vergleich und Lookup eine normalisierte Form verwendet.

### Vergleich

Pfadvergleiche sind case-insensitive.

Die originale Schreibweise von `Item.Path` bleibt fuer Anzeige, Metadaten und Persistenz erhalten.

### Mehrdeutige Roots

Wenn mehrere Root-Keys passen, gewinnt der laengste passende Root-Pfad.

Beispiel:

```text
Roots:
Device01
Device01.Read

Lookup:
Device01.Read.Value
```

Dann wird zuerst `Device01.Read` als Root betrachtet, weil es spezifischer ist.

### Parameter sind keine Pfadsegmente

Ein Parameter wird nicht wie ein Child-Item aufgeloest.

Richtig:

```text
Item path: Device01.Read
Parameter: Value
Value: 23
```

Nicht als fachliche Item-Struktur gemeint:

```text
Device01.Read.Value
```

Eine Transport-Schicht darf ein Topic wie `Device01/Read/Value` verwenden. Die Bridge muss das aber in `Item path = Device01.Read` und `parameter = Value` uebersetzen.

## Publishing

Ein Client kann ganze Snapshots oder einzelne Updates publizieren.

### Snapshot

Ein Snapshot beschreibt einen kompletten Item-Baum oder Teilbaum.

```text
Root: Runtime.Mqtt.Device01

Device01
├─ Read
├─ Set
└─ Raw
```

Der Host publiziert diesen Baum ueber:

```text
UpsertSnapshot(rootPath, itemTree)
```

### Value Update

Ein Value Update aendert den Wert eines Items.

```text
UpdateValue("Runtime.Mqtt.Device01.Read", 23)
```

Das Ziel kann ein Root-Item oder ein Descendant-Item sein.

### Parameter Update

Ein Parameter Update aendert einen Parameter eines Items.

```text
UpdateParameter("Runtime.Mqtt.Device01.Read", "Unit", "ABC")
```

Auch hier wird das Item ueber den zentralen Resolver gefunden.

## Write Model

Schreibbarkeit wird ueber Parameter beschrieben.

Typische Parameter:

```text
Writable = true
WritePath = "Runtime.Mqtt.Device01.Set"
WriteMode = "Request"
```

Beispiel:

```text
Runtime.Mqtt.Device01.Read
  Params["Writable"] = false

Runtime.Mqtt.Device01.Set
  Params["Writable"] = true
  Params["WritePath"] = "Runtime.Mqtt.Device01.Set"
```

Ein Widget kann daraus ableiten, ob ein Wert geschrieben werden darf und wohin der Schreibzugriff gehen soll.

## UDL-Struktur

UDL-Module verwenden bereits ein Item-Baummodell.

Typischer Aufbau:

```text
Module
├─ Read
│  └─ Request
├─ Set
│  └─ Request
├─ Out
│  └─ Request
├─ State
├─ Alert
└─ Command
   └─ Request
```

`Read`, `Set`, `Out`, `State`, `Alert` und `Command` sind Items.

`Writable`, `WritePath`, `WriteMode`, `Unit`, `Format` und `Value` bleiben Parameter dieser Items.

## EnhancedSignals-Struktur

EnhancedSignals publizieren ebenfalls Item-Baeume.

Beispiel:

```text
Studio.Page1.EnhancedSignals.Signal01
├─ Read
├─ Set
├─ Raw
├─ Config
├─ Statistics
│  ├─ Min
│  │  └─ TimeStamp
│  ├─ Max
│  │  └─ TimeStamp
│  ├─ Average
│  ├─ StdDev
│  ├─ Integral
│  └─ Reset
├─ Kalman
│  └─ Request
└─ Dynamic
```

Jeder dieser Knoten ist ein adressierbares Item.

Beispiele fuer gueltige Zielpfade:

```text
Studio.Page1.EnhancedSignals.Signal01.Read
Studio.Page1.EnhancedSignals.Signal01.Statistics.Min
Studio.Page1.EnhancedSignals.Signal01.Statistics.Min.TimeStamp
Studio.Page1.EnhancedSignals.Signal01.Kalman.Request
```

## CustomSignals-Struktur

CustomSignals erzeugen publizierte Items unter einem stabilen Projekt- und Folder-Pfad.

Beispiel:

```text
Studio.Page1.CustomSignals.Speed
Studio.Page1.CustomSignals.Speed.Trigger
```

Input-Signale, berechnete Signale und Schreibziele muessen ueber `TryResolve(...)` aufgeloest werden.

## Python-Struktur

Python-Clients koennen Host-Werte lesen, schreiben oder eigene Werte publizieren.

Auch hier gilt:

```text
Path -> Item
Parameter -> Value/Unit/Format/...
```

Host-Wert-Projektionen duerfen Root-Items enumerieren, sollen finale Updates aber ueber `TryResolve(...)` ausfuehren.

## MQTT-Zielstruktur

MQTT ist ein Transport, nicht das fachliche Datenmodell.

Ein MQTT-Topic wird in Item-Pfad und Parameter uebersetzt.

Aktuelles Broker-Topic-Modell:

```text
hornet/broker/heartbeat
hornet/broker/uptime
hornet/broker/mqtt/status
clients/Device01/Read
clients/Device01/Read/params/Unit
clients/Device01/Set
clients/Device01/Raw
clients/Device01/Raw/params/Unit
```

`hornet/...` ist fuer HornetStudio- und ItemBroker-Zustand reserviert. Client-Werte werden direkt unter `clients/{clientId}/{item path}` veroeffentlicht. Item-Parameter werden unter `clients/{clientId}/{item path}/params/{parameter}` veroeffentlicht. Der MQTT-`clientId` wird aus der Broker Message `SourceClientId` abgeleitet; fehlt diese Angabe, wird explizit `unknown` verwendet.

Mapping in den Host/Broker:

```text
Read | Value | 23
Read | Unit  | "ABC"
Set  | Value | 24
Raw  | Value | 0.23
Raw  | Unit  | "mA"
```

Die MQTT-Bridge darf keine eigene dauerhafte Pfadlogik neben `DataRegistry.TryResolve(...)` aufbauen.

## Zugriffsmuster

### Runtime Update

```text
Client
  -> Item path + parameter/value
  -> DataRegistry.UpdateValue(...)
  -> ItemChanged
  -> SignalRegistry
  -> Widgets/Logger/Scripts
```

### UI Binding

```text
Widget TargetPath
  -> Candidate generation
  -> DataRegistry.TryResolve(...)
  -> Item or ISignal
  -> direct runtime access
```

### Write Access

```text
Widget/Scripting write
  -> Resolve visible item
  -> inspect Writable/WritePath/WriteMode
  -> DataRegistry.UpdateValue(writePath, value)
  -> transport bridge sends command if needed
```

## Aktueller Host-Stand

Dieser Abschnitt beschreibt, wie der Host aktuell mit der Struktur arbeitet. Er ist als Einstieg fuer neue Chats gedacht.

### Zentrale Registry

Die zentrale Registry liegt in:

```text
src/HornetStudio.Host/HostRegistries.cs
```

`HostRegistries.Data` ist eine `IDataRegistry`-Instanz. Die konkrete Implementierung ist `DataRegistry`.

Aktuelle Kernmethoden:

```text
GetAllKeys()
TryGet(key, out item)
TryResolve(path, out item)
UpsertSnapshot(key, snapshot, pruneMissingMembers)
UpdateValue(key, value, timestamp)
UpdateParameter(key, parameterName, value, timestamp)
Remove(key)
```

Aktueller Stand:

- `TryGet(...)` ist weiterhin ein direkter Root-Key-Zugriff.
- `TryResolve(...)` ist der zentrale Lookup fuer Root- und Descendant-Items.
- `UpdateValue(...)` nutzt `TryResolve(...)`.
- `UpdateParameter(...)` nutzt `TryResolve(...)`.
- `UpsertSnapshot(...)` speichert oder merged Root-Items.
- `Remove(...)` entfernt aktuell Root-Keys.

Wichtig: Ein separater Path-Index fuer alle Descendants ist aktuell noch nicht finaler Bestandteil. `TryResolve(...)` loest Descendants ueber Root-Prefix-Suche und Child-Traversal auf.

### Aktuelle Resolver-Regeln im Host

`DataRegistry.TryResolve(...)` arbeitet aktuell nach diesem Muster:

```text
1. Direkter Key-Treffer in _items
2. Normalisierter exakter Root-Key-Treffer
3. Laengster passender Root-Prefix
4. Relative Child-Aufloesung unter diesem Root
```

Die Child-Aufloesung ist case-insensitive.

Diese Separatoren werden normalisiert:

```text
.
/
\
```

Beispiel:

```text
Root in Registry:
Runtime.Mqtt.Device01

Child tree:
Device01
  Read
  Set
  Raw

Resolve:
Runtime.Mqtt.Device01.Read
Runtime/Mqtt/Device01/Read
runtime.mqtt.device01.read
```

Alle drei Varianten koennen dasselbe `Read`-Item aufloesen, sofern der Baum so publiziert wurde.

### Events

`DataRegistry` feuert:

```text
ItemChanged
RegistryChanged
```

`ItemChanged` wird bei diesen Aenderungen verwendet:

```text
SnapshotUpserted
ValueUpdated
ParameterUpdated
```

`RegistryChanged` wird aktuell beim Hinzufuegen neuer Root-Items verwendet.

Bei `UpdateValue(...)` wird das aufgeloeste Item aktualisiert und mit dem uebergebenen Key gemeldet. Dadurch koennen Alias-Pfade und kanonische Pfade relevant sein.

### SignalRegistry

Die Signalschicht liegt in:

```text
src/HornetStudio.Host/SignalRegistry.cs
```

Aktueller Stand:

- `SignalRegistry.TryGetBySourcePath(...)` nutzt `DataRegistry.TryResolve(...)`.
- Dadurch koennen auch Descendant-Items als Signale verwendet werden.
- Beim Erzeugen eines Signals wird bevorzugt `item.Path` als kanonischer SourcePath verwendet.
- Die Registry speichert sowohl den angefragten Pfad als auch den kanonischen Pfad als Lookup-Alias.
- Signal-Updates reagieren auf `DataRegistry.ItemChanged`.

Das bedeutet:

```text
TryGetBySourcePath("Device01/Read")
  -> TryResolve(...)
  -> Item.Path z.B. "Device01.Read"
  -> SignalDescriptor.SourcePath = "Device01.Read"
```

Noch zu beachten:

- `IsWritable` wird aktuell noch nicht vollstaendig aus `Writable`/`WritePath` abgeleitet.
- Fuer maximale Robustheit sollen Signal-Aliase bei weiteren Umbauten gezielt getestet werden.

### UI Target-Aufloesung

Die UI verwendet weiterhin `TargetPathHelper` fuer layoutbezogene Kandidatenbildung.

Datei:

```text
src/HornetStudio.Editor/Helpers/TargetPathHelper.cs
```

Typischer Ablauf:

```text
Configured TargetPath
  -> TargetPathHelper.EnumerateResolutionCandidates(targetPath, pageName)
  -> HostRegistries.Data.TryResolve(candidate)
  -> Item
```

Mehrere wichtige Bereiche nutzen bereits `TryResolve(...)` fuer finale Lookups:

```text
src/HornetStudio.Editor/ViewModels/MainWindowViewModel.cs
src/HornetStudio.Editor/Models/PageItemModel.cs
src/HornetStudio.Editor/Widgets/RealtimeChart/RealtimeChartControl.axaml.cs
src/HornetStudio.Editor/Widgets/CsvLogger/EditorCsvLoggerControl.axaml.cs
src/HornetStudio.Editor/Widgets/SqlLogger/EditorSqlLoggerControl.axaml.cs
src/HornetStudio.Editor/Widgets/CustomSignals/CustomSignalsControl.axaml.cs
src/HornetStudio.Editor/Widgets/CircleDisplay/EditorCircleDisplayControl.axaml.cs
```

Restpunkt:

- Einige Auswahl- und Diagnosepfade enumerieren weiterhin `GetAllKeys()` und greifen danach mit `TryGet(...)` auf Root-Items zu. Das ist fuer Root-Listen korrekt, muss aber erweitert werden, wenn dort Descendant-Items direkt auswählbar sein sollen.

### UDL

UDL-Strukturen liegen unter anderem in:

```text
src/HornetStudio.Host/UdlModule.cs
src/HornetStudio.Editor/Widgets/UdlClient/UdlClientControl.axaml.cs
```

`UdlModule` erzeugt aktuell diese Grundstruktur:

```text
Module
  Read
    Request
  Set
    Request
  Out
    Request
  State
  Alert
  Command
    Request
```

`Read`, `Set`, `Out`, `State`, `Alert`, `Command` und `Request` sind Items.

Diese Metadaten bleiben Parameter:

```text
Writable
WritePath
WriteMode
Value
Unit
Format
Kind
Text
```

Der aktuelle Resolver kann UDL-SubItems aufloesen, wenn der Root-Baum publiziert wurde.

### EnhancedSignals

EnhancedSignals werden im Host ueber `EnhancedSignalRuntime` publiziert.

Datei:

```text
src/HornetStudio.Host/EnhancedSignalRuntime.cs
```

Aktueller Stand:

- Interne Aufloesung von Source-Items verwendet `HostRegistries.Data.TryResolve(...)`.
- Runtime-Branches wie `Raw`, `Read`, `Set`, `Config`, `Statistics`, `Kalman` und `Dynamic` sind Items.
- Schreibpfade fuer Kalman Requests und Statistics Reset laufen ueber Item-Pfade.
- Updates werden ueber `HostRegistries.Data.UpdateValue(...)` und `UpdateParameter(...)` geschrieben.

Beispiele fuer aktuelle Zielpfade:

```text
Studio.Page1.EnhancedSignals.Signal01.Read
Studio.Page1.EnhancedSignals.Signal01.Raw
Studio.Page1.EnhancedSignals.Signal01.Statistics.Reset
Studio.Page1.EnhancedSignals.Signal01.Kalman.Request
```

Restpunkt:

- Ein Path-Index wuerde bei vielen EnhancedSignal-Branches die Lookup-Kosten reduzieren.
- Branch- und Nested-Item-Aufloesung sollte durch Tests abgesichert werden.

### CustomSignals

CustomSignals liegen in:

```text
src/HornetStudio.Editor/Widgets/CustomSignals/CustomSignalsControl.axaml.cs
```

Aktueller Stand:

- Source-Pfade werden ueber `TargetPathHelper` und `HostRegistries.Data.TryResolve(...)` aufgeloest.
- Write Targets werden ueber `TryResolve(...)` gesucht.
- Publizierte CustomSignal-Items liegen unter stabilen Projekt-/Folder-Pfaden.

Beispiel:

```text
Studio.Page1.CustomSignals.Speed
Studio.Page1.CustomSignals.Speed.Trigger
```

Restpunkt:

- Einige Stellen nutzen `TryGet(...)`, um bestehende Root-Werte bei erneutem Publish zu erhalten. Das ist nur dann korrekt, wenn der CustomSignal-Pfad selbst Root-Key ist.

### RealtimeChart, CsvLogger und SqlLogger

Aktueller Stand:

- RealtimeChart loest Serienpfade ueber `TryResolve(...)`.
- CsvLogger loest konfigurierte Pfade ueber `TryResolve(...)`.
- SqlLogger loest konfigurierte Pfade ueber `TryResolve(...)`.
- CsvLogger kann bevorzugt `ISignal` verwenden und auf `Item` zurueckfallen.

Dateien:

```text
src/HornetStudio.Editor/Widgets/RealtimeChart/RealtimeChartControl.axaml.cs
src/HornetStudio.Editor/Widgets/CsvLogger/EditorCsvLoggerControl.axaml.cs
src/HornetStudio.Editor/Widgets/SqlLogger/EditorSqlLoggerControl.axaml.cs
src/HornetStudio.Host/Logging/CsvLogger.cs
```

Restpunkt:

- RealtimeChart arbeitet aktuell noch direkt item-basiert und sampled periodisch.
- Fuer hochfrequente Daten waere ein stabiler Signal-/Item-Cache nach initialem Resolve sinnvoll.

### Python

Python-Client-Integration liegt in:

```text
src/HornetStudio.Host/Python/Client/PythonClient.cs
```

Aktueller Stand:

- Python kann eigene Host-Werte publizieren.
- Host-Wert-Projektionen enumerieren Root-Keys und traversieren deren Children.
- Runtime-Updates verwenden an relevanten Stellen `TryResolve(...)`.

Restpunkt:

- Root-Enumeration mit `TryGet(...)` ist fuer Projektion korrekt, sollte aber klar als Root-only Zugriff verstanden werden.

### Dokumentationsstand

Diese Datei beschreibt die Zielstruktur und den aktuellen Host-Stand.

Weitere relevante Dokumente:

```text
docs/data-flow-and-signals.md
src/HornetStudio/docs/data-flow-and-signals.md
docs/interface-architecture-notes.md
docs/project-folder-architecture-notes.md
```

Restpunkt:

- Doppelte Dokumente muessen synchron bleiben.
- Aeltere Formulierungen mit `TryGet(...)` als finalem Lookup sollten durch `TryResolve(...)` ersetzt werden.

### Aktuelle Build-Situation

Der serielle Build wurde zuletzt erfolgreich ausgefuehrt mit:

```text
dotnet build b:\HornetStudio\HornetStudio.sln --configfile b:\HornetStudio\NuGet.Config -m:1
```

Bekannte Hinweise:

- Nicht-serieller Build kann transient an gesperrten MSBuild-Obj-Dateien scheitern.
- Es gibt bestehende nullable warnings in `CsvLogger.cs`, die nicht direkt Teil der Item-Struktur sind.

### Aktuelle Restpunkte fuer eine saubere Fertigstellung

1. Path-Index fuer `DataRegistry` ergaenzen oder bewusst dagegen entscheiden.
2. Alle verbleibenden `TryGet(...)`-Stellen pruefen und als Root-only bestaetigen oder auf `TryResolve(...)` umstellen.
3. UI-Auswahllisten erweitern, wenn Descendant-Items direkt auswählbar sein sollen.
4. Resolver- und Signal-Tests ergaenzen.
5. Dokumentation synchronisieren.
6. `SignalRegistry.IsWritable` aus Item-Metadaten ableiten, falls risikoarm.
7. Event-Key-/Alias-Verhalten fuer kanonische Pfade und angefragte Pfade testen.

## Zielbild

```text
Device / Client / Plugin / Script
        |
        v
Amium.Item tree or item delta
        |
        v
HostRegistries.Data
        |
        +--> HostRegistries.Signals
        +--> Widgets
        +--> Logger
        +--> Charts
        +--> Python
        +--> Future MQTT bridge
```

## Regeln

1. `Amium.Item` ist die gemeinsame fachliche Datenstruktur.
2. Transportprotokolle werden auf `Item path + parameter + value` gemappt.
3. `TryResolve(...)` ist der finale Lookup fuer Host-Items.
4. `TryGet(...)` bleibt Root-only.
5. `Value`, `Unit`, `Format`, `Kind`, `Writable`, `WritePath` und `WriteMode` sind Parameter.
6. Child-Items repraesentieren fachliche Unterstrukturen wie `Read`, `Set`, `Raw`, `Statistics` oder `Kalman`.
7. Widgets arbeiten mit Pfaden, sollen nach dem Resolve aber direkte `Item`- oder `ISignal`-Referenzen nutzen.
8. Mehrere Clients oder Plugin-Instanzen muessen eindeutige Root-Kontexte verwenden.
9. Externe Bridges duerfen keine konkurrierende Registry einfuehren.
10. Die zentrale Wahrheit fuer Live-Werte ist `HostRegistries.Data`.
