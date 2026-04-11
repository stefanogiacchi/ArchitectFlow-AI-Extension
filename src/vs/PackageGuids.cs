using System;

namespace ArchitectFlow_AI
{
    internal static class PackageGuids
    {
        public const string PackageGuidString = "a1b2c3d4-e5f6-7890-abcd-ef1234567890";
        public const string CommandSetGuidString = "b2c3d4e5-f6a7-8901-bcde-f12345678901";

        public static readonly Guid Package = new Guid(PackageGuidString);
        public static readonly Guid CommandSet = new Guid(CommandSetGuidString);
    }
}
