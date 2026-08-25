using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

/*
 * Parameters for FASTCOVER_tryParameters().
 */
[StructLayout(LayoutKind.Sequential)]
public unsafe struct FastcoverTryParametersDataS
{
    public FastcoverCtxT* ctx;
    public CoverBestS* best;
    public nuint dictBufferCapacity;
    public ZdictCoverParamsT parameters;
}