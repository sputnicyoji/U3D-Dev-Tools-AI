namespace Yoji.U3DAILinker.Agents
{
    /// <summary>Outcome of a managed-block write attempt. No partial writes on Conflict.</summary>
    public sealed class BlockWriteResult
    {
        public enum Status
        {
            /// <summary>File created or block inserted/updated successfully.</summary>
            Written,
            /// <summary>Content already matched; nothing was written.</summary>
            Unchanged,
            /// <summary>Markers corrupt/duplicated; file left untouched.</summary>
            Conflict
        }

        public Status Outcome { get; }
        public string Message { get; }

        private BlockWriteResult(Status outcome, string message)
        {
            Outcome = outcome;
            Message = message;
        }

        public static BlockWriteResult Written() => new BlockWriteResult(Status.Written, null);
        public static BlockWriteResult Unchanged() => new BlockWriteResult(Status.Unchanged, null);
        public static BlockWriteResult Conflict(string message) => new BlockWriteResult(Status.Conflict, message);

        public bool IsConflict => Outcome == Status.Conflict;
    }
}
