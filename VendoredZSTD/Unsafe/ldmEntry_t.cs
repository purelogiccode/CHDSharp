using System.Runtime.InteropServices;
namespace ZstdSharp.Unsafe
{
    [StructLayout(LayoutKind.Sequential)]
    public struct ldmEntry_t
    {
        public uint offset;
        public uint checksum;
    }
}
