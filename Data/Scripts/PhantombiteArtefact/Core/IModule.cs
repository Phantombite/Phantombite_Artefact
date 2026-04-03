namespace PhantombiteArtefact.Core
{
    public interface IModule
    {
        string ModuleName { get; }
        void Init();
        void Update();
        void SaveData();
        void Close();
    }
}