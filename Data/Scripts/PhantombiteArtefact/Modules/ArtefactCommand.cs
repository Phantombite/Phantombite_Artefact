using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using PhantombiteArtefact.Core;

namespace PhantombiteArtefact.Modules
{
    /// <summary>
    /// Artefact_Command
    ///
    /// Registriert Artefact beim PhantomBite Core über Messaging.
    /// Empfängt Commands vom Core und leitet sie an SpaceArtefactController weiter.
    ///
    /// Kanal:
    ///   Core empfängt Registrierung: 1995000
    ///   Artefact empfängt Commands:  1995001
    ///
    /// Registrierungs-Format:
    ///   "REGISTER|artefact|Space Artefact|1995001|on:1:Artefakt aktivieren|off:1:Artefakt deaktivieren|..."
    ///
    /// Command-Format vom Core:
    ///   "CMD|on" / "CMD|off" / "CMD|reset" / "CMD|trigger" / "CMD|trigger|1"
    /// </summary>
    public class ArtefactCommandModule : IModule
    {
        public string ModuleName { get { return "Artefact_Command"; } }

        private const long CORE_CHANNEL     = 1995000L;
        private const long ARTEFACT_CHANNEL = 1995001L;
        private const long LOG_CHANNEL      = 1995999L;
        private const string MOD_NAME       = "Phantombite_Artefact";
        private const string VERSION         = "1.1.0";

        private bool _initialized = false;

        private enum LogLevel { Normal = 0, Debug = 1, Trace = 2 }
        private LogLevel _logLevel = LogLevel.Normal;

        /// <summary>0=voll, 1=Check/2Frames, 2=Check/5Frames, 3=Check/10Frames</summary>
        public int PerfLevel { get; private set; } = 0;

        // ── IModule ──────────────────────────────────────────────────────────

        public void Init()
        {
            if (_initialized) return;
            MyAPIGateway.Utilities.RegisterMessageHandler(ARTEFACT_CHANNEL, OnMessageReceived);
            _initialized = true;
            MyLog.Default.WriteLineAndConsole("[PhantombiteArtefact] Artefact_Command: Initialized — warte auf Core READY");
        }

        public void Update()   { }
        public void SaveData() { }

        public void Close()
        {
            if (!_initialized) return;
            if (MyAPIGateway.Utilities != null)
                MyAPIGateway.Utilities.UnregisterMessageHandler(ARTEFACT_CHANNEL, OnMessageReceived);
            _initialized = false;
            MyLog.Default.WriteLineAndConsole("[PhantombiteArtefact] Artefact_Command: Closed");
        }

        // ── Nachrichten vom Core ──────────────────────────────────────────────

        private void OnMessageReceived(object data)
        {
            try
            {
                string msg = data as string;
                if (string.IsNullOrEmpty(msg)) return;

                if (msg == "READY")
                {
                    Log("Artefact_Command", "Core READY empfangen");
                    RegisterWithCore();
                    return;
                }

                if (msg.StartsWith("LOGLEVEL|"))
                {
                    int lvl;
                    if (int.TryParse(msg.Substring(9), out lvl))
                        _logLevel = (LogLevel)Math.Min(lvl, 2);
                    else { string s = msg.Substring(9).ToLower(); _logLevel = s == "trace" ? LogLevel.Trace : s == "debug" ? LogLevel.Debug : LogLevel.Normal; }
                    Log("Artefact_Command", "LOGLEVEL gesetzt: " + (int)_logLevel, 1);
                    return;
                }

                if (msg.StartsWith("PERFLEVEL|"))
                {
                    int lvl;
                    if (int.TryParse(msg.Substring(10), out lvl))
                    {
                        PerfLevel = Math.Max(0, Math.Min(3, lvl));
                        Log("Artefact_Command", "PERFLEVEL gesetzt: " + PerfLevel, 1);
                        MyAPIGateway.Utilities.SendModMessage(CORE_CHANNEL, "PERFACK|artefact|" + PerfLevel);
                    }
                    return;
                }

                if (msg.StartsWith("CMD|"))
                    OnCommandReceived(msg);
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole("[PhantombiteArtefact] Artefact_Command: Fehler in OnMessageReceived: " + ex.Message);
            }
        }

        // ── Log API für andere Artefact Module ───────────────────────────────

        public void Warn(string module, string message)
        {
            MyLog.Default.WriteLineAndConsole("[PhantombiteArtefact] [WARN] [" + module + "] " + message);
            SendLog("WARN", module, message);
        }

        public void Error(string module, string message)
        {
            MyLog.Default.WriteLineAndConsole("[PhantombiteArtefact] [ERROR] [" + module + "] " + message);
            SendLog("ERROR", module, message);
        }

        public void Log(string module, string message, int level = 0)
        {
            if (level > 0 && (int)_logLevel < level) return;
            MyLog.Default.WriteLineAndConsole("[PhantombiteArtefact] [" + level + "] [" + module + "] " + message);
            SendLog(level.ToString(), module, message);
        }

        public void HeavyStart(string op) { try { MyAPIGateway.Utilities.SendModMessage(CORE_CHANNEL, "HEAVY_START|artefact|" + op); } catch { } }
        public void HeavyEnd(string op)   { try { MyAPIGateway.Utilities.SendModMessage(CORE_CHANNEL, "HEAVY_END|artefact|" + op);   } catch { } }

        private void SendLog(string level, string module, string message)
        {
            try
            {
                MyAPIGateway.Utilities.SendModMessage(LOG_CHANNEL,
                    "LOG|" + MOD_NAME + "|" + level + "|" + module + "|" + message);
            }
            catch { }
        }

        // ── Registrierung beim Core ───────────────────────────────────────────

        private void RegisterWithCore()
        {
            try
            {
                // Format: "REGISTER|modname|description|channel|cmd:adminOnly:desc|..."
                string msg = "REGISTER"
                    + "|artefact"
                    + "|Space Artefact Steuerung"
                    + "|" + VERSION
                    + "|" + ARTEFACT_CHANNEL
                    + "|on:1:Artefakt aktivieren (!pbc artefact [ID] on)"
                    + "|off:1:Artefakt deaktivieren (!pbc artefact [ID] off)"
                    + "|reset:1:Artefakt zurücksetzen (!pbc artefact [ID|all] reset)"
                    + "|trigger:1:Schockwelle auslösen (!pbc artefact [ID] trigger)";

                MyAPIGateway.Utilities.SendModMessage(CORE_CHANNEL, msg);
                Log("Artefact_Command", "Registrierung an Core gesendet");
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole("[PhantombiteArtefact] Artefact_Command: Fehler bei Registrierung: " + ex.Message);
            }
        }

        // ── Command Empfang vom Core ──────────────────────────────────────────

        private void OnCommandReceived(string msg)
        {
            try
            {
                if (string.IsNullOrEmpty(msg) || !msg.StartsWith("CMD|")) return;

                string[] parts = msg.Split('|');
                if (parts.Length < 2) return;

                string command = parts[1].ToLower();

                // STEAM:steamId aus letztem Arg extrahieren
                ulong steamId = 0;
                int   argEnd  = parts.Length;
                if (parts[parts.Length - 1].StartsWith("STEAM:"))
                {
                    ulong.TryParse(parts[parts.Length - 1].Substring(6), out steamId);
                    argEnd = parts.Length - 1;
                }

                string[] args = new string[argEnd - 2];
                Array.Copy(parts, 2, args, 0, args.Length);

                Log("Artefact_Command", "Command empfangen: " + command, 1);
                ExecuteOnBlocks(command, args, steamId);
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole("[PhantombiteArtefact] Artefact_Command: Fehler in OnCommandReceived: " + ex.Message);
            }
        }

        // ── Command Ausführung auf allen SpaceArtefact Blöcken ───────────────

        private void ExecuteOnBlocks(string command, string[] args, ulong steamId)
        {
            try
            {
                int  targetId  = 0;
                bool hasId     = false;
                bool allTarget = false;

                if (args.Length > 0)
                {
                    if (args[0].ToLower() == "all")
                        allTarget = true;
                    else if (int.TryParse(args[0], out targetId))
                        hasId = true;
                }

                string argsJoined = string.Join("|", args);
                bool   executed   = false;

                var entities = new HashSet<IMyEntity>();
                MyAPIGateway.Entities.GetEntities(entities);

                foreach (var entity in entities)
                {
                    var grid = entity as IMyCubeGrid;
                    if (grid == null) continue;

                    var blocks = new List<IMySlimBlock>();
                    grid.GetBlocks(blocks, b => b.FatBlock is IMyFunctionalBlock);

                    foreach (var slim in blocks)
                    {
                        var block = slim.FatBlock as IMyFunctionalBlock;
                        if (block == null) continue;
                        if (block.BlockDefinition.SubtypeId != "SpaceArtefact" &&
                            block.BlockDefinition.SubtypeId != "SpaceArtefactT2" &&
                            block.BlockDefinition.SubtypeId != "SpaceArtefactT3") continue;

                        var controller = block.GameLogic?.GetAs<ArtefactController>();
                        if (controller == null) continue;

                        controller.SetLogger(this);
                        controller.ExecuteCommand(command, targetId, hasId, allTarget);
                        executed = true;
                    }
                }

                // CMDRESULT zurück an Core
                string status  = executed ? "ok" : "error";
                string target  = allTarget ? "alle" : (hasId ? "ID " + targetId : "ID 0");
                string message = executed
                    ? "Artefact " + target + ": " + command + " ausgeführt"
                    : "Artefact " + target + ": nicht gefunden";

                string result = "CMDRESULT|artefact|" + command + "|" + argsJoined + "|" + steamId + "|" + status + "|" + message;
                MyAPIGateway.Utilities.SendModMessage(CORE_CHANNEL, result);
                Log("Artefact_Command", "CMDRESULT gesendet: " + status, 1);
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole("[PhantombiteArtefact] Artefact_Command: Fehler in ExecuteOnBlocks: " + ex.Message);
            }
        }
    }
}