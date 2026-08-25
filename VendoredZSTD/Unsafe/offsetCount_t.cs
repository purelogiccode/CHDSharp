using System.Runtime.InteropServices;
namespace VendoredZSTD.Unsafe
{
    [StructLayout(LayoutKind.Sequential)]
    public struct offsetCount_t
    {
        public uint offset;
        public uint count;
    }
}
