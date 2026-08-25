using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

[StructLayout(LayoutKind.Sequential)]
public struct DictItem
{
    public uint pos;
    public uint length;
    public uint savings;
}