using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe
{
    /****************************
     *  Streaming
     ****************************/
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct ZSTD_inBuffer_s
    {
        /**< start of input buffer */
        public void* src;

        /**< size of input buffer */
        public nuint size;

        /**< position where reading stopped. Will be updated. Necessarily 0 <= pos <= size */
        public nuint pos;
    }
}
