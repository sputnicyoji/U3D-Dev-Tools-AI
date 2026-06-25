using System.Collections.Generic;
using System.Linq;

namespace Yoji.U3DAILinker.Registry
{
    public static class TopologicalSorter
    {
        public static IReadOnlyList<RegistryEntry> Sort(IReadOnlyList<RegistryEntry> entries)
        {
            var byId = new Dictionary<string, RegistryEntry>();
            foreach (var e in entries)
            {
                byId[e.Id] = e;
            }

            foreach (var e in entries)
            {
                if (ToolKindExtensions.TryParse(e.Kind, out var kind) && kind == ToolKind.Infra && e.DefaultEnabled)
                {
                    throw new TopologicalSortException(
                        "Infra entry '" + e.Id + "' must not be directly enabled (defaultEnabled=true); infra is installed only as a dependency.");
                }

                foreach (var dep in e.DependsOn ?? new string[0])
                {
                    if (!byId.ContainsKey(dep))
                    {
                        throw new TopologicalSortException(
                            "Entry '" + e.Id + "' depends on unknown id '" + dep + "'.");
                    }
                }
            }

            var inDegree = new Dictionary<string, int>();
            var dependents = new Dictionary<string, List<string>>();
            foreach (var e in entries)
            {
                inDegree[e.Id] = 0;
                dependents[e.Id] = new List<string>();
            }

            foreach (var e in entries)
            {
                foreach (var dep in e.DependsOn ?? new string[0])
                {
                    inDegree[e.Id]++;
                    dependents[dep].Add(e.Id);
                }
            }

            var result = new List<RegistryEntry>();
            var ready = new List<string>();
            foreach (var e in entries)
            {
                if (inDegree[e.Id] == 0)
                {
                    ready.Add(e.Id);
                }
            }

            while (ready.Count > 0)
            {
                ready.Sort((x, y) =>
                {
                    var cmp = byId[x].Order.CompareTo(byId[y].Order);
                    return cmp != 0 ? cmp : string.CompareOrdinal(x, y);
                });

                var nextId = ready[0];
                ready.RemoveAt(0);
                result.Add(byId[nextId]);

                foreach (var dependent in dependents[nextId])
                {
                    inDegree[dependent]--;
                    if (inDegree[dependent] == 0)
                    {
                        ready.Add(dependent);
                    }
                }
            }

            if (result.Count != entries.Count)
            {
                var remaining = entries.Select(e => e.Id).Where(id => !result.Any(r => r.Id == id));
                throw new TopologicalSortException(
                    "Dependency cycle detected among entries: " + string.Join(", ", remaining) + ".");
            }

            return result;
        }
    }
}
