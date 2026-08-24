using System.Runtime.InteropServices;
using static VendoredZSTD.UnsafeHelper;

namespace VendoredZSTD.Unsafe;

public static unsafe partial class Methods
{
#if NET7_0_OR_GREATER
    private static ReadOnlySpan<uint> SpanKInverseProbabilityLog256 => new uint[256]
    {
        0,
        2048,
        1792,
        1642,
        1536,
        1453,
        1386,
        1329,
        1280,
        1236,
        1197,
        1162,
        1130,
        1100,
        1073,
        1047,
        1024,
        1001,
        980,
        960,
        941,
        923,
        906,
        889,
        874,
        859,
        844,
        830,
        817,
        804,
        791,
        779,
        768,
        756,
        745,
        734,
        724,
        714,
        704,
        694,
        685,
        676,
        667,
        658,
        650,
        642,
        633,
        626,
        618,
        610,
        603,
        595,
        588,
        581,
        574,
        567,
        561,
        554,
        548,
        542,
        535,
        529,
        523,
        517,
        512,
        506,
        500,
        495,
        489,
        484,
        478,
        473,
        468,
        463,
        458,
        453,
        448,
        443,
        438,
        434,
        429,
        424,
        420,
        415,
        411,
        407,
        402,
        398,
        394,
        390,
        386,
        382,
        377,
        373,
        370,
        366,
        362,
        358,
        354,
        350,
        347,
        343,
        339,
        336,
        332,
        329,
        325,
        322,
        318,
        315,
        311,
        308,
        305,
        302,
        298,
        295,
        292,
        289,
        286,
        282,
        279,
        276,
        273,
        270,
        267,
        264,
        261,
        258,
        256,
        253,
        250,
        247,
        244,
        241,
        239,
        236,
        233,
        230,
        228,
        225,
        222,
        220,
        217,
        215,
        212,
        209,
        207,
        204,
        202,
        199,
        197,
        194,
        192,
        190,
        187,
        185,
        182,
        180,
        178,
        175,
        173,
        171,
        168,
        166,
        164,
        162,
        159,
        157,
        155,
        153,
        151,
        149,
        146,
        144,
        142,
        140,
        138,
        136,
        134,
        132,
        130,
        128,
        126,
        123,
        121,
        119,
        117,
        115,
        114,
        112,
        110,
        108,
        106,
        104,
        102,
        100,
        98,
        96,
        94,
        93,
        91,
        89,
        87,
        85,
        83,
        82,
        80,
        78,
        76,
        74,
        73,
        71,
        69,
        67,
        66,
        64,
        62,
        61,
        59,
        57,
        55,
        54,
        52,
        50,
        49,
        47,
        46,
        44,
        42,
        41,
        39,
        37,
        36,
        34,
        33,
        31,
        30,
        28,
        26,
        25,
        23,
        22,
        20,
        19,
        17,
        16,
        14,
        13,
        11,
        10,
        8,
        7,
        5,
        4,
        2,
        1
    };
    private static uint* KInverseProbabilityLog256 => (uint*)System.Runtime.CompilerServices.Unsafe.AsPointer(ref MemoryMarshal.GetReference(SpanKInverseProbabilityLog256));
#else

        private static readonly uint* kInverseProbabilityLog256 = GetArrayPointer(new uint[256] { 0, 2048, 1792, 1642, 1536, 1453, 1386, 1329, 1280, 1236, 1197, 1162, 1130, 1100, 1073, 1047, 1024, 1001, 980, 960, 941, 923, 906, 889, 874, 859, 844, 830, 817, 804, 791, 779, 768, 756, 745, 734, 724, 714, 704, 694, 685, 676, 667, 658, 650, 642, 633, 626, 618, 610, 603, 595, 588, 581, 574, 567, 561, 554, 548, 542, 535, 529, 523, 517, 512, 506, 500, 495, 489, 484, 478, 473, 468, 463, 458, 453, 448, 443, 438, 434, 429, 424, 420, 415, 411, 407, 402, 398, 394, 390, 386, 382, 377, 373, 370, 366, 362, 358, 354, 350, 347, 343, 339, 336, 332, 329, 325, 322, 318, 315, 311, 308, 305, 302, 298, 295, 292, 289, 286, 282, 279, 276, 273, 270, 267, 264, 261, 258, 256, 253, 250, 247, 244, 241, 239, 236, 233, 230, 228, 225, 222, 220, 217, 215, 212, 209, 207, 204, 202, 199, 197, 194, 192, 190, 187, 185, 182, 180, 178, 175, 173, 171, 168, 166, 164, 162, 159, 157, 155, 153, 151, 149, 146, 144, 142, 140, 138, 136, 134, 132, 130, 128, 126, 123, 121, 119, 117, 115, 114, 112, 110, 108, 106, 104, 102, 100, 98, 96, 94, 93, 91, 89, 87, 85, 83, 82, 80, 78, 76, 74, 73, 71, 69, 67, 66, 64, 62, 61, 59, 57, 55, 54, 52, 50, 49, 47, 46, 44, 42, 41, 39, 37, 36, 34, 33, 31, 30, 28, 26, 25, 23, 22, 20, 19, 17, 16, 14, 13, 11, 10, 8, 7, 5, 4, 2, 1 });
#endif
    private static uint ZSTD_getFSEMaxSymbolValue(uint* ctable)
    {
        void* ptr = ctable;
        var u16Ptr = (ushort*)ptr;
        uint maxSymbolValue = MEM_read16(u16Ptr + 1);
        return maxSymbolValue;
    }

    /**
     * Returns true if we should use ncount=-1 else we should
     * use ncount=1 for low probability symbols instead.
     */
    private static uint ZSTD_useLowProbCount(nuint nbSeq)
    {
        return nbSeq >= 2048 ? 1U : 0U;
    }

    /**
     * Returns the cost in bytes of encoding the normalized count header.
     * Returns an error if any of the helper functions return an error.
     */
    private static nuint ZSTD_NCountCost(uint* count, uint max, nuint nbSeq, uint fseLog)
    {
        var wksp = stackalloc byte[512];
        var norm = stackalloc short[53];
        var tableLog = FSE_optimalTableLog(fseLog, nbSeq, max);
        {
            var errCode = FSE_normalizeCount(norm, tableLog, count, nbSeq, max, ZSTD_useLowProbCount(nbSeq));
            if (ERR_isError(errCode))
            {
                return errCode;
            }
        }

        return FSE_writeNCount(wksp, sizeof(byte) * 512, norm, max, tableLog);
    }

    /**
     * Returns the cost in bits of encoding the distribution described by count
     * using the entropy bound.
     */
    private static nuint ZSTD_entropyCost(uint* count, uint max, nuint total)
    {
        uint cost = 0;
        uint s;
        assert(total > 0);
        for (s = 0; s <= max; ++s)
        {
            var norm = (uint)(256 * count[s] / total);
            if (count[s] != 0 && norm == 0)
            {
                norm = 1;
            }

            assert(count[s] < total);
            cost += count[s] * KInverseProbabilityLog256[norm];
        }

        return cost >> 8;
    }

    /**
     * Returns the cost in bits of encoding the distribution in count using ctable.
     * Returns an error if ctable cannot represent all the symbols in count.
     */
    private static nuint ZSTD_fseBitCost(uint* ctable, uint* count, uint max)
    {
        const uint kAccuracyLog = 8;
        nuint cost = 0;
        uint s;
        FseCStateT cstate;
        FSE_initCState(&cstate, ctable);
        if (ZSTD_getFSEMaxSymbolValue(ctable) < max)
        {
            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorGeneric));
        }

        for (s = 0; s <= max; ++s)
        {
            var tableLog = cstate.stateLog;
            var badCost = (tableLog + 1) << (int)kAccuracyLog;
            var bitCost = FSE_bitCost(cstate.symbolTT, tableLog, s, kAccuracyLog);
            if (count[s] == 0)
                continue;

            if (bitCost >= badCost)
            {
                return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorGeneric));
            }

            cost += (nuint)count[s] * bitCost;
        }

        return cost >> (int)kAccuracyLog;
    }

    /**
     * Returns the cost in bits of encoding the distribution in count using the
     * table described by norm. The max symbol support by norm is assumed >= max.
     * norm must be valid for every symbol with non-zero probability in count.
     */
    private static nuint ZSTD_crossEntropyCost(short* norm, uint accuracyLog, uint* count, uint max)
    {
        var shift = 8 - accuracyLog;
        nuint cost = 0;
        uint s;
        assert(accuracyLog <= 8);
        for (s = 0; s <= max; ++s)
        {
            var normAcc = norm[s] != -1 ? (uint)norm[s] : 1;
            var norm256 = normAcc << (int)shift;
            assert(norm256 > 0);
            assert(norm256 < 256);
            cost += count[s] * KInverseProbabilityLog256[norm256];
        }

        return cost >> 8;
    }

    private static SymbolEncodingTypeE ZSTD_selectEncodingType(FseRepeat* repeatMode, uint* count, uint max, nuint mostFrequent, nuint nbSeq, uint fseLog, uint* prevCTable, short* defaultNorm, uint defaultNormLog, ZstdDefaultPolicyE isDefaultAllowed, ZstdStrategy strategy)
    {
        if (mostFrequent == nbSeq)
        {
            *repeatMode = FseRepeat.FseRepeatNone;
            if (isDefaultAllowed != default && nbSeq <= 2)
            {
                return SymbolEncodingTypeE.SetBasic;
            }

            return SymbolEncodingTypeE.SetRle;
        }

        if (strategy < ZstdStrategy.ZstdLazy)
        {
            if (isDefaultAllowed != default)
            {
                const nuint staticFseNbSeqMax = 1000;
                var mult = (nuint)(10 - strategy);
                const nuint baseLog = 3;
                /* 28-36 for offset, 56-72 for lengths */
                var dynamicFseNbSeqMin = (((nuint)1 << (int)defaultNormLog) * mult) >> (int)baseLog;
                assert(defaultNormLog is >= 5 and <= 6);
                assert(mult is <= 9 and >= 7);
                if (*repeatMode == FseRepeat.FseRepeatValid && nbSeq < staticFseNbSeqMax)
                {
                    return SymbolEncodingTypeE.SetRepeat;
                }

                if (nbSeq < dynamicFseNbSeqMin || mostFrequent < nbSeq >> (int)(defaultNormLog - 1))
                {
                    *repeatMode = FseRepeat.FseRepeatNone;
                    return SymbolEncodingTypeE.SetBasic;
                }
            }
        }
        else
        {
            var basicCost = isDefaultAllowed != default ? ZSTD_crossEntropyCost(defaultNorm, defaultNormLog, count, max) : unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorGeneric));
            var repeatCost = *repeatMode != FseRepeat.FseRepeatNone ? ZSTD_fseBitCost(prevCTable, count, max) : unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorGeneric));
            var nCountCost = ZSTD_NCountCost(count, max, nbSeq, fseLog);
            var compressedCost = (nCountCost << 3) + ZSTD_entropyCost(count, max, nbSeq);
#if DEBUG
            if (isDefaultAllowed != default)
            {
                assert(!ERR_isError(basicCost));
                assert(!(*repeatMode == FseRepeat.FseRepeatValid && ERR_isError(repeatCost)));
            }
#endif

            assert(!ERR_isError(nCountCost));
            assert(compressedCost < unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorMaxCode)));
            if (basicCost <= repeatCost && basicCost <= compressedCost)
            {
                assert(isDefaultAllowed != default);
                *repeatMode = FseRepeat.FseRepeatNone;
                return SymbolEncodingTypeE.SetBasic;
            }

            if (repeatCost <= compressedCost)
            {
                assert(!ERR_isError(repeatCost));
                return SymbolEncodingTypeE.SetRepeat;
            }

            assert(compressedCost < basicCost && compressedCost < repeatCost);
        }

        *repeatMode = FseRepeat.FseRepeatCheck;
        return SymbolEncodingTypeE.SetCompressed;
    }

    private static nuint ZSTD_buildCTable(void* dst, nuint dstCapacity, uint* nextCTable, uint fseLog, SymbolEncodingTypeE type, uint* count, uint max, byte* codeTable, nuint nbSeq, short* defaultNorm, uint defaultNormLog, uint defaultMax, uint* prevCTable, nuint prevCTableSize, void* entropyWorkspace, nuint entropyWorkspaceSize)
    {
        var op = (byte*)dst;
        var oend = op + dstCapacity;
        switch (type)
        {
            case SymbolEncodingTypeE.SetRle:
            {
                var errCode = FSE_buildCTable_rle(nextCTable, (byte)max);
                if (ERR_isError(errCode))
                {
                    return errCode;
                }
            }

                if (dstCapacity == 0)
                {
                    return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorDstSizeTooSmall));
                }

                *op = codeTable[0];
                return 1;
            case SymbolEncodingTypeE.SetRepeat:
                memcpy(nextCTable, prevCTable, (uint)prevCTableSize);
                return 0;
            case SymbolEncodingTypeE.SetBasic:
            {
                /* note : could be pre-calculated */
                var errCode = FSE_buildCTable_wksp(nextCTable, defaultNorm, defaultMax, defaultNormLog, entropyWorkspace, entropyWorkspaceSize);
                if (ERR_isError(errCode))
                {
                    return errCode;
                }
            }

                return 0;
            case SymbolEncodingTypeE.SetCompressed:
            {
                var wksp = (ZstdBuildCTableWksp*)entropyWorkspace;
                var nbSeq1 = nbSeq;
                var tableLog = FSE_optimalTableLog(fseLog, nbSeq, max);
                if (count[codeTable[nbSeq - 1]] > 1)
                {
                    count[codeTable[nbSeq - 1]]--;
                    nbSeq1--;
                }

                assert(nbSeq1 > 1);
                assert(entropyWorkspaceSize >= (nuint)sizeof(ZstdBuildCTableWksp));
                {
                    var errCode = FSE_normalizeCount(wksp->norm, tableLog, count, nbSeq1, max, ZSTD_useLowProbCount(nbSeq1));
                    if (ERR_isError(errCode))
                    {
                        return errCode;
                    }
                }

                assert(oend >= op);
                {
                    /* overflow protected */
                    var nCountSize = FSE_writeNCount(op, (nuint)(oend - op), wksp->norm, max, tableLog);
                    {
                        if (ERR_isError(nCountSize))
                        {
                            return nCountSize;
                        }
                    }

                    {
                        var errCode = FSE_buildCTable_wksp(nextCTable, wksp->norm, max, tableLog, wksp->wksp, sizeof(uint) * 285);
                        if (ERR_isError(errCode))
                        {
                            return errCode;
                        }
                    }

                    return nCountSize;
                }
            }

            default:
                assert(0 != 0);
                return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorGeneric));
        }
    }

    private static nuint ZSTD_encodeSequences_body(void* dst, nuint dstCapacity, uint* cTableMatchLength, byte* mlCodeTable, uint* cTableOffsetBits, byte* ofCodeTable, uint* cTableLitLength, byte* llCodeTable, SeqDefS* sequences, nuint nbSeq, int longOffsets)
    {
        System.Runtime.CompilerServices.Unsafe.SkipInit(out BitCStreamT blockStream);
        System.Runtime.CompilerServices.Unsafe.SkipInit(out FseCStateT stateMatchLength);
        System.Runtime.CompilerServices.Unsafe.SkipInit(out FseCStateT stateOffsetBits);
        System.Runtime.CompilerServices.Unsafe.SkipInit(out FseCStateT stateLitLength);
        if (ERR_isError(BIT_initCStream(ref blockStream, dst, dstCapacity)))
        {
            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorDstSizeTooSmall));
        }

        var blockStreamBitContainer = blockStream.bitContainer;
        var blockStreamBitPos = blockStream.bitPos;
        var blockStreamPtr = blockStream.ptr;
        var blockStreamEndPtr = blockStream.endPtr;
        FSE_initCState2(ref stateMatchLength, cTableMatchLength, mlCodeTable[nbSeq - 1]);
        FSE_initCState2(ref stateOffsetBits, cTableOffsetBits, ofCodeTable[nbSeq - 1]);
        FSE_initCState2(ref stateLitLength, cTableLitLength, llCodeTable[nbSeq - 1]);
        BIT_addBits(ref blockStreamBitContainer, ref blockStreamBitPos, sequences[nbSeq - 1].litLength, LlBits[llCodeTable[nbSeq - 1]]);
        if (MEM_32bits)
            BIT_flushBits(ref blockStreamBitContainer, ref blockStreamBitPos, ref blockStreamPtr, blockStreamEndPtr);
        BIT_addBits(ref blockStreamBitContainer, ref blockStreamBitPos, sequences[nbSeq - 1].mlBase, MlBits[mlCodeTable[nbSeq - 1]]);
        if (MEM_32bits)
            BIT_flushBits(ref blockStreamBitContainer, ref blockStreamBitPos, ref blockStreamPtr, blockStreamEndPtr);
        if (longOffsets != 0)
        {
            uint ofBits = ofCodeTable[nbSeq - 1];
            var extraBits = ofBits - (ofBits < (uint)(MEM_32bits ? 25 : 57) - 1 ? ofBits : (uint)(MEM_32bits ? 25 : 57) - 1);
            if (extraBits != 0)
            {
                BIT_addBits(ref blockStreamBitContainer, ref blockStreamBitPos, sequences[nbSeq - 1].offBase, extraBits);
                BIT_flushBits(ref blockStreamBitContainer, ref blockStreamBitPos, ref blockStreamPtr, blockStreamEndPtr);
            }

            BIT_addBits(ref blockStreamBitContainer, ref blockStreamBitPos, sequences[nbSeq - 1].offBase >> (int)extraBits, ofBits - extraBits);
        }
        else
        {
            BIT_addBits(ref blockStreamBitContainer, ref blockStreamBitPos, sequences[nbSeq - 1].offBase, ofCodeTable[nbSeq - 1]);
        }

        BIT_flushBits(ref blockStreamBitContainer, ref blockStreamBitPos, ref blockStreamPtr, blockStreamEndPtr);
        {
            nuint n;
            for (n = nbSeq - 2; n < nbSeq; n--)
            {
                var llCode = llCodeTable[n];
                var ofCode = ofCodeTable[n];
                var mlCode = mlCodeTable[n];
                uint llBits = LlBits[llCode];
                uint ofBits = ofCode;
                uint mlBits = MlBits[mlCode];
                FSE_encodeSymbol(ref blockStreamBitContainer, ref blockStreamBitPos, ref stateOffsetBits, ofCode);
                FSE_encodeSymbol(ref blockStreamBitContainer, ref blockStreamBitPos, ref stateMatchLength, mlCode);
                if (MEM_32bits)
                    BIT_flushBits(ref blockStreamBitContainer, ref blockStreamBitPos, ref blockStreamPtr, blockStreamEndPtr);
                FSE_encodeSymbol(ref blockStreamBitContainer, ref blockStreamBitPos, ref stateLitLength, llCode);
                if (MEM_32bits || ofBits + mlBits + llBits >= 64 - 7 - (9 + 9 + 8))
                    BIT_flushBits(ref blockStreamBitContainer, ref blockStreamBitPos, ref blockStreamPtr, blockStreamEndPtr);
                BIT_addBits(ref blockStreamBitContainer, ref blockStreamBitPos, sequences[n].litLength, llBits);
                if (MEM_32bits && llBits + mlBits > 24)
                    BIT_flushBits(ref blockStreamBitContainer, ref blockStreamBitPos, ref blockStreamPtr, blockStreamEndPtr);
                BIT_addBits(ref blockStreamBitContainer, ref blockStreamBitPos, sequences[n].mlBase, mlBits);
                if (MEM_32bits || ofBits + mlBits + llBits > 56)
                    BIT_flushBits(ref blockStreamBitContainer, ref blockStreamBitPos, ref blockStreamPtr, blockStreamEndPtr);
                if (longOffsets != 0)
                {
                    var extraBits = ofBits - (ofBits < (uint)(MEM_32bits ? 25 : 57) - 1 ? ofBits : (uint)(MEM_32bits ? 25 : 57) - 1);
                    if (extraBits != 0)
                    {
                        BIT_addBits(ref blockStreamBitContainer, ref blockStreamBitPos, sequences[n].offBase, extraBits);
                        BIT_flushBits(ref blockStreamBitContainer, ref blockStreamBitPos, ref blockStreamPtr, blockStreamEndPtr);
                    }

                    BIT_addBits(ref blockStreamBitContainer, ref blockStreamBitPos, sequences[n].offBase >> (int)extraBits, ofBits - extraBits);
                }
                else
                {
                    BIT_addBits(ref blockStreamBitContainer, ref blockStreamBitPos, sequences[n].offBase, ofBits);
                }

                BIT_flushBits(ref blockStreamBitContainer, ref blockStreamBitPos, ref blockStreamPtr, blockStreamEndPtr);
            }
        }

        FSE_flushCState(ref blockStreamBitContainer, ref blockStreamBitPos, ref blockStreamPtr, blockStreamEndPtr, ref stateMatchLength);
        FSE_flushCState(ref blockStreamBitContainer, ref blockStreamBitPos, ref blockStreamPtr, blockStreamEndPtr, ref stateOffsetBits);
        FSE_flushCState(ref blockStreamBitContainer, ref blockStreamBitPos, ref blockStreamPtr, blockStreamEndPtr, ref stateLitLength);
        {
            var streamSize = BIT_closeCStream(ref blockStreamBitContainer, ref blockStreamBitPos, blockStreamPtr, blockStreamEndPtr, blockStream.startPtr);
            if (streamSize == 0)
            {
                return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorDstSizeTooSmall));
            }

            return streamSize;
        }
    }

    private static nuint ZSTD_encodeSequences_default(void* dst, nuint dstCapacity, uint* cTableMatchLength, byte* mlCodeTable, uint* cTableOffsetBits, byte* ofCodeTable, uint* cTableLitLength, byte* llCodeTable, SeqDefS* sequences, nuint nbSeq, int longOffsets)
    {
        return ZSTD_encodeSequences_body(dst, dstCapacity, cTableMatchLength, mlCodeTable, cTableOffsetBits, ofCodeTable, cTableLitLength, llCodeTable, sequences, nbSeq, longOffsets);
    }

    private static nuint ZSTD_encodeSequences(void* dst, nuint dstCapacity, uint* cTableMatchLength, byte* mlCodeTable, uint* cTableOffsetBits, byte* ofCodeTable, uint* cTableLitLength, byte* llCodeTable, SeqDefS* sequences, nuint nbSeq, int longOffsets, int bmi2)
    {
        return ZSTD_encodeSequences_default(dst, dstCapacity, cTableMatchLength, mlCodeTable, cTableOffsetBits, ofCodeTable, cTableLitLength, llCodeTable, sequences, nbSeq, longOffsets);
    }
}