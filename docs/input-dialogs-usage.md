# Eingabedialoge und Parameterformate

Dieses Dokument beschreibt, wie die verschiedenen Eingaben im System verwendet und konfiguriert werden:

- Welche Dialoge es gibt (Text, Numeric, Hex, Passwort/Maskiert)
- Wie `TargetParameterFormat` / `FormatParameter` den Dialog steuern
- Wie `InteractionRules` mit `OpenValueEditor` bzw. `SendInputTo` eingesetzt werden
- Wie die globalen Hilfsmethoden im Host (z.B. UdlBook) aussehen

---

## 1. Dialogtypen

### 1.1 Text-Dialog

- Dialogfenster mit Textbox und Text-Tastatur (`EditorTextInputPad`).
- Geeignet für freie Texte, einfache Werte, Kennungen.
- Wird verwendet, wenn das Format **Text** ist:
  - `TargetParameterFormat` leer oder
  - explizit `Text` / anderes unbekanntes Format.
- Ergebnis wird als `string` über `TrySendInput(...)` an den Parameter geschrieben.

**Beispiel YAML (SignalWidget-Auszug):**

```yaml
Control:
  TargetPath: "UdlBook/Page1/udl1/m310"
  TargetParameterPath: "Value"
  TargetParameterFormat: ""      # Text
  FormatParameter: ""
```

### 1.2 Numerischer Dialog

- Dialogfenster mit Textbox und numerischer Tastatur (`EditorNumericInputPad`).
- Geeignet für Ganzzahlen und Fließkommazahlen.
- Wird verwendet, wenn das Format **Numeric** ist:
  - `numeric` → Standardformat `0.##`
  - `numeric:0` → Ganze Zahl
  - `numeric:0.00` → 2 Nachkommastellen
  - Kurzformen:
    - `D3` → `000` (3-stellig, ganzzahlig)
    - `F2` → `0.00` (2 Nachkommastellen)
- Ergebnis wird als `double` gelesen und anschließend in den Zieltyp konvertiert (`ConvertEditorValue`).

**Beispiele für `TargetParameterFormat`:**

```yaml
TargetParameterFormat: "numeric"       # z.B. 0.##
TargetParameterFormat: "numeric:0"     # z.B. 42
TargetParameterFormat: "numeric:0.00"  # z.B. 3.14
TargetParameterFormat: "F2"            # Kurzform für 0.00
```

### 1.3 Hex-Dialog

- Dialogfenster mit Textbox und Hex-Tastatur (`EditorHexInputPad`).
- Geeignet für Bitmasken, Register, IDs.
- Wird verwendet, wenn das Format **Hex** ist:
  - `hex` → Hex ohne feste Stellenzahl
  - `hex:4` → 4 Hex-Stellen
  - Kurzformen:
    - `h2`, `h4`, `h8` → 2, 4 bzw. 8 Stellen.
- Ausgabeformat im Display (Parameteranzeige): `0x....`.
- Im Dialog wird nur der reine Hex-Wert ohne `0x` eingegeben.

**Beispiele für `TargetParameterFormat`:**

```yaml
TargetParameterFormat: "hex"     # Hex, variable Länge
TargetParameterFormat: "hex:4"   # 4 Stellen, z.B. 00AF
TargetParameterFormat: "h8"      # Kurzform für 8 Stellen
```

### 1.4 Bool / Bits (ohne Dialog)

- Werden aktuell **nicht** über die neuen Dialogfenster editiert, sondern direkt im Widget:
  - `bool` / `bool:AN,AUS` → zwei Buttons (z.B. True/False).
  - `b4`, `b8`, `b16` (+ optionale Labels) → Bit-Buttons.
- Klicks lösen direkt `TrySendInput(...)` oder `TryToggleTargetBit(...)` aus.

**Beispiele für `TargetParameterFormat`:**

```yaml
TargetParameterFormat: "bool"                 # Standard-Labels True/False
TargetParameterFormat: "bool:AN,AUS"         # Benutzerdefinierte Labels
TargetParameterFormat: "b4"                  # 4 Bit ohne Labels
TargetParameterFormat: "b4:DI1,DI2,Alert,4"  # 4 Bit mit Labels
```

---

## 2. TargetParameterFormat und FormatParameter

Die Widget-Properties im YAML werden auf das interne Format abgebildet:

- `TargetParameterFormat` → steuert die **Art der Darstellung / Eingabe**.
- `FormatParameter`      → wird vom Editor in `TargetParameterFormat` eingepackt, wenn nötig.

Intern wird der Formatstring so zerlegt:

```csharp
(string Kind, string Parameter) SplitParameterFormat(string? format)
```

- `Kind` bestimmt den Typ (`Numeric`, `Hex`, `Text`, `bool`, `b4`, ...).
- `Parameter` enthält z.B. das Zahlenformat (`0.00`) oder Bit-Labels.

Der Dialog wählt anhand von `Kind` den passenden Modus:

- `Numeric` → Numerik-Dialog
- `Hex`     → Hex-Dialog
- Anderes/Text/leer → Text-Dialog

---

## 3. InteractionRules und OpenValueEditor

### 3.1 Allgemein

Jedes Widget kann unter `InteractionRules` Aktionen definieren, die auf Benutzeraktionen reagieren.

Wichtige Aktionen:

- `OpenValueEditor` → Öffnet den Value-Editor (jetzt mit neuen Dialogen).
- `SendInputTo`     → Schreibt einen Wert direkt in ein Ziel (ohne Dialog).

Die Auswertung passiert in `PageItemModel.TryExecuteInteraction(...)` und im Fall `OpenValueEditor` über die neuen Dialoge.

### 3.2 OpenValueEditor (neue Dialoge)

Wenn eine Regel mit `Action: OpenValueEditor` existiert, wird zunächst das Ziel-Item bestimmt:

1. `TargetPath: "this"` oder leer → aktuelles Item.
2. Anderer Pfad → es wird versucht, das entsprechende Daten-Item zu finden und ggf. ein Proxy-Item gebaut.
3. Aus `TargetParameterView` des Ziel-Items wird das Format gelesen → Dialogtyp.

**Beispiel: ListView-Item (vereinfacht)**

```yaml
InteractionRules:
  - Event: BodyLeftClick
    Action: OpenValueEditor
    TargetPath: "this"    # Öffnet Wert des Items im Dialog
    Argument: ""
```

**Beispiel: Button, der einen Parameter per Dialog editiert**

```yaml
InteractionRules:
  - Event: BodyLeftClick
    Action: OpenValueEditor
    TargetPath: "UdlBook/Page1/udl1/m310"  # Ziel-Item
    Argument: ""

Control:
  ButtonText: "Edit m310"
```

- Beim Klick auf den Button wird:
  - Das Ziel-Item `m310` gesucht.
  - Aus dessen `TargetParameterFormat` das Format ermittelt.
  - Der passende Dialog (Text/Numeric/Hex) geöffnet.
  - Das Ergebnis via `TrySendInput` zurückgeschrieben.

### 3.3 SendInputTo (ohne Dialog)

`SendInputTo` wird verwendet, wenn kein Dialog erscheinen soll, sondern direkt ein Wert an einen set/request-Pfad gesendet wird.

**Beispiel: Button, der einen festen Wert sendet**

```yaml
InteractionRules:
  - Event: BodyLeftClick
    Action: SendInputTo
    TargetPath: "UdlBook/Page1/udl1/m310/set/request"
    Argument: "1"   # fester Wert
```

Im Code wird dafür `TrySendInput(...)` mit dem `Argument` als Wert ausgeführt.

---

## 4. Globale Hilfsmethoden (Host, z.B. UdlBook)

Für direkte Aufrufe aus dem Host-Fenster gibt es statische Helfer:

```csharp
// Text
var text = await EditorInputDialogs.EditTextAsync(this,
    header: "Enter value",
    subHeader: "Demo",
    initialValue: "" /* optional */);

// Passwort-Text
var password = await EditorInputDialogs.EditTextAsync(this,
    header: "Enter password",
    subHeader: "Demo",
    initialValue: string.Empty,
    isPassword: true);

// Numerik
var number = await EditorInputDialogs.EditNumericAsync(this,
    header: "Enter number",
    subHeader: "Demo",
    format: "0.00",      // oder "0" etc.
    initialValue: 1.23);

// Maskierte Numerik (z.B. PIN)
var pin = await EditorInputDialogs.EditNumericAsync(this,
    header: "Enter PIN",
    subHeader: "Demo",
    format: "0",
    initialValue: null,
    maskInput: true);

// Hex
var hex = await EditorInputDialogs.EditHexAsync(this,
    header: "Enter value",
    subHeader: "Demo",
    digits: 4,            // z.B. 4 Stellen
    initialValue: 0x12UL);
```

Diese Methoden werden intern an den gleichen Dialogen und Styling-Regeln ausgerichtet wie die Widgets (gemeinsames Theme, gleiche Pads, gleiche Logik für DEL / < usw.).

---

## 5. Zusammenfassung

- Der **Dialogtyp** wird über das Parameterformat bestimmt (`TargetParameterFormat`).
- `OpenValueEditor` in `InteractionRules` öffnet jetzt immer die neuen Dialoge und verwendet dieselben Formatregeln wie der Editor.
- `SendInputTo` eignet sich für direkte Schreibaktionen ohne Dialog.
- Die globalen `EditorInputDialogs.*`-Methoden erlauben dieselben Eingaben direkt aus Host-Anwendungen (z.B. UdlBook).
