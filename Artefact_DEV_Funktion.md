# DEV Funktion — PhantomBite Artefact

## Zweck
Das Space Artefact ist ein mysteriöser außerirdischer Block der Spieler mit Schockwellen, Batterie-Drain und globalem Wetter bestraft. Er wird über den PhantomBite Core gesteuert.

---

## Dateistruktur
```
Phantombite_Artefact/
├── modinfo.sbmi
├── metadata.mod
├── Data/
│   ├── CubeBlocks/SpaceArtefact.sbc
│   ├── Weather/Artefactstorm.sbc
│   └── Scripts/PhantombiteArtefact/
│       ├── Core/
│       │   ├── IModule.cs              (Interface für alle Module)
│       │   ├── ModuleManager.cs        (Fehler-Isolation)
│       │   └── Session.cs              (Haupt-Session)
│       ├── Modules/
│       │   ├── Artefact_Command.cs     (Registrierung beim Core + Command-Empfang)
│       │   └── Artefact_Controller.cs  (MyGameLogicComponent — Animation + Logik)
│       └── SpaceArtefactController.cs  (VERALTET — nicht mehr genutzt)
├── Models/Cubes/large/SpaceArtefact/
│   ├── SpaceArtefact.mwm + BS1/BS2/BS3
│   ├── InnerRing.mwm + BS1/BS2/BS3
│   ├── MiddleRing.mwm + BS1/BS2/BS3
│   └── OuterRing.mwm + BS1/BS2/BS3
└── Textures/
```

---

## Block

- **TypeId:** JumpDrive (Basis-Block)
- **SubtypeIds:** SpaceArtefact / SpaceArtefactT2 / SpaceArtefactT3
- **Größe:** 3x3x3 Large Grid
- **CriticalComponent:** AdminChip (1000x) — nur Admins können ihn bauen
- **Stromverbrauch:** 300 MW

---

## Module

### Artefact_Command
- Registriert Artefact beim Core über READY-System
- Empfängt Commands vom Core (Kanal 1995001)
- Leitet Commands an alle passenden ArtefactController-Instanzen weiter
- Sendet CMDRESULT zurück an Core (Kanal 1995999)
- Hat eigene Log-API: Warn/Error/Info/Debug/Trace → sendet auf Kanal 1995999
- Speichert LOGLEVEL vom Core, filtert selbst bevor gesendet wird

**Registrierung beim Core:**
```
"REGISTER|artefact|Space Artefact Steuerung|1995001
  |on:1:Artefakt aktivieren (!pbc artefact [ID] on)
  |off:1:Artefakt deaktivieren (!pbc artefact [ID] off)
  |reset:1:Artefakt zurücksetzen (!pbc artefact [ID|all] reset)
  |trigger:1:Schockwelle auslösen (!pbc artefact [ID] trigger)"
```

**Command-Ablauf:**
1. Core sendet: `"CMD|reset|1|STEAM:76561198xxxxxxx"`
2. Artefact_Command parst Command + ID + SteamId
3. Sucht alle SpaceArtefact-Blöcke, gibt Command an ArtefactController weiter
4. Sendet CMDRESULT: `"CMDRESULT|artefact|reset|1|76561198xxxxxxx|ok|Artefact ID 1: reset ausgeführt"`

### Artefact_Controller (MyGameLogicComponent)
- Läuft auf jedem SpaceArtefact-Block separat
- Update: EACH_FRAME (60 Hz)
- Spieler-Check: alle 30 Ticks (0.5 Sek)
- Liest Config aus Custom Data des Blocks
- Steuert Animation (Rotation, Glow, Emissive)
- Steuert Trigger-Logik (Spieler + Random)
- Führt Batterie-Drain + Wetter aus
- Sendet kein eigenes HUD-Feedback — das macht der Core

---

## Zustände

| Zustand | Glow | Rotation | Beschreibung |
|---------|------|----------|--------------|
| INAKTIV | Schwarz | Keine | Status=off, kein Trigger |
| IDLE | Grün pulsierend | Ring=1.0 Global=0.5 | Aktiviert, wartet |
| AUFLADEN | Rot wandert Mitte→außen | Ring=3.0 Global=2.0 | Schockwelle lädt |
| WETTER/AFTERSHOCK | Grün schnell | Ring=4.0 Global=2.5 | Storm aktiv, Nachbeben |
| WETTER + Spieler nah | Grün sehr schnell | Ring=6.0 Global=4.0 | Spieler in Zone während Storm |

---

## Trigger

### Spieler-Trigger
- Spieler betritt TriggerRange (Standard 50m)
- Einmalig pro Annäherung (ShockwaveFired-Flag)
- Nicht wenn Cooldown aktiv oder Wetter bereits läuft
- Sofortige Alien-Nachricht → Aufladung startet

### Random-Trigger
- Alle TriggerInterval Ticks (Standard 216000 = 1h) wird gewürfelt
- TriggerChance% Wahrscheinlichkeit (Standard 20%)
- Nur wenn: kein Wetter aktiv, kein Impuls läuft, mindestens 1 Spieler online
- 7 gestaffelte Alien-Nachrichten (je 2 Sek) → dann Aufladung

### Manuell (Admin-Command)
- `!pbc artefact trigger` — sofort Aufladung ohne Wartezeit

---

## Impuls-Sequenz

```
Trigger (Spieler/Random/Manuell)
  ↓
Alien-Nachricht(en)
  ↓
Aufladung (ImpulsePhase.Charging)
  Rot wandert: Center(1s) → Inner(1s) → Middle(1s) → Outer(1s) → Explosion(1s)
  ↓
Schockwelle (FireShockwave)
  → Batterie-Drain auf alle Grids in Reichweite
  → ArtefactStorm Wetter starten (WeatherDuration Sekunden)
  → Cooldown = WeatherDuration * 60 Ticks
  ↓
Nachbeben (ImpulsePhase.Aftershock) — läuft bis Wetter endet
  Rot wandert: Center → Inner → Middle → Outer → Explosion
  → Batterie-Drain (kein neues Wetter!)
  → 6 Sekunden Pause
  → Wiederholen
  ↓
Wetter endet → IDLE
```

---

## Batterie-Drain

- **Formel:** `drainBase = BatteryDrainBase + (Impuls-1) * BatteryDrainStep`
- **Distanz-Abfall:** `drainPct = drainBase - (dist/10 * 0.005)` (min 0.01, max 0.95)
- **Beispiel Impuls 1, 0m:** 45%
- **Beispiel Impuls 1, 100m:** 40%
- **Beispiel Impuls 2, 0m:** 50%
- **Nur aktive + funktionale Batterien** (Enabled=true, IsFunctional=true)
- **Alle Grids** innerhalb BatteryDrainRange werden gedrained

---

## DOT-Schaden

- Nur während Wetter aktiv
- Nur Spieler zu Fuß (nicht im Cockpit) innerhalb DotRange (Standard 5m)
- DotDamage HP pro Sekunde (alle 30 Ticks = 0.5 Sek → DotDamage/2 pro Tick)
- Schadenstyp: Energy

---

## ArtefactStorm Wetter

- **SubtypeId:** ArtefactStorm
- **Typ:** Schwerer Sandsturm mit Blitzen
- **Blitzschaden:** 80 HP
- **Blitzintervall:** 0.5–2 Sekunden
- **Radioaktivität:** RadiationGain 2.5 ab Intensität 0.35
- **Sauerstoff:** -1 (kein Sauerstoff außerhalb)
- **Dauer:** WeatherDuration Sekunden (Standard 300)
- **Reichweite:** Global auf dem Planeten

---

## Custom Data Config

```ini
[SpaceArtefact]
ID=0                    ; Artefakt-ID (0 = Standard)
Status=off              ; on = aktiv, off = deaktiviert
TriggerRange=50         ; Meter - Spieler löst Aufladung aus
DotDamage=20            ; HP pro Sekunde bei sehr nahem Aufenthalt
DotRange=5              ; Meter - Radius für DOT Schaden
BatteryDrainBase=0.45   ; Basis-Drain beim ersten Impuls (0.45 = 45%)
BatteryDrainStep=0.05   ; Anstieg pro Impuls (0.05 = +5% pro Impuls)
BatteryDrainRange=300   ; Meter - Reichweite des Batterie-Drains (max 300m)
WeatherDuration=300     ; Sekunden - Dauer des globalen ArtefactStorm Wetters
TriggerInterval=216000  ; Ticks - Intervall zwischen Random-Trigger Würfen (1h)
TriggerChance=20        ; Prozent - Wahrscheinlichkeit beim Würfeln (20%)
```

Config wird beim Start geladen. `reset` Command lädt sie neu.
Wenn Custom Data leer ist wird der Default automatisch geschrieben.

---

## Logging

Artefact_Command hat eine eigene Log-API die über Core-Logger loggt:

```csharp
_logger?.Debug("Artefact_Controller", "SPIELER-TRIGGER — Distanz: 42.1m");
_logger?.Trace("Artefact_Controller", "Spieler-Check: 42.1m | inRange=True | ...");
```

**Debug zeigt:** Commands, Trigger, Schockwellen, Drain-Zusammenfassung, Wetter, Zustände
**Trace zeigt:** Spieler-Check alle 30 Ticks, Timer-Werte, Impuls-Steps, Drain pro Grid, Rotation

Debug-Level wird vom Core gesetzt: `!pbc debug artefact debug`

---

## Anbindung an Core (Zusammenfassung)

```
SE-Start
  ↓
Artefact_Command.Init() — hört auf Kanal 1995001
  ↓
Core sendet "READY" → Artefact_Command sendet REGISTER
  ↓
Core sendet "LOGLEVEL|normal" → Artefact_Command speichert Level
  ↓
Admin tippt "!pbc artefact 1 reset"
  ↓
Core sendet "CMD|reset|1|STEAM:76561198..." auf Kanal 1995001
  ↓
Artefact_Command findet alle SpaceArtefact-Blöcke
  ↓
ArtefactController.ExecuteCommand("reset", 1, true, false)
  ↓
Artefact_Command sendet "CMDRESULT|artefact|reset|1|76561198...|ok|Artefact ID 1: reset ausgeführt"
  ↓
Core zeigt HUD "[PB] Artefact ID 1: reset ausgeführt" (grün)
```