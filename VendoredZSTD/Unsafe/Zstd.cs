namespace VendoredZSTD.Unsafe;

public static unsafe partial class Methods
{
    private static readonly ZstdCustomMem ZstdDefaultCMem = new(customAlloc: null, customFree: null, opaque: null);
}