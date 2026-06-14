using Newtonsoft.Json;

namespace Yoji.U3DAILinker.Registry
{
    public sealed class RegistryEntry
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; }

        [JsonProperty("kind")]
        public string Kind { get; set; }

        [JsonProperty("order")]
        public int Order { get; set; }

        [JsonProperty("packageName")]
        public string PackageName { get; set; }

        [JsonProperty("packagePath")]
        public string PackagePath { get; set; }

        [JsonProperty("revision")]
        public string Revision { get; set; }

        [JsonProperty("defaultEnabled")]
        public bool DefaultEnabled { get; set; }

        [JsonProperty("userToggle")]
        public bool UserToggle { get; set; }

        [JsonProperty("agentAssets")]
        public string AgentAssets { get; set; }

        [JsonProperty("minUnity")]
        public string MinUnity { get; set; }

        [JsonProperty("dependsOn")]
        public string[] DependsOn { get; set; } = new string[0];
    }
}
