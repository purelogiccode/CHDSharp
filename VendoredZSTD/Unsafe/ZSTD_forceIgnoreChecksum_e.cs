namespace VendoredZSTD.Unsafe;

public enum ZstdForceIgnoreChecksumE
{
    /* Note: this enum controls ZSTD_d_forceIgnoreChecksum */
    ZstdDValidateChecksum = 0,

    /* Note: this enum controls ZSTD_d_forceIgnoreChecksum */
    ZstdDIgnoreChecksum = 1
}