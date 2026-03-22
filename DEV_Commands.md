# DEV Commands — PhantomBite Artefact

Alle Commands erfordern Admin-Rechte.
Singleplayer: immer erlaubt. Multiplayer: PromoteLevel >= Admin.

## Syntax
```
!spaceartefact [ID] <command>
!spaceartefact all <command>
```

## Commands

| Command | Beschreibung |
|---------|-------------|
| `!spaceartefact on` | Artefakt ID=0 aktivieren |
| `!spaceartefact off` | Artefakt ID=0 deaktivieren |
| `!spaceartefact reset` | ID=0 zurücksetzen + Config neu laden + Wetter entfernen |
| `!spaceartefact trigger` | ID=0 manuell auslösen |
| `!spaceartefact 1 on` | Artefakt mit ID=1 aktivieren |
| `!spaceartefact 1 off` | Artefakt mit ID=1 deaktivieren |
| `!spaceartefact 1 reset` | Artefakt mit ID=1 zurücksetzen |
| `!spaceartefact 1 trigger` | Artefakt mit ID=1 manuell auslösen |
| `!spaceartefact all on` | Alle Artefakte aktivieren |
| `!spaceartefact all off` | Alle Artefakte deaktivieren |
| `!spaceartefact all reset` | Alle Artefakte zurücksetzen |
| `!spaceartefact all trigger` | Alle Artefakte manuell auslösen |

## Hinweise
- `reset` lädt die Custom Data Config neu — ID-Änderungen werden damit wirksam
- `trigger` funktioniert nur wenn das Artefakt aktiv ist (Status=on)
- Mehrere Artefakte mit gleicher ID reagieren gleichzeitig auf Commands
- Wetter wird pro Planet gesetzt — jedes Artefakt setzt es an seiner eigenen Position
