using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

[StructLayout(LayoutKind.Sequential)]
public unsafe struct HufCTableHeader
{
    public byte tableLog;
    public byte maxSymbolValue;
    public fixed byte unused[6];
}