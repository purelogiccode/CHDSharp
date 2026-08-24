using System.Runtime.InteropServices;
namespace ZstdSharp.Unsafe
{
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct repcodes_s
    {
        public fixed uint rep[3];
    }
}
