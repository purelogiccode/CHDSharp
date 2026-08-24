using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

/**
 * Parameters for FASTCOVER_tryParameters().
 */
[StructLayout(LayoutKind.Sequential)]
public unsafe struct FastcoverTryParametersDataS
{
    public FASTCOVER_ctx_t* ctx;
    public CoverBestS* best;
    public nuint dictBufferCapacity;
    public ZDICT_cover_params_t parameters;
}