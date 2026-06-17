using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Yoji.EditorCore.Ports
{
    public interface IPortProbe
    {
        bool IsAvailable(int port);
    }

    public sealed class TcpPortProbe : IPortProbe
    {
        public bool IsAvailable(int port)
        {
            PortRangeValidator.ValidatePort(port);

            TcpListener listener = null;
            try
            {
                listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
                return true;
            }
            catch (SocketException)
            {
                return false;
            }
            finally
            {
                if (listener != null)
                    listener.Stop();
            }
        }
    }

    public static class ServicePortAllocator
    {
        public static ServicePortAssignment Allocate(ServicePortDefinition definition, ServicePortPolicy policy, IPortProbe probe)
        {
            ServicePortAssignment assignment;
            string error;
            if (!TryAllocate(definition, policy, probe, out assignment, out error))
                throw new InvalidOperationException(error);
            return assignment;
        }

        public static bool TryAllocate(
            ServicePortDefinition definition,
            ServicePortPolicy policy,
            IPortProbe probe,
            out ServicePortAssignment assignment,
            out string error)
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));
            if (policy == null)
                throw new ArgumentNullException(nameof(policy));
            if (probe == null)
                throw new ArgumentNullException(nameof(probe));

            assignment = null;
            error = null;

            if (policy.OverrideBasePort < 0)
            {
                error = "invalid override base port: " + policy.OverrideBasePort + " (must be 0 or a valid base port)";
                return false;
            }

            if (policy.PreferredBasePort < 0)
            {
                error = "invalid preferred base port: " + policy.PreferredBasePort + " (must be 0 or a valid base port)";
                return false;
            }

            if (policy.OverrideBasePort > 0)
            {
                Candidate candidate;
                if (!TryBuildBaseCandidate(policy.OverrideBasePort, definition.Offset, "user-override", out candidate, out error))
                    return false;

                if (!probe.IsAvailable(candidate.Port))
                {
                    error = "configured override port is occupied: " + candidate.Port;
                    return false;
                }

                return Assign(definition, policy, candidate, out assignment);
            }

            if (policy.Mode == ServicePortMode.FixedProject)
            {
                if (policy.PreferredBasePort <= 0)
                {
                    error = "PreferredBasePort is required for fixed project mode";
                    return false;
                }

                Candidate candidate;
                if (!TryBuildBaseCandidate(policy.PreferredBasePort, definition.Offset, "fixed-project", out candidate, out error))
                    return false;

                if (!probe.IsAvailable(candidate.Port))
                {
                    error = "configured port is occupied: " + candidate.Port;
                    return false;
                }

                return Assign(definition, policy, candidate, out assignment);
            }

            Candidate preferredCandidate = default(Candidate);
            var hasPreferredCandidate = false;
            if (policy.PreferredBasePort > 0)
            {
                if (!TryBuildBaseCandidate(policy.PreferredBasePort, definition.Offset, "project-auto", out preferredCandidate, out error))
                    return false;

                hasPreferredCandidate = true;
            }

            if (policy.PreferLegacyPorts)
            {
                foreach (var port in definition.LegacyPorts)
                {
                    if (!TryBuildLegacyCandidate(port, out var candidate, out error))
                        return false;

                    if (probe.IsAvailable(candidate.Port))
                        return Assign(definition, policy, candidate, out assignment);
                }
            }

            if (hasPreferredCandidate && probe.IsAvailable(preferredCandidate.Port))
                return Assign(definition, policy, preferredCandidate, out assignment);

            foreach (var basePort in BuildProjectBasePorts(policy))
            {
                Candidate candidate;
                if (!TryBuildBaseCandidate(basePort, definition.Offset, "project-auto", out candidate, out error))
                    return false;

                if (probe.IsAvailable(candidate.Port))
                    return Assign(definition, policy, candidate, out assignment);
            }

            error = "no available port for " + definition.ServiceId;
            return false;
        }

        private static bool Assign(ServicePortDefinition definition, ServicePortPolicy policy, Candidate candidate, out ServicePortAssignment assignment)
        {
            assignment = new ServicePortAssignment
            {
                ServiceId = definition.ServiceId,
                DisplayName = definition.DisplayName,
                Host = "127.0.0.1",
                Port = candidate.Port,
                Source = candidate.Source,
                ProjectRoot = policy.ProjectRoot,
                ProjectId = policy.ProjectId,
            };
            return true;
        }

        private static bool TryBuildBaseCandidate(int basePort, int offset, string source, out Candidate candidate, out string error)
        {
            candidate = default(Candidate);
            error = null;

            if (!TryValidateBasePort(basePort, out error))
                return false;
            if (!TryValidateOffset(offset, out error))
                return false;

            var finalPort = (long)basePort + offset;
            if (finalPort < PortRangeValidator.MinPort || finalPort > PortRangeValidator.MaxPort)
            {
                error = "final port out of range: " + basePort + " + " + offset + " = " + finalPort;
                return false;
            }

            candidate = new Candidate((int)finalPort, source);
            return true;
        }

        private static bool TryBuildLegacyCandidate(int port, out Candidate candidate, out string error)
        {
            candidate = default(Candidate);
            error = null;

            if (!TryValidatePort(port, out error))
                return false;

            candidate = new Candidate(port, "legacy");
            return true;
        }

        private static bool TryValidatePort(int port, out string error)
        {
            error = null;
            if (port < PortRangeValidator.MinPort || port > PortRangeValidator.MaxPort)
            {
                error = "invalid port: " + port + " (must be in 1024..65535)";
                return false;
            }

            return true;
        }

        private static bool TryValidateBasePort(int basePort, out string error)
        {
            if (!TryValidatePort(basePort, out error))
                return false;

            if (basePort % PortRangeValidator.BaseStep != 0)
            {
                error = "invalid base port: " + basePort + " (must be aligned to step 10)";
                return false;
            }

            return true;
        }

        private static bool TryValidateOffset(int offset, out string error)
        {
            error = null;
            if (offset < 0 || offset >= PortRangeValidator.BaseStep)
            {
                error = "invalid offset: " + offset + " (must be in 0..9)";
                return false;
            }

            return true;
        }

        private static IEnumerable<int> BuildProjectBasePorts(ServicePortPolicy policy)
        {
            var slotCount = ((PortRangeValidator.DefaultBaseMax - PortRangeValidator.DefaultBaseMin) / PortRangeValidator.BaseStep) + 1;
            var startSlot = StableSlot(policy.ProjectId, policy.ProjectRoot, slotCount);

            for (var i = 0; i < slotCount; i++)
            {
                var slot = (startSlot + i) % slotCount;
                yield return PortRangeValidator.DefaultBaseMin + slot * PortRangeValidator.BaseStep;
            }
        }

        private static int StableSlot(string projectId, string projectRoot, int slotCount)
        {
            var key = !string.IsNullOrEmpty(projectId) ? projectId : (projectRoot ?? string.Empty);
            if (string.IsNullOrEmpty(key))
                key = "default";

            unchecked
            {
                const uint fnvOffset = 2166136261;
                const uint fnvPrime = 16777619;
                var hash = fnvOffset;
                var bytes = Encoding.UTF8.GetBytes(key.ToLowerInvariant());
                for (var i = 0; i < bytes.Length; i++)
                {
                    hash ^= bytes[i];
                    hash *= fnvPrime;
                }

                return (int)(hash % (uint)slotCount);
            }
        }

        private struct Candidate
        {
            public readonly int Port;
            public readonly string Source;

            public Candidate(int port, string source)
            {
                Port = port;
                Source = source;
            }
        }
    }
}
