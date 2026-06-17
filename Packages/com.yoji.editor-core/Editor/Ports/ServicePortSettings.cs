using System;
using System.Collections.Generic;

namespace Yoji.EditorCore.Ports
{
    [Serializable]
    public sealed class ServicePortSettings
    {
        public int SchemaVersion = 1;
        public string ProjectId = string.Empty;
        public string Mode = "auto";
        public int PreferredBasePort = 0;
        public List<ServicePortOverride> ServiceOverrides = new List<ServicePortOverride>();
    }

    [Serializable]
    public sealed class ServicePortUserSettings
    {
        public int SchemaVersion = 1;
        public int OverrideBasePort = 0;
        public bool PreferLegacyPorts = true;
    }

    [Serializable]
    public sealed class ServicePortOverride
    {
        public string ServiceId = string.Empty;
        public int OverrideBasePort = 0;
        public bool PreferLegacyPorts = true;
    }
}
