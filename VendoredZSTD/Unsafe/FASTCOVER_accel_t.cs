using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

/*-*************************************
 * Acceleration
 ***************************************/
[StructLayout(LayoutKind.Sequential)]
public struct FastcoverAccelT
{
    /* Percentage of training samples used for ZDICT_finalizeDictionary */
    public uint finalize;

    /* Number of dmer skipped between each dmer counted in computeFrequency */
    public uint skip;

    public FastcoverAccelT(uint finalize, uint skip)
    {
        this.finalize = finalize;
        this.skip = skip;
    }
}