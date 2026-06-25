using System;

namespace Yoji.EditorCore.Ports
{
    public static class PortRangeValidator
    {
        public const int MinPort = 1024;
        public const int MaxPort = 65535;
        public const int DefaultBaseMin = 21900;
        public const int DefaultBaseMax = 29990;
        public const int BaseStep = 10;

        public static void ValidatePort(int port)
        {
            if (port < MinPort || port > MaxPort)
                throw new ArgumentOutOfRangeException(nameof(port), "port must be in 1024..65535");
        }

        public static void ValidateBasePort(int basePort)
        {
            ValidatePort(basePort);
            if (basePort % BaseStep != 0)
                throw new ArgumentException("basePort must be aligned to step 10", nameof(basePort));
        }

        public static void ValidateOffset(int offset)
        {
            if (offset < 0 || offset >= BaseStep)
                throw new ArgumentOutOfRangeException(nameof(offset), "offset must be in 0..9");
        }

        public static void ValidateProjectBasePort(int basePort)
        {
            ValidateBasePort(basePort);
            if (basePort < DefaultBaseMin || basePort > DefaultBaseMax)
                throw new ArgumentOutOfRangeException(nameof(basePort), "project base port must be in 21900..29990");
        }
    }
}
