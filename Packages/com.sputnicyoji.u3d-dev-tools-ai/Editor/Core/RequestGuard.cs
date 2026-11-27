using System;
using System.Collections.Generic;

namespace Yoji.EditorCore
{
    /// Browser-CSRF guard shared by the loopback HTTP services. Pure function so each service keeps its own
    /// response envelope; callers pass the same path string their router switches on.
    public static class RequestGuard
    {
        public const string CrossOriginRejected = "cross-origin request rejected";

        /// Returns null when the request may proceed, otherwise the rejection reason.
        public static string Check(string method, string path, string origin, ICollection<string> mutatingRoutes)
        {
            // Browsers send Origin on every cross-site non-GET request (sometimes the literal "null"); local CLI clients send none.
            if (!string.IsNullOrEmpty(origin))
                return CrossOriginRejected;
            // <img>/navigation GETs carry no Origin, so state-changing routes must refuse anything but POST.
            if (mutatingRoutes != null && mutatingRoutes.Contains(path) && !string.Equals(method, "POST", StringComparison.Ordinal))
                return path + " requires POST";
            return null;
        }
    }
}
