using NUnit.Framework;
using Yoji.U3DAILinker.Registry;

namespace Yoji.U3DAILinker.Registry.Tests
{
    public class RegistryParserTests
    {
        private const string MinimalValid = @"{
  ""schemaVersion"": 1,
  ""channel"": ""stable"",
  ""entries"": [
    {
      ""id"": ""editor-debug"",
      ""status"": ""ready"",
      ""kind"": ""tool"",
      ""order"": 20,
      ""packageName"": ""com.yoji.editor-debug"",
      ""packagePath"": ""Packages/com.yoji.editor-debug"",
      ""revision"": ""editor-debug-v1.2.0"",
      ""defaultEnabled"": true,
      ""userToggle"": true,
      ""agentAssets"": ""Agent~"",
      ""minUnity"": ""2022.3"",
      ""dependsOn"": []
    }
  ]
}";

        [Test] public void Parse_MinimalValid_Succeeds()
        {
            var doc = RegistryParser.Parse(MinimalValid);
            Assert.AreEqual(1, doc.SchemaVersion);
            Assert.AreEqual("stable", doc.Channel);
            Assert.AreEqual(1, doc.Entries.Length);
            Assert.AreEqual("editor-debug", doc.Entries[0].Id);
            Assert.AreEqual(20, doc.Entries[0].Order);
        }

        [Test] public void Parse_UnknownTopLevelField_Throws()
        {
            var json = @"{ ""schemaVersion"": 1, ""channel"": ""stable"", ""entries"": [], ""extra"": 1 }";
            Assert.Throws<RegistryParseException>(() => RegistryParser.Parse(json));
        }

        [Test] public void Parse_UnknownEntryField_Throws()
        {
            var json = @"{ ""schemaVersion"": 1, ""channel"": ""stable"", ""entries"": [ { ""id"": ""x"", ""mystery"": true } ] }";
            Assert.Throws<RegistryParseException>(() => RegistryParser.Parse(json));
        }

        [Test] public void Parse_UnsupportedSchemaVersion_Throws()
        {
            var json = @"{ ""schemaVersion"": 2, ""channel"": ""stable"", ""entries"": [] }";
            var ex = Assert.Throws<RegistryParseException>(() => RegistryParser.Parse(json));
            StringAssert.Contains("schemaVersion", ex.Message);
        }

        [Test] public void Parse_MissingSchemaVersion_Throws()
        {
            var json = @"{ ""channel"": ""stable"", ""entries"": [] }";
            Assert.Throws<RegistryParseException>(() => RegistryParser.Parse(json));
        }

        [Test] public void Parse_MalformedJson_Throws()
            => Assert.Throws<RegistryParseException>(() => RegistryParser.Parse("{ not json"));
    }
}
