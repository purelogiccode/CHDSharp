namespace VendoredZSTD.Unsafe;

public enum ZstdLiteralCompressionModeE
{
    /**< Automatically determine the compression mode based on the compression level.
     *   Negative compression levels will be uncompressed, and positive compression
     *   levels will be compressed. */
    ZstdLcmAuto = 0,

    /**< Always attempt Huffman compression. Uncompressed literals will still be
     *   emitted if Huffman compression is not profitable. */
    ZstdLcmHuffman = 1,

    /**< Always emit uncompressed literals. */
    ZstdLcmUncompressed = 2
}