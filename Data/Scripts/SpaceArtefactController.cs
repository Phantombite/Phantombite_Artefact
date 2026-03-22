using System;
using System.Collections.Generic;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using Sandbox.Game.EntityComponents;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;

namespace SpaceArtefact
{
    // ============================================================
    //  SpaceArtefact – v3 (Dune Server Edition)
    //
    //  ZUSTÄNDE:
    //    INAKTIV     Block platziert, /artefact=off oder noch nie aktiviert
    //                → Schwarz, keine Rotation, kein Trigger
    //
    //    IDLE        /artefact=on, kein Spieler in Reichweite
    //                → Grün, langsame Rotation
    //
    //    AUFLADEN    Spieler betritt 50m Zone ODER Random-Trigger
    //                → Rot wandert Center→Inner→Middle→Outer
    //                → Alien-Nachricht
    //                → Am Ende: Schockwelle + ArtefactStorm
    //
    //    WETTER      ArtefactStorm läuft (300 Sek)
    //                → Orange, schnelle Rotation
    //                → Wiederholende Nachbeben-Impulse
    //                → DOT 20 HP/Sek für Spieler innerhalb 5m zu Fuß
    //
    //  TRIGGER:
    //    Spieler-Trigger: einmalig pro Annäherung, Cooldown = Wetterdauer
    //    Random-Trigger:  alle 30-60 Min wenn kein Wetter aktiv
    //
    //  COMMANDS (Admin-only):
    //    /artefact=on      → Aktivieren
    //    /artefact=off     → Deaktivieren
    //    /artefact=reset   → Alles zurücksetzen
    //    /artefact=trigger → Manuell Schockwelle auslösen
    // ============================================================

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_JumpDrive), false,
        "SpaceArtefact", "SpaceArtefactT2", "SpaceArtefactT3")]
    public class SpaceArtefactController : MyGameLogicComponent
    {
        // ──────────────────────────────────────────────────────────
        //  KONFIGURATION
        // ──────────────────────────────────────────────────────────

        // ── CONFIG DEFAULTS (werden aus Custom Data geladen) ──────
        private const string DEFAULT_CONFIG = 
            "[SpaceArtefact]\n" +
            "ID=0                    ; Artefakt-ID (0 = Standard)\n" +
            "Status=off              ; on = aktiv, off = deaktiviert\n" +
            "TriggerRange=50         ; Meter - Spieler löst Aufladung aus\n" +
            "DotDamage=20            ; HP pro Sekunde bei sehr nahem Aufenthalt\n" +
            "DotRange=5              ; Meter - Radius fuer DOT Schaden\n" +
            "BatteryDrainBase=0.45   ; Basis-Drain beim ersten Impuls (0.45 = 45%)\n" +
            "BatteryDrainStep=0.05   ; Anstieg pro Impuls (0.05 = +5% pro Impuls)\n" +
            "BatteryDrainRange=300   ; Meter - Reichweite des Batterie-Drains (max 300m)\n" +
            "WeatherDuration=300     ; Sekunden - Dauer des globalen ArtefactStorm Wetters\n" +
            "TriggerInterval=216000  ; Ticks - Intervall zwischen Random-Trigger Wuerfen (216000 = 1 Stunde)\n" +
            "TriggerChance=20        ; Prozent - Wahrscheinlichkeit beim Wuerfeln (20 = 20%)\n" +
            "\n" +
            "; ── COMMANDS (nur Admins) ──────────────────────────────\n" +
            "; !spaceartefact on          - Artefakt aktivieren (ID=0)\n" +
            "; !spaceartefact off         - Artefakt deaktivieren (ID=0)\n" +
            "; !spaceartefact reset       - Alles zuruecksetzen + Wetter entfernen (ID=0)\n" +
            "; !spaceartefact trigger     - Manuell Schockwelle ausloesen (ID=0)\n" +
            "; !spaceartefact 1 on        - Artefakt mit ID=1 aktivieren\n" +
            "; !spaceartefact 1 off       - Artefakt mit ID=1 deaktivieren\n" +
            "; !spaceartefact 1 reset     - Artefakt mit ID=1 zuruecksetzen\n" +
            "; !spaceartefact 1 trigger   - Artefakt mit ID=1 manuell ausloesen\n";

        // Feste Konstante
        private const string WEATHER_ARTEFACT = "ArtefactStorm";

        // Laufzeit-Konfig (aus Custom Data geladen)
        private int   _artefactId          = 0;
        private float _rangeTrigger        = 50f;
        private float _rangeDot            = 5f;
        private float _dotDamagePerTick    = 10f;   // 20 HP/Sek bei 30-Tick Intervall
        private float _batteryDrainBase    = 0.45f;
        private float _batteryDrainStep    = 0.05f;
        private float _batteryDrainRange   = 300f;
        private int   _weatherDuration     = 300;
        private int   _triggerInterval     = 216000;
        private int   _triggerChance       = 20;

        // Timings
        private int COOLDOWN_TICKS => _weatherDuration * 60; // = Wetterdauer
        private const int CHECK_INTERVAL         = 30;   // Spieler-Check alle 0.5 Sek
        private const int CHARGE_STEP_TICKS      = 60;   // 1 Sek pro Auflade-Step
        private const int AFTERSHOCK_STEP_TICKS  = 40;   // Nachbeben-Step
        private const int AFTERSHOCK_PAUSE_TICKS = 360;  // 6 Sek Pause zwischen Nachbeben

        // ──────────────────────────────────────────────────────────
        //  RANDOM-TRIGGER KONFIGURATION
        //  Werte werden aus Custom Data geladen (siehe DEFAULT_CONFIG)
        //  Frühester möglicher Abstand zwischen zwei Triggern = _triggerInterval
        // ──────────────────────────────────────────────────────────

        // Rotation
        private const float ROT_IDLE_RING    = 1.0f;  private const float ROT_IDLE_GLOB    = 0.5f;
        private const float ROT_CHARGE_RING  = 3.0f;  private const float ROT_CHARGE_GLOB  = 2.0f;
        private const float ROT_STORM_RING   = 4.0f;  private const float ROT_STORM_GLOB   = 2.5f;
        private const float ROT_MAX_RING     = 6.0f;  private const float ROT_MAX_GLOB     = 4.0f;

        // Glow
        private const float GLOW_MIN         = 1.5f;
        private const float GLOW_MAX         = 3.5f;
        private const float GLOW_PULSE_IDLE  = 0.02f;
        private const float GLOW_PULSE_STORM = 0.05f;

        // Farben
        private static readonly Color COLOR_GREEN  = new Color(0, 255, 0)   * 3.0f;
        private static readonly Color COLOR_ORANGE = new Color(255, 140, 0) * 3.0f;
        private static readonly Color COLOR_RED    = new Color(255, 0, 0)   * 3.0f;

        // Alien-Nachrichten
        private const string MSG_PLAYER =
            "Kre'shah... voth'nal... zim'kora eth'win... nor'tal bin'kess...";

        // Random-Trigger: gestaffelte Nachrichten (je 2 Sek = 120 Ticks)
        private static readonly string[] MSG_RANDOM_SEQUENCE = new string[]
        {
            "Kre'shah... voth'nal...",
            "...zim'kora... eth'win...",
            "Nor'tal... bin'kess... voth'ghral...",
            "...kre'kre'shah... ZIM'KORA'VEKH...",
            "ETH'MORT... NOR'SHAL'TARA...",
            "BIN'GHRAL... KESS'KESS...",
            "...M O R T !!!"
        };

        // Subparts
        private static readonly string[] SUBPART_NAMES =
            { "OuterRing_section_1", "MiddleRing", "InnerRing" };
        private static readonly Vector3 AXIS_OUTER  = Vector3.Forward;
        private static readonly Vector3 AXIS_MIDDLE = Vector3.Right;
        private static readonly Vector3 AXIS_INNER  = Vector3.Down;
        private static readonly Vector3 AXIS_GLOBAL = Vector3.Up;

        // ──────────────────────────────────────────────────────────
        //  LAUFZEIT-VARIABLEN
        // ──────────────────────────────────────────────────────────

        private IMyFunctionalBlock _block;
        private bool _active = false;  // Startet INAKTIV
        private bool _msgHandlerRegistered;
        private bool _dmgHandlerRegistered;
        private Random _rng = new Random();

        // Timer
        private int _frameTick    = 0;
        private int _weatherTimer = 0;   // > 0 = Wetter aktiv
        private int _cooldown     = 0;   // Verhindert Re-Trigger
        private int _randomTimer  = 0;   // Countdown bis Random-Trigger

        // Spieler-Nähe
        private bool _playerInRange  = false;
        private bool _shockwaveFired = false;

        // Hit-Counter für Batterie-Skalierung
        private int _hitCount = 0;

        // Rotation (smooth)
        private float  _rotSpeed         = ROT_IDLE_RING;
        private float  _rotSpeedTarget   = ROT_IDLE_RING;
        private float  _globalSpeed      = ROT_IDLE_GLOB;
        private float  _globalSpeedTarget = ROT_IDLE_GLOB;
        private Matrix _globalMatrix     = Matrix.Identity;

        // Glow
        private float _glowIntensity  = 2.0f;
        private bool  _glowReverse    = false;
        private float _glowPulseSpeed = GLOW_PULSE_IDLE;

        // Subpart-Cache
        private struct SubpartInfo { public Matrix LocalMatrix; public bool Init; }
        private readonly Dictionary<string, SubpartInfo> _subparts =
            new Dictionary<string, SubpartInfo>();

        // Impulse
        private enum ImpulsePhase { Idle, Charging, Aftershock }
        private ImpulsePhase _impulsePhase = ImpulsePhase.Idle;
        private int  _impulseStep  = 0;
        private int  _impulseTimer = 0;
        private bool _impulsePause = false;
        private int  _stepDuration;
        private int  _pauseDuration;
        private bool _isRandomTrigger = false;

        // Random-Nachrichten Sequenz
        private int  _randomMsgIndex = 0;
        private int  _randomMsgTimer = 0;
        private bool _randomMsgActive = false;
        private const int RANDOM_MSG_INTERVAL = 120; // 2 Sek in Ticks

        // Delayed Damage (nur für DOT, kein Schockwellen-Spielerschaden)
        private class DelayedDamage
        {
            public IMyCharacter Target;
            public float        Damage;
            public int          TicksLeft;
        }
        private readonly List<DelayedDamage> _delayedDamages = new List<DelayedDamage>();

        // Sound
        private MyEntity3DSoundEmitter _chargeEmitter;
        private bool _chargeSoundActive;

        // ──────────────────────────────────────────────────────────
        //  INITIALISIERUNG
        // ──────────────────────────────────────────────────────────

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            NeedsUpdate = MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
        }

        public override void UpdateOnceBeforeFrame()
        {
            _block = Entity as IMyFunctionalBlock;
            if (_block == null) return;

            // EACH_FRAME starten - Custom Data wird im ersten UpdateBeforeSimulation geschrieben
            NeedsUpdate = MyEntityUpdateEnum.EACH_FRAME;
        }

        private bool _fullyInitialized = false;

        // Zweiter Init-Frame — CustomData ist jetzt verfügbar
        // ──────────────────────────────────────────────────────────
        //  CONFIG LADEN / SPEICHERN
        // ──────────────────────────────────────────────────────────

        private void LoadConfig()
        {
            try
            {
                string data = _block.CustomData;

                // Erste Initialisierung - Default Config schreiben
                if (string.IsNullOrWhiteSpace(data) || !data.Contains("[SpaceArtefact]"))
                {
                    _block.CustomData = DEFAULT_CONFIG;
                    data = DEFAULT_CONFIG;
                }

                foreach (var line in data.Split('\n'))
                {
                    // Kommentare und leere Zeilen überspringen
                    string trimmed = line.Trim();
                    if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith(";")) continue;

                    // Key=Value parsen (Kommentar nach Wert abschneiden)
                    int eq = trimmed.IndexOf('=');
                    if (eq < 0) continue;
                    string key = trimmed.Substring(0, eq).Trim().ToLower();
                    string val = trimmed.Substring(eq + 1).Trim();
                    // Inline-Kommentar abschneiden
                    int sc = val.IndexOf(';');
                    if (sc >= 0) val = val.Substring(0, sc).Trim();

                    switch (key)
                    {
                        case "id":              int.TryParse(val, out _artefactId);                         break;
                        case "status":          _active = val.ToLower() == "on";                            break;
                        case "triggerrange":    float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _rangeTrigger);      break;
                        case "dotdamage":       float dotDmg; if (float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out dotDmg)) _dotDamagePerTick = dotDmg / 2f; break;
                        case "dotrange":        float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _rangeDot);         break;
                        case "batterydrainbase":float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _batteryDrainBase); break;
                        case "batterydrainstep":float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _batteryDrainStep); break;
                        case "batterydrainrange":float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _batteryDrainRange); break;
                        case "weatherduration": int.TryParse(val, out _weatherDuration);                    break;
                        case "triggerinterval": int.TryParse(val, out _triggerInterval);                    break;
                        case "triggerchance":   int.TryParse(val, out _triggerChance);                      break;
                    }
                }
            }
            catch { }
        }

        private void SaveStatus(bool active)
        {
            try
            {
                string data = _block.CustomData;
                var lines = data.Split('\n');
                var result = new System.Text.StringBuilder();
                foreach (var line in lines)
                {
                    string trimmed = line.Trim();
                    if (trimmed.ToLower().StartsWith("status=") || 
                        trimmed.ToLower().StartsWith("status ="))
                        result.AppendLine("Status=" + (active ? "on" : "off") + "              ; on = aktiv, off = deaktiviert");
                    else
                        result.AppendLine(line);
                }
                _block.CustomData = result.ToString().TrimEnd();
            }
            catch { }
        }

        // ──────────────────────────────────────────────────────────
        //  HAUPT-UPDATE
        // ──────────────────────────────────────────────────────────

        public override void UpdateBeforeSimulation()
        {
            // Init-Check: erst nach erstem Frame ist CustomData verfügbar
            if (!_fullyInitialized)
            {
                _fullyInitialized = true;

                if (!_msgHandlerRegistered)
                {
                    MyAPIGateway.Utilities.MessageEntered += OnMessageEntered;
                    _msgHandlerRegistered = true;
                }
                if (!_dmgHandlerRegistered)
                {
                    MyAPIGateway.Session.DamageSystem.RegisterBeforeDamageHandler(0, BeforeDamageHandler);
                    _dmgHandlerRegistered = true;
                }

                try
                {
                    _chargeEmitter = new MyEntity3DSoundEmitter((MyEntity)Entity);
                    _chargeEmitter.CustomMaxDistance = 200f;
                    _chargeEmitter.CustomVolume      = 1.5f;
                }
                catch { }

                // Custom Data leer? → Default schreiben (wie SpaceEconomy ProcessBlock)
                if (string.IsNullOrWhiteSpace(_block.CustomData))
                    _block.CustomData = DEFAULT_CONFIG;

                // Config laden
                LoadConfig();

                // Wenn aktiv → sofort grün setzen
                if (_active)
                {
                    SetAllEmissive(COLOR_GREEN, _glowIntensity);
                    SetRotationTarget();
                }

                // Random-Timer initialisieren
                _randomTimer = _triggerInterval;
                return;
            }

            if (_block == null || _block.Closed) return;

            ProcessDelayedDamages();

            if (!_active)
            {
                SetAllEmissive(Color.Black, 0f);
                return;
            }

            _frameTick++;

            // Timer
            if (_weatherTimer > 0) _weatherTimer--;
            if (_cooldown     > 0) _cooldown--;

            // Rotation smooth
            _rotSpeed    = MathHelper.Lerp(_rotSpeed,    _rotSpeedTarget,    0.08f);
            _globalSpeed = MathHelper.Lerp(_globalSpeed, _globalSpeedTarget, 0.08f);

            // Glow-Puls
            float glowTarget = WeatherActive ? GLOW_PULSE_STORM : GLOW_PULSE_IDLE;
            _glowPulseSpeed  = MathHelper.Lerp(_glowPulseSpeed, glowTarget, 0.05f);

            // EACH_FRAME: Rotation + Glow
            UpdateRotationAndGlow();

            // EACH_FRAME: Impulse-Animation
            UpdateImpulse();

            // Random-Nachrichten Sequenz (läuft jeden Frame)
            UpdateRandomMessages();

            // 30-TICK: Spieler-Check + Random-Timer + DOT
            if (_frameTick % CHECK_INTERVAL == 0)
            {
                UpdatePlayerCheck();
                UpdateRandomTimer();

                // DOT alle 30 Ticks statt jeden Frame (30x weniger GetPlayers() Aufrufe)
                if (WeatherActive)
                    ApplyDot();
            }

            // Wetter endet → Nachbeben stoppen + Spieler-Flag zurücksetzen
            if (!WeatherActive && _impulsePhase == ImpulsePhase.Aftershock)
            {
                StopImpulse();
                _shockwaveFired = false; // Spieler in Zone lösen erneut aus
            }
        }

        private bool WeatherActive => _weatherTimer > 0;

        // ──────────────────────────────────────────────────────────
        //  ROTATION + GLOW
        // ──────────────────────────────────────────────────────────

        private void UpdateRotationAndGlow()
        {
            float globalRad = MathHelper.ToRadians(0.5f * _globalSpeed);
            _globalMatrix = Matrix.Normalize(
                Matrix.Multiply(Matrix.CreateFromAxisAngle(AXIS_GLOBAL, globalRad), _globalMatrix));

            RotateSubpart("OuterRing_section_1", AXIS_OUTER,  _rotSpeed);
            RotateSubpart("MiddleRing",           AXIS_MIDDLE, _rotSpeed);
            RotateSubpart("InnerRing",            AXIS_INNER,  _rotSpeed);

            _glowIntensity += _glowReverse ? -_glowPulseSpeed : _glowPulseSpeed;
            if (_glowIntensity >= GLOW_MAX) _glowReverse = true;
            else if (_glowIntensity <= GLOW_MIN) _glowReverse = false;
            _glowIntensity = MathHelper.Clamp(_glowIntensity, GLOW_MIN, GLOW_MAX);
        }

        private void SetRotationTarget()
        {
            if (_impulsePhase == ImpulsePhase.Charging)
            {
                _rotSpeedTarget    = ROT_CHARGE_RING;
                _globalSpeedTarget = ROT_CHARGE_GLOB;
            }
            else if (WeatherActive && _playerInRange)
            {
                _rotSpeedTarget    = ROT_MAX_RING;
                _globalSpeedTarget = ROT_MAX_GLOB;
            }
            else if (WeatherActive)
            {
                _rotSpeedTarget    = ROT_STORM_RING;
                _globalSpeedTarget = ROT_STORM_GLOB;
            }
            else
            {
                _rotSpeedTarget    = ROT_IDLE_RING;
                _globalSpeedTarget = ROT_IDLE_GLOB;
            }
        }

        // ──────────────────────────────────────────────────────────
        //  DOT (20 HP/Sek innerhalb 5m, nur zu Fuß)
        // ──────────────────────────────────────────────────────────

        private void ApplyDot()
        {
            try
            {
                Vector3D pos = _block.PositionComp.WorldAABB.Center;
                var players = new List<IMyPlayer>();
                MyAPIGateway.Players.GetPlayers(players);

                foreach (var p in players)
                {
                    if (p?.Character == null || p.Character.IsDead) continue;
                    if (p.Character.Parent != null) continue; // im Cockpit = sicher

                    float dist = (float)Vector3D.Distance(pos, p.Character.GetPosition());
                    if (dist > _rangeDot) continue;

                    p.Character.DoDamage(_dotDamagePerTick,
                        MyStringHash.GetOrCompute("Energy"), true);
                }
            }
            catch { }
        }

        // ──────────────────────────────────────────────────────────
        //  SPIELER-CHECK (alle 30 Ticks)
        // ──────────────────────────────────────────────────────────

        private void UpdatePlayerCheck()
        {
            float closest = GetClosestPlayerDistance();
            bool  inRange = closest <= _rangeTrigger;

            if (!inRange && _playerInRange)
                _shockwaveFired = false;

            _playerInRange = inRange;

            // Spieler-Trigger: einmalig wenn reinkommt, kein Cooldown, kein Wetter aktiv
            if (_playerInRange && !_shockwaveFired && _cooldown == 0 && !WeatherActive
                && _impulsePhase == ImpulsePhase.Idle)
            {
                _shockwaveFired  = true;
                _isRandomTrigger = false;
                ShowAlienMessage(false);
                StartChargeImpulse();
            }

            SetRotationTarget();
        }

        // ──────────────────────────────────────────────────────────
        //  RANDOM-TRIGGER (alle 30-60 Min)
        // ──────────────────────────────────────────────────────────

        private void UpdateRandomTimer()
        {
            if (WeatherActive || _impulsePhase != ImpulsePhase.Idle || _randomMsgActive) return;

            _randomTimer -= CHECK_INTERVAL;
            if (_randomTimer > 0) return;

            // Timer abgelaufen → neu starten für nächste Runde
            _randomTimer = _triggerInterval;

            // Würfeln: _triggerChance% Wahrscheinlichkeit
            if (_rng.Next(0, 100) >= _triggerChance) return;

            // Spieler-Check: nur triggern wenn mindestens ein Spieler online ist
            var players = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(players, p => !p.IsBot);
            if (players.Count == 0) return;

            // Trigger auslösen — Aufladung startet erst nach allen Nachrichten
            _isRandomTrigger = true;
            ShowAlienMessage(true);
            // StartChargeImpulse() wird von UpdateRandomMessages() aufgerufen
        }

        // ──────────────────────────────────────────────────────────
        //  DISTANZ
        // ──────────────────────────────────────────────────────────

        private float GetClosestPlayerDistance()
        {
            float closest = float.MaxValue;
            try
            {
                Vector3D pos = _block.PositionComp.WorldAABB.Center;
                var players = new List<IMyPlayer>();
                MyAPIGateway.Players.GetPlayers(players);
                foreach (var p in players)
                {
                    if (p?.Character == null) continue;
                    float d = (float)Vector3D.Distance(pos, p.Character.GetPosition());
                    if (d < closest) closest = d;
                }
            }
            catch { }
            return closest;
        }

        // ──────────────────────────────────────────────────────────
        //  ALIEN-NACHRICHTEN
        // ──────────────────────────────────────────────────────────

        private void ShowAlienMessage(bool isRandom)
        {
            try
            {
                if (!isRandom)
                {
                    // Spieler-Trigger: sofort eine Nachricht, dann Aufladung
                    MyAPIGateway.Utilities.ShowNotification(MSG_PLAYER, 6000, MyFontEnum.Red);
                }
                else
                {
                    // Random-Trigger: gestaffelte Nachrichten starten
                    _randomMsgIndex  = 0;
                    _randomMsgTimer  = 0;
                    _randomMsgActive = true;
                    // Erste Nachricht sofort senden
                    MyAPIGateway.Utilities.ShowNotification(
                        MSG_RANDOM_SEQUENCE[0], 2500, MyFontEnum.Red);
                    _randomMsgIndex = 1;
                }
            }
            catch { }
        }

        // Wird in UpdateBeforeSimulation aufgerufen
        private void UpdateRandomMessages()
        {
            if (!_randomMsgActive) return;

            _randomMsgTimer++;
            if (_randomMsgTimer < RANDOM_MSG_INTERVAL) return;
            _randomMsgTimer = 0;

            if (_randomMsgIndex < MSG_RANDOM_SEQUENCE.Length)
            {
                try
                {
                    MyAPIGateway.Utilities.ShowNotification(
                        MSG_RANDOM_SEQUENCE[_randomMsgIndex], 2500, MyFontEnum.Red);
                }
                catch { }
                _randomMsgIndex++;
            }
            else
            {
                // Alle Nachrichten gesendet → Aufladung starten
                _randomMsgActive = false;
                StartChargeImpulse();
            }
        }

        // ──────────────────────────────────────────────────────────
        //  SCHOCKWELLE (am Ende der Auflade-Sequenz)
        // ──────────────────────────────────────────────────────────

        private void FireShockwave()
        {
            try
            {
                _hitCount++;
                Vector3D origin = _block.PositionComp.WorldAABB.Center;

                // Batterie-Drain auf alle Grids in Reichweite
                DrainNearbyBatteries(origin);

                // ArtefactStorm Wetter triggern
                try
                {
                    MyAPIGateway.Session.WeatherEffects.SetWeather(
                        WEATHER_ARTEFACT,
                        0f,
                        _block.PositionComp.WorldAABB.Center,
                        false,
                        Vector3D.Zero,
                        _weatherDuration,
                        1f);
                }
                catch { }

                // Wetter-Timer setzen (intern für Aftershock/DOT)
                _weatherTimer = _weatherDuration * 60;
                _cooldown     = COOLDOWN_TICKS;

                // Explosion-Sound
                MyVisualScriptLogicProvider.PlaySingleSoundAtPosition(
                    "WepSmallWarheadExpl", origin);
            }
            catch { }
        }

        // ──────────────────────────────────────────────────────────
        //  BATTERIE-DRAIN
        // ──────────────────────────────────────────────────────────

        private void DrainNearbyBatteries(Vector3D origin)
        {
            try
            {
                // Drain steigt mit jedem Impuls: 45%, 50%, 55%... (+5% pro Impuls)
                float drainBase = Math.Min(
                    _batteryDrainBase + (_hitCount - 1) * _batteryDrainStep,
                    0.95f);

                var entities = new HashSet<IMyEntity>();
                MyAPIGateway.Entities.GetEntities(entities);

                foreach (var entity in entities)
                {
                    var grid = entity as IMyCubeGrid;
                    if (grid == null || grid.Closed) continue;

                    float gridDist = (float)Vector3D.Distance(
                        origin, grid.PositionComp.WorldAABB.Center);
                    if (gridDist > _batteryDrainRange) continue;

                    // Distanz-Abfall: -0.5% pro 10m Entfernung
                    float distancePenalty = (gridDist / 10f) * 0.005f;
                    float drainPct = Math.Max(0.01f, drainBase - distancePenalty);
                    drainPct = Math.Min(drainPct, 0.95f);

                    var gts = MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(grid);
                    if (gts == null) continue;

                    var batteries = new List<Sandbox.ModAPI.IMyBatteryBlock>();
                    gts.GetBlocksOfType(batteries, b => b.IsFunctional && b.Enabled);

                    foreach (var bat in batteries)
                    {
                        var internalBat = bat as MyBatteryBlock;
                        if (internalBat == null) continue;

                        float drain = bat.MaxStoredPower * drainPct;
                        float newPower = Math.Max(0f, bat.CurrentStoredPower - drain);

                        internalBat.SourceComp.SetRemainingCapacityByType(
                            MyResourceDistributorComponent.ElectricityId, newPower);
                    }
                }
            }
            catch { }
        }

        // Aftershock-Impuls: Batterie-Drain aber kein neues Wetter
        private void FireAftershockImpulse()
        {
            try
            {
                _hitCount++;
                Vector3D origin = _block.PositionComp.WorldAABB.Center;
                DrainNearbyBatteries(origin);
                MyVisualScriptLogicProvider.PlaySingleSoundAtPosition(
                    "WepSmallWarheadExpl", origin);
            }
            catch { }
        }

        // ──────────────────────────────────────────────────────────
        //  DELAYED DAMAGE
        // ──────────────────────────────────────────────────────────

        private void ProcessDelayedDamages()
        {
            for (int i = _delayedDamages.Count - 1; i >= 0; i--)
            {
                _delayedDamages[i].TicksLeft--;
                if (_delayedDamages[i].TicksLeft > 0) continue;
                try
                {
                    var dd = _delayedDamages[i];
                    if (dd.Target != null && !dd.Target.IsDead)
                        dd.Target.DoDamage(dd.Damage,
                            MyStringHash.GetOrCompute("Energy"), true);
                }
                catch { }
                _delayedDamages.RemoveAt(i);
            }
        }

        // ──────────────────────────────────────────────────────────
        //  IMPULSE-SEQUENZ
        //  Center(0)→Inner(1)→Middle(2)→Outer(3)→Explosion(4)
        //  Charging:   Basis=GRÜN,   Welle=ROT → danach Aftershock
        //  Aftershock: Basis=ORANGE, Welle=ROT → wiederholt bis Wetter endet
        // ──────────────────────────────────────────────────────────

        private void StartChargeImpulse()
        {
            _impulsePhase  = ImpulsePhase.Charging;
            _impulseStep   = 0;
            _impulsePause  = false;
            _stepDuration  = CHARGE_STEP_TICKS;
            _pauseDuration = 0;
            _impulseTimer  = _stepDuration;
            PlayChargeSound();
            SetRotationTarget();
        }

        private void StartAftershock()
        {
            _impulsePhase  = ImpulsePhase.Aftershock;
            _impulseStep   = 0;
            _impulsePause  = false;
            _stepDuration  = AFTERSHOCK_STEP_TICKS;
            _pauseDuration = AFTERSHOCK_PAUSE_TICKS;
            _impulseTimer  = _stepDuration;
            SetAllEmissive(COLOR_GREEN, _glowIntensity);
            PlayChargeSound();
            SetRotationTarget();
        }

        private void StopImpulse()
        {
            _impulsePhase = ImpulsePhase.Idle;
            StopChargeSound();
            SetAllEmissive(COLOR_GREEN, _glowIntensity);
            SetRotationTarget();
        }

        private void UpdateImpulse()
        {
            if (_impulsePhase == ImpulsePhase.Idle) return;

            // Basis immer grün — kein Orange mehr
            Color baseColor = COLOR_GREEN;

            if (_impulsePause)
            {
                SetAllEmissive(baseColor, _glowIntensity);
                _impulseTimer--;
                if (_impulseTimer <= 0)
                {
                    _impulsePause = false;
                    _impulseStep  = 0;
                    _impulseTimer = _stepDuration;
                    PlayChargeSound();
                }
                return;
            }

            Color centerColor = baseColor;
            Color innerColor  = baseColor;
            Color middleColor = baseColor;
            Color outerColor  = baseColor;

            switch (_impulseStep)
            {
                case 0: centerColor = COLOR_RED; break;
                case 1: innerColor  = COLOR_RED; break;
                case 2: middleColor = COLOR_RED; break;
                case 3: outerColor  = COLOR_RED; break;
                case 4:
                    if (_impulseTimer == _stepDuration)
                    {
                        StopChargeSound();
                        // Batterie-Drain bei JEDEM Impuls
                        // ArtefactStorm nur beim ersten (Charging-Phase)
                        if (_impulsePhase == ImpulsePhase.Charging)
                            FireShockwave();
                        else
                            FireAftershockImpulse();
                    }
                    break;
            }

            SetEmissive("WhiteDwarf",          centerColor, _glowIntensity);
            SetEmissiveSubpart("InnerRing",          innerColor,  _glowIntensity);
            SetEmissiveSubpart("MiddleRing",          middleColor, _glowIntensity);
            SetEmissiveSubpart("OuterRing_section_1", outerColor,  _glowIntensity);

            _impulseTimer--;
            if (_impulseTimer > 0) return;

            _impulseStep++;
            if (_impulseStep < 5)
            {
                _impulseTimer = _stepDuration;
                return;
            }

            if (_impulsePhase == ImpulsePhase.Charging)
            {
                StartAftershock();
            }
            else if (WeatherActive)
            {
                _impulsePause = true;
                _impulseTimer = _pauseDuration;
            }
            else
            {
                StopImpulse();
            }
        }

        // ──────────────────────────────────────────────────────────
        //  SOUND
        // ──────────────────────────────────────────────────────────

        private void PlayChargeSound()
        {
            try
            {
                if (_chargeSoundActive) return;
                _chargeEmitter.PlaySingleSound(
                    new MySoundPair("ShipJumpDriveCharging"), stopPrevious: true);
                _chargeSoundActive = true;
            }
            catch { }
        }

        private void StopChargeSound()
        {
            try
            {
                if (!_chargeSoundActive) return;
                _chargeEmitter.StopSound(forced: true);
                _chargeSoundActive = false;
            }
            catch { }
        }

        // ──────────────────────────────────────────────────────────
        //  EMISSIVE HELPER
        // ──────────────────────────────────────────────────────────

        private void SetAllEmissive(Color color, float intensity)
        {
            SetEmissive("WhiteDwarf", color, intensity);
            foreach (var name in SUBPART_NAMES)
                SetEmissiveSubpart(name, color, intensity);
        }

        private void SetEmissive(string material, Color color, float intensity)
        {
            try { ((MyEntity)Entity).SetEmissiveParts(material, color, intensity); }
            catch { }
        }

        private void SetEmissiveSubpart(string name, Color color, float intensity)
        {
            try
            {
                MyEntitySubpart sub;
                if (Entity.TryGetSubpart(name, out sub) && sub != null)
                    sub.SetEmissiveParts("Emissive", color, intensity);
            }
            catch { }
        }

        // ──────────────────────────────────────────────────────────
        //  ROTATIONS-HELPER
        // ──────────────────────────────────────────────────────────

        private void RotateSubpart(string name, Vector3 axis, float speed)
        {
            try
            {
                MyEntitySubpart sub;
                if (!Entity.TryGetSubpart(name, out sub) || sub == null) return;

                SubpartInfo info;
                if (!_subparts.TryGetValue(name, out info))
                    info = new SubpartInfo { LocalMatrix = Matrix.Identity, Init = false };

                if (!info.Init)
                {
                    info.LocalMatrix = sub.PositionComp.LocalMatrix;
                    info.Init = true;
                }

                float rad = MathHelper.ToRadians(speed);
                info.LocalMatrix = Matrix.Normalize(
                    Matrix.CreateFromAxisAngle(axis, rad) * info.LocalMatrix);
                sub.PositionComp.LocalMatrix = info.LocalMatrix * _globalMatrix;
                _subparts[name] = info;
            }
            catch { }
        }

        // ──────────────────────────────────────────────────────────
        //  DAMAGE HANDLER (Artefakt unzerstörbar wenn aktiv)
        // ──────────────────────────────────────────────────────────

        private void BeforeDamageHandler(object target, ref MyDamageInformation info)
        {
            if (_active && target == Entity)
                info.Amount = 0f;
        }

        // ──────────────────────────────────────────────────────────
        //  CHAT-BEFEHLE (Admin-only)
        // ──────────────────────────────────────────────────────────

        private void OnMessageEntered(string msg, ref bool sendToOthers)
        {
            if (string.IsNullOrWhiteSpace(msg)) return;
            string m = msg.Trim().ToLowerInvariant();
            if (!m.StartsWith("!spaceartefact")) return;

            sendToOthers = false;

            if (!IsAdmin())
            {
                MyAPIGateway.Utilities.ShowNotification(
                    "[Artefakt] Nur Admins.", 3000, MyFontEnum.Red);
                return;
            }

            // Command nach Prefix extrahieren und parsen
            string remainder = m.Substring("!spaceartefact".Length).Trim();
            string[] parts = remainder.Split(new char[]{' '}, System.StringSplitOptions.RemoveEmptyEntries);

            // ID und Command ermitteln
            // Format: !spaceartefact [id|all] <cmd>
            int targetId = 0;       // Default ID=0
            string cmd = "";
            bool hasId = false;
            bool allTargeted = false;

            if (parts.Length == 1)
            {
                // !spaceartefact on  → ID=0, cmd=on
                cmd = parts[0];
            }
            else if (parts.Length == 2)
            {
                if (parts[0] == "all")
                {
                    // !spaceartefact all reset  → alle Artefakte
                    allTargeted = true;
                    cmd = parts[1];
                }
                else
                {
                    int parsedId;
                    if (int.TryParse(parts[0], out parsedId))
                    {
                        // !spaceartefact 1 on  → ID=1, cmd=on
                        targetId = parsedId;
                        cmd      = parts[1];
                        hasId    = true;
                    }
                    else
                    {
                        cmd = parts[0];
                    }
                }
            }

            // Prüfen ob dieses Artefakt angesprochen wird
            if (!allTargeted)
            {
                if (hasId && _artefactId != targetId) return;
                if (!hasId && _artefactId != 0) return;
            }

            switch (cmd)
            {
                case "on":
                    _active = true;
                    SaveStatus(true);
                    SetAllEmissive(COLOR_GREEN, _glowIntensity);
                    SetRotationTarget();
                    MyAPIGateway.Utilities.ShowNotification(
                        $"[Artefakt {_artefactId}] Aktiviert", 2000, MyFontEnum.Green);
                    break;

                case "off":
                    _active = false;
                    SaveStatus(false);
                    StopImpulse();
                    _delayedDamages.Clear();
                    SetAllEmissive(Color.Black, 0f);
                    MyAPIGateway.Utilities.ShowNotification(
                        $"[Artefakt {_artefactId}] Deaktiviert", 2000, MyFontEnum.Red);
                    break;

                case "reset":
                    _shockwaveFired    = false;
                    _weatherTimer      = 0;
                    _cooldown          = 0;
                    _hitCount          = 0;
                    _playerInRange     = false;
                    _rotSpeedTarget    = ROT_IDLE_RING;
                    _globalSpeedTarget = ROT_IDLE_GLOB;
                    _randomTimer       = _triggerInterval;
                    _randomMsgActive   = false;
                    _randomMsgIndex    = 0;
                    _randomMsgTimer    = 0;
                    StopImpulse();
                    _delayedDamages.Clear();
                    LoadConfig();
                    try
                    {
                        MyAPIGateway.Session.WeatherEffects.RemoveWeather(
                            _block.PositionComp.WorldAABB.Center);
                    }
                    catch { }
                    MyAPIGateway.Utilities.ShowNotification(
                        $"[Artefakt {_artefactId}] Reset", 2000, MyFontEnum.Blue);
                    break;

                case "trigger":
                    if (!_active)
                    {
                        MyAPIGateway.Utilities.ShowNotification(
                            $"[Artefakt {_artefactId}] Erst aktivieren.", 2000, MyFontEnum.Red);
                        break;
                    }
                    if (_impulsePhase != ImpulsePhase.Idle)
                    {
                        MyAPIGateway.Utilities.ShowNotification(
                            $"[Artefakt {_artefactId}] Läuft bereits.", 2000, MyFontEnum.Red);
                        break;
                    }
                    _isRandomTrigger = false;
                    ShowAlienMessage(false);
                    StartChargeImpulse();
                    MyAPIGateway.Utilities.ShowNotification(
                        $"[Artefakt {_artefactId}] Manuell getriggert", 2000, MyFontEnum.Green);
                    break;

                default:
                    MyAPIGateway.Utilities.ShowNotification(
                        "[Artefakt] Befehle: on | off | reset | trigger | all reset", 3000, MyFontEnum.White);
                    break;
            }
        }

        private bool IsAdmin()
        {
            var p = MyAPIGateway.Session?.Player;
            if (p == null) return false;

            // Singleplayer: immer erlaubt
            if (MyAPIGateway.Session.OnlineMode == MyOnlineModeEnum.OFFLINE)
                return true;

            // Multiplayer / Dedicated Server: Admin-Check
            return p.PromoteLevel >= MyPromoteLevel.Admin;
        }

        // ──────────────────────────────────────────────────────────
        //  CLEANUP
        // ──────────────────────────────────────────────────────────

        public override void Close()
        {
            if (_msgHandlerRegistered)
            {
                MyAPIGateway.Utilities.MessageEntered -= OnMessageEntered;
                _msgHandlerRegistered = false;
            }
            try { _chargeEmitter?.StopSound(true); _chargeEmitter?.Cleanup(); } catch { }
            base.Close();
        }
    }
}