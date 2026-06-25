namespace Yoji.LuaDeviceDebug
{
    public interface ILuaDeviceDebugHost
    {
        bool IsReady { get; }
        string DescribeCommands();
        string Execute(string command, string argsJson, bool allowMutation);
    }
}
