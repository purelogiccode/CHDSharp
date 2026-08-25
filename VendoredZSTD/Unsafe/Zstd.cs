namespace VendoredZSTD.Unsafe;

public static unsafe partial class Methods
{
    private static readonly ZSTD_customMem ZSTD_defaultCMem = new(
        null,
        null,
        null
    );
}