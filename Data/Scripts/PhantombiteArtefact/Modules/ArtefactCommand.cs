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

        private bool _initialized = false;

        // Log-Level vom Core — nur senden was Core wirklich will
        private enum LogLevel { Normal = 0, Debug = 1, Trace = 2 }
        private LogLevel _logLevel = LogLevel.Normal;

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
                    MyLog.Default.WriteLineAndConsole("[PhantombiteArtefact] Artefact_Command: Core READY empfangen");
                    RegisterWithCore();
                    return;
                }

                if (msg.StartsWith("LOGLEVEL|"))
                {
                    string levelStr = msg.Substring(9).ToLower();
                    _logLevel = levelStr == "trace" ? LogLevel.Trace
                              : levelStr == "debug" ? LogLevel.Debug
                              : LogLevel.Normal;
                    MyLog.Default.WriteLineAndConsole("[PhantombiteArtefact] Artefact_Command: LogLevel gesetzt: " + _logLevel);
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
            SendLog("WARN", module, message);
        }

        public void Error(string module, string message)
        {
            SendLog("ERROR", module, message);
        }

        public void Info(string module, string message)
        {
            if (_logLevel < LogLevel.Debug) return;
            SendLog("INFO", module, message);
        }

        public void Debug(string module, string message)
        {
            if (_logLevel < LogLevel.Debug) return;
            SendLog("DEBUG", module, message);
        }

        public void Trace(string module, string message)
        {
            if (_logLevel < LogLevel.Trace) return;
            SendLog("TRACE", module, message);
        }

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
                    + "|" + ARTEFACT_CHANNEL
                    + "|on:1:Artefakt aktivieren (!pbc artefact [ID] on)"
                    + "|off:1:Artefakt deaktivieren (!pbc artefact [ID] off)"
                    + "|reset:1:Artefakt zurücksetzen (!pbc artefact [ID|all] reset)"
                    + "|trigger:1:Schockwelle auslösen (!pbc artefact [ID] trigger)";

                MyAPIGateway.Utilities.SendModMessage(CORE_CHANNEL, msg);
                MyLog.Default.WriteLineAndConsole("[PhantombiteArtefact] Artefact_Command: Registrierung an Core gesendet");
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

                MyLog.Default.WriteLineAndConsole("[PhantombiteArtefact] Artefact_Command: Command empfangen: " + command);
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
                string status  = executed ? "ok" : "fail";
                string target  = allTarget ? "alle" : (hasId ? "ID " + targetId : "ID 0");
                string message = executed
                    ? "Artefact " + target + ": " + command + " ausgeführt"
                    : "Artefact " + target + ": nicht gefunden";

                string result = "CMDRESULT|artefact|" + command + "|" + argsJoined + "|" + steamId + "|" + status + "|" + message;
                MyAPIGateway.Utilities.SendModMessage(CORE_CHANNEL, result);
                MyLog.Default.WriteLineAndConsole("[PhantombiteArtefact] Artefact_Command: CMDRESULT gesendet: " + status);
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole("[PhantombiteArtefact] Artefact_Command: Fehler in ExecuteOnBlocks: " + ex.Message);
            }
        }
    }
}