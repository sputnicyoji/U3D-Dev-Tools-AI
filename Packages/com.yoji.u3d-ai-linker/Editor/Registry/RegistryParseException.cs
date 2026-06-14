using System;

namespace Yoji.U3DAILinker.Registry
{
    public sealed class RegistryParseException : Exception
    {
        public RegistryParseException(string message) : base(message) { }

        public RegistryParseException(string message, Exception inner) : base(message, inner) { }
    }
}
