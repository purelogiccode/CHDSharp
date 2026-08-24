using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

/****************************
 *  Streaming
 ****************************/
[StructLayout(LayoutKind.Sequential)]
public unsafe struct ZstdInBufferS
{
    /**< start of input buffer */
    public void* src;
    /**< size of input buffer */
    public nuint size;
    /**< position where reading stopped. Will be updated. Necessarily 0 <= pos <= size */
    public nuint pos;
}