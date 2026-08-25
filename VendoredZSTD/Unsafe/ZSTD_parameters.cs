using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe
{
    [StructLayout(LayoutKind.Sequential)]
    public struct ZSTD_parameters
    {
        public ZSTD_compressionParameters cParams;
        public ZSTD_frameParameters fParams;
    }
}