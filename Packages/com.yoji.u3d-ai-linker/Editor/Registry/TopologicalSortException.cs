using System;

namespace Yoji.U3DAILinker.Registry
{
    public sealed class TopologicalSortException : Exception
    {
        public TopologicalSortException(string message) : base(message) { }
    }
}
