using System;

namespace Yoji.EditorCore.Ports
{
    public sealed class ServicePortDefinition
    {
        private int[] m_LegacyPorts;

        public string ServiceId { get; private set; }
        public string DisplayName { get; private set; }
        public int Offset { get; private set; }
        public int[] LegacyPorts
        {
            get { return (int[])m_LegacyPorts.Clone(); }
            private set { m_LegacyPorts = value ?? new int[0]; }
        }

        public static ServicePortDefinition Create(string serviceId, string displayName, int offset, int[] legacyPorts)
        {
            if (string.IsNullOrEmpty(serviceId))
                throw new ArgumentException("serviceId is required", nameof(serviceId));
            if (string.IsNullOrEmpty(displayName))
                throw new ArgumentException("displayName is required", nameof(displayName));

            PortRangeValidator.ValidateOffset(offset);

            var normalizedLegacyPorts = legacyPorts ?? new int[0];
            for (var i = 0; i < normalizedLegacyPorts.Length; i++)
                PortRangeValidator.ValidatePort(normalizedLegacyPorts[i]);

            return new ServicePortDefinition
            {
                ServiceId = serviceId,
                DisplayName = displayName,
                Offset = offset,
                LegacyPorts = (int[])normalizedLegacyPorts.Clone(),
            };
        }
    }
}
