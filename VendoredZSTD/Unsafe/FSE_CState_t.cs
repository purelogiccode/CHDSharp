using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

/* *****************************************
 *  FSE symbol compression API
 *******************************************/
/*!
This API consists of small unitary functions, which highly benefit from being inlined.
Hence their body are included in next section.
 */
[StructLayout(LayoutKind.Sequential)]
public unsafe struct FseCStateT
{
    public nint value;
    public void* stateTable;
    public void* symbolTT;
    public uint stateLog;
}