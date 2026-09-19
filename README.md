# Phantombite Artefact

Ein mysteriöses außerirdisches Artefakt für Server-Events. Der Block `SpaceArtefact` (3×3×3, Large Grid) bestraft Spieler in
seiner Nähe mit **Schockwellen**, **Batterie-Entladung** und **globalem Wetter** (Artefakt-Sturm auf dem Planeten).
Er kann automatisch zufällig auslösen oder von Admins per Command gesteuert werden.

## Funktionen
- Blöcke: `SpaceArtefact`, `SpaceArtefactT2`, `SpaceArtefactT3`
- Animierte Ringe, Sound, Schockwellen in mehreren Stufen mit Nachschock
- Zufälliger Auslöser mit einstellbarem Intervall und einstellbarer Chance (läuft auf dem Server, auch wenn kein Spieler in der Nähe ist)
- Warn-Nachrichten im Chat, Wetter pro Planet
- Mehrere Artefakte gleichzeitig, jedes mit eigener ID (Einstellungen in der Custom Data des Blocks)

## Commands (nur Admin)
```
!pbc artefact [ID] <command>      z. B. !pbc artefact 1 on
!pbc artefact all <command>       für alle Artefakte
```
| Command | Beschreibung |
|---|---|
| `on` | Artefakt aktivieren |
| `off` | Artefakt deaktivieren |
| `reset` | Zurücksetzen, Config neu laden, Wetter entfernen |
| `trigger` | Manuell auslösen (nur wenn aktiv) |

Ohne ID gilt ID 0. `reset` lädt die Custom Data neu, damit ID-Änderungen wirksam werden.

## Voraussetzungen
- **Phantombite Core** (Commands und `AdminChip`). Der Block braucht 1000× `AdminChip`, nur Admins können ihn bauen.

Workshop-ID: 3689668016
