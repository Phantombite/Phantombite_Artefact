using Sandbox.ModAPI;
using VRage.Utils;
using PhantombiteArtefact.Core;

namespace PhantombiteArtefact.Modules
{
    /// <summary>
    /// Empfängt Chat-Nachrichten vom Server und zeigt sie lokal an.
    /// Läuft als Session-Komponente → immer aktiv, unabhängig vom
    /// Streaming-Radius des Artefakt-Blocks.
    /// </summary>
    public class ArtefactChatModule : IModule
    {
        public string ModuleName => "ArtefactChat";

        private const ushort STATE_SYNC_PACKET = 5999;

        public void Init()
        {
            // Nur auf Clients registrieren — der Server sendet, empfängt nicht
            if (MyAPIGateway.Multiplayer.IsServer) return;

            MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(STATE_SYNC_PACKET, OnPacketReceived);
            MyLog.Default.WriteLineAndConsole("[PhantombiteArtefact] ArtefactChatModule: Handler registriert.");
        }

        private void OnPacketReceived(ushort handler, byte[] data, ulong senderId, bool fromServer)
        {
            // Nur Pakete vom Server akzeptieren
            if (!fromServer) return;

            try
            {
                string msg = System.Text.Encoding.UTF8.GetString(data);

                // Format: {entityId}|msg|{text}
                string[] parts = msg.Split('|');
                if (parts.Length < 3) return;
                if (parts[1] != "msg") return;

                string text = string.Join("|", parts, 2, parts.Length - 2);
                if (!string.IsNullOrEmpty(text))
                    MyAPIGateway.Utilities.ShowMessage("[ Artefakt ]", text);
            }
            catch { }
        }

        public void Update()   { }
        public void SaveData() { }

        public void Close()
        {
            if (MyAPIGateway.Multiplayer.IsServer) return;

            MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(STATE_SYNC_PACKET, OnPacketReceived);
            MyLog.Default.WriteLineAndConsole("[PhantombiteArtefact] ArtefactChatModule: Handler deregistriert.");
        }
    }
}
