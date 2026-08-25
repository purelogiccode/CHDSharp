using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe
{
    /* ======    Decompression    ====== */
    [StructLayout(LayoutKind.Sequential)]
    public struct FSE_DTableHeader
    {
        public ushort tableLog;
        public ushort fastMode;
    }
}
