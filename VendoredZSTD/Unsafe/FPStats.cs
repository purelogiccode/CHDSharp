using System.Runtime.InteropServices;
namespace ZstdSharp.Unsafe
{
    [StructLayout(LayoutKind.Sequential)]
    public struct FPStats
    {
        public Fingerprint pastEvents;
        public Fingerprint newEvents;
    }
}
