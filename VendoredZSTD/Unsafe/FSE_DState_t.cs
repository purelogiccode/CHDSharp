using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

/* *****************************************
 *  FSE symbol decompression API
 *******************************************/
[StructLayout(LayoutKind.Sequential)]
public unsafe struct FseDStateT
{
    public nuint state;

    /* precise table may vary, depending on U16 */
    public void* table;
}