using System.Runtime.InteropServices;
namespace ZstdSharp.Unsafe
{
    [StructLayout(LayoutKind.Sequential)]
    public struct FSE_decode_t
    {
        public ushort newState;
        public byte symbol;
        public byte nbBits;
    }
}
