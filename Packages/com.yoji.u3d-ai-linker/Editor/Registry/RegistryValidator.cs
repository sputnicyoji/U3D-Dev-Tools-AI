using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Yoji.U3DAILinker.Registry
{
    public static class RegistryValidator
    {
        public const string RequiredPackagePrefix = "com.yoji.";
        private static readonly Regex SemverRevisionTail = new Regex(@"^v\d+\.\d+\.\d+$");
        private static readonly Regex FullSha = new Regex(@"^[0-9a-f]{40}$");

        public static void Validate(RegistryDocument doc, RegistryChannel channel)
        {
            var errors = new List<string>();

            if (doc == null)
            {
                errors.Add("Registry document is null.");
                throw new RegistryValidationException(errors);
            }

            if (channel == RegistryChannel.Dev && doc.Branch != null && doc.Branch != "main")
            {
                errors.Add("Dev registry branch must be 'main' but was '" + doc.Branch + "'.");
            }

            var seenIds = new HashSet<string>();
            var seenPackageNames = new HashSet<string>();

            var entries = doc.Entries ?? new RegistryEntry[0];
            foreach (var e in entries)
            {
                var label = e != null && !string.IsNullOrEmpty(e.Id) ? e.Id : "<missing-id>";

                if (e == null)
                {
                    errors.Add("Registry contains a null entry.");
                    continue;
                }

                if (string.IsNullOrEmpty(e.Id))
                {
                    errors.Add("Entry has empty id.");
                }
                else if (!seenIds.Add(e.Id))
                {
                    errors.Add("Duplicate id '" + e.Id + "'.");
                }

                if (!ToolStatusExtensions.TryParse(e.Status, out _))
                {
                    errors.Add("Entry '" + label + "' has unknown status '" + e.Status + "'.");
                }

                if (!ToolKindExtensions.TryParse(e.Kind, out _))
                {
                    errors.Add("Entry '" + label + "' has unknown kind '" + e.Kind + "'.");
                }

                if (string.IsNullOrEmpty(e.PackageName))
                {
                    errors.Add("Entry '" + label + "' has empty packageName.");
                }
                else
                {
                    if (!e.PackageName.StartsWith(RequiredPackagePrefix))
                    {
                        errors.Add("Entry '" + label + "' packageName '" + e.PackageName + "' must start with '" + RequiredPackagePrefix + "'.");
                    }

                    if (!seenPackageNames.Add(e.PackageName))
                    {
                        errors.Add("Duplicate packageName '" + e.PackageName + "'.");
                    }

                    ValidatePackagePath(e, label, errors);
                }

                ValidateRevision(e, label, channel, errors);

                if (string.IsNullOrEmpty(e.MinUnity))
                {
                    errors.Add("Entry '" + label + "' is missing required minUnity.");
                }
            }

            if (errors.Count > 0)
            {
                throw new RegistryValidationException(errors);
            }
        }

        private static void ValidatePackagePath(RegistryEntry e, string label, List<string> errors)
        {
            var path = e.PackagePath;
            if (string.IsNullOrEmpty(path))
            {
                errors.Add("Entry '" + label + "' has empty packagePath.");
                return;
            }

            if (path.Contains(".."))
            {
                errors.Add("Entry '" + label + "' packagePath '" + path + "' must not contain '..'.");
            }

            if (path.Contains("://") || path.StartsWith("/") || (path.Length >= 2 && path[1] == ':'))
            {
                errors.Add("Entry '" + label + "' packagePath '" + path + "' must be a relative 'Packages/<name>' path, not an absolute path or URL.");
            }

            var expected = "Packages/" + e.PackageName;
            if (path != expected)
            {
                errors.Add("Entry '" + label + "' packagePath '" + path + "' must equal '" + expected + "'.");
            }
        }

        private static void ValidateRevision(RegistryEntry e, string label, RegistryChannel channel, List<string> errors)
        {
            var rev = e.Revision;
            if (string.IsNullOrEmpty(rev))
            {
                errors.Add("Entry '" + label + "' has empty revision.");
                return;
            }

            if (channel == RegistryChannel.Stable)
            {
                var prefix = (e.Id ?? string.Empty) + "-";
                if (!rev.StartsWith(prefix))
                {
                    errors.Add("Entry '" + label + "' stable revision '" + rev + "' must start with '" + prefix + "'.");
                    return;
                }

                var tail = rev.Substring(prefix.Length);
                if (!SemverRevisionTail.IsMatch(tail))
                {
                    errors.Add("Entry '" + label + "' stable revision '" + rev + "' must match '<id>-v<major>.<minor>.<patch>'.");
                }
            }
            else
            {
                if (!FullSha.IsMatch(rev))
                {
                    errors.Add("Entry '" + label + "' dev revision '" + rev + "' must be a 40-character lowercase Git commit SHA.");
                }
            }
        }
    }
}
