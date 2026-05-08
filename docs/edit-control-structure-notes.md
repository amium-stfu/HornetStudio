# Edit Control Structure Notes

## Ziel

Fuer `EditControls` soll eine klare und durchgaengige Begriffsdefinition gelten.

Das Ziel ist:

- sichtbare Textrollen sauber von Layout-Containern zu trennen
- Begriffe wie `Title`, `Header` und `Caption` nicht mehr gemischt zu verwenden
- dieselben Begriffe in JSON, C# und Editor-UI konsistent zu benutzen

## Grundentscheidung

Fuer `EditControls` werden diese Begriffe verwendet:

- `ControlCaption`
- `ControlPanel`
- `BodyCaption`
- `BodyPanel`
- `FooterPanel`

Dabei gilt:

- `Caption` bezeichnet sichtbaren, beschreibenden Text
- `Panel` bezeichnet einen strukturellen Containerbereich

## Struktur

### ControlCaption

`ControlCaption` ist die primaere sichtbare Beschriftung des Controls.

Typisch:

- oben links
- kurz
- nicht interaktiv

Beispiel:

```text
Hex
```

### ControlPanel

`ControlPanel` ist der obere Funktionsbereich des Controls.

Typisch:

- oben rechts
- Buttons
- Menues
- kleine Funktionsicons

`ControlPanel` ist ein Container, kein Textfeld.

### BodyCaption

`BodyCaption` ist eine optionale sekundaere Beschreibung direkt ueber dem Hauptinhalt.

Typisch:

- kleiner Text unter oder nahe `ControlCaption`
- erklaert den Inhalt des Hauptbereichs
- nicht interaktiv

Beispiel:

```text
Editable Register
```

`BodyCaption` ist kein allgemeiner Sammelbegriff fuer beliebige Zusatztexte irgendwo im Control.

### BodyPanel

`BodyPanel` ist der Hauptbereich des Controls.

Dort liegt der eigentliche Inhalt:

- Hauptwert
- Visualisierung
- Textinhalt
- Custom Content

Beispiel:

```text
0x1A2B
```

### FooterPanel

`FooterPanel` ist der untere Bereich des Controls.

Er bleibt bewusst als `Panel` benannt, weil dort je nach Control Unterschiedliches liegen kann:

- einfacher Text
- Statusanzeige
- Funktionsbuttons
- kleine Aktionsleiste

Beispiel:

- bei einfachen Controls nur ein kurzer Hinweistext
- beim `LogControl` zusaetzliche Buttons oder Aktionen

## Begriffe, die vermieden werden sollen

### Title

`Title` soll in `EditControls` nicht fuer die obere linke Beschriftung verwendet werden.

Der Begriff ist zu unscharf und wird leicht als fachlicher Haupttitel einer Seite, eines Dokuments oder eines groesseren Inhaltsbereichs verstanden.

Wenn der gemeinte Text oben links als sichtbare Beschriftung des Controls dient, ist `ControlCaption` der passende Begriff.

### Header

`Header` soll moeglichst nicht als allgemeiner Oberbegriff fuer den oberen Control-Bereich verwendet werden.

Der Begriff ist zu grob.

Stattdessen soll konkret benannt werden:

- `ControlCaption` fuer den Text
- `ControlPanel` fuer den Funktionsbereich

## Layout-Regeln

### Caption bleibt Text, Panel bleibt Container

Es soll keine Vermischung geben:

- keine interaktiven Elemente in einer `Caption`
- keine reine Textrolle unter einem `Panel`-Namen modellieren

### BodyCaption ist optional

`BodyCaption` darf fehlen, ohne dass die Grundstruktur des Controls unklar wird.

Praktisch gilt fuer `ItemControl`:

- `ShowBodyCaption = false` blendet die Zeile aus
- leerer `BodyCaption`-Text blendet die Zeile ebenfalls aus
- der Wertbereich bekommt den frei werdenden Platz

### BodyCaption skaliert nicht wie der Hauptwert

`BodyCaption` ist ein eigener Textlayer und darf nicht mit der starken Skalierung des Hauptinhalts gekoppelt werden.

Sonst kippt die visuelle Hierarchie.

Deshalb:

- `BodyCaption` klein und stabil halten
- Hauptwert im `BodyPanel` separat layouten
- Sekundaertexte nicht ueber dieselbe Skalierungslogik wie den Hauptwert fuehren

### Skalierung im ItemControl

Fuer `ItemControl` gilt aktuell bewusst eine einfache und stabile Regel:

- `Value` skaliert nur aus der verfuegbaren `Body`-Hoehe
- wenn `BodyCaption` sichtbar ist, wird ihre Hoehe vorher von der verfuegbaren `Body`-Hoehe abgezogen
- `Unit` skaliert als fester Anteil vom `Value`
- die Breite bestimmt nicht die Fontgroesse

Das bedeutet konkret:

- `AvailableBodyHeight = BodyHoehe - BodyCaptionHoehe` wenn `ShowBodyCaption = true`
- `ItemValueFontSize` leitet sich direkt aus `AvailableBodyHeight` ab
- `ItemUnitFontSize` leitet sich direkt aus `ItemValueFontSize` ab

Damit bleibt die Hierarchie stabil:

- `BodyCaption` bleibt klein
- `Value` bleibt der Hauptinhalt
- `Unit` bleibt visuell nachgeordnet

### Linke Ausrichtung

`ControlCaption`, `BodyCaption` und `Value` sollen links buendig auf derselben Grundlinie starten.

Der linke Einzug dieser drei Ebenen darf daher nicht separat voneinander abweichen.

### Target-Mapping im ItemControl

Beim Auswaehlen eines Targets gilt fuer `ItemControl`:

- der Target-Parameter-`Text` ist nicht `BodyCaption`
- der Target-Parameter-`Text` darf nicht als internes Body-Label im `PropertyControl` erscheinen
- wenn vorhanden, wird der Target-Parameter-`Text` in `ControlCaption` uebernommen
- `BodyCaption` bleibt ein eigener, explizit gesetzter Text des Controls

Dadurch bleibt die Bedeutung sauber getrennt:

- `ControlCaption` = Benennung des Controls
- `BodyCaption` = optionale zweite Beschreibung
- `Value` = aktueller Inhalt des Targets

### Icon-Farben im ControlPanel

Die Icons im `ControlPanel` des `ItemControl` folgen dem Theme:

- Light Theme: schwarze Icons
- Dark Theme: weisse Icons

Die Buttons im `ControlPanel` bleiben dabei:

- mit transparentem Hintergrund
- quadratisch
- rein funktional, ohne eigene Caption-Rolle

## Beispiel

Fuer das gezeigte `Hex`-Control waere die Zuordnung:

- `ControlCaption`: `Hex`
- `BodyCaption`: `Editable Register`
- `BodyPanel`: `0x1A2B`
- rechter kursiver Zusatztext: nicht `BodyCaption`, sondern spaeter separat benennen

## Empfohlene Projektregel

Fuer neue und bestehende `EditControls` gilt:

1. Oberer linker Beschriftungstext heisst `ControlCaption`.
2. Oberer Funktionsbereich heisst `ControlPanel`.
3. Sekundaere Beschreibung ueber dem Hauptinhalt heisst `BodyCaption`.
4. Hauptinhalt liegt im `BodyPanel`.
5. Unterer Bereich heisst `FooterPanel`, auch wenn dort nur Text steht.
6. `Title` und `Header` werden in diesem Kontext nicht mehr als primaere Strukturbegriffe verwendet.

## Kurzfassung

```text
ControlCaption = primaere Beschriftung
ControlPanel   = oberer Funktionsbereich
BodyCaption    = optionale sekundaere Beschreibung
BodyPanel      = Hauptinhalt
FooterPanel    = unterer Bereich fuer Text, Status oder Aktionen
```
