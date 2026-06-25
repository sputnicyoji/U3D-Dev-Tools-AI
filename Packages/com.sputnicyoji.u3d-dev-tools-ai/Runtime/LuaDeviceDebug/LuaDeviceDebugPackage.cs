namespace Yoji.LuaDeviceDebug
{
    public static class LuaDeviceDebugPackage
    {
        public const string ServiceId = "unity-lua-device-debug";
        public const string ServiceName = "LuaDeviceDebug";
        public const string Version = "0.1.1";
        public const int DefaultPort = 21894;
        public const int EditorPortOffset = 4;

        public static int[] CreateEditorLegacyPorts()
        {
            return new int[] { DefaultPort };
        }
    }
}
