namespace Yoji.EditorCore.Ports
{
    public enum ServicePortMode
    {
        Auto,
        FixedProject,
    }

    public sealed class ServicePortPolicy
    {
        public string ProjectRoot;
        public string ProjectId;
        public ServicePortMode Mode = ServicePortMode.Auto;
        public int PreferredBasePort;
        public int OverrideBasePort;
        public bool PreferLegacyPorts = true;
    }
}
