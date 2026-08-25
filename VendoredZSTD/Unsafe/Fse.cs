using System.Runtime.CompilerServices;
using InlineMethod;
using static VendoredZSTD.UnsafeHelper;

namespace VendoredZSTD.Unsafe;

public static unsafe partial class Methods
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void FSE_initCState(FseCStateT* statePtr, uint* ct)
    {
        void* ptr = ct;
        var u16Ptr = (ushort*)ptr;
        uint tableLog = MEM_read16(ptr);
        statePtr->value = (nint)1 << (int)tableLog;
        statePtr->stateTable = u16Ptr + 2;
        statePtr->symbolTT = ct + 1 + (tableLog != 0 ? 1 << (int)(tableLog - 1) : 1);
        statePtr->stateLog = tableLog;
    }

    /*! FSE_initCState2() :
     *   Same as FSE_initCState(), but the first symbol to include (which will be the last to be read)
     *   uses the smallest state value possible, saving the cost of this symbol */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void FSE_initCState2(FseCStateT* statePtr, uint* ct, uint symbol)
    {
        FSE_initCState(statePtr, ct);
        {
            var symbolTt = ((FseSymbolCompressionTransform*)statePtr->symbolTT)[symbol];
            var stateTable = (ushort*)statePtr->stateTable;
            var nbBitsOut = (symbolTt.deltaNbBits + (1 << 15)) >> 16;
            statePtr->value = (nint)((nbBitsOut << 16) - symbolTt.deltaNbBits);
            statePtr->value = stateTable[
                (statePtr->value >> (int)nbBitsOut) + symbolTt.deltaFindState
            ];
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void FSE_encodeSymbol(BitCStreamT* bitC, FseCStateT* statePtr, uint symbol)
    {
        var symbolTt = ((FseSymbolCompressionTransform*)statePtr->symbolTT)[symbol];
        var stateTable = (ushort*)statePtr->stateTable;
        var nbBitsOut = ((uint)statePtr->value + symbolTt.deltaNbBits) >> 16;
        BIT_addBits(bitC, (nuint)statePtr->value, nbBitsOut);
        statePtr->value = stateTable[(statePtr->value >> (int)nbBitsOut) + symbolTt.deltaFindState];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void FSE_flushCState(BitCStreamT* bitC, FseCStateT* statePtr)
    {
        BIT_addBits(bitC, (nuint)statePtr->value, statePtr->stateLog);
        BIT_flushBits(bitC);
    }

    /* FSE_getMaxNbBits() :
     * Approximate maximum cost of a symbol, in bits.
     * Fractional get rounded up (i.e. a symbol with a normalized frequency of 3 gives the same result as a frequency of 2)
     * note 1 : assume symbolValue is valid (<= maxSymbolValue)
     * note 2 : if freq[symbolValue]==0, @return a fake cost of tableLog+1 bits */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint FSE_getMaxNbBits(void* symbolTtPtr, uint symbolValue)
    {
        var symbolTt = (FseSymbolCompressionTransform*)symbolTtPtr;
        return (symbolTt[symbolValue].deltaNbBits + ((1 << 16) - 1)) >> 16;
    }

    /* FSE_bitCost() :
     * Approximate symbol cost, as fractional value, using fixed-point format (accuracyLog fractional bits)
     * note 1 : assume symbolValue is valid (<= maxSymbolValue)
     * note 2 : if freq[symbolValue]==0, @return a fake cost of tableLog+1 bits */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint FSE_bitCost(
        void* symbolTtPtr,
        uint tableLog,
        uint symbolValue,
        uint accuracyLog
    )
    {
        var symbolTt = (FseSymbolCompressionTransform*)symbolTtPtr;
        var minNbBits = symbolTt[symbolValue].deltaNbBits >> 16;
        var threshold = (minNbBits + 1) << 16;
        assert(tableLog < 16);
        assert(accuracyLog < 31 - tableLog);
        {
            var tableSize = (uint)(1 << (int)tableLog);
            var deltaFromThreshold = threshold - (symbolTt[symbolValue].deltaNbBits + tableSize);
            /* linear interpolation (very approximate) */
            var normalizedDeltaFromThreshold =
                (deltaFromThreshold << (int)accuracyLog) >> (int)tableLog;
            var bitMultiplier = (uint)(1 << (int)accuracyLog);
            assert(symbolTt[symbolValue].deltaNbBits + tableSize <= threshold);
            assert(normalizedDeltaFromThreshold <= bitMultiplier);
            return (minNbBits + 1) * bitMultiplier - normalizedDeltaFromThreshold;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void FSE_initDState(FseDStateT* dStatePtr, BitDStreamT* bitD, uint* dt)
    {
        void* ptr = dt;
        var dTableH = (FseDTableHeader*)ptr;
        dStatePtr->state = BIT_readBits(bitD, dTableH->tableLog);
        BIT_reloadDStream(bitD);
        dStatePtr->table = dt + 1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte FSE_peekSymbol(FseDStateT* dStatePtr)
    {
        var dInfo = ((FseDecodeT*)dStatePtr->table)[dStatePtr->state];
        return dInfo.symbol;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void FSE_updateState(FseDStateT* dStatePtr, BitDStreamT* bitD)
    {
        var dInfo = ((FseDecodeT*)dStatePtr->table)[dStatePtr->state];
        uint nbBits = dInfo.nbBits;
        var lowBits = BIT_readBits(bitD, nbBits);
        dStatePtr->state = dInfo.newState + lowBits;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [Inline]
    private static byte FSE_decodeSymbol(FseDStateT* dStatePtr, BitDStreamT* bitD)
    {
        var dInfo = ((FseDecodeT*)dStatePtr->table)[dStatePtr->state];
        uint nbBits = dInfo.nbBits;
        var symbol = dInfo.symbol;
        var lowBits = BIT_readBits(bitD, nbBits);
        dStatePtr->state = dInfo.newState + lowBits;
        return symbol;
    }

    /*! FSE_decodeSymbolFast() :
    unsafe, only works if no symbol has a probability > 50% */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte FSE_decodeSymbolFast(FseDStateT* dStatePtr, BitDStreamT* bitD)
    {
        var dInfo = ((FseDecodeT*)dStatePtr->table)[dStatePtr->state];
        uint nbBits = dInfo.nbBits;
        var symbol = dInfo.symbol;
        var lowBits = BIT_readBitsFast(bitD, nbBits);
        dStatePtr->state = dInfo.newState + lowBits;
        return symbol;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint FSE_endOfDState(FseDStateT* dStatePtr)
    {
        return dStatePtr->state == 0 ? 1U : 0U;
    }
}