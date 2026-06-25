using System;
using Newtonsoft.Json.Linq;

namespace Yoji.LuaDeviceDebug
{
    internal static class JsonGuard
    {
        public const int MaxRequestBytes = 1024 * 1024;
        public const int MaxResponseBytes = 4 * 1024 * 1024;
        private const int k_MaxDepth = 32;
        private const int k_MaxMembers = 4096;

        public static void EnsureWithinLimits(JToken token)
        {
            var members = 0;
            Visit(token, 0, ref members);
        }

        private static void Visit(JToken token, int depth, ref int members)
        {
            if (token == null) return;
            if (depth > k_MaxDepth)
                throw new LuaDeviceDebugException(413, "RESULT_TOO_DEEP", "JSON depth limit exceeded");

            switch (token.Type)
            {
                case JTokenType.Object:
                    foreach (var child in token.Children<JProperty>())
                    {
                        members++;
                        CheckMembers(members);
                        Visit(child.Value, depth + 1, ref members);
                    }
                    break;
                case JTokenType.Array:
                    foreach (var child in token.Children())
                    {
                        members++;
                        CheckMembers(members);
                        Visit(child, depth + 1, ref members);
                    }
                    break;
                case JTokenType.Null:
                case JTokenType.Boolean:
                case JTokenType.Integer:
                case JTokenType.Float:
                case JTokenType.String:
                    break;
                default:
                    throw new LuaDeviceDebugException(400, "INVALID_JSON_TYPE", "unsupported JSON token type: " + token.Type);
            }
        }

        private static void CheckMembers(int members)
        {
            if (members > k_MaxMembers)
                throw new LuaDeviceDebugException(413, "RESULT_TOO_LARGE", "JSON member limit exceeded");
        }
    }
}
