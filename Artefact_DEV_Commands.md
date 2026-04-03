# DEV Commands — PhantomBite Artefact

Alle Commands erfordern Admin-Rechte.
Singleplayer: immer erlaubt. Multiplayer: PromoteLevel >= Admin.

Commands werden über den PhantomBite Core gesendet (`!pbc`).
Feedback kommt als HUD-Notification vom Core (Grün = Erfolg, Rot = Fehler).

## Syntax
```
!pbc artefact [ID] <command>
!pbc artefact all <command>
```

## Commands

| Command | Beschreibung |
|---------|-------------|
| `!pbc artefact on` | Artefakt ID=0 aktivieren |
| `!pbc artefact off` | Artefakt ID=0 deaktivieren |
| `!pbc artefact reset` | ID=0 zurücksetzen + Config neu laden + Wetter entfernen |
| `!pbc artefact trigger` | ID=0 manuell auslösen |
| `!pbc artefact 1 on` | Artefakt mit ID=1 aktivieren |
| `!pbc artefact 1 off` | Artefakt mit ID=1 deaktivieren |
| `!pbc artefact 1 reset` | Artefakt mit ID=1 zurücksetzen |
| `!pbc artefact 1 trigger` | Artefakt mit ID=1 manuell auslösen |
| `!pbc artefact all on` | Alle Artefakte aktivieren |
| `!pbc artefact all off` | Alle Artefakte deaktivieren |
| `!pbc artefact all reset` | Alle Artefakte zurücksetzen |
| `!pbc artefact all trigger` | Alle Artefakte manuell auslösen |

## Hinweise
- `reset` lädt die Custom Data Config neu — ID-Änderungen werden damit wirksam
- `trigger` funktioniert nur wenn das Artefakt aktiv ist (Status=on)
- Mehrere Artefakte mit gleicher ID reagieren gleichzeitig auf Commands
- Wetter wird pro Planet gesetzt — jedes Artefakt setzt es an seiner eigenen Position
- Der alte Prefix `!spaceartefact` existiert nicht mehr — alles läuft über `!pbc`