using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

[StructLayout(LayoutKind.Sequential)]
public struct ZdictLegacyParamsT
{
    /* 0 means default; larger => select more => larger dictionary */
    public uint selectivityLevel;
    public ZdictParamsT zParams;
}