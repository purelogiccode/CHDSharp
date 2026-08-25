namespace VendoredZSTD.Unsafe;

public enum BitDStreamStatus
{
    BitDStreamUnfinished = 0,
    BitDStreamEndOfBuffer = 1,
    BitDStreamCompleted = 2,

    /* result of BIT_reloadDStream() */
    BitDStreamOverflow = 3
}