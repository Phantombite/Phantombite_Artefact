using System;
using System.Collections.Generic;
using VRage.Utils;

namespace PhantombiteArtefact.Core
{
    public class ModuleManager
    {
        private readonly List<IModule> _modules      = new List<IModule>();
        private readonly Dictionary<string, int>  _crashes  = new Dictionary<string, int>();
        private readonly Dictionary<string, bool> _disabled = new Dictionary<string, bool>();
        private const int MAX_CRASHES = 3;

        public void RegisterModule(IModule module)
        {
            if (module == null) return;
            _modules.Add(module);
            _crashes[module.ModuleName]  = 0;
            _disabled[module.ModuleName] = false;
            MyLog.Default.WriteLineAndConsole("[PhantombiteArtefact] ModuleManager: Registered '" + module.ModuleName + "'");
        }

        public void InitAll()
        {
            foreach (var m in _modules)
            {
                if (_disabled[m.ModuleName]) continue;
                try { m.Init(); }
                catch (Exception ex) { HandleError(m, "Init", ex); }
            }
        }

        public void UpdateAll()
        {
            foreach (var m in _modules)
            {
                if (_disabled[m.ModuleName]) continue;
                try { m.Update(); }
                catch (Exception ex) { HandleError(m, "Update", ex); }
            }
        }

        public void SaveAll()
        {
            foreach (var m in _modules)
            {
                if (_disabled[m.ModuleName]) continue;
                try { m.SaveData(); }
                catch (Exception ex) { HandleError(m, "SaveData", ex); }
            }
        }

        public void CloseAll()
        {
            foreach (var m in _modules)
            {
                try { m.Close(); }
                catch (Exception ex) { MyLog.Default.WriteLineAndConsole("[PhantombiteArtefact] Error closing '" + m.ModuleName + "': " + ex.Message); }
            }
        }

        private void HandleError(IModule m, string op, Exception ex)
        {
            _crashes[m.ModuleName]++;
            MyLog.Default.WriteLineAndConsole("[PhantombiteArtefact] ERROR in '" + m.ModuleName + "." + op + "': " + ex.Message);
            if (_crashes[m.ModuleName] >= MAX_CRASHES)
            {
                _disabled[m.ModuleName] = true;
                MyLog.Default.WriteLineAndConsole("[PhantombiteArtefact] '" + m.ModuleName + "' DISABLED after " + MAX_CRASHES + " crashes!");
            }
        }
    }
}