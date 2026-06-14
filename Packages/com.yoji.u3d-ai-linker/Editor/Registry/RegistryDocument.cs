using Newtonsoft.Json;

namespace Yoji.U3DAILinker.Registry
{
    public sealed class RegistryDocument
    {
        [JsonProperty("schemaVersion")]
        public int SchemaVersion { get; set; }

        [JsonProperty("channel")]
        public string Channel { get; set; }

        [JsonProperty("branch")]
        public string Branch { get; set; }

        [JsonProperty("entries")]
        public RegistryEntry[] Entries { get; set; } = new RegistryEntry[0];
    }
}
