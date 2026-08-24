using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

[StructLayout(LayoutKind.Sequential)]
public unsafe struct HUF_CTableHeader
{
    public byte tableLog;
    public byte maxSymbolValue;
    public fixed byte unused[6];
}