using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe
{
    [StructLayout(LayoutKind.Sequential)]
    public struct ZSTD_bounds
    {
        public nuint error;
        public int lowerBound;
        public int upperBound;
    }
}