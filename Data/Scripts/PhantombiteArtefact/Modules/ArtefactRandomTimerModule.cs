using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Utils;
using PhantombiteArtefact.Core;

namespace PhantombiteArtefact.Modules
{
    /// <summary>
    /// Läuft auf dem Server als Session-Komponente.
    /// Verwaltet den Random-Timer für alle Artefakt-Blöcke unabhängig
    /// vom Streaming-Radius — der Timer friert nicht mehr ein wenn
    /// kein Spieler in der Nähe ist.
    /// </summary>
    public class ArtefactRandomTimerModule : IModule
    {
        public string ModuleName => "ArtefactRandomTimer";

        private const string MODULE = "ArtefactRandomTimer";
        private const int CHECK_INTERVAL = 30; // Ticks zwischen Timer-Updates

        private class ArtefactEntry
        {
            public long   EntityId;
            public int    Timer;
            public Random Rng = new Random();
        }

        private readonly List<ArtefactEntry> _entries = new List<ArtefactEntry>();
        private int _frameTick = 0;
        private bool _initialized = false;

        public void Init()
        {
            // Nur auf dem Server laufen
            if (!MyAPIGateway.Multiplayer.IsServer) return;

            // Verzögertes Init — Blocks brauchen ein paar Frames zum Laden
            // wird in Update() beim ersten Aufruf ausgeführt
            MyLog.Default.WriteLineAndConsole("[PhantombiteArtefact] ArtefactRandomTimerModule: Init (warte auf Block-Load).");
        }

        private void ScanForArtefacts()
        {
            _entries.Clear();

            foreach (var kv in ArtefactController.Instances)
            {
                var controller = kv.Value;
                var entry = new ArtefactEntry
                {
                    EntityId = kv.Key,
                    Timer    = controller.TriggerInterval
                };
                _entries.Add(entry);
                MyLog.Default.WriteLineAndConsole(
                    "[PhantombiteArtefact] ArtefactRandomTimerModule: Artefakt gefunden — EntityId=" +
                    kv.Key + " Interval=" + controller.TriggerInterval + " Ticks");
            }

            MyLog.Default.WriteLineAndConsole(
                "[PhantombiteArtefact] ArtefactRandomTimerModule: " + _entries.Count + " Artefakt(e) gefunden.");
            _initialized = true;
        }

        public void Update()
        {
            if (!MyAPIGateway.Multiplayer.IsServer) return;

            _frameTick++;

            // Einmalig nach 300 Frames (~5s) alle Artefakte suchen
            if (!_initialized)
            {
                if (_frameTick < 300) return;
                ScanForArtefacts();
                return;
            }

            // Nur alle CHECK_INTERVAL Frames updaten
            if (_frameTick % CHECK_INTERVAL != 0) return;

            foreach (var entry in _entries)
            {
                ArtefactController controller;
                if (!ArtefactController.Instances.TryGetValue(entry.EntityId, out controller))
                    continue; // Block nicht mehr vorhanden

                if (!controller.IsActive) continue;

                entry.Timer -= CHECK_INTERVAL;

                if (entry.Timer > 0) continue;

                // Timer abgelaufen — würfeln
                entry.Timer = controller.TriggerInterval;

                int  roll      = entry.Rng.Next(0, 100);
                bool triggered = roll < controller.TriggerChance;

                MyLog.Default.WriteLineAndConsole(
                    "[PhantombiteArtefact] ArtefactRandomTimerModule: Wuerfel=" + roll +
                    " Chance=" + controller.TriggerChance + "% — " +
                    (triggered ? "GETRIGGERT" : "nicht getriggert"));

                if (!triggered) continue;

                var players = new System.Collections.Generic.List<IMyPlayer>();
                MyAPIGateway.Players.GetPlayers(players, p => !p.IsBot);
                if (players.Count == 0)
                {
                    MyLog.Default.WriteLineAndConsole(
                        "[PhantombiteArtefact] ArtefactRandomTimerModule: Abgebrochen — keine Spieler online.");
                    continue;
                }

                MyLog.Default.WriteLineAndConsole(
                    "[PhantombiteArtefact] ArtefactRandomTimerModule: TRIGGER — " +
                    players.Count + " Spieler online.");

                try 
                { 
                    controller.TriggerRandom();
                    MyLog.Default.WriteLineAndConsole(
                        "[PhantombiteArtefact] ArtefactRandomTimerModule: Wetter ausgeloest um " +
                        DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss"));
                }
                catch (Exception ex)
                {
                    MyLog.Default.WriteLineAndConsole(
                        "[PhantombiteArtefact] ArtefactRandomTimerModule: Fehler bei TriggerRandom: " + ex.Message);
                }
            }
        }

        public void SaveData() { }

        public void Close()
        {
            _entries.Clear();
            MyLog.Default.WriteLineAndConsole("[PhantombiteArtefact] ArtefactRandomTimerModule: Closed.");
        }
    }
}