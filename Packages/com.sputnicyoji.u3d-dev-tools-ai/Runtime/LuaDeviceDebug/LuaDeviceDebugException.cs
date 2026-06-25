using System;

namespace Yoji.LuaDeviceDebug
{
    internal sealed class LuaDeviceDebugException : Exception
    {
        public readonly int HttpStatus;
        public readonly string Code;

        public LuaDeviceDebugException(int httpStatus, string code, string message) : base(message)
        {
            HttpStatus = httpStatus;
            Code = code;
        }
    }
}
