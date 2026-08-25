using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

[StructLayout(LayoutKind.Sequential)]
public unsafe struct HufWriteCTableWksp
{
    public HufCompressWeightsWksp wksp;

    /* precomputed conversion table */
    public fixed byte bitsToWeight[13];
    public fixed byte huffWeight[255];
}