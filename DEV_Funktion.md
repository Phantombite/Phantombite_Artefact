# DEV Funktion — PhantomBite Artefact

## Zweck
Das Space Artefact ist ein mysteriöser außerirdischer Block der Spieler mit Schockwellen, Batterie-Drain und globalem Wetter bestraft.

## Block

- **TypeId:** JumpDrive (Basis-Block)
- **SubtypeId:** SpaceArtefact
- **Größe:** 3x3x3 Large Grid
- **CriticalComponent:** AdminChip (1000x) — nur Admins können ihn bauen
- **Stromverbrauch:** 300 MW

## Zustände

| Zustand | Farbe | Rotation | Beschreibung |
|---------|-------|----------|--------------|
| INAKTIV | Schwarz | Keine | Block platziert, Status=off |
| IDLE | Grün | Langsam | Aktiviert, wartet auf Trigger |
| AUFLADEN | Rot (Welle Mitte→außen) | Schnell | Lädt Schockwelle auf |
| AFTERSHOCK | Rot (Welle Mitte→außen) | Schnell | Weitere Impulse nach dem Storm |

## Trigger

**Spieler-Trigger:**
- Spieler betritt 50m Zone → einmalige Alien-Nachricht → Aufladung startet
- Cooldown = Wetterdauer (kein erneuter Trigger während Wetter aktiv)
- Nach Wetterablauf: wenn Spieler noch in Zone → sofort neu triggern

**Random-Trigger:**
- Alle 1 Stunde wird gewürfelt (20% Wahrscheinlichkeit)
- Nur wenn mindestens ein Spieler online ist
- 7 gestaffelte Alien-Nachrichten (je 2 Sek) → dann Aufladung

## Impuls-Sequenz

1. Alien-Nachricht an alle Spieler in Reichweite
2. Rotation erhöht sich
3. Rot wandert von Kugel → InnerRing → MiddleRing → OuterRing
4. **Erster Impuls:** Batterie-Drain + ArtefactStorm starten
5. Zyklus beginnt neu (Kugel wieder rot) — Aftershocks
6. Jeder weitere Impuls: Drain steigt um +5%, kein neues Wetter

## Batterie-Drain

- **Formel:** `MaxStoredPower * (BatteryDrainBase + (Impuls-1) * BatteryDrainStep)`
- **Distanz-Abfall:** -0.5% pro 10m Entfernung (Minimum: immer über 0)
- **Beispiel bei 1. Impuls, 0m:** 45% der Maximalkapazität
- **Beispiel bei 1. Impuls, 100m:** 40% der Maximalkapazität
- **Nur aktive Batterien** (Enabled=true) werden gedrained

## ArtefactStorm Wetter

- **SubtypeId:** ArtefactStorm
- **Typ:** Schwerer Sandsturm mit Blitzen
- **Blitzschaden:** 80 HP
- **Blitzintervall:** 0.5–2 Sekunden
- **Radioaktivität:** RadiationGain 2.5 ab Intensität 0.35
- **Sauerstoff:** -1 (kein Sauerstoff außerhalb)
- **Dauer:** Konfigurierbar (Standard: 300 Sekunden)
- **Reichweite:** Global auf dem Planeten

## Custom Data Config

```
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
TriggerInterval=216000  ; Ticks - Intervall zwischen Random-Trigger Würfen (216000 = 1h)
TriggerChance=20        ; Prozent - Wahrscheinlichkeit beim Würfeln (20 = 20%)
```

## Script
- **Datei:** Data/Scripts/SpaceArtefactController.cs
- **Typ:** MyGameLogicComponent auf JumpDrive Blöcken
- **Update:** EACH_FRAME (60 Hz)
- **Spieler-Check:** alle 30 Ticks (0.5 Sek)

## Dateistruktur
```
Phantombite_Artefact/
├── modinfo.sbmi
├── metadata.mod
├── Data/
│   ├── CubeBlocks/SpaceArtefact.sbc
│   ├── Scripts/SpaceArtefactController.cs
│   └── Weather/Artefactstorm.sbc
├── Models/Cubes/large/SpaceArtefact/
│   ├── SpaceArtefact.mwm + BS1/BS2/BS3
│   ├── InnerRing.mwm + BS1/BS2/BS3
│   ├── MiddleRing.mwm + BS1/BS2/BS3
│   └── OuterRing.mwm + BS1/BS2/BS3
└── Textures/
```
