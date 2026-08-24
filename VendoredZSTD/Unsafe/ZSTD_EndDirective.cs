namespace VendoredZSTD.Unsafe;

/*===== Streaming compression functions =====*/
public enum ZstdEndDirective
{
    /* collect more data, encoder decides when to output compressed result, for optimal compression ratio */
    ZstdEContinue = 0,
    /* flush any data provided so far,
     * it creates (at least) one new block, that can be decoded immediately on reception;
     * frame will continue: any future data can still reference previously compressed data, improving compression.
     * note : multithreaded compression will block to flush as much output as possible. */
    ZstdEFlush = 1,
    /* flush any remaining data _and_ close current frame.
     * note that frame is only closed after compressed data is fully flushed (return value == 0).
     * After that point, any additional data starts a new frame.
     * note : each frame is independent (does not reference any content from previous frame).
    : note : multithreaded compression will block to flush as much output as possible. */
    ZstdEEnd = 2
}