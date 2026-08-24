using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static VendoredZSTD.UnsafeHelper;

namespace VendoredZSTD.Unsafe;

public static unsafe partial class Methods
{
    /**
     * Returns the ZSTD_SequenceLength for the given sequences. It handles the decoding of long sequences
     * indicated by longLengthPos and longLengthType, and adds MINMATCH back to matchLength.
     */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ZSTD_SequenceLength ZSTD_getSequenceLength(SeqStore_t* seqStore, SeqDef_s* seq)
    {
        ZSTD_SequenceLength seqLen;
        seqLen.litLength = seq->litLength;
        seqLen.matchLength = (uint)(seq->mlBase + 3);
        if (seqStore->longLengthPos == (uint)(seq - seqStore->sequencesStart))
        {
            if (seqStore->longLengthType == ZSTD_longLengthType_e.ZSTD_llt_literalLength)
            {
                seqLen.litLength += 0x10000;
            }

            if (seqStore->longLengthType == ZSTD_longLengthType_e.ZSTD_llt_matchLength)
            {
                seqLen.matchLength += 0x10000;
            }
        }

        return seqLen;
    }

    private static readonly RawSeqStore_t KNullRawSeqStore = new(seq: null, pos: 0, posInSequence: 0, size: 0, capacity: 0);
#if NET7_0_OR_GREATER
    private static ReadOnlySpan<byte> SpanLlCode => new byte[]
    {
        0,
        1,
        2,
        3,
        4,
        5,
        6,
        7,
        8,
        9,
        10,
        11,
        12,
        13,
        14,
        15,
        16,
        16,
        17,
        17,
        18,
        18,
        19,
        19,
        20,
        20,
        20,
        20,
        21,
        21,
        21,
        21,
        22,
        22,
        22,
        22,
        22,
        22,
        22,
        22,
        23,
        23,
        23,
        23,
        23,
        23,
        23,
        23,
        24,
        24,
        24,
        24,
        24,
        24,
        24,
        24,
        24,
        24,
        24,
        24,
        24,
        24,
        24,
        24
    };

    private static byte* LlCode => (byte*)System.Runtime.CompilerServices.Unsafe.AsPointer(ref MemoryMarshal.GetReference(SpanLlCode));
#else

        private static readonly byte* LL_Code = GetArrayPointer(new byte[64] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 16, 17, 17, 18, 18, 19, 19, 20, 20, 20, 20, 21, 21, 21, 21, 22, 22, 22, 22, 22, 22, 22, 22, 23, 23, 23, 23, 23, 23, 23, 23, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24 });
#endif
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint ZSTD_LLcode(uint litLength)
    {
        const uint llDeltaCode = 19;
        return litLength > 63 ? ZSTD_highbit32(litLength) + llDeltaCode : LlCode[litLength];
    }

#if NET7_0_OR_GREATER
    private static ReadOnlySpan<byte> SpanMlCode => new byte[128]
    {
        0,
        1,
        2,
        3,
        4,
        5,
        6,
        7,
        8,
        9,
        10,
        11,
        12,
        13,
        14,
        15,
        16,
        17,
        18,
        19,
        20,
        21,
        22,
        23,
        24,
        25,
        26,
        27,
        28,
        29,
        30,
        31,
        32,
        32,
        33,
        33,
        34,
        34,
        35,
        35,
        36,
        36,
        36,
        36,
        37,
        37,
        37,
        37,
        38,
        38,
        38,
        38,
        38,
        38,
        38,
        38,
        39,
        39,
        39,
        39,
        39,
        39,
        39,
        39,
        40,
        40,
        40,
        40,
        40,
        40,
        40,
        40,
        40,
        40,
        40,
        40,
        40,
        40,
        40,
        40,
        41,
        41,
        41,
        41,
        41,
        41,
        41,
        41,
        41,
        41,
        41,
        41,
        41,
        41,
        41,
        41,
        42,
        42,
        42,
        42,
        42,
        42,
        42,
        42,
        42,
        42,
        42,
        42,
        42,
        42,
        42,
        42,
        42,
        42,
        42,
        42,
        42,
        42,
        42,
        42,
        42,
        42,
        42,
        42,
        42,
        42,
        42,
        42
    };

    private static byte* MlCode => (byte*)System.Runtime.CompilerServices.Unsafe.AsPointer(ref MemoryMarshal.GetReference(SpanMlCode));
#else

        private static readonly byte* ML_Code = GetArrayPointer(new byte[128] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 32, 33, 33, 34, 34, 35, 35, 36, 36, 36, 36, 37, 37, 37, 37, 38, 38, 38, 38, 38, 38, 38, 38, 39, 39, 39, 39, 39, 39, 39, 39, 40, 40, 40, 40, 40, 40, 40, 40, 40, 40, 40, 40, 40, 40, 40, 40, 41, 41, 41, 41, 41, 41, 41, 41, 41, 41, 41, 41, 41, 41, 41, 41, 42, 42, 42, 42, 42, 42, 42, 42, 42, 42, 42, 42, 42, 42, 42, 42, 42, 42, 42, 42, 42, 42, 42, 42, 42, 42, 42, 42, 42, 42, 42, 42 });
#endif
    /* ZSTD_MLcode() :
     * note : mlBase = matchLength - MINMATCH;
     *        because it's the format it's stored in seqStore->sequences */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint ZSTD_MLcode(uint mlBase)
    {
        const uint mlDeltaCode = 36;
        return mlBase > 127 ? ZSTD_highbit32(mlBase) + mlDeltaCode : MlCode[mlBase];
    }

    /* ZSTD_cParam_withinBounds:
     * @return 1 if value is within cParam bounds,
     * 0 otherwise */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ZSTD_cParam_withinBounds(ZstdCParameter cParam, int value)
    {
        var bounds = ZSTD_cParam_getBounds(cParam);
        if (ERR_isError(bounds.error))
            return 0;
        if (value < bounds.lowerBound)
            return 0;
        if (value > bounds.upperBound)
            return 0;

        return 1;
    }

    /* ZSTD_selectAddr:
     * @return index >= lowLimit ? candidate : backup,
     * tries to force branchless codegen. */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte* ZSTD_selectAddr(uint index, uint lowLimit, byte* candidate, byte* backup)
    {
        return index >= lowLimit ? candidate : backup;
    }

    /* ZSTD_noCompressBlock() :
     * Writes uncompressed block to dst buffer from given src.
     * Returns the size of the block */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static nuint ZSTD_noCompressBlock(void* dst, nuint dstCapacity, void* src, nuint srcSize, uint lastBlock)
    {
        var cBlockHeader24 = lastBlock + ((uint)blockType_e.bt_raw << 1) + (uint)(srcSize << 3);
        if (srcSize + ZSTD_blockHeaderSize > dstCapacity)
        {
            return unchecked((nuint)(-(int)ZSTD_ErrorCode.ZSTD_error_dstSize_tooSmall));
        }

        MEM_writeLE24(dst, cBlockHeader24);
        memcpy((byte*)dst + ZSTD_blockHeaderSize, src, (uint)srcSize);
        return ZSTD_blockHeaderSize + srcSize;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static nuint ZSTD_rleCompressBlock(void* dst, nuint dstCapacity, byte src, nuint srcSize, uint lastBlock)
    {
        var op = (byte*)dst;
        var cBlockHeader = lastBlock + ((uint)blockType_e.bt_rle << 1) + (uint)(srcSize << 3);
        if (dstCapacity < 4)
        {
            return unchecked((nuint)(-(int)ZSTD_ErrorCode.ZSTD_error_dstSize_tooSmall));
        }

        MEM_writeLE24(op, cBlockHeader);
        op[3] = src;
        return 4;
    }

    /* ZSTD_minGain() :
     * minimum compression required
     * to generate a compress block or a compressed literals section.
     * note : use same formula for both situations */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static nuint ZSTD_minGain(nuint srcSize, ZSTD_strategy strat)
    {
        var minlog = strat >= ZSTD_strategy.ZSTD_btultra ? (uint)strat - 1 : 6;
        assert(ZSTD_cParam_withinBounds(ZstdCParameter.ZstdCStrategy, (int)strat) != 0);
        return (srcSize >> (int)minlog) + 2;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ZSTD_literalsCompressionIsDisabled(ZSTD_CCtx_params_s* cctxParams)
    {
        switch (cctxParams->literalCompressionMode)
        {
            case ZSTD_paramSwitch_e.ZSTD_ps_enable:
                return 0;
            case ZSTD_paramSwitch_e.ZSTD_ps_disable:
                return 1;
            default:
                assert(0 != 0);
                goto case ZSTD_paramSwitch_e.ZSTD_ps_auto;
            case ZSTD_paramSwitch_e.ZSTD_ps_auto:
                return cctxParams->cParams.strategy == ZSTD_strategy.ZSTD_fast && cctxParams->cParams.targetLength > 0 ? 1 : 0;
        }
    }

    /*! ZSTD_safecopyLiterals() :
     *  memcpy() function that won't read beyond more than WILDCOPY_OVERLENGTH bytes past ilimit_w.
     *  Only called when the sequence ends past ilimit_w, so it only needs to be optimized for single
     *  large copies.
     */
    private static void ZSTD_safecopyLiterals(byte* op, byte* ip, byte* iend, byte* ilimitW)
    {
        assert(iend > ilimitW);
        if (ip <= ilimitW)
        {
            ZSTD_wildcopy(op, ip, (nint)(ilimitW - ip), ZSTD_overlap_e.ZSTD_no_overlap);
            op += ilimitW - ip;
            ip = ilimitW;
        }

        while (ip < iend)
        {
            *op++ = *ip++;
        }
    }

    /*! ZSTD_storeSeqOnly() :
     *  Store a sequence (litlen, litPtr, offBase and matchLength) into SeqStore_t.
     *  Literals themselves are not copied, but @litPtr is updated.
     *  @offBase : Users should employ macros REPCODE_TO_OFFBASE() and OFFSET_TO_OFFBASE().
     *  @matchLength : must be >= MINMATCH
     */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ZSTD_storeSeqOnly(SeqStore_t* seqStorePtr, nuint litLength, uint offBase, nuint matchLength)
    {
        assert((nuint)(seqStorePtr->sequences - seqStorePtr->sequencesStart) < seqStorePtr->maxNbSeq);
        assert(litLength <= 1 << 17);
        if (litLength > 0xFFFF)
        {
            assert(seqStorePtr->longLengthType == ZSTD_longLengthType_e.ZSTD_llt_none);
            seqStorePtr->longLengthType = ZSTD_longLengthType_e.ZSTD_llt_literalLength;
            seqStorePtr->longLengthPos = (uint)(seqStorePtr->sequences - seqStorePtr->sequencesStart);
        }

        seqStorePtr->sequences[0].litLength = (ushort)litLength;
        seqStorePtr->sequences[0].offBase = offBase;
        assert(matchLength <= 1 << 17);
        assert(matchLength >= 3);
        {
            var mlBase = matchLength - 3;
            if (mlBase > 0xFFFF)
            {
                assert(seqStorePtr->longLengthType == ZSTD_longLengthType_e.ZSTD_llt_none);
                seqStorePtr->longLengthType = ZSTD_longLengthType_e.ZSTD_llt_matchLength;
                seqStorePtr->longLengthPos = (uint)(seqStorePtr->sequences - seqStorePtr->sequencesStart);
            }

            seqStorePtr->sequences[0].mlBase = (ushort)mlBase;
        }

        seqStorePtr->sequences++;
    }

    /*! ZSTD_storeSeq() :
     *  Store a sequence (litlen, litPtr, offBase and matchLength) into SeqStore_t.
     *  @offBase : Users should employ macros REPCODE_TO_OFFBASE() and OFFSET_TO_OFFBASE().
     *  @matchLength : must be >= MINMATCH
     *  Allowed to over-read literals up to litLimit.
     */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [InlineMethod.Inline]
    private static void ZSTD_storeSeq(SeqStore_t* seqStorePtr, nuint litLength, byte* literals, byte* litLimit, uint offBase, nuint matchLength)
    {
        var litLimitW = litLimit - 32;
        var litEnd = literals + litLength;
        assert((nuint)(seqStorePtr->sequences - seqStorePtr->sequencesStart) < seqStorePtr->maxNbSeq);
        assert(seqStorePtr->maxNbLit <= 128 * (1 << 10));
        assert(seqStorePtr->lit + litLength <= seqStorePtr->litStart + seqStorePtr->maxNbLit);
        assert(literals + litLength <= litLimit);
        if (litEnd <= litLimitW)
        {
            ZSTD_copy16(seqStorePtr->lit, literals);
            if (litLength > 16)
            {
                ZSTD_wildcopy(seqStorePtr->lit + 16, literals + 16, (nint)litLength - 16, ZSTD_overlap_e.ZSTD_no_overlap);
            }
        }
        else
        {
            ZSTD_safecopyLiterals(seqStorePtr->lit, literals, litEnd, litLimitW);
        }

        seqStorePtr->lit += litLength;
        ZSTD_storeSeqOnly(seqStorePtr, litLength, offBase, matchLength);
    }

    /* ZSTD_updateRep() :
     * updates in-place @rep (array of repeat offsets)
     * @offBase : sum-type, using numeric representation of ZSTD_storeSeq()
     */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ZSTD_updateRep(uint* rep, uint offBase, uint ll0)
    {
        if (offBase > 3)
        {
            rep[2] = rep[1];
            rep[1] = rep[0];
            assert(offBase > 3);
            rep[0] = offBase - 3;
        }
        else
        {
            assert(offBase is >= 1 and <= 3);
            var repCode = offBase - 1 + ll0;
            if (repCode > 0)
            {
                var currentOffset = repCode == 3 ? rep[0] - 1 : rep[repCode];
                rep[2] = repCode >= 2 ? rep[1] : rep[2];
                rep[1] = rep[0];
                rep[0] = currentOffset;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static repcodes_s ZSTD_newRep(uint* rep, uint offBase, uint ll0)
    {
        repcodes_s newReps;
        memcpy(&newReps, rep, (uint)sizeof(repcodes_s));
        ZSTD_updateRep(newReps.rep, offBase, ll0);
        return newReps;
    }

    /*-*************************************
     *  Match length counter
     ***************************************/
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static nuint ZSTD_count(byte* pIn, byte* pMatch, byte* pInLimit)
    {
        var pStart = pIn;
        var pInLoopLimit = pInLimit - (sizeof(nuint) - 1);
        if (pIn < pInLoopLimit)
        {
            {
                var diff = MEM_readST(pMatch) ^ MEM_readST(pIn);
                if (diff != 0)
                    return ZSTD_NbCommonBytes(diff);
            }

            pIn += sizeof(nuint);
            pMatch += sizeof(nuint);
            while (pIn < pInLoopLimit)
            {
                var diff = MEM_readST(pMatch) ^ MEM_readST(pIn);
                if (diff == 0)
                {
                    pIn += sizeof(nuint);
                    pMatch += sizeof(nuint);
                    continue;
                }

                pIn += ZSTD_NbCommonBytes(diff);
                return (nuint)(pIn - pStart);
            }
        }

        if (MEM_64bits && pIn < pInLimit - 3 && MEM_read32(pMatch) == MEM_read32(pIn))
        {
            pIn += 4;
            pMatch += 4;
        }

        if (pIn < pInLimit - 1 && MEM_read16(pMatch) == MEM_read16(pIn))
        {
            pIn += 2;
            pMatch += 2;
        }

        if (pIn < pInLimit && *pMatch == *pIn)
        {
            pIn++;
        }

        return (nuint)(pIn - pStart);
    }

    /** ZSTD_count_2segments() :
     *  can count match length with `ip` & `match` in 2 different segments.
     *  convention : on reaching mEnd, match count continue starting from iStart
     */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static nuint ZSTD_count_2segments(byte* ip, byte* match, byte* iEnd, byte* mEnd, byte* iStart)
    {
        var vEnd = ip + (mEnd - match) < iEnd ? ip + (mEnd - match) : iEnd;
        var matchLength = ZSTD_count(ip, match, vEnd);
        if (match + matchLength != mEnd)
            return matchLength;

        return matchLength + ZSTD_count(ip + matchLength, iStart, iEnd);
    }

    private const uint Prime3Bytes = 506832829U;
    [InlineMethod.Inline]
    private static uint ZSTD_hash3(uint u, uint h, uint s)
    {
        assert(h <= 32);
        return (((u << (32 - 24)) * Prime3Bytes) ^ s) >> (int)(32 - h);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [InlineMethod.Inline]
    private static nuint ZSTD_hash3Ptr(void* ptr, uint h)
    {
        return ZSTD_hash3(MEM_readLE32(ptr), h, 0);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static nuint ZSTD_hash3PtrS(void* ptr, uint h, uint s)
    {
        return ZSTD_hash3(MEM_readLE32(ptr), h, s);
    }

    private const uint Prime4Bytes = 2654435761U;
    [InlineMethod.Inline]
    private static uint ZSTD_hash4(uint u, uint h, uint s)
    {
        assert(h <= 32);
        return ((u * Prime4Bytes) ^ s) >> (int)(32 - h);
    }

    [InlineMethod.Inline]
    private static nuint ZSTD_hash4Ptr(void* ptr, uint h)
    {
        return ZSTD_hash4(MEM_readLE32(ptr), h, 0);
    }

    [InlineMethod.Inline]
    private static nuint ZSTD_hash4PtrS(void* ptr, uint h, uint s)
    {
        return ZSTD_hash4(MEM_readLE32(ptr), h, s);
    }

    private const ulong Prime5Bytes = 889523592379UL;
    [InlineMethod.Inline]
    private static nuint ZSTD_hash5(ulong u, uint h, ulong s)
    {
        assert(h <= 64);
        return (nuint)((((u << (64 - 40)) * Prime5Bytes) ^ s) >> (int)(64 - h));
    }

    [InlineMethod.Inline]
    private static nuint ZSTD_hash5Ptr(void* p, uint h)
    {
        return ZSTD_hash5(MEM_readLE64(p), h, 0);
    }

    [InlineMethod.Inline]
    private static nuint ZSTD_hash5PtrS(void* p, uint h, ulong s)
    {
        return ZSTD_hash5(MEM_readLE64(p), h, s);
    }

    private const ulong Prime6Bytes = 227718039650203UL;
    [InlineMethod.Inline]
    private static nuint ZSTD_hash6(ulong u, uint h, ulong s)
    {
        assert(h <= 64);
        return (nuint)((((u << (64 - 48)) * Prime6Bytes) ^ s) >> (int)(64 - h));
    }

    [InlineMethod.Inline]
    private static nuint ZSTD_hash6Ptr(void* p, uint h)
    {
        return ZSTD_hash6(MEM_readLE64(p), h, 0);
    }

    [InlineMethod.Inline]
    private static nuint ZSTD_hash6PtrS(void* p, uint h, ulong s)
    {
        return ZSTD_hash6(MEM_readLE64(p), h, s);
    }

    private const ulong Prime7Bytes = 58295818150454627UL;
    [InlineMethod.Inline]
    private static nuint ZSTD_hash7(ulong u, uint h, ulong s)
    {
        assert(h <= 64);
        return (nuint)((((u << (64 - 56)) * Prime7Bytes) ^ s) >> (int)(64 - h));
    }

    [InlineMethod.Inline]
    private static nuint ZSTD_hash7Ptr(void* p, uint h)
    {
        return ZSTD_hash7(MEM_readLE64(p), h, 0);
    }

    [InlineMethod.Inline]
    private static nuint ZSTD_hash7PtrS(void* p, uint h, ulong s)
    {
        return ZSTD_hash7(MEM_readLE64(p), h, s);
    }

    private const ulong Prime8Bytes = 0xCF1BBCDCB7A56463UL;
    [InlineMethod.Inline]
    private static nuint ZSTD_hash8(ulong u, uint h, ulong s)
    {
        assert(h <= 64);
        return (nuint)(((u * Prime8Bytes) ^ s) >> (int)(64 - h));
    }

    [InlineMethod.Inline]
    private static nuint ZSTD_hash8Ptr(void* p, uint h)
    {
        return ZSTD_hash8(MEM_readLE64(p), h, 0);
    }

    [InlineMethod.Inline]
    private static nuint ZSTD_hash8PtrS(void* p, uint h, ulong s)
    {
        return ZSTD_hash8(MEM_readLE64(p), h, s);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static nuint ZSTD_hashPtr(void* p, uint hBits, uint mls)
    {
        assert(hBits <= 32);
        if (mls == 5)
            return ZSTD_hash5Ptr(p, hBits);
        if (mls == 6)
            return ZSTD_hash6Ptr(p, hBits);
        if (mls == 7)
            return ZSTD_hash7Ptr(p, hBits);
        if (mls == 8)
            return ZSTD_hash8Ptr(p, hBits);

        return ZSTD_hash4Ptr(p, hBits);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static nuint ZSTD_hashPtrSalted(void* p, uint hBits, uint mls, ulong hashSalt)
    {
        assert(hBits <= 32);
        if (mls == 5)
            return ZSTD_hash5PtrS(p, hBits, hashSalt);
        if (mls == 6)
            return ZSTD_hash6PtrS(p, hBits, hashSalt);
        if (mls == 7)
            return ZSTD_hash7PtrS(p, hBits, hashSalt);
        if (mls == 8)
            return ZSTD_hash8PtrS(p, hBits, hashSalt);

        return ZSTD_hash4PtrS(p, hBits, (uint)hashSalt);
    }

    /** ZSTD_ipow() :
     * Return base^exponent.
     */
    private static ulong ZSTD_ipow(ulong @base, ulong exponent)
    {
        ulong power = 1;
        while (exponent != 0)
        {
            if ((exponent & 1) != 0)
            {
                power *= @base;
            }

            exponent >>= 1;
            @base *= @base;
        }

        return power;
    }

    /** ZSTD_rollingHash_append() :
     * Add the buffer to the hash value.
     */
    private static ulong ZSTD_rollingHash_append(ulong hash, void* buf, nuint size)
    {
        var istart = (byte*)buf;
        nuint pos;
        for (pos = 0; pos < size; ++pos)
        {
            hash *= Prime8Bytes;
            hash += (ulong)(istart[pos] + 10);
        }

        return hash;
    }

    /** ZSTD_rollingHash_compute() :
     * Compute the rolling hash value of the buffer.
     */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong ZSTD_rollingHash_compute(void* buf, nuint size)
    {
        return ZSTD_rollingHash_append(0, buf, size);
    }

    /** ZSTD_rollingHash_primePower() :
     * Compute the primePower to be passed to ZSTD_rollingHash_rotate() for a hash
     * over a window of length bytes.
     */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong ZSTD_rollingHash_primePower(uint length)
    {
        return ZSTD_ipow(Prime8Bytes, length - 1);
    }

    /** ZSTD_rollingHash_rotate() :
     * Rotate the rolling hash by one byte.
     */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong ZSTD_rollingHash_rotate(ulong hash, byte toRemove, byte toAdd, ulong primePower)
    {
        hash -= (ulong)(toRemove + 10) * primePower;
        hash *= Prime8Bytes;
        hash += (ulong)(toAdd + 10);
        return hash;
    }

    /**
     * ZSTD_window_clear():
     * Clears the window containing the history by simply setting it to empty.
     */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ZSTD_window_clear(ZSTD_window_t* window)
    {
        var endT = (nuint)(window->nextSrc - window->@base);
        var end = (uint)endT;
        window->lowLimit = end;
        window->dictLimit = end;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint ZSTD_window_isEmpty(ZSTD_window_t window)
    {
        return window is { dictLimit: 2, lowLimit: 2 } && window.nextSrc - window.@base == 2 ? 1U : 0U;
    }

    /**
     * ZSTD_window_hasExtDict():
     * Returns non-zero if the window has a non-empty extDict.
     */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint ZSTD_window_hasExtDict(ZSTD_window_t window)
    {
        return window.lowLimit < window.dictLimit ? 1U : 0U;
    }

    /**
     * ZSTD_matchState_dictMode():
     * Inspects the provided matchState and figures out what dictMode should be
     * passed to the compressor.
     */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ZSTD_dictMode_e ZSTD_matchState_dictMode(ZSTD_MatchState_t* ms)
    {
        return ZSTD_window_hasExtDict(ms->window) != 0 ? ZSTD_dictMode_e.ZSTD_extDict : ms->dictMatchState != null ? ms->dictMatchState->dedicatedDictSearch != 0 ? ZSTD_dictMode_e.ZSTD_dedicatedDictSearch : ZSTD_dictMode_e.ZSTD_dictMatchState : ZSTD_dictMode_e.ZSTD_noDict;
    }

    /**
     * ZSTD_window_canOverflowCorrect():
     * Returns non-zero if the indices are large enough for overflow correction
     * to work correctly without impacting compression ratio.
     */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint ZSTD_window_canOverflowCorrect(ZSTD_window_t window, uint cycleLog, uint maxDist, uint loadedDictEnd, void* src)
    {
        var cycleSize = 1U << (int)cycleLog;
        var curr = (uint)((byte*)src - window.@base);
        var minIndexToOverflowCorrect = cycleSize + (maxDist > cycleSize ? maxDist : cycleSize) + 2;
        /* Adjust the min index to backoff the overflow correction frequency,
         * so we don't waste too much CPU in overflow correction. If this
         * computation overflows we don't really care, we just need to make
         * sure it is at least minIndexToOverflowCorrect.
         */
        var adjustment = window.nbOverflowCorrections + 1;
        var adjustedIndex = minIndexToOverflowCorrect * adjustment > minIndexToOverflowCorrect ? minIndexToOverflowCorrect * adjustment : minIndexToOverflowCorrect;
        var indexLargeEnough = curr > adjustedIndex ? 1U : 0U;
        /* Only overflow correct early if the dictionary is invalidated already,
         * so we don't hurt compression ratio.
         */
        var dictionaryInvalidated = curr > maxDist + loadedDictEnd ? 1U : 0U;
        return indexLargeEnough != 0 && dictionaryInvalidated != 0 ? 1U : 0U;
    }

    /**
     * ZSTD_window_needOverflowCorrection():
     * Returns non-zero if the indices are getting too large and need overflow
     * protection.
     */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint ZSTD_window_needOverflowCorrection(ZSTD_window_t window, uint cycleLog, uint maxDist, uint loadedDictEnd, void* src, void* srcEnd)
    {
        var curr = (uint)((byte*)srcEnd - window.@base);
        return curr > (MEM_64bits ? 3500U * (1 << 20) : 2000U * (1 << 20)) ? 1U : 0U;
    }

    /**
     * ZSTD_window_correctOverflow():
     * Reduces the indices to protect from index overflow.
     * Returns the correction made to the indices, which must be applied to every
     * stored index.
     *
     * The least significant cycleLog bits of the indices must remain the same,
     * which may be 0. Every index up to maxDist in the past must be valid.
     */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint ZSTD_window_correctOverflow(ZSTD_window_t* window, uint cycleLog, uint maxDist, void* src)
    {
        /* preemptive overflow correction:
         * 1. correction is large enough:
         *    lowLimit > (3<<29) ==> current > 3<<29 + 1<<windowLog
         *    1<<windowLog <= newCurrent < 1<<chainLog + 1<<windowLog
         *
         *    current - newCurrent
         *    > (3<<29 + 1<<windowLog) - (1<<windowLog + 1<<chainLog)
         *    > (3<<29) - (1<<chainLog)
         *    > (3<<29) - (1<<30)             (NOTE: chainLog <= 30)
         *    > 1<<29
         *
         * 2. (ip+ZSTD_CHUNKSIZE_MAX - cctx->base) doesn't overflow:
         *    After correction, current is less than (1<<chainLog + 1<<windowLog).
         *    In 64-bit mode we are safe, because we have 64-bit ptrdiff_t.
         *    In 32-bit mode we are safe, because (chainLog <= 29), so
         *    ip+ZSTD_CHUNKSIZE_MAX - cctx->base < 1<<32.
         * 3. (cctx->lowLimit + 1<<windowLog) < 1<<32:
         *    windowLog <= 31 ==> 3<<29 + 1<<windowLog < 7<<29 < 1<<32.
         */
        var cycleSize = 1U << (int)cycleLog;
        var cycleMask = cycleSize - 1;
        var curr = (uint)((byte*)src - window->@base);
        var currentCycle = curr & cycleMask;
        /* Ensure newCurrent - maxDist >= ZSTD_WINDOW_START_INDEX. */
        var currentCycleCorrection = currentCycle < 2 ? cycleSize > 2 ? cycleSize : 2 : 0;
        var newCurrent = currentCycle + currentCycleCorrection + (maxDist > cycleSize ? maxDist : cycleSize);
        var correction = curr - newCurrent;
        assert((maxDist & (maxDist - 1)) == 0);
        assert((curr & cycleMask) == (newCurrent & cycleMask));
        assert(curr > newCurrent);
        {
            assert(correction > 1 << 28);
        }

        window->@base += correction;
        window->dictBase += correction;
        if (window->lowLimit < correction + 2)
        {
            window->lowLimit = 2;
        }
        else
        {
            window->lowLimit -= correction;
        }

        if (window->dictLimit < correction + 2)
        {
            window->dictLimit = 2;
        }
        else
        {
            window->dictLimit -= correction;
        }

        assert(newCurrent >= maxDist);
        assert(newCurrent - maxDist >= 2);
        assert(window->lowLimit <= newCurrent);
        assert(window->dictLimit <= newCurrent);
        ++window->nbOverflowCorrections;
        return correction;
    }

    /**
     * ZSTD_window_enforceMaxDist():
     * Updates lowLimit so that:
     *    (srcEnd - base) - lowLimit == maxDist + loadedDictEnd
     *
     * It ensures index is valid as long as index >= lowLimit.
     * This must be called before a block compression call.
     *
     * loadedDictEnd is only defined if a dictionary is in use for current compression.
     * As the name implies, loadedDictEnd represents the index at end of dictionary.
     * The value lies within context's referential, it can be directly compared to blockEndIdx.
     *
     * If loadedDictEndPtr is NULL, no dictionary is in use, and we use loadedDictEnd == 0.
     * If loadedDictEndPtr is not NULL, we set it to zero after updating lowLimit.
     * This is because dictionaries are allowed to be referenced fully
     * as long as the last byte of the dictionary is in the window.
     * Once input has progressed beyond window size, dictionary cannot be referenced anymore.
     *
     * In normal dict mode, the dictionary lies between lowLimit and dictLimit.
     * In dictMatchState mode, lowLimit and dictLimit are the same,
     * and the dictionary is below them.
     * forceWindow and dictMatchState are therefore incompatible.
     */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ZSTD_window_enforceMaxDist(ZSTD_window_t* window, void* blockEnd, uint maxDist, uint* loadedDictEndPtr, ZSTD_MatchState_t** dictMatchStatePtr)
    {
        var blockEndIdx = (uint)((byte*)blockEnd - window->@base);
        var loadedDictEnd = loadedDictEndPtr != null ? *loadedDictEndPtr : 0;
        if (blockEndIdx > maxDist + loadedDictEnd)
        {
            var newLowLimit = blockEndIdx - maxDist;
            if (window->lowLimit < newLowLimit)
            {
                window->lowLimit = newLowLimit;
            }

            if (window->dictLimit < window->lowLimit)
            {
                window->dictLimit = window->lowLimit;
            }

            if (loadedDictEndPtr != null)
            {
                *loadedDictEndPtr = 0;
            }

            if (dictMatchStatePtr != null)
            {
                *dictMatchStatePtr = null;
            }
        }
    }

    /* Similar to ZSTD_window_enforceMaxDist(),
     * but only invalidates dictionary
     * when input progresses beyond window size.
     * assumption : loadedDictEndPtr and dictMatchStatePtr are valid (non NULL)
     *              loadedDictEnd uses same referential as window->base
     *              maxDist is the window size */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ZSTD_checkDictValidity(ZSTD_window_t* window, void* blockEnd, uint maxDist, uint* loadedDictEndPtr, ZSTD_MatchState_t** dictMatchStatePtr)
    {
        assert(loadedDictEndPtr != null);
        assert(dictMatchStatePtr != null);
        {
            var blockEndIdx = (uint)((byte*)blockEnd - window->@base);
            var loadedDictEnd = *loadedDictEndPtr;
            assert(blockEndIdx >= loadedDictEnd);
            if (blockEndIdx > loadedDictEnd + maxDist || loadedDictEnd != window->dictLimit)
            {
                *loadedDictEndPtr = 0;
                *dictMatchStatePtr = null;
            }
        }
    }

#if NET7_0_OR_GREATER
    private static ReadOnlySpan<byte> SpanStringToByte2000 => " \0"u8;
    private static byte* StringToByte2000 => (byte*)System.Runtime.CompilerServices.Unsafe.AsPointer(ref MemoryMarshal.GetReference(SpanStringToByte2000));
#else

        private static readonly byte* stringToByte_20_00 = GetArrayPointer(new byte[] { 32, 0 });
#endif
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ZSTD_window_init(ZSTD_window_t* window)
    {
        *window = new ZSTD_window_t
        {
            @base = StringToByte2000,
            dictBase = StringToByte2000,
            dictLimit = 2,
            lowLimit = 2,
            nextSrc = StringToByte2000 + 2,
            nbOverflowCorrections = 0
        };
    }

    /**
     * ZSTD_window_update():
     * Updates the window by appending [src, src + srcSize) to the window.
     * If it is not contiguous, the current prefix becomes the extDict, and we
     * forget about the extDict. Handles overlap of the prefix and extDict.
     * Returns non-zero if the segment is contiguous.
     */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint ZSTD_window_update(ZSTD_window_t* window, void* src, nuint srcSize, int forceNonContiguous)
    {
        var ip = (byte*)src;
        uint contiguous = 1;
        if (srcSize == 0)
            return contiguous;

        assert(window->@base != null);
        assert(window->dictBase != null);
        if (src != window->nextSrc || forceNonContiguous != 0)
        {
            /* not contiguous */
            var distanceFromBase = (nuint)(window->nextSrc - window->@base);
            window->lowLimit = window->dictLimit;
            assert(distanceFromBase == (uint)distanceFromBase);
            window->dictLimit = (uint)distanceFromBase;
            window->dictBase = window->@base;
            window->@base = ip - distanceFromBase;
            if (window->dictLimit - window->lowLimit < 8)
            {
                window->lowLimit = window->dictLimit;
            }

            contiguous = 0;
        }

        window->nextSrc = ip + srcSize;
        if (ip + srcSize > window->dictBase + window->lowLimit && ip < window->dictBase + window->dictLimit)
        {
            var highInputIdx = (nuint)(ip + srcSize - window->dictBase);
            var lowLimitMax = highInputIdx > window->dictLimit ? window->dictLimit : (uint)highInputIdx;
            assert(highInputIdx < 0xffffffff);
            window->lowLimit = lowLimitMax;
        }

        return contiguous;
    }

    /**
     * Returns the lowest allowed match index. It may either be in the ext-dict or the prefix.
     */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint ZSTD_getLowestMatchIndex(ZSTD_MatchState_t* ms, uint curr, uint windowLog)
    {
        var maxDistance = 1U << (int)windowLog;
        var lowestValid = ms->window.lowLimit;
        var withinWindow = curr - lowestValid > maxDistance ? curr - maxDistance : lowestValid;
        var isDictionary = ms->loadedDictEnd != 0 ? 1U : 0U;
        /* When using a dictionary the entire dictionary is valid if a single byte of the dictionary
         * is within the window. We invalidate the dictionary (and set loadedDictEnd to 0) when it isn't
         * valid for the entire block. So this check is sufficient to find the lowest valid match index.
         */
        var matchLowest = isDictionary != 0 ? lowestValid : withinWindow;
        return matchLowest;
    }

    /**
     * Returns the lowest allowed match index in the prefix.
     */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint ZSTD_getLowestPrefixIndex(ZSTD_MatchState_t* ms, uint curr, uint windowLog)
    {
        var maxDistance = 1U << (int)windowLog;
        var lowestValid = ms->window.dictLimit;
        var withinWindow = curr - lowestValid > maxDistance ? curr - maxDistance : lowestValid;
        var isDictionary = ms->loadedDictEnd != 0 ? 1U : 0U;
        /* When computing the lowest prefix index we need to take the dictionary into account to handle
         * the edge case where the dictionary and the source are contiguous in memory.
         */
        var matchLowest = isDictionary != 0 ? lowestValid : withinWindow;
        return matchLowest;
    }

    /* index_safety_check:
     * intentional underflow : ensure repIndex isn't overlapping dict + prefix
     * @return 1 if values are not overlapping,
     * 0 otherwise */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ZSTD_index_overlap_check(uint prefixLowestIndex, uint repIndex)
    {
        return prefixLowestIndex - 1 - repIndex >= 3 ? 1 : 0;
    }

    /* Helper function for ZSTD_fillHashTable and ZSTD_fillDoubleHashTable.
     * Unpacks hashAndTag into (hash, tag), then packs (index, tag) into hashTable[hash]. */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ZSTD_writeTaggedIndex(uint* hashTable, nuint hashAndTag, uint index)
    {
        var hash = hashAndTag >> 8;
        var tag = (uint)(hashAndTag & ((1U << 8) - 1));
        assert(index >> (32 - 8) == 0);
        hashTable[hash] = (index << 8) | tag;
    }

    /* Helper function for short cache matchfinders.
     * Unpacks tag1 and tag2 from lower bits of packedTag1 and packedTag2, then checks if the tags match. */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ZSTD_comparePackedTags(nuint packedTag1, nuint packedTag2)
    {
        var tag1 = (uint)(packedTag1 & ((1U << 8) - 1));
        var tag2 = (uint)(packedTag2 & ((1U << 8) - 1));
        return tag1 == tag2 ? 1 : 0;
    }

    /* Returns 1 if an external sequence producer is registered, otherwise returns 0. */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ZSTD_hasExtSeqProd(ZSTD_CCtx_params_s* @params)
    {
        return @params->extSeqProdFunc != null ? 1 : 0;
    }
}