using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

[StructLayout(LayoutKind.Sequential)]
public unsafe struct ZSTD_fseCTables_t
{
    public fixed uint offcodeCTable[193];
    public fixed uint matchlengthCTable[363];
    public fixed uint litlengthCTable[329];
    public FseRepeat offcode_repeatMode;
    public FseRepeat matchlength_repeatMode;
    public FseRepeat litlength_repeatMode;
}