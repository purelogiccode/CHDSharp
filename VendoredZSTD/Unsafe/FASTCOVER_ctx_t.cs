using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

/*-*************************************
 * Context
 ***************************************/
[StructLayout(LayoutKind.Sequential)]
public unsafe struct FastcoverCtxT
{
    public byte* samples;
    public nuint* offsets;
    public nuint* samplesSizes;
    public nuint nbSamples;
    public nuint nbTrainSamples;
    public nuint nbTestSamples;
    public nuint nbDmers;
    public uint* freqs;
    public uint d;
    public uint f;
    public FastcoverAccelT accelParams;
}