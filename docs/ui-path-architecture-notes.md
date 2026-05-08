# UI Path Architecture Notes

## Ziel

Der zentrale Punkt ist nicht, dass `Project` und `Folder` unbedingt selbst `Item` sein muessen.

Der eigentliche Zweck ist:

- `Path` soll automatisch und korrekt erzeugt werden
- `Path` soll nicht manuell als freier String gepflegt werden
- die `Path`-Syntax soll stabil bleiben, weil sie spaeter direkt fuer MQTT-Topics verwendet werden kann
- der User soll weiterhin explizit entscheiden, was in die UI publiziert wird, damit kein unnoetiger Overhead entsteht

## Wichtige Entscheidung

Nicht alles muss ein `Item` werden.

Stattdessen:

- `Project` und `Folder` liefern den Hierarchie-Kontext
- `Item` bleibt das publizierbare UI-Objekt
- `Path` wird aus der Hierarchie abgeleitet
- `Publish(...)` passiert nur explizit

## Gewuenschtes Modell

Beispielhierarchie:

```text
TestProject
TestProject/Folder1
TestProject/Folder1/Sinus
TestProject/Folder1/Timer
TestProject/Folder1/Logs/Folder1Demo
```

Oder technisch formuliert:

- `project.Name` ist das Root-Segment
- `Folder.Name` haengt unter `Project`
- `Item.Name` haengt unter `Folder` oder unter einem Parent-`Item`
- `Path` entsteht automatisch aus Parent + Name

## Warum das wichtig ist

Die `Path`-Syntax ist nicht zufaellig.

Sie soll spaeter direkt als MQTT-kompatible Adressstruktur nutzbar sein, z.B.:

```text
TestProject/Folder1/Sinus
TestProject/Folder1/Task
TestProject/Folder1/Logs/Folder1Demo
```

Wenn Pfade von Hand geschrieben werden:

- entstehen Tippfehler
- entstehen Inkonsistenzen
- brechen Referenzen leichter
- ist MQTT-Topic-Generierung unzuverlaessig

Wenn Pfade automatisch erzeugt werden:

- bleibt die Struktur deterministisch
- UI und Transport koennen dieselbe Adresslogik nutzen
- Refactorings werden einfacher

## Konsequenz fuer Project und Folder

`Project` und `Folder` muessen nicht zu `Item` gezwungen werden.

Das ist optional und aktuell nicht der eigentliche Bedarf.

Wichtiger ist:

- `Project` ist Root-Kontext
- `Folder` ist Child-Kontext
- beide helfen beim automatischen Aufbau des finalen `Path`

## Konsequenz fuer Publishing

Publishing soll **nicht automatisch** beim Erzeugen von Objekten passieren.

Das war explizit so gewuenscht, damit nur wirklich benoetigte Dinge in die UI gelangen.

Deshalb:

- Objekte koennen einen korrekten `Path` besitzen
- publiziert wird aber nur ueber explizite APIs wie `Publish(...)`

## Aktuelle Richtung fuer ProcessLog

Die neue Richtung ist:

- `ProcessLog` wird fuer die UI als `Item` unter einem stabilen Pfad publiziert
- Beispiel: `Logs/Host`, `Logs/Folder1Demo`
- `EditorLogControl` loest den Log nicht mehr primaer aus einer Sonderregistry auf, sondern ueber ein `Item` in `HostRegistries.Data`

Das ist ein Schritt in Richtung eines einheitlicheren UI-Modells.

## Empfohlene naechste Architektur

Nicht:

- `Project` muss ein `Item` sein
- `Folder` muss ein `Item` sein

Sondern:

1. zentrale Path-Erzeugung
2. `Project` als Root-Kontext
3. `Folder` als Child-Kontext
4. `Item` als publizierbares UI-Objekt
5. explizites `Publish(...)`

## Empfohlene Regeln

### 1. Path nicht frei von aussen setzen

`Path` sollte nicht beliebig manuell zusammengebaut werden.

Besser:

- readonly
- intern gesetzt
- oder nur ueber klar definierte Builder/Attach-Methoden

### 2. Path aus Hierarchie ableiten

Beispielregel:

```text
Path = Parent.Path + "/" + Name
```

Falls kein Parent vorhanden ist:

```text
Path = Name
```

### 3. Project und Folder geben Kontext

Beispiel:

- `project.Name = TestProject`
- `Folder.Name = Folder1`
- `Item.Name = Sinus`

Dann wird automatisch:

```text
TestProject/Folder1/Sinus
```

### 4. Publish bleibt explizit

Beispiel:

```csharp
Folder.Attach(item);
UiPublisher.Publish(item);
```

Oder spaeter:

```csharp
Folder.PublishItem(item);
```

Aber nur als expliziter Aufruf, nicht implizit beim Erzeugen.

## Sinnvolle API-Richtung

### Variante A: zentraler Path-Builder

```csharp
UiPath.ForFolder(project.Name, Folder.Name)
UiPath.ForItem(project.Name, Folder.Name, item.Name)
UiPath.ForChild(parent.Path, child.Name)
```

### Variante B: Kontext ueber Parent/Attach

```csharp
Folder.Attach(item);
Folder.AttachProcessLog(log, "Folder1Demo");
```

Dann berechnet `Attach(...)` intern den finalen Pfad.

## Empfehlung fuer Folder

Wenn `Folder` Lifecycle hat wie:

- `Initialize()`
- `Run()`
- `Destroy()`

dann ist eine spezialisierte Folder-Klasse sinnvoll.

Aber:

- nicht primaer, damit `Folder` ein `Item` wird
- sondern weil `Folder` Verhalten/Lifecycle besitzt

Das Path-Thema ist davon getrennt.

## Kurzfassung

- `Project` und `Folder` muessen nicht zwingend `Item` sein
- entscheidend ist die automatische, kanonische `Path`-Erzeugung
- `project.Name` ist Root
- `Folder.Name` dockt darunter an
- alle UI-`Item`s docken daran an
- `Publish(...)` bleibt explizit
- die Path-Struktur soll MQTT-kompatibel und stabil sein

## Gute Zielvorstellung

```text
Project = Root-Kontext
Folder = Sub-Kontext
Item = publizierbares UI-Objekt
Path = automatisch aus der Hierarchie erzeugt
Publish = explizit, nicht automatisch
```

