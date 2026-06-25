using Newtonsoft.Json.Linq;

namespace Yoji.LuaDeviceDebug.Tests
{
    internal sealed class FakeHost : ILuaDeviceDebugHost
    {
        public string CommandsJson;
        public int DescribeCount;
        public int ExecuteCount;

        public bool IsReady
        {
            get { return true; }
        }

        public string DescribeCommands()
        {
            DescribeCount++;
            if (!string.IsNullOrEmpty(CommandsJson))
                return CommandsJson;

            return new JObject
            {
                ["commands"] = new JArray
                {
                    new JObject
                    {
                        ["name"] = "system.info",
                        ["description"] = "fake read-only command",
                        ["mutating"] = false,
                    },
                    new JObject
                    {
                        ["name"] = "state.reset",
                        ["description"] = "fake mutating command",
                        ["mutating"] = true,
                    },
                },
            }.ToString();
        }

        public string Execute(string command, string argsJson, bool allowMutation)
        {
            ExecuteCount++;
            return new JObject
            {
                ["command"] = command,
                ["args"] = JObject.Parse(argsJson),
                ["allowMutation"] = allowMutation,
            }.ToString();
        }
    }
}
