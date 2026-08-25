using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe
{
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct ZSTD_customMem
    {
        public void* customAlloc;
        public void* customFree;
        public void* opaque;

        public ZSTD_customMem(void* customAlloc, void* customFree, void* opaque)
        {
            this.customAlloc = customAlloc;
            this.customFree = customFree;
            this.opaque = opaque;
        }
    }
}