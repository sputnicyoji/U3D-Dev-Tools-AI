namespace Yoji.U3DAILinker.Agents
{
    /// <summary>Outcome of merging tool fragments for one managed file kind.</summary>
    public sealed class FragmentMergeResult
    {
        public bool Succeeded { get; }
        /// <summary>Merged block body (no markers), or null when not succeeded.</summary>
        public string Body { get; }
        /// <summary>Preflight failure reason, or null on success.</summary>
        public string Error { get; }

        private FragmentMergeResult(bool ok, string body, string error)
        {
            Succeeded = ok;
            Body = body;
            Error = error;
        }

        public static FragmentMergeResult Ok(string body) => new FragmentMergeResult(true, body, null);
        public static FragmentMergeResult Fail(string error) => new FragmentMergeResult(false, null, error);
    }
}
