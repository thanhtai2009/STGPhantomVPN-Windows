namespace STGPhantomVPN.Core
{
    /// <summary>Maps the native machine type to the architecture names of the OpenVPN MSIs.</summary>
    public static class MachineArch
    {
        public const ushort ImageFileMachineI386 = 0x014C;
        public const ushort ImageFileMachineAmd64 = 0x8664;
        public const ushort ImageFileMachineArm64 = 0xAA64;

        /// <summary>"amd64", "arm64" or "x86" for an IMAGE_FILE_MACHINE_* value; null for anything else.</summary>
        public static string FromImageFileMachine(ushort machine)
        {
            switch (machine)
            {
                case ImageFileMachineAmd64: return "amd64";
                case ImageFileMachineArm64: return "arm64";
                case ImageFileMachineI386: return "x86";
                default: return null;
            }
        }
    }
}
