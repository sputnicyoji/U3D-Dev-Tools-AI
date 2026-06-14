namespace Yoji.U3DAILinker.Registry
{
    public enum ToolKind
    {
        Tool,
        Infra,
        Linker
    }

    public static class ToolKindExtensions
    {
        public static bool TryParse(string raw, out ToolKind kind)
        {
            switch (raw)
            {
                case "tool":
                    kind = ToolKind.Tool;
                    return true;
                case "infra":
                    kind = ToolKind.Infra;
                    return true;
                case "linker":
                    kind = ToolKind.Linker;
                    return true;
                default:
                    kind = ToolKind.Tool;
                    return false;
            }
        }
    }
}
