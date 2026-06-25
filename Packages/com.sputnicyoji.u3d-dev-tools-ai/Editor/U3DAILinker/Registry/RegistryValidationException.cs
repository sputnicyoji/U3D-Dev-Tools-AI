using System;
using System.Collections.Generic;

namespace Yoji.U3DAILinker.Registry
{
    public sealed class RegistryValidationException : Exception
    {
        public IReadOnlyList<string> Errors { get; }

        public RegistryValidationException(IReadOnlyList<string> errors)
            : base("Registry validation failed:\n" + string.Join("\n", errors))
        {
            Errors = errors;
        }
    }
}
