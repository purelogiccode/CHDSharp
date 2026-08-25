namespace VendoredZSTD.Unsafe;

/* Controls whether the input/output buffer is buffered or stable. */
public enum ZstdBufferModeE
{
    /* Buffer the input/output */
    ZstdBmBuffered = 0,

    /* ZSTD_inBuffer/ZSTD_outBuffer is stable */
    ZstdBmStable = 1
}