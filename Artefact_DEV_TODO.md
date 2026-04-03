# DEV TODO — PhantomBite Artefact

## Offen

### Tests
- [ ] Server-Test: Batterie-Drain über SetRemainingCapacityByType — Wirksamkeit prüfen
- [ ] Server-Test: Wetter bei mehreren Artefakten auf verschiedenen Planeten
- [ ] Server-Test: CMDRESULT kommt korrekt beim Core an
- [ ] Server-Test: Log-Nachrichten erscheinen im Phantombite-Log

### Inhalt
- [ ] T2 und T3 Varianten (SubtypeId SpaceArtefactT2, SpaceArtefactT3) implementieren
- [ ] Alien-Nachrichten Text-Balancing
- [ ] Alte `SpaceArtefactController.cs` im Root entfernen (veraltet)

---

## Erledigt

### Core-Anbindung
- [x] Registrierung beim Core über READY-System
- [x] Commands kommen vom Core statt eigenem Handler
- [x] Alter Prefix `!spaceartefact` entfernt — alles über `!pbc artefact`
- [x] CMDRESULT-System — Artefact bestätigt, Core zeigt HUD
- [x] HUD-Feedback aus Controller entfernt — Core macht das
- [x] LOGLEVEL-System — Core setzt Level, Artefact filtert selbst
- [x] ID-Support in CMDRESULT: "Artefact ID 1: reset ausgeführt"

### Logging
- [x] Vollständiges Debug Logging: Commands, Trigger, Schockwellen, Wetter
- [x] Vollständiges Trace Logging: Spieler-Check, Timer, Steps, Drain pro Grid
- [x] Log über Kanal 1995999 in Phantombite-Core-Log

### Bugfixes
- [x] reset-Fix: LoadConfig() vor Reset-Variablen — RandomTimer korrekt
- [x] Alle Module von M0x auf Artefact_Name umbenannt

### v1.0.0
- [x] Custom Data Config
- [x] ID-System für mehrere Artefakte
- [x] all Command
- [x] Batterie-Drain skaliert pro Impuls
- [x] Distanzabhängiger Batterie-Drain
- [x] ArtefactStorm nur erster Impuls
- [x] Gestaffelte Alien-Nachrichten (Random-Trigger)
- [x] Random-Trigger mit Spieler-Check
- [x] Farbwelle Mitte nach außen
- [x] Grüne Farbe beim Start wenn aktiv
- [x] DOT Performance-Fix (30 Ticks)
- [x] Wetter-Reset bei reset Command
- [x] ArtefactStorm Radioaktivität