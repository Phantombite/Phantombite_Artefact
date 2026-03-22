# DEV History — PhantomBite Artefact

## 2026-03-22 — v1.0.0 — Initialer Release

- Space Artefact aus NimbusMod extrahiert und als eigenständiger Mod veröffentlicht
- Steam Workshop ID: 3689668016
- GitHub Repository: https://github.com/Phantombite/PhantomBiteArtefact
- MIT License

### Änderungen in dieser Session
- Custom Data Config eingebaut — alle Werte im Block einstellbar
- ID-System für mehrere Artefakte implementiert (`!spaceartefact 1 on`)
- `all` Command hinzugefügt (`!spaceartefact all reset`)
- Batterie-Drain bei jedem Impuls statt nur beim ersten
- Distanzabhängiger Batterie-Drain: -0.5% pro 10m Entfernung
- ArtefactStorm Wetter nur beim ersten Impuls — nicht bei Aftershocks
- Gestaffelte Alien-Nachrichten beim Random-Trigger (7 Nachrichten, 2 Sek Abstand)
- Random-Trigger: 1h Intervall, 20% Wahrscheinlichkeit, Spieler-Check
- Farbzyklus: Rot wandert von Mitte nach außen — kein Orange mehr
- Grüne Farbe beim Serverstart wenn Status=on in Custom Data
- DOT-Schaden auf 30-Tick Intervall für Performance
- Wetter-Position auf Artefakt-Position korrigiert
- `!spaceartefact reset` entfernt jetzt auch das Wetter
- Command Prefix von `/artefact=on` auf `!spaceartefact on` geändert
- Admin-Check: Singleplayer immer erlaubt, Multiplayer per PromoteLevel
- ArtefactStorm Radioaktivität auf RadiationGain 2.5 gesetzt
