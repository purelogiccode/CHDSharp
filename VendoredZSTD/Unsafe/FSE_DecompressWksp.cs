using System.Runtime.InteropServices;
namespace ZstdSharp.Unsafe
{
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct FSE_DecompressWksp
    {
        public fixed short ncount[256];
    }
}
