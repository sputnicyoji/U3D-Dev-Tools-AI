namespace Yoji.LuaDeviceDebug
{
    public sealed class CommandDescriptor
    {
        public string Name;
        public string Description;
        public bool Mutating;
        public string ArgsSchemaJson;
        public string ResultSchemaJson;
    }
}
