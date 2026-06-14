namespace Yoji.U3DAILinker.Registry
{
    public enum ToolStatus
    {
        Ready,
        SkillOnly,
        Planned
    }

    public static class ToolStatusExtensions
    {
        public static bool TryParse(string raw, out ToolStatus status)
        {
            switch (raw)
            {
                case "ready":
                    status = ToolStatus.Ready;
                    return true;
                case "skill-only":
                    status = ToolStatus.SkillOnly;
                    return true;
                case "planned":
                    status = ToolStatus.Planned;
                    return true;
                default:
                    status = ToolStatus.Planned;
                    return false;
            }
        }
    }
}
