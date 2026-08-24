namespace VendoredZSTD.Unsafe;

public enum BitDStreamStatus
{
    /* fully refilled */
    BitDStreamUnfinished = 0,

    /* still some bits left in bitstream */
    BitDStreamEndOfBuffer = 1,

    /* bitstream entirely consumed, bit-exact */
    BitDStreamCompleted = 2,

    /* user requested more bits than present in bitstream */
    BitDStreamOverflow = 3
}