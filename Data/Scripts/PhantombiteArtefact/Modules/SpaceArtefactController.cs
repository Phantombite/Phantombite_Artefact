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

namespace PhantombiteArtefact.Modules
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_JumpDrive), false,
        "SpaceArtefact", "SpaceArtefactT2", "SpaceArtefactT3")]
    public class ArtefactController : MyGameLogicComponent
    {
        private const string MODULE = "Artefact_Controller";

        // ──────────────────────────────────────────────────────────
        //  INSTANZ-REGISTRY (für Session-Zugriff auf dem Server)
        // ──────────────────────────────────────────────────────────
        internal static readonly Dictionary<long, ArtefactController> Instances =
            new Dictionary<long, ArtefactController>();

        // ──────────────────────────────────────────────────────────
        //  STATE-SYNC (Server → Client)
        //  Format: "entityId|cmd"
        //  cmd: "on" / "off" / "reset" / "storm_start" / "storm_end"
        // ──────────────────────────────────────────────────────────
        private const ushort STATE_SYNC_PACKET = 5999;

        // ──────────────────────────────────────────────────────────
        //  KONFIGURATION
        // ──────────────────────────────────────────────────────────

        private const string DEFAULT_CONFIG =
            "[SpaceArtefact]\n" +
            "ID=0                    ; Artefakt-ID (0 = Standard)\n" +
            "Status=off              ; on = aktiv, off = deaktiviert\n" +
            "TriggerRange=50         ; Meter - Spieler loest Aufladung aus\n" +
            "DotDamage=20            ; HP pro Sekunde bei sehr nahem Aufenthalt\n" +
            "DotRange=5              ; Meter - Radius fuer DOT Schaden\n" +
            "BatteryDrainBase=0.45   ; Basis-Drain beim ersten Impuls (0.45 = 45%)\n" +
            "BatteryDrainStep=0.05   ; Anstieg pro Impuls (0.05 = +5% pro Impuls)\n" +
            "BatteryDrainRange=300   ; Meter - Reichweite des Batterie-Drains (max 300m)\n" +
            "WeatherDuration=300     ; Sekunden - Dauer des globalen ArtefactStorm Wetters\n" +
            "TriggerInterval=216000  ; Ticks - Intervall zwischen Random-Trigger Wuerfen (216000 = 1 Stunde)\n" +
            "TriggerChance=20        ; Prozent - Wahrscheinlichkeit beim Wuerfeln (20 = 20%)\n";

        private const string WEATHER_ARTEFACT = "ArtefactStorm";

        private int   _artefactId        = 0;
        private float _rangeTrigger      = 50f;
        private float _rangeDot          = 5f;
        private float _dotDamagePerTick  = 10f;
        private float _batteryDrainBase  = 0.45f;
        private float _batteryDrainStep  = 0.05f;
        private float _batteryDrainRange = 300f;
        private int   _weatherDuration   = 300;
        private int   _triggerInterval   = 216000;
        private int   _triggerChance     = 20;

        private int COOLDOWN_TICKS => _weatherDuration * 60;
        private const int CHECK_INTERVAL         = 30;  // Basis: alle 30 Frames (PerfLevel 0)
        // PerfLevel 1: 60 Frames | PerfLevel 2: 150 Frames | PerfLevel 3: 300 Frames
        private const int CHARGE_STEP_TICKS      = 60;
        private const int AFTERSHOCK_STEP_TICKS  = 40;
        private const int AFTERSHOCK_PAUSE_TICKS = 360;

        private const float ROT_IDLE_RING   = 1.0f; private const float ROT_IDLE_GLOB   = 0.5f;
        private const float ROT_CHARGE_RING = 3.0f; private const float ROT_CHARGE_GLOB = 2.0f;
        private const float ROT_STORM_RING  = 4.0f; private const float ROT_STORM_GLOB  = 2.5f;
        private const float ROT_MAX_RING    = 6.0f; private const float ROT_MAX_GLOB    = 4.0f;

        private const float GLOW_MIN         = 1.5f;
        private const float GLOW_MAX         = 3.5f;
        private const float GLOW_PULSE_IDLE  = 0.02f;
        private const float GLOW_PULSE_STORM = 0.05f;

        private static readonly Color COLOR_GREEN  = new Color(0, 255, 0)   * 3.0f;
        private static readonly Color COLOR_ORANGE = new Color(255, 140, 0) * 3.0f;
        private static readonly Color COLOR_RED    = new Color(255, 0, 0)   * 3.0f;

        private const string MSG_PLAYER = "Kre'shah... voth'nal... zim'kora eth'win... nor'tal bin'kess...";
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

        private static readonly string[] SUBPART_NAMES = { "OuterRing_section_1", "MiddleRing", "InnerRing" };
        private static readonly Vector3 AXIS_OUTER  = Vector3.Forward;
        private static readonly Vector3 AXIS_MIDDLE = Vector3.Right;
        private static readonly Vector3 AXIS_INNER  = Vector3.Down;
        private static readonly Vector3 AXIS_GLOBAL = Vector3.Up;

        // ──────────────────────────────────────────────────────────
        //  LAUFZEIT-VARIABLEN
        // ──────────────────────────────────────────────────────────

        private IMyFunctionalBlock _block;
        private bool _active           = false;
        private bool _fullyInitialized = false;
        private bool _dmgHandlerRegistered;
        private Random _rng = new Random();

        // Logger von Artefact_Command
        private ArtefactCommandModule _logger;

        // Timer
        private int _frameTick    = 0;
        private int _weatherTimer = 0;
        private int _cooldown     = 0;
        private int _randomTimer  = 0;

        // Spieler
        private bool _playerInRange  = false;
        private bool _shockwaveFired = false;
        private int  _hitCount       = 0;

        // Rotation
        private float  _rotSpeed          = ROT_IDLE_RING;
        private float  _rotSpeedTarget    = ROT_IDLE_RING;
        private float  _globalSpeed       = ROT_IDLE_GLOB;
        private float  _globalSpeedTarget = ROT_IDLE_GLOB;
        private Matrix _globalMatrix      = Matrix.Identity;

        // Glow
        private float _glowIntensity  = 2.0f;
        private bool  _glowReverse    = false;
        private float _glowPulseSpeed = GLOW_PULSE_IDLE;

        // Trace: letzte Rotation für Änderungs-Erkennung
        private float  _lastRotTarget = -1f;
        private string _lastRotGrund  = "";

        // Subpart-Cache
        private struct SubpartInfo { public Matrix LocalMatrix; public bool Init; }
        private readonly Dictionary<string, SubpartInfo> _subparts = new Dictionary<string, SubpartInfo>();

        // Impulse
        private enum ImpulsePhase { Idle, Charging, Aftershock }
        private ImpulsePhase _impulsePhase = ImpulsePhase.Idle;
        private int  _impulseStep   = 0;
        private int  _impulseTimer  = 0;
        private bool _impulsePause  = false;
        private int  _stepDuration;
        private int  _pauseDuration;
        private bool _isRandomTrigger = false;

        // Random-Nachrichten
        private int  _randomMsgIndex  = 0;
        private int  _randomMsgTimer  = 0;
        private bool _randomMsgActive = false;
        private const int RANDOM_MSG_INTERVAL = 120;

        // Delayed Damage
        private class DelayedDamage { public IMyCharacter Target; public float Damage; public int TicksLeft; }
        private readonly List<DelayedDamage> _delayedDamages = new List<DelayedDamage>();

        // Sound
        private MyEntity3DSoundEmitter _chargeEmitter;
        private bool _chargeSoundActive;

        // ──────────────────────────────────────────────────────────
        //  LOGGER
        // ──────────────────────────────────────────────────────────

        public void SetLogger(ArtefactCommandModule logger) { _logger = logger; }

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

            // Hinweis: Chat-Pakete werden von ArtefactChatModule (Session-Ebene) empfangen.
            // State-Sync für non-msg Befehle (on/off/reset etc.) bleibt hier auf Block-Ebene.
            if (!MyAPIGateway.Multiplayer.IsServer)
                MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(STATE_SYNC_PACKET, OnStateSyncReceived);

            // Auf dem Server in Registry eintragen (für ArtefactRandomTimerModule)
            if (MyAPIGateway.Multiplayer.IsServer)
                Instances[Entity.EntityId] = this;

            NeedsUpdate = MyEntityUpdateEnum.EACH_FRAME;
        }

        // ──────────────────────────────────────────────────────────
        //  CONFIG LADEN / SPEICHERN
        // ──────────────────────────────────────────────────────────

        private void LoadConfig()
        {
            try
            {
                string data = _block.CustomData;
                if (string.IsNullOrWhiteSpace(data) || !data.Contains("[SpaceArtefact]"))
                {
                    _block.CustomData = DEFAULT_CONFIG;
                    data = DEFAULT_CONFIG;
                    _logger?.Log(MODULE, "Custom Data leer — Default Config geschrieben", 2);
                }

                foreach (var line in data.Split('\n'))
                {
                    string trimmed = line.Trim();
                    if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith(";")) continue;
                    int eq = trimmed.IndexOf('=');
                    if (eq < 0) continue;
                    string key = trimmed.Substring(0, eq).Trim().ToLower();
                    string val = trimmed.Substring(eq + 1).Trim();
                    int sc = val.IndexOf(';');
                    if (sc >= 0) val = val.Substring(0, sc).Trim();

                    switch (key)
                    {
                        case "id":               int.TryParse(val, out _artefactId);                                                                                                     break;
                        case "status":           _active = val.ToLower() == "on";                                                                                                        break;
                        case "triggerrange":     float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _rangeTrigger);     break;
                        case "dotdamage":        float dotDmg; if (float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out dotDmg)) _dotDamagePerTick = dotDmg / 2f; break;
                        case "dotrange":         float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _rangeDot);         break;
                        case "batterydrainbase": float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _batteryDrainBase);  break;
                        case "batterydrainstep": float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _batteryDrainStep);  break;
                        case "batterydrainrange":float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _batteryDrainRange); break;
                        case "weatherduration":  int.TryParse(val, out _weatherDuration);                                                                                                break;
                        case "triggerinterval":  int.TryParse(val, out _triggerInterval);                                                                                                break;
                        case "triggerchance":    int.TryParse(val, out _triggerChance);                                                                                                  break;
                    }
                }

                _logger?.Log(MODULE, "Config geladen — " +
                    "ID=" + _artefactId +
                    " Status=" + (_active ? "on" : "off") +
                    " TriggerRange=" + _rangeTrigger + "m" +
                    " DotDamage=" + (_dotDamagePerTick * 2f) + "HP/s" +
                    " DotRange=" + _rangeDot + "m" +
                    " DrainBase=" + (_batteryDrainBase * 100f).ToString("F0") + "%" +
                    " DrainStep=" + (_batteryDrainStep * 100f).ToString("F0") + "%" +
                    " DrainRange=" + _batteryDrainRange + "m" +
                    " WeatherDuration=" + _weatherDuration + "s" +
                    " TriggerInterval=" + _triggerInterval + " Ticks" +
                    " TriggerChance=" + _triggerChance + "%", 1);
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
                    if (trimmed.ToLower().StartsWith("status=") || trimmed.ToLower().StartsWith("status ="))
                        result.AppendLine("Status=" + (active ? "on" : "off") + "              ; on = aktiv, off = deaktiviert");
                    else
                        result.AppendLine(line);
                }
                _block.CustomData = result.ToString().TrimEnd();
                _logger?.Log(MODULE, "Status gespeichert: " + (active ? "on" : "off"), 2);
            }
            catch { }
        }

        // ──────────────────────────────────────────────────────────
        //  HAUPT-UPDATE
        // ──────────────────────────────────────────────────────────

        public override void UpdateBeforeSimulation()
        {
            if (!_fullyInitialized)
            {
                _fullyInitialized = true;

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

                if (string.IsNullOrWhiteSpace(_block.CustomData))
                    _block.CustomData = DEFAULT_CONFIG;

                LoadConfig();

                if (_active)
                {
                    SetAllEmissive(COLOR_GREEN, _glowIntensity);
                    SetRotationTarget("Init — aktiv");
                    _logger?.Log(MODULE, "Start: IDLE — Aktiv", 1);
                }
                else
                {
                    _logger?.Log(MODULE, "Start: INAKTIV", 1);
                }

                _randomTimer = _triggerInterval;
                _logger?.Log(MODULE, "Random-Timer init: " + (_randomTimer / 60 / 60) + "h", 1);
                return;
            }

            if (_block == null || _block.Closed) return;

            ProcessDelayedDamages();

            if (!_active)
            {
                SetAllEmissive(Color.Black, 0f);
                // Sicherstellen dass Rotation wirklich 0 ist
                _rotSpeed    = 0f;
                _globalSpeed = 0f;
                return;
            }

            _frameTick++;

            if (_weatherTimer > 0)
            {
                _weatherTimer--;
                if (_weatherTimer % 600 == 0 && _weatherTimer > 0)
                    _logger?.Log(MODULE, "Wetter läuft: " + (_weatherTimer / 60) + "s — Cooldown: " + (_cooldown / 60) + "s", 2);
            }
            if (_cooldown > 0) _cooldown--;

            _rotSpeed    = MathHelper.Lerp(_rotSpeed,    _rotSpeedTarget,    0.08f);
            _globalSpeed = MathHelper.Lerp(_globalSpeed, _globalSpeedTarget, 0.08f);

            float glowTarget = WeatherActive ? GLOW_PULSE_STORM : GLOW_PULSE_IDLE;
            _glowPulseSpeed  = MathHelper.Lerp(_glowPulseSpeed, glowTarget, 0.05f);

            // TRACE: Glow-Puls Geschwindigkeit geändert (nur wenn Wechsel)
            if (Math.Abs(glowTarget - _lastGlowTarget) > 0.001f)
            {
                _logger?.Log(MODULE, "Glow: " + _lastGlowTarget.ToString("F3") + " → " + glowTarget.ToString("F3"), 2);
                _lastGlowTarget = glowTarget;
            }

            UpdateRotationAndGlow();
            UpdateImpulse();
            UpdateRandomMessages();

            // PerfLevel bestimmt wie oft Spieler-Check + DOT läuft
            int perfLevel = _logger?.PerfLevel ?? 0;
            int checkInterval = perfLevel >= 1 ? CHECK_INTERVAL * 2 : CHECK_INTERVAL;

            if (_frameTick % checkInterval == 0)
            {
                UpdatePlayerCheck();
                if (WeatherActive) ApplyDot();
            }

            if (!WeatherActive && _impulsePhase == ImpulsePhase.Aftershock)
            {
                _logger?.Log(MODULE, "Wetter beendet — Zustand: IDLE", 1);
                StopImpulse();
                _shockwaveFired = false;
            }
        }

        private float _lastGlowTarget = GLOW_PULSE_IDLE;
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

        private void SetRotationTarget(string grund)
        {
            float newRing, newGlob;

            if (_impulsePhase == ImpulsePhase.Charging)
            { newRing = ROT_CHARGE_RING; newGlob = ROT_CHARGE_GLOB; }
            else if (WeatherActive && _playerInRange)
            { newRing = ROT_MAX_RING; newGlob = ROT_MAX_GLOB; }
            else if (WeatherActive)
            { newRing = ROT_STORM_RING; newGlob = ROT_STORM_GLOB; }
            else
            { newRing = ROT_IDLE_RING; newGlob = ROT_IDLE_GLOB; }

            // TRACE: nur bei Änderung
            if (Math.Abs(newRing - _lastRotTarget) > 0.01f || grund != _lastRotGrund)
            {
                _logger?.Log(MODULE, "Rotation: Ring=" + newRing + " Glob=" + newGlob, 2);
                _lastRotTarget = newRing;
                _lastRotGrund  = grund;
            }

            _rotSpeedTarget    = newRing;
            _globalSpeedTarget = newGlob;
        }

        // ──────────────────────────────────────────────────────────
        //  DOT
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
                    if (p.Character.Parent != null) continue;

                    float dist = (float)Vector3D.Distance(pos, p.Character.GetPosition());
                    if (dist > _rangeDot) continue;

                    p.Character.DoDamage(_dotDamagePerTick, MyStringHash.GetOrCompute("Energy"), true);
                    _logger?.Log(MODULE, "DOT: " + p.DisplayName + " — " + dist.ToString("F1") + "m — " + (_dotDamagePerTick * 2f) + " HP/s", 1);
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

            _logger?.Log(MODULE, "Spieler-Check: " +
                (closest == float.MaxValue ? "kein Spieler" : closest.ToString("F1") + "m") +
                " | inRange=" + inRange +
                " | ShockwaveFired=" + _shockwaveFired +
                " | Cooldown=" + (_cooldown / 60) + "s" +
                " | Wetter=" + WeatherActive +
                " | Phase=" + _impulsePhase, 1);

            if (!inRange && _playerInRange)
            {
                _shockwaveFired = false;
                _logger?.Log(MODULE, "Spieler hat Reichweite verlassen — ShockwaveFired=false", 1);
            }

            _playerInRange = inRange;

            if (_playerInRange && !_shockwaveFired && _cooldown == 0 && !WeatherActive
                && _impulsePhase == ImpulsePhase.Idle)
            {
                if (MyAPIGateway.Multiplayer.IsServer)
                {
                    _shockwaveFired  = true;
                    _isRandomTrigger = false;
                    _logger?.Log(MODULE, "SPIELER-TRIGGER — Distanz: " + closest.ToString("F1") + "m — Aufladung startet", 1);
                    ShowAlienMessage(false);
                    StartChargeImpulse();
                }
            }
            else if (_playerInRange && _cooldown > 0)
            {
                _logger?.Log(MODULE, "Spieler in Reichweite — Cooldown aktiv: " + (_cooldown / 60) + "s verbleibend — kein Trigger", 1);
            }

            SetRotationTarget("Spieler-Check");
        }

        // ──────────────────────────────────────────────────────────
        //  RANDOM-TRIGGER
        // ──────────────────────────────────────────────────────────

        private void UpdateRandomTimer()
        {
            // PerfLevel 2+: Random-Timer pausiert — kein automatisches Event
            if (_logger != null && _logger.PerfLevel >= 2) return;

            if (WeatherActive || _impulsePhase != ImpulsePhase.Idle || _randomMsgActive) return;

            _randomTimer -= CHECK_INTERVAL;

            if (_frameTick % 600 == 0)
                _logger?.Log(MODULE, "Random-Timer: " + _randomTimer + " Ticks (" + (_randomTimer / 60 / 60) + "h " + (_randomTimer / 60 % 60) + "m verbleibend)", 1);

            if (_randomTimer > 0) return;

            _randomTimer = _triggerInterval;

            int  roll      = _rng.Next(0, 100);
            bool triggered = roll < _triggerChance;

            _logger?.Log(MODULE, "Random-Wuerfel: " + roll + " (Chance=" + _triggerChance + "%) — " +
                (triggered ? "GETRIGGERT" : "nicht getriggert") +
                " — Timer neu: " + _triggerInterval + " Ticks (" + (_triggerInterval / 60 / 60) + "h)", 1);

            if (!triggered) return;

            if (!MyAPIGateway.Multiplayer.IsServer) return;

            var players = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(players, p => !p.IsBot);

            if (players.Count == 0)
            {
                _logger?.Log(MODULE, "Random-Trigger abgebrochen — keine Spieler online", 1);
                return;
            }

            _logger?.Log(MODULE, "RANDOM-TRIGGER — " + players.Count + " Spieler online — Nachrichtensequenz startet", 1);
            _isRandomTrigger = true;
            ShowAlienMessage(true);
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
                    BroadcastMessage(MSG_PLAYER);
                    _logger?.Log(MODULE, "Alien-Nachricht gesendet (Spieler-Trigger)", 1);
                }
                else
                {
                    _randomMsgIndex  = 0;
                    _randomMsgTimer  = 0;
                    _randomMsgActive = true;
                    BroadcastMessage(MSG_RANDOM_SEQUENCE[0]);
                    _randomMsgIndex = 1;
                    _logger?.Log(MODULE, "Alien-Nachrichtensequenz gestartet (" + MSG_RANDOM_SEQUENCE.Length + " Nachrichten)", 1);
                }
            }
            catch { }
        }

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
                    BroadcastMessage(MSG_RANDOM_SEQUENCE[_randomMsgIndex]);
                    _logger?.Log(MODULE, "Alien-Nachricht " + _randomMsgIndex + "/" + MSG_RANDOM_SEQUENCE.Length + " gesendet", 1);
                }
                catch { }
                _randomMsgIndex++;
            }
            else
            {
                _randomMsgActive = false;
                _logger?.Log(MODULE, "Alien-Nachrichten abgeschlossen — Aufladung startet", 1);
                StartChargeImpulse();
            }
        }

        // ──────────────────────────────────────────────────────────
        //  SCHOCKWELLE
        // ──────────────────────────────────────────────────────────

        private void FireShockwave()
        {
            try
            {
                _hitCount++;
                Vector3D origin = _block.PositionComp.WorldAABB.Center;
                float drainBase = Math.Min(_batteryDrainBase + (_hitCount - 1) * _batteryDrainStep, 0.95f);

                _logger?.Log(MODULE, "SCHOCKWELLE — Impuls #" + _hitCount +
                    " — Drain: " + (drainBase * 100f).ToString("F1") + "%" +
                    " — Wetter: " + _weatherDuration + "s", 1);

                DrainNearbyBatteries(origin, drainBase);

                try
                {
                    MyAPIGateway.Session.WeatherEffects.SetWeather(
                        WEATHER_ARTEFACT, 0f,
                        _block.PositionComp.WorldAABB.Center,
                        false, Vector3D.Zero, _weatherDuration, 1f);
                    _logger?.Log(MODULE, "WETTER aktiviert: ArtefactStorm fuer " + _weatherDuration + "s", 1);
                }
                catch { }

                _weatherTimer = _weatherDuration * 60;
                _cooldown     = COOLDOWN_TICKS;

                _logger?.Log(MODULE, "WeatherTimer=" + _weatherTimer + " Ticks — Cooldown=" + _cooldown + " Ticks", 1);

                BroadcastState("storm_start");
            }
            catch { }
        }

        // ──────────────────────────────────────────────────────────
        //  BATTERIE-DRAIN
        // ──────────────────────────────────────────────────────────

        private void DrainNearbyBatteries(Vector3D origin, float drainBase)
        {
            try
            {
                var entities = new HashSet<IMyEntity>();
                MyAPIGateway.Entities.GetEntities(entities);

                int gridCount    = 0;
                int batteryCount = 0;

                foreach (var entity in entities)
                {
                    var grid = entity as IMyCubeGrid;
                    if (grid == null || grid.Closed) continue;

                    // Eigenes Grid nicht drainieren
                    if (grid.EntityId == _block.CubeGrid.EntityId) continue;

                    float gridDist = (float)Vector3D.Distance(origin, grid.PositionComp.WorldAABB.Center);
                    if (gridDist > _batteryDrainRange) continue;

                    float distancePenalty = (gridDist / 10f) * 0.005f;
                    float drainPct = Math.Max(0.01f, Math.Min(drainBase - distancePenalty, 0.95f));

                    var gts = MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(grid);
                    if (gts == null) continue;

                    var batteries = new List<Sandbox.ModAPI.IMyBatteryBlock>();
                    gts.GetBlocksOfType(batteries, b => b.IsFunctional && b.Enabled);
                    if (batteries.Count == 0) continue;

                    gridCount++;

                    _logger?.Log(MODULE, "Drain: " + grid.DisplayName +
                        " (" + gridDist.ToString("F0") + "m)" +
                        " — " + (drainPct * 100f).ToString("F1") + "%" +
                        " — " + batteries.Count + " Batterien", 1);

                    foreach (var bat in batteries)
                    {
                        var internalBat = bat as MyBatteryBlock;
                        if (internalBat == null) continue;

                        float drain    = bat.MaxStoredPower * drainPct;
                        float newPower = Math.Max(0f, bat.CurrentStoredPower - drain);

                        internalBat.SourceComp.SetRemainingCapacityByType(
                            MyResourceDistributorComponent.ElectricityId, newPower);

                        batteryCount++;
                    }
                }

                _logger?.Log(MODULE, "Drain abgeschlossen — " + gridCount + " Grids — " + batteryCount + " Batterien — Drain: " + (drainBase * 100f).ToString("F1") + "%", 1);
            }
            catch { }
        }

        private void FireAftershockImpulse()
        {
            try
            {
                _hitCount++;
                Vector3D origin  = _block.PositionComp.WorldAABB.Center;
                float drainBase  = Math.Min(_batteryDrainBase + (_hitCount - 1) * _batteryDrainStep, 0.95f);

                _logger?.Log(MODULE, "NACHBEBEN — Impuls #" + _hitCount + " — Drain: " + (drainBase * 100f).ToString("F1") + "%", 1);

                DrainNearbyBatteries(origin, drainBase);
                BroadcastState("impulse_fire");
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
                        dd.Target.DoDamage(dd.Damage, MyStringHash.GetOrCompute("Energy"), true);
                }
                catch { }
                _delayedDamages.RemoveAt(i);
            }
        }

        // ──────────────────────────────────────────────────────────
        //  IMPULSE-SEQUENZ
        // ──────────────────────────────────────────────────────────

        private static readonly string[] STEP_NAMES = { "Center", "Inner", "Middle", "Outer", "Explosion" };

        private void StartChargeImpulse()
        {
            _impulsePhase  = ImpulsePhase.Charging;
            _impulseStep   = 0;
            _impulsePause  = false;
            _stepDuration  = CHARGE_STEP_TICKS;
            _pauseDuration = 0;
            _impulseTimer  = _stepDuration;
            PlayChargeSound();
            SetRotationTarget("Aufladung");
            BroadcastState("charge_start");

            _logger?.Log(MODULE, "AUFLADUNG gestartet — Zustand: AUFLADEN" +
                " — Rotation: Ring=" + ROT_CHARGE_RING + " Global=" + ROT_CHARGE_GLOB +
                " — Glow: ROT wandert Center->Inner->Middle->Outer", 1);
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
            SetRotationTarget("Nachbeben");

            _logger?.Log(MODULE, "NACHBEBEN gestartet — Zustand: WETTER" +
                " — Rotation: Ring=" + ROT_STORM_RING + " Global=" + ROT_STORM_GLOB +
                " — Pause zwischen Impulsen: " + (AFTERSHOCK_PAUSE_TICKS / 60f).ToString("F1") + "s", 1);
        }

        private void StopImpulse()
        {
            _impulsePhase = ImpulsePhase.Idle;
            StopChargeSound();

            if (_active)
            {
                SetAllEmissive(COLOR_GREEN, _glowIntensity);
                SetRotationTarget("Impuls gestoppt");
                _logger?.Log(MODULE, "IMPULS gestoppt — Zustand: IDLE" +
                    " — Glow: GRUEN — Rotation: Ring=" + ROT_IDLE_RING + " Global=" + ROT_IDLE_GLOB, 1);
            }
            else
            {
                _logger?.Log(MODULE, "IMPULS gestoppt — Artefakt inaktiv — kein Glow/Rotation Reset", 1);
            }
        }

        private void UpdateImpulse()
        {
            if (_impulsePhase == ImpulsePhase.Idle) return;

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
                    _logger?.Log(MODULE, "Nachbeben-Pause beendet — neuer Impuls startet bei Step 0 (Center)", 1);
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
                        // PerfLevel 3: Animation läuft, aber kein Batterie-Drain
                        bool fireDamage = _logger == null || _logger.PerfLevel < 3;
                        if (_impulsePhase == ImpulsePhase.Charging)
                        {
                            if (fireDamage) FireShockwave();
                            else _logger?.Log(MODULE, "Impulse: Batterie-Drain übersprungen (PerfLevel 3)", 1);
                        }
                        else
                        {
                            if (fireDamage) FireAftershockImpulse();
                        }
                    }
                    break;
            }

            SetEmissive("WhiteDwarf",                centerColor, _glowIntensity);
            SetEmissiveSubpart("InnerRing",          innerColor,  _glowIntensity);
            SetEmissiveSubpart("MiddleRing",         middleColor, _glowIntensity);
            SetEmissiveSubpart("OuterRing_section_1",outerColor,  _glowIntensity);

            _impulseTimer--;
            if (_impulseTimer > 0) return;

            string currentStep = _impulseStep < STEP_NAMES.Length ? STEP_NAMES[_impulseStep] : "?";
            _logger?.Log(MODULE, "Impuls Step " + _impulseStep + " (" + currentStep + ") abgeschlossen", 1);

            _impulseStep++;

            if (_impulseStep < 5)
            {
                _impulseTimer = _stepDuration;
                string nextStep = _impulseStep < STEP_NAMES.Length ? STEP_NAMES[_impulseStep] : "?";
                _logger?.Log(MODULE, "Impuls Step " + _impulseStep + " (" + nextStep + ") startet — Glow ROT an " + nextStep, 1);
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
                _logger?.Log(MODULE, "Nachbeben-Pause: " + (AFTERSHOCK_PAUSE_TICKS / 60f).ToString("F1") + "s — Wetter noch: " + (_weatherTimer / 60) + "s", 1);
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
                _chargeEmitter.PlaySingleSound(new MySoundPair("ShipJumpDriveCharging"), stopPrevious: true);
                _chargeSoundActive = true;
                _logger?.Log(MODULE, "LadeSound gestartet", 1);
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
                _logger?.Log(MODULE, "LadeSound gestoppt", 1);
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
                info.LocalMatrix = Matrix.Normalize(Matrix.CreateFromAxisAngle(axis, rad) * info.LocalMatrix);
                sub.PositionComp.LocalMatrix = info.LocalMatrix * _globalMatrix;
                _subparts[name] = info;
            }
            catch { }
        }

        // ──────────────────────────────────────────────────────────
        //  DAMAGE HANDLER
        // ──────────────────────────────────────────────────────────

        private void BeforeDamageHandler(object target, ref MyDamageInformation info)
        {
            if (_active && target == Entity)
            {
                info.Amount = 0f;
                _logger?.Log(MODULE, "Schaden blockiert — Artefakt unzerstoerbar waehrend aktiv", 1);
            }
        }

        // ──────────────────────────────────────────────────────────
        //  PUBLIC COMMAND API
        // ──────────────────────────────────────────────────────────

        public void ExecuteCommand(string cmd, int targetId, bool hasId, bool allTarget)
        {
            if (!allTarget)
            {
                if (hasId && _artefactId != targetId) return;
                if (!hasId && _artefactId != 0) return;
            }

            string ziel = allTarget ? "all" : (hasId ? "ID=" + targetId : "ID=0");
            _logger?.Log(MODULE, "Command: '" + cmd + "' — Ziel: " + ziel, 1);

            switch (cmd)
            {
                case "on":
                    _active            = true;
                    _shockwaveFired    = false;
                    _playerInRange     = false;
                    _randomMsgActive   = false;
                    _randomMsgIndex    = 0;
                    _randomMsgTimer    = 0;
                    _randomTimer       = _triggerInterval;
                    _rotSpeed          = ROT_IDLE_RING;
                    _globalSpeed       = ROT_IDLE_GLOB;
                    _rotSpeedTarget    = ROT_IDLE_RING;
                    _globalSpeedTarget = ROT_IDLE_GLOB;
                    SaveStatus(true);
                    SetAllEmissive(COLOR_GREEN, _glowIntensity);
                    SetRotationTarget("Command: on");
                    BroadcastState("on");
                    _logger?.Log(MODULE, "AKTIVIERT — Zustand: IDLE — Glow: GRUEN — Rotation: Ring=" + ROT_IDLE_RING, 1);
                    break;

                case "off":
                    _active            = false;
                    _shockwaveFired    = false;
                    _playerInRange     = false;
                    _randomMsgActive   = false;
                    _randomMsgIndex    = 0;
                    _randomMsgTimer    = 0;
                    _weatherTimer      = 0;
                    _cooldown          = 0;
                    _rotSpeed          = 0f;
                    _globalSpeed       = 0f;
                    _rotSpeedTarget    = 0f;
                    _globalSpeedTarget = 0f;
                    SaveStatus(false);
                    StopImpulse();
                    _delayedDamages.Clear();
                    SetAllEmissive(Color.Black, 0f);
                    try { MyAPIGateway.Session.WeatherEffects.RemoveWeather(_block.PositionComp.WorldAABB.Center); } catch { }
                    BroadcastState("off");
                    _logger?.Log(MODULE, "DEAKTIVIERT — Zustand: INAKTIV — Glow: SCHWARZ — Rotation: 0 — Wetter entfernt", 1);
                    break;

                case "reset":
                    LoadConfig();
                    _shockwaveFired    = false;
                    _weatherTimer      = 0;
                    _cooldown          = 0;
                    _hitCount          = 0;
                    _playerInRange     = false;
                    _rotSpeed          = ROT_IDLE_RING;
                    _globalSpeed       = ROT_IDLE_GLOB;
                    _rotSpeedTarget    = ROT_IDLE_RING;
                    _globalSpeedTarget = ROT_IDLE_GLOB;
                    _randomTimer       = _triggerInterval;
                    _randomMsgActive   = false;
                    _randomMsgIndex    = 0;
                    _randomMsgTimer    = 0;
                    _active            = true;
                    StopImpulse();
                    _delayedDamages.Clear();
                    try { MyAPIGateway.Session.WeatherEffects.RemoveWeather(_block.PositionComp.WorldAABB.Center); } catch { }
                    SaveStatus(true);
                    SetAllEmissive(COLOR_GREEN, _glowIntensity);
                    SetRotationTarget("Command: reset");
                    BroadcastState("reset");
                    _logger?.Log(MODULE, "RESET — Wetter entfernt — Impulse gestoppt — zurück zu IDLE" +
                        " — Glow: GRUEN — Rotation: Ring=" + ROT_IDLE_RING +
                        " — RandomTimer=" + _randomTimer + " Ticks (" + (_randomTimer / 60 / 60) + "h)", 1);
                    break;

                case "trigger":
                    if (!_active)
                    {
                        _logger?.Log(MODULE, "Command 'trigger' abgelehnt — Artefakt inaktiv", 1);
                        break;
                    }
                    if (_impulsePhase != ImpulsePhase.Idle)
                    {
                        _logger?.Log(MODULE, "Command 'trigger' abgelehnt — Phase=" + _impulsePhase + " bereits aktiv", 1);
                        break;
                    }
                    _isRandomTrigger = false;
                    _logger?.Log(MODULE, "MANUELL-TRIGGER — Aufladung startet sofort", 1);
                    ShowAlienMessage(false);
                    StartChargeImpulse();
                    break;

                default:
                    _logger?.Warn(MODULE, "Unbekannter Command: '" + cmd + "'");
                    break;
            }
        }

        // ──────────────────────────────────────────────────────────
        //  STATE-SYNC
        // ──────────────────────────────────────────────────────────

        private void BroadcastState(string cmd)
        {
            try
            {
                if (!MyAPIGateway.Multiplayer.IsServer) return;
                string msg = Entity.EntityId + "|" + cmd;
                byte[] data = System.Text.Encoding.UTF8.GetBytes(msg);
                MyAPIGateway.Multiplayer.SendMessageToOthers(STATE_SYNC_PACKET, data);
                _logger?.Log(MODULE, "State-Sync gesendet: " + msg, 1);
            }
            catch { }
        }

        private void BroadcastMessage(string text)
        {
            try
            {
                if (!MyAPIGateway.Multiplayer.IsServer) return;

                // An alle anderen Clients senden
                string packet = Entity.EntityId + "|msg|" + text;
                byte[] data = System.Text.Encoding.UTF8.GetBytes(packet);
                MyAPIGateway.Multiplayer.SendMessageToOthers(STATE_SYNC_PACKET, data);

                // Auf Listen-Server auch lokal anzeigen (auf Dedicated gibt es keinen lokalen Spieler)
                if (!MyAPIGateway.Utilities.IsDedicated)
                    MyAPIGateway.Utilities.ShowMessage("[ Artefakt ]", text);

                _logger?.Log(MODULE, "Nachricht gesendet: " + text, 1);
            }
            catch { }
        }

        private void OnStateSyncReceived(ushort handler, byte[] data, ulong senderId, bool fromServer)
        {
            if (!fromServer) return;
            try
            {
                string msg = System.Text.Encoding.UTF8.GetString(data);
                string[] parts = msg.Split('|');
                if (parts.Length < 2) return;

                long entityId;
                if (!long.TryParse(parts[0], out entityId)) return;
                if (entityId != Entity.EntityId) return;

                string cmd   = parts[1];
                string extra = parts.Length > 2 ? string.Join("|", parts, 2, parts.Length - 2) : "";

                // "msg" wird von ArtefactChatModule (Session-Ebene) angezeigt
                if (cmd == "msg") return;

                ApplyStateClient(cmd, extra);
            }
            catch { }
        }

        private void ApplyStateClient(string cmd, string extra = "")
        {
            switch (cmd)
            {
                case "on":
                    _active            = true;
                    _rotSpeed          = ROT_IDLE_RING;
                    _globalSpeed       = ROT_IDLE_GLOB;
                    _rotSpeedTarget    = ROT_IDLE_RING;
                    _globalSpeedTarget = ROT_IDLE_GLOB;
                    SetAllEmissive(COLOR_GREEN, _glowIntensity);
                    SetRotationTarget("StateSync: on");
                    break;

                case "off":
                    _active            = false;
                    _rotSpeed          = 0f;
                    _globalSpeed       = 0f;
                    _rotSpeedTarget    = 0f;
                    _globalSpeedTarget = 0f;
                    SetAllEmissive(Color.Black, 0f);
                    break;

                case "reset":
                    _active            = true;
                    _weatherTimer      = 0;
                    _rotSpeed          = ROT_IDLE_RING;
                    _globalSpeed       = ROT_IDLE_GLOB;
                    _rotSpeedTarget    = ROT_IDLE_RING;
                    _globalSpeedTarget = ROT_IDLE_GLOB;
                    StopImpulse();
                    SetAllEmissive(COLOR_GREEN, _glowIntensity);
                    SetRotationTarget("StateSync: reset");
                    break;

                case "charge_start":
                    if (_impulsePhase == ImpulsePhase.Idle)
                    {
                        _impulsePhase  = ImpulsePhase.Charging;
                        _impulseStep   = 0;
                        _impulsePause  = false;
                        _stepDuration  = CHARGE_STEP_TICKS;
                        _pauseDuration = 0;
                        _impulseTimer  = _stepDuration;
                        PlayChargeSound();
                        SetRotationTarget("StateSync: charge_start");
                    }
                    break;

                case "storm_start":
                    _weatherTimer = _weatherDuration * 60;
                    SetRotationTarget("StateSync: storm_start");
                    MyVisualScriptLogicProvider.PlaySingleSoundAtPosition("WepSmallWarheadExpl", _block.PositionComp.WorldAABB.Center);
                    break;

                case "impulse_fire":
                    MyVisualScriptLogicProvider.PlaySingleSoundAtPosition("WepSmallWarheadExpl", _block.PositionComp.WorldAABB.Center);
                    break;

                case "storm_end":
                    _weatherTimer = 0;
                    StopImpulse();
                    SetRotationTarget("StateSync: storm_end");
                    break;
            }
        }

        // ──────────────────────────────────────────────────────────
        //  CLEANUP
        // ──────────────────────────────────────────────────────────

        public override void Close()
        {
            _logger?.Log(MODULE, "Controller geschlossen — ID=" + _artefactId, 1);
            try { _chargeEmitter?.StopSound(true); _chargeEmitter?.Cleanup(); } catch { }
            try
            {
                if (!MyAPIGateway.Multiplayer.IsServer)
                    MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(STATE_SYNC_PACKET, OnStateSyncReceived);
                else
                    Instances.Remove(Entity.EntityId);
            }
            catch { }
            base.Close();
        }

        // ──────────────────────────────────────────────────────────
        //  INTERNER AUFRUF vom ArtefactRandomTimerModule (Session)
        // ──────────────────────────────────────────────────────────
        internal void TriggerRandom()
        {
            if (!MyAPIGateway.Multiplayer.IsServer) return;
            if (WeatherActive || _impulsePhase != ImpulsePhase.Idle || _randomMsgActive) return;

            _logger?.Log(MODULE, "RANDOM-TRIGGER (von Session ausgelöst)", 1);
            _isRandomTrigger = true;
            ShowAlienMessage(true);
        }

        // Config-Werte für ArtefactRandomTimerModule lesbar machen
        internal int   TriggerInterval  => _triggerInterval;
        internal int   TriggerChance    => _triggerChance;
        internal int   WeatherDuration  => _weatherDuration;
        internal bool  IsActive         => _active;
        internal Vector3D BlockPosition => _block?.PositionComp.WorldAABB.Center ?? Vector3D.Zero;
    }
}