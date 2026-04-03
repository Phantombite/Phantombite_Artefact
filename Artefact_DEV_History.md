# DEV History — PhantomBite Artefact

## 2026-03-27 — v2.0.0 — Core-Anbindung + Logging

### Core-Anbindung
- Artefact registriert sich beim Core über READY-System (Kanal 1995001)
- Commands kommen jetzt vom Core statt eigenem Command-Handler
- Alter Prefix `!spaceartefact` entfernt — alles über `!pbc artefact`
- CMDRESULT-System: Artefact bestätigt Ausführung, Core zeigt HUD-Feedback
- HUD-Feedback aus Controller entfernt — Core macht das zentral
- LOGLEVEL-System: Core teilt Artefact mit welches Level er empfangen will

### Neue Dateistruktur
- `Artefact_Command.cs` — Command-Modul (Registrierung + Empfang + CMDRESULT)
- `Artefact_Controller.cs` — Game Logic Component (Animation + Logik)
- Alte `SpaceArtefactController.cs` im Root veraltet — nicht mehr genutzt
- Alle Module von `M0x_Name` auf `Artefact_Name` umbenannt

### Logging
- Vollständiges Debug/Trace Logging im Controller eingebaut
- Debug: Commands, Trigger, Schockwellen, Drain-Zusammenfassung, Wetter, Zustände
- Trace: Spieler-Check alle 30 Ticks, Timer, Impuls-Steps, Drain pro Grid, Rotation/Glow
- Log geht über Kanal 1995999 in den Phantombite-Core-Log

### ID-Support
- `!pbc artefact 1 on` → ID=1
- `!pbc artefact all reset` → alle
- CMDRESULT zeigt korrekte Ziel-Info: "Artefact ID 1: reset ausgeführt"

### reset-Fix
- LoadConfig() wird jetzt vor den Reset-Variablen aufgerufen
- RandomTimer wird korrekt auf neuen TriggerInterval gesetzt

---

## 2026-03-22 — v1.0.0 — Initialer Release

- Space Artefact aus NimbusMod extrahiert und als eigenständiger Mod veröffentlicht
- Steam Workshop ID: 3689668016
- GitHub Repository: https://github.com/Phantombite/PhantomBiteArtefact
- MIT License

### Features v1.0.0
- Custom Data Config — alle Werte im Block einstellbar
- ID-System für mehrere Artefakte
- `all` Command: `!spaceartefact all reset`
- Batterie-Drain bei jedem Impuls (steigt pro Impuls)
- Distanzabhängiger Batterie-Drain: -0.5% pro 10m
- ArtefactStorm nur beim ersten Impuls — Aftershocks ohne Wetter
- 7 gestaffelte Alien-Nachrichten beim Random-Trigger (je 2 Sek)
- Random-Trigger: 1h Intervall, 20% Wahrscheinlichkeit, Spieler-Check
- Rot wandert von Mitte nach außen — kein Orange mehr
- Grüne Farbe beim Serverstart wenn Status=on
- DOT-Schaden auf 30-Tick Intervall
- Wetter-Position auf Artefakt-Position
- `reset` entfernt Wetter
- Admin-Check: Singleplayer immer, Multiplayer per PromoteLevel
- ArtefactStorm Radioaktivität: RadiationGain 2.5