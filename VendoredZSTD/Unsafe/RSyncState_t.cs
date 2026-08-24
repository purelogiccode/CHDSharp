using System.Runtime.InteropServices;
namespace ZstdSharp.Unsafe
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RSyncState_t
    {
        public ulong hash;
        public ulong hitMask;
        public ulong primePower;
    }
}
