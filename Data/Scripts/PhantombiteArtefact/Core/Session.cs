using System;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Utils;
using PhantombiteArtefact.Core;
using PhantombiteArtefact.Modules;

namespace PhantombiteArtefact
{
    /// <summary>
    /// Session für PhantomBite Artefact.
    ///
    /// Registriert:
    /// - Artefact_Command (Registrierung beim Core + Command-Empfang)
    ///
    /// Der Block-Script SpaceArtefactController läuft weiterhin separat
    /// für Animation und Spieler-Trigger.
    /// </summary>
    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
    public class PhantombiteArtefactSession : MySessionComponentBase
    {
        private ModuleManager         _moduleManager;
        private ArtefactCommandModule _commandModule;

        private bool _isInitialized = false;
        private const string MOD_NAME = "PhantombiteArtefact";

        public override void LoadData()
        {
            try
            {
                MyLog.Default.WriteLineAndConsole("[" + MOD_NAME + "] Session LoadData started...");
                _moduleManager = new ModuleManager();
                _isInitialized = true;
                MyLog.Default.WriteLineAndConsole("[" + MOD_NAME + "] Session LoadData completed.");
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole("[" + MOD_NAME + "] CRITICAL ERROR in LoadData:\n" + ex);
            }
        }

        public override void BeforeStart()
        {
            if (!_isInitialized) return;

            try
            {
                // Artefact_Command: registriert sich beim Core
                _commandModule = new ArtefactCommandModule();
                _moduleManager.RegisterModule(_commandModule);
                _moduleManager.InitAll();

                MyLog.Default.WriteLineAndConsole("[" + MOD_NAME + "] BeforeStart completed.");
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole("[" + MOD_NAME + "] CRITICAL ERROR in BeforeStart:\n" + ex);
            }
        }

        public override void UpdateBeforeSimulation()
        {
            if (!_isInitialized) return;
            try { _moduleManager.UpdateAll(); }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole("[" + MOD_NAME + "] ERROR in Update:\n" + ex);
            }
        }

        public override void SaveData()
        {
            if (!_isInitialized) return;
            try { _moduleManager.SaveAll(); }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole("[" + MOD_NAME + "] ERROR in SaveData:\n" + ex);
            }
        }

        protected override void UnloadData()
        {
            try
            {
                _moduleManager.CloseAll();
                _isInitialized = false;
                MyLog.Default.WriteLineAndConsole("[" + MOD_NAME + "] UnloadData completed.");
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole("[" + MOD_NAME + "] ERROR in UnloadData:\n" + ex);
            }
        }
    }
}