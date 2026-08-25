using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

/**
 * Parameters for COVER_tryParameters().
 */
[StructLayout(LayoutKind.Sequential)]
public unsafe struct CoverTryParametersDataS
{
    public CoverCtxT* ctx;
    public CoverBestS* best;
    public nuint dictBufferCapacity;
    public ZdictCoverParamsT parameters;
}