using System;
using Newtonsoft.Json;

namespace Yoji.U3DAILinker.Registry
{
    public static class RegistryParser
    {
        public const int SupportedSchemaVersion = 1;

        public static RegistryDocument Parse(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                throw new RegistryParseException("Registry JSON is empty.");
            }

            RegistryDocument doc;
            var settings = new JsonSerializerSettings
            {
                MissingMemberHandling = MissingMemberHandling.Error
            };

            try
            {
                doc = JsonConvert.DeserializeObject<RegistryDocument>(json, settings);
            }
            catch (JsonException ex)
            {
                throw new RegistryParseException("Registry JSON is not well-formed or contains unknown fields: " + ex.Message, ex);
            }

            if (doc == null)
            {
                throw new RegistryParseException("Registry JSON deserialized to null.");
            }

            if (doc.SchemaVersion != SupportedSchemaVersion)
            {
                throw new RegistryParseException(
                    "Unsupported schemaVersion " + doc.SchemaVersion + "; this Linker supports schemaVersion " + SupportedSchemaVersion + ".");
            }

            if (doc.Entries == null)
            {
                doc.Entries = new RegistryEntry[0];
            }

            return doc;
        }
    }
}
