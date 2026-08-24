using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

/*-***************************/
/*  generic DTableDesc       */
/*-***************************/
[StructLayout(LayoutKind.Sequential)]
public struct DTableDesc
{
    public byte maxTableLog;
    public byte tableType;
    public byte tableLog;
    public byte reserved;
}