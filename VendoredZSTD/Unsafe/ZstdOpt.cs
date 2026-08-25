using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static VendoredZSTD.UnsafeHelper;

namespace VendoredZSTD.Unsafe;

public static unsafe partial class Methods
{
    /* ZSTD_bitWeight() :
     * provide estimated "cost" of a stat in full bits only */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint ZSTD_bitWeight(uint stat)
    {
        return ZSTD_highbit32(stat + 1) * (1 << 8);
    }

    /* ZSTD_fracWeight() :
     * provide fractional-bit "cost" of a stat,
     * using linear interpolation approximation */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint ZSTD_fracWeight(uint rawStat)
    {
        var stat = rawStat + 1;
        var hb = ZSTD_highbit32(stat);
        var bWeight = hb * (1 << 8);
        /* Fweight was meant for "Fractional weight"
         * but it's effectively a value between 1 and 2
         * using fixed point arithmetic */
        var fWeight = (stat << 8) >> (int)hb;
        var weight = bWeight + fWeight;
        assert(hb + 8 < 31);
        return weight;
    }

    private static int ZSTD_compressedLiterals(OptStateT* optPtr)
    {
        return optPtr->literalCompressionMode != ZstdParamSwitchE.ZstdPsDisable ? 1 : 0;
    }

    private static void ZSTD_setBasePrices(OptStateT* optPtr, int optLevel)
    {
        if (ZSTD_compressedLiterals(optPtr) != 0)
            optPtr->litSumBasePrice =
                optLevel != 0 ? ZSTD_fracWeight(optPtr->litSum) : ZSTD_bitWeight(optPtr->litSum);
        optPtr->litLengthSumBasePrice =
            optLevel != 0
                ? ZSTD_fracWeight(optPtr->litLengthSum)
                : ZSTD_bitWeight(optPtr->litLengthSum);
        optPtr->matchLengthSumBasePrice =
            optLevel != 0
                ? ZSTD_fracWeight(optPtr->matchLengthSum)
                : ZSTD_bitWeight(optPtr->matchLengthSum);
        optPtr->offCodeSumBasePrice =
            optLevel != 0
                ? ZSTD_fracWeight(optPtr->offCodeSum)
                : ZSTD_bitWeight(optPtr->offCodeSum);
    }

    private static uint sum_u32(uint* table, nuint nbElts)
    {
        nuint n;
        uint total = 0;
        for (n = 0; n < nbElts; n++)
            total += table[n];

        return total;
    }

    private static uint ZSTD_downscaleStats(
        uint* table,
        uint lastEltIndex,
        uint shift,
        BaseDirectiveE base1
    )
    {
        uint s,
            sum = 0;
        assert(shift < 30);
        for (s = 0; s < lastEltIndex + 1; s++)
        {
            var @base = (uint)(
                base1 != default ? 1
                : table[s] > 0 ? 1
                : 0
            );
            var newStat = @base + (table[s] >> (int)shift);
            sum += newStat;
            table[s] = newStat;
        }

        return sum;
    }

    /* ZSTD_scaleStats() :
     * reduce all elt frequencies in table if sum too large
     * return the resulting sum of elements */
    private static uint ZSTD_scaleStats(uint* table, uint lastEltIndex, uint logTarget)
    {
        var prevsum = sum_u32(table, lastEltIndex + 1);
        var factor = prevsum >> (int)logTarget;
        assert(logTarget < 30);
        if (factor <= 1)
            return prevsum;
        return ZSTD_downscaleStats(
            table,
            lastEltIndex,
            ZSTD_highbit32(factor),
            BaseDirectiveE.Base1Guaranteed
        );
    }

#if NET8_0_OR_GREATER
    private static ReadOnlySpan<uint> SpanBaseLLfreqs =>
        new uint[36]
        {
            4,
            2,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            1
        };

    private static uint* BaseLLfreqs =>
        (uint*)
        System.Runtime.CompilerServices.Unsafe.AsPointer(
            ref MemoryMarshal.GetReference(SpanBaseLLfreqs)
        );
#else
    private static readonly uint* baseLLfreqs = GetArrayPointer(
        new uint[36]
        {
            4,
            2,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
        }
    );
#endif
#if NET8_0_OR_GREATER
    private static ReadOnlySpan<uint> SpanBaseOfCfreqs =>
        new uint[32]
        {
            6,
            2,
            1,
            1,
            2,
            3,
            4,
            4,
            4,
            3,
            2,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            1
        };

    private static uint* BaseOfCfreqs =>
        (uint*)
        System.Runtime.CompilerServices.Unsafe.AsPointer(
            ref MemoryMarshal.GetReference(SpanBaseOfCfreqs)
        );
#else
    private static readonly uint* baseOFCfreqs = GetArrayPointer(
        new uint[32]
        {
            6,
            2,
            1,
            1,
            2,
            3,
            4,
            4,
            4,
            3,
            2,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
            1,
        }
    );
#endif
    /* ZSTD_rescaleFreqs() :
     * if first block (detected by optPtr->litLengthSum == 0) : init statistics
     *    take hints from dictionary if there is one
     *    and init from zero if there is none,
     *    using src for literals stats, and baseline stats for sequence symbols
     * otherwise downscale existing stats, to be used as seed for next block.
     */
    private static void ZSTD_rescaleFreqs(
        OptStateT* optPtr,
        byte* src,
        nuint srcSize,
        int optLevel
    )
    {
        var compressedLiterals = ZSTD_compressedLiterals(optPtr);
        optPtr->priceType = ZstdOptPriceE.ZopDynamic;
        if (optPtr->litLengthSum == 0)
        {
            if (srcSize <= 8)
                optPtr->priceType = ZstdOptPriceE.ZopPredef;

            assert(optPtr->symbolCosts != null);
            if (optPtr->symbolCosts->huf.repeatMode == HufRepeat.HufRepeatValid)
            {
                optPtr->priceType = ZstdOptPriceE.ZopDynamic;
                if (compressedLiterals != 0)
                {
                    /* generate literals statistics from huffman table */
                    uint lit;
                    assert(optPtr->litFreq != null);
                    optPtr->litSum = 0;
                    for (lit = 0; lit <= (1 << 8) - 1; lit++)
                    {
                        /* scale to 2K */
                        const uint scaleLog = 11;
                        var bitCost = HUF_getNbBitsFromCTable(
                            &optPtr->symbolCosts->huf.CTable.e0,
                            lit
                        );
                        assert(bitCost <= scaleLog);
                        optPtr->litFreq[lit] = (uint)(
                            bitCost != 0 ? 1 << (int)(scaleLog - bitCost) : 1
                        );
                        optPtr->litSum += optPtr->litFreq[lit];
                    }
                }

                {
                    uint ll;
                    FseCStateT llstate;
                    FSE_initCState(&llstate, optPtr->symbolCosts->fse.litlengthCTable);
                    optPtr->litLengthSum = 0;
                    for (ll = 0; ll <= 35; ll++)
                    {
                        /* scale to 1K */
                        const uint scaleLog = 10;
                        var bitCost = FSE_getMaxNbBits(llstate.symbolTT, ll);
                        assert(bitCost < scaleLog);
                        optPtr->litLengthFreq[ll] = (uint)(
                            bitCost != 0 ? 1 << (int)(scaleLog - bitCost) : 1
                        );
                        optPtr->litLengthSum += optPtr->litLengthFreq[ll];
                    }
                }

                {
                    uint ml;
                    FseCStateT mlstate;
                    FSE_initCState(&mlstate, optPtr->symbolCosts->fse.matchlengthCTable);
                    optPtr->matchLengthSum = 0;
                    for (ml = 0; ml <= 52; ml++)
                    {
                        const uint scaleLog = 10;
                        var bitCost = FSE_getMaxNbBits(mlstate.symbolTT, ml);
                        assert(bitCost < scaleLog);
                        optPtr->matchLengthFreq[ml] = (uint)(
                            bitCost != 0 ? 1 << (int)(scaleLog - bitCost) : 1
                        );
                        optPtr->matchLengthSum += optPtr->matchLengthFreq[ml];
                    }
                }

                {
                    uint of;
                    FseCStateT ofstate;
                    FSE_initCState(&ofstate, optPtr->symbolCosts->fse.offcodeCTable);
                    optPtr->offCodeSum = 0;
                    for (of = 0; of <= 31; of++)
                    {
                        const uint scaleLog = 10;
                        var bitCost = FSE_getMaxNbBits(ofstate.symbolTT, of);
                        assert(bitCost < scaleLog);
                        optPtr->offCodeFreq[of] = (uint)(
                            bitCost != 0 ? 1 << (int)(scaleLog - bitCost) : 1
                        );
                        optPtr->offCodeSum += optPtr->offCodeFreq[of];
                    }
                }
            }
            else
            {
                assert(optPtr->litFreq != null);
                if (compressedLiterals != 0)
                {
                    /* base initial cost of literals on direct frequency within src */
                    uint lit = (1 << 8) - 1;
                    HIST_count_simple(optPtr->litFreq, &lit, src, srcSize);
                    optPtr->litSum = ZSTD_downscaleStats(
                        optPtr->litFreq,
                        (1 << 8) - 1,
                        8,
                        BaseDirectiveE.Base0Possible
                    );
                }

                {
                    memcpy(optPtr->litLengthFreq, BaseLLfreqs, sizeof(uint) * 36);
                    optPtr->litLengthSum = sum_u32(BaseLLfreqs, 35 + 1);
                }

                {
                    uint ml;
                    for (ml = 0; ml <= 52; ml++)
                        optPtr->matchLengthFreq[ml] = 1;
                }

                optPtr->matchLengthSum = 52 + 1;
                {
                    memcpy(optPtr->offCodeFreq, BaseOfCfreqs, sizeof(uint) * 32);
                    optPtr->offCodeSum = sum_u32(BaseOfCfreqs, 31 + 1);
                }
            }
        }
        else
        {
            if (compressedLiterals != 0)
                optPtr->litSum = ZSTD_scaleStats(optPtr->litFreq, (1 << 8) - 1, 12);
            optPtr->litLengthSum = ZSTD_scaleStats(optPtr->litLengthFreq, 35, 11);
            optPtr->matchLengthSum = ZSTD_scaleStats(optPtr->matchLengthFreq, 52, 11);
            optPtr->offCodeSum = ZSTD_scaleStats(optPtr->offCodeFreq, 31, 11);
        }

        ZSTD_setBasePrices(optPtr, optLevel);
    }

    /* ZSTD_rawLiteralsCost() :
     * price of literals (only) in specified segment (which length can be 0).
     * does not include price of literalLength symbol */
    private static uint ZSTD_rawLiteralsCost(
        byte* literals,
        uint litLength,
        OptStateT* optPtr,
        int optLevel
    )
    {
        if (litLength == 0)
            return 0;
        if (ZSTD_compressedLiterals(optPtr) == 0)
            return (litLength << 3) * (1 << 8);
        if (optPtr->priceType == ZstdOptPriceE.ZopPredef)
            return litLength * 6 * (1 << 8);
        {
            var price = optPtr->litSumBasePrice * litLength;
            var litPriceMax = optPtr->litSumBasePrice - (1 << 8);
            uint u;
            assert(optPtr->litSumBasePrice >= 1 << 8);
            for (u = 0; u < litLength; u++)
            {
                var litPrice =
                    optLevel != 0
                        ? ZSTD_fracWeight(optPtr->litFreq[literals[u]])
                        : ZSTD_bitWeight(optPtr->litFreq[literals[u]]);
                if (litPrice > litPriceMax)
                    litPrice = litPriceMax;
                price -= litPrice;
            }

            return price;
        }
    }

    /* ZSTD_litLengthPrice() :
     * cost of literalLength symbol */
    private static uint ZSTD_litLengthPrice(uint litLength, OptStateT* optPtr, int optLevel)
    {
        assert(litLength <= 1 << 17);
        if (optPtr->priceType == ZstdOptPriceE.ZopPredef)
            return optLevel != 0 ? ZSTD_fracWeight(litLength) : ZSTD_bitWeight(litLength);
        if (litLength == 1 << 17)
            return (1 << 8) + ZSTD_litLengthPrice((1 << 17) - 1, optPtr, optLevel);
        {
            var llCode = ZSTD_LLcode(litLength);
            return (uint)(LlBits[llCode] * (1 << 8))
                   + optPtr->litLengthSumBasePrice
                   - (
                       optLevel != 0
                           ? ZSTD_fracWeight(optPtr->litLengthFreq[llCode])
                           : ZSTD_bitWeight(optPtr->litLengthFreq[llCode])
                   );
        }
    }

    /* ZSTD_getMatchPrice() :
     * Provides the cost of the match part (offset + matchLength) of a sequence.
     * Must be combined with ZSTD_fullLiteralsCost() to get the full cost of a sequence.
     * @offBase : sumtype, representing an offset or a repcode, and using numeric representation of ZSTD_storeSeq()
     * @optLevel: when <2, favors small offset for decompression speed (improved cache efficiency)
     */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint ZSTD_getMatchPrice(
        uint offBase,
        uint matchLength,
        OptStateT* optPtr,
        int optLevel
    )
    {
        uint price;
        var offCode = ZSTD_highbit32(offBase);
        var mlBase = matchLength - 3;
        assert(matchLength >= 3);
        if (optPtr->priceType == ZstdOptPriceE.ZopPredef)
            return (optLevel != 0 ? ZSTD_fracWeight(mlBase) : ZSTD_bitWeight(mlBase))
                   + (16 + offCode) * (1 << 8);
        price =
            offCode * (1 << 8)
            + (
                optPtr->offCodeSumBasePrice
                - (
                    optLevel != 0
                        ? ZSTD_fracWeight(optPtr->offCodeFreq[offCode])
                        : ZSTD_bitWeight(optPtr->offCodeFreq[offCode])
                )
            );
        if (optLevel < 2 && offCode >= 20)
            price += (offCode - 19) * 2 * (1 << 8);
        {
            var mlCode = ZSTD_MLcode(mlBase);
            price +=
                (uint)(MlBits[mlCode] * (1 << 8))
                + (
                    optPtr->matchLengthSumBasePrice
                    - (
                        optLevel != 0
                            ? ZSTD_fracWeight(optPtr->matchLengthFreq[mlCode])
                            : ZSTD_bitWeight(optPtr->matchLengthFreq[mlCode])
                    )
                );
        }

        price += (1 << 8) / 5;
        return price;
    }

    /* ZSTD_updateStats() :
     * assumption : literals + litLength <= iend */
    private static void ZSTD_updateStats(
        OptStateT* optPtr,
        uint litLength,
        byte* literals,
        uint offBase,
        uint matchLength
    )
    {
        if (ZSTD_compressedLiterals(optPtr) != 0)
        {
            uint u;
            for (u = 0; u < litLength; u++)
                optPtr->litFreq[literals[u]] += 2;
            optPtr->litSum += litLength * 2;
        }

        {
            var llCode = ZSTD_LLcode(litLength);
            optPtr->litLengthFreq[llCode]++;
            optPtr->litLengthSum++;
        }

        {
            var offCode = ZSTD_highbit32(offBase);
            assert(offCode <= 31);
            optPtr->offCodeFreq[offCode]++;
            optPtr->offCodeSum++;
        }

        {
            var mlBase = matchLength - 3;
            var mlCode = ZSTD_MLcode(mlBase);
            optPtr->matchLengthFreq[mlCode]++;
            optPtr->matchLengthSum++;
        }
    }

    /* ZSTD_readMINMATCH() :
     * function safe only for comparisons
     * assumption : memPtr must be at least 4 bytes before end of buffer */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint ZSTD_readMINMATCH(void* memPtr, uint length)
    {
        switch (length)
        {
            default:
            case 4:
                return MEM_read32(memPtr);
            case 3:
                if (BitConverter.IsLittleEndian)
                    return MEM_read32(memPtr) << 8;
                return MEM_read32(memPtr) >> 8;
        }
    }

    /* Update hashTable3 up to ip (excluded)
    Assumption : always within prefix (i.e. not within extDict) */
    private static uint ZSTD_insertAndFindFirstIndexHash3(
        ZstdMatchStateT* ms,
        uint* nextToUpdate3,
        byte* ip
    )
    {
        var hashTable3 = ms->hashTable3;
        var hashLog3 = ms->hashLog3;
        var @base = ms->window.@base;
        var idx = *nextToUpdate3;
        var target = (uint)(ip - @base);
        var hash3 = ZSTD_hash3Ptr(ip, hashLog3);
        assert(hashLog3 > 0);
        while (idx < target)
        {
            hashTable3[ZSTD_hash3Ptr(@base + idx, hashLog3)] = idx;
            idx++;
        }

        *nextToUpdate3 = target;
        return hashTable3[hash3];
    }

    /*-*************************************
     *  Binary Tree search
     ***************************************/
    /**
     * ZSTD_insertBt1() : add one or multiple positions to tree.
     * @param ip assumed
     * <
     * =
     * iend-8
     * .
     * @
     * param
     * target
     * The
     * target
     * of
     * ZSTD_updateTree_internal
     * (
     * )
     * -
     * we
     * are
     * filling
     * to
     * this
     * position
     * @
     * return
     * :
     * nb
     * of
     * positions
     * added
     */
    private static uint ZSTD_insertBt1(
        ZstdMatchStateT* ms,
        byte* ip,
        byte* iend,
        uint target,
        uint mls,
        int extDict
    )
    {
        var cParams = &ms->cParams;
        var hashTable = ms->hashTable;
        var hashLog = cParams->hashLog;
        var h = ZSTD_hashPtr(ip, hashLog, mls);
        var bt = ms->chainTable;
        var btLog = cParams->chainLog - 1;
        var btMask = (uint)((1 << (int)btLog) - 1);
        var matchIndex = hashTable[h];
        nuint commonLengthSmaller = 0,
            commonLengthLarger = 0;
        var @base = ms->window.@base;
        var dictBase = ms->window.dictBase;
        var dictLimit = ms->window.dictLimit;
        var dictEnd = dictBase + dictLimit;
        var prefixStart = @base + dictLimit;
        byte* match;
        var curr = (uint)(ip - @base);
        var btLow = btMask >= curr ? 0 : curr - btMask;
        var smallerPtr = bt + 2 * (curr & btMask);
        var largerPtr = smallerPtr + 1;
        /* to be nullified at the end */
        uint dummy32;
        /* windowLow is based on target because
         * we only need positions that will be in the window at the end of the tree update.
         */
        var windowLow = ZSTD_getLowestMatchIndex(ms, target, cParams->windowLog);
        var matchEndIdx = curr + 8 + 1;
        nuint bestLength = 8;
        var nbCompares = 1U << (int)cParams->searchLog;
        assert(curr <= target);
        assert(ip <= iend - 8);
        hashTable[h] = curr;
        assert(windowLow > 0);
        for (; nbCompares != 0 && matchIndex >= windowLow; --nbCompares)
        {
            var nextPtr = bt + 2 * (matchIndex & btMask);
            /* guaranteed minimum nb of common bytes */
            var matchLength =
                commonLengthSmaller < commonLengthLarger ? commonLengthSmaller : commonLengthLarger;
            assert(matchIndex < curr);
            if (extDict == 0 || matchIndex + matchLength >= dictLimit)
            {
                assert(matchIndex + matchLength >= dictLimit);
                match = @base + matchIndex;
                matchLength += ZSTD_count(ip + matchLength, match + matchLength, iend);
            }
            else
            {
                match = dictBase + matchIndex;
                matchLength += ZSTD_count_2segments(
                    ip + matchLength,
                    match + matchLength,
                    iend,
                    dictEnd,
                    prefixStart
                );
                if (matchIndex + matchLength >= dictLimit)
                    match = @base + matchIndex;
            }

            if (matchLength > bestLength)
            {
                bestLength = matchLength;
                if (matchLength > matchEndIdx - matchIndex)
                    matchEndIdx = matchIndex + (uint)matchLength;
            }

            if (ip + matchLength == iend)
                break;

            if (match[matchLength] < ip[matchLength])
            {
                *smallerPtr = matchIndex;
                commonLengthSmaller = matchLength;
                if (matchIndex <= btLow)
                {
                    smallerPtr = &dummy32;
                    break;
                }

                smallerPtr = nextPtr + 1;
                matchIndex = nextPtr[1];
            }
            else
            {
                *largerPtr = matchIndex;
                commonLengthLarger = matchLength;
                if (matchIndex <= btLow)
                {
                    largerPtr = &dummy32;
                    break;
                }

                largerPtr = nextPtr;
                matchIndex = nextPtr[0];
            }
        }

        *smallerPtr = *largerPtr = 0;
        {
            uint positions = 0;
            if (bestLength > 384)
                positions = 192 < (uint)(bestLength - 384) ? 192 : (uint)(bestLength - 384);
            assert(matchEndIdx > curr + 8);
            return positions > matchEndIdx - (curr + 8) ? positions : matchEndIdx - (curr + 8);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ZSTD_updateTree_internal(
        ZstdMatchStateT* ms,
        byte* ip,
        byte* iend,
        uint mls,
        ZstdDictModeE dictMode
    )
    {
        var @base = ms->window.@base;
        var target = (uint)(ip - @base);
        var idx = ms->nextToUpdate;
        while (idx < target)
        {
            var forward = ZSTD_insertBt1(
                ms,
                @base + idx,
                iend,
                target,
                mls,
                dictMode == ZstdDictModeE.ZstdExtDict ? 1 : 0
            );
            assert(idx < idx + forward);
            idx += forward;
        }

        assert((nuint)(ip - @base) <= unchecked((uint)-1));
        assert((nuint)(iend - @base) <= unchecked((uint)-1));
        ms->nextToUpdate = target;
    }

    /* used in ZSTD_loadDictionaryContent() */
    private static void ZSTD_updateTree(ZstdMatchStateT* ms, byte* ip, byte* iend)
    {
        ZSTD_updateTree_internal(ms, ip, iend, ms->cParams.minMatch, ZstdDictModeE.ZstdNoDict);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint ZSTD_insertBtAndGetAllMatches(
        ZstdMatchT* matches,
        ZstdMatchStateT* ms,
        uint* nextToUpdate3,
        byte* ip,
        byte* iLimit,
        ZstdDictModeE dictMode,
        uint* rep,
        uint ll0,
        uint lengthToBeat,
        uint mls
    )
    {
        var cParams = &ms->cParams;
        var sufficientLen =
            cParams->targetLength < (1 << 12) - 1 ? cParams->targetLength : (1 << 12) - 1;
        var @base = ms->window.@base;
        var curr = (uint)(ip - @base);
        var hashLog = cParams->hashLog;
        var minMatch = (uint)(mls == 3 ? 3 : 4);
        var hashTable = ms->hashTable;
        var h = ZSTD_hashPtr(ip, hashLog, mls);
        var matchIndex = hashTable[h];
        var bt = ms->chainTable;
        var btLog = cParams->chainLog - 1;
        var btMask = (1U << (int)btLog) - 1;
        nuint commonLengthSmaller = 0,
            commonLengthLarger = 0;
        var dictBase = ms->window.dictBase;
        var dictLimit = ms->window.dictLimit;
        var dictEnd = dictBase + dictLimit;
        var prefixStart = @base + dictLimit;
        var btLow = btMask >= curr ? 0 : curr - btMask;
        var windowLow = ZSTD_getLowestMatchIndex(ms, curr, cParams->windowLog);
        var matchLow = windowLow != 0 ? windowLow : 1;
        var smallerPtr = bt + 2 * (curr & btMask);
        var largerPtr = bt + 2 * (curr & btMask) + 1;
        /* farthest referenced position of any match => detects repetitive patterns */
        var matchEndIdx = curr + 8 + 1;
        /* to be nullified at the end */
        uint dummy32;
        uint mnum = 0;
        var nbCompares = 1U << (int)cParams->searchLog;
        var dms = dictMode == ZstdDictModeE.ZstdDictMatchState ? ms->dictMatchState : null;
        var dmsCParams = dictMode == ZstdDictModeE.ZstdDictMatchState ? &dms->cParams : null;
        var dmsBase = dictMode == ZstdDictModeE.ZstdDictMatchState ? dms->window.@base : null;
        var dmsEnd = dictMode == ZstdDictModeE.ZstdDictMatchState ? dms->window.nextSrc : null;
        var dmsHighLimit =
            dictMode == ZstdDictModeE.ZstdDictMatchState ? (uint)(dmsEnd - dmsBase) : 0;
        var dmsLowLimit =
            dictMode == ZstdDictModeE.ZstdDictMatchState ? dms->window.lowLimit : 0;
        var dmsIndexDelta =
            dictMode == ZstdDictModeE.ZstdDictMatchState ? windowLow - dmsHighLimit : 0;
        var dmsHashLog =
            dictMode == ZstdDictModeE.ZstdDictMatchState ? dmsCParams->hashLog : hashLog;
        var dmsBtLog =
            dictMode == ZstdDictModeE.ZstdDictMatchState ? dmsCParams->chainLog - 1 : btLog;
        var dmsBtMask =
            dictMode == ZstdDictModeE.ZstdDictMatchState ? (1U << (int)dmsBtLog) - 1 : 0;
        var dmsBtLow =
            dictMode == ZstdDictModeE.ZstdDictMatchState
            && dmsBtMask < dmsHighLimit - dmsLowLimit
                ? dmsHighLimit - dmsBtMask
                : dmsLowLimit;
        nuint bestLength = lengthToBeat - 1;
        assert(ll0 <= 1);
        {
            var lastR = 3 + ll0;
            uint repCode;
            for (repCode = ll0; repCode < lastR; repCode++)
            {
                var repOffset = repCode == 3 ? rep[0] - 1 : rep[repCode];
                var repIndex = curr - repOffset;
                uint repLen = 0;
                assert(curr >= dictLimit);
                if (repOffset - 1 < curr - dictLimit)
                {
                    if (
                        repIndex >= windowLow
                        && ZSTD_readMINMATCH(ip, minMatch)
                        == ZSTD_readMINMATCH(ip - repOffset, minMatch)
                    )
                        repLen =
                            (uint)ZSTD_count(ip + minMatch, ip + minMatch - repOffset, iLimit)
                            + minMatch;
                }
                else
                {
                    var repMatch =
                        dictMode == ZstdDictModeE.ZstdDictMatchState
                            ? dmsBase + repIndex - dmsIndexDelta
                            : dictBase + repIndex;
                    assert(curr >= windowLow);
                    if (
                        dictMode == ZstdDictModeE.ZstdExtDict
                        && repOffset - 1 < curr - windowLow
                        && dictLimit - 1 - repIndex >= 3
                        && ZSTD_readMINMATCH(ip, minMatch) == ZSTD_readMINMATCH(repMatch, minMatch)
                    )
                        repLen =
                            (uint)ZSTD_count_2segments(
                                ip + minMatch,
                                repMatch + minMatch,
                                iLimit,
                                dictEnd,
                                prefixStart
                            ) + minMatch;

                    if (
                        dictMode == ZstdDictModeE.ZstdDictMatchState
                        && repOffset - 1 < curr - (dmsLowLimit + dmsIndexDelta)
                        && dictLimit - 1 - repIndex >= 3
                        && ZSTD_readMINMATCH(ip, minMatch) == ZSTD_readMINMATCH(repMatch, minMatch)
                    )
                        repLen =
                            (uint)ZSTD_count_2segments(
                                ip + minMatch,
                                repMatch + minMatch,
                                iLimit,
                                dmsEnd,
                                prefixStart
                            ) + minMatch;
                }

                if (repLen > bestLength)
                {
                    bestLength = repLen;
                    assert(repCode - ll0 + 1 >= 1);
                    assert(repCode - ll0 + 1 <= 3);
                    matches[mnum].off = repCode - ll0 + 1;
                    matches[mnum].len = repLen;
                    mnum++;
                    if (repLen > sufficientLen || ip + repLen == iLimit)
                        return mnum;
                }
            }
        }

        if (mls == 3 && bestLength < mls)
        {
            var matchIndex3 = ZSTD_insertAndFindFirstIndexHash3(ms, nextToUpdate3, ip);
            if (matchIndex3 >= matchLow && curr - matchIndex3 < 1 << 18)
            {
                nuint mlen;
                if (
                    dictMode == ZstdDictModeE.ZstdNoDict
                    || dictMode == ZstdDictModeE.ZstdDictMatchState
                    || matchIndex3 >= dictLimit
                )
                {
                    var match = @base + matchIndex3;
                    mlen = ZSTD_count(ip, match, iLimit);
                }
                else
                {
                    var match = dictBase + matchIndex3;
                    mlen = ZSTD_count_2segments(ip, match, iLimit, dictEnd, prefixStart);
                }

                if (mlen >= mls)
                {
                    bestLength = mlen;
                    assert(curr > matchIndex3);
                    assert(mnum == 0);
                    assert(curr - matchIndex3 > 0);
                    matches[0].off = curr - matchIndex3 + 3;
                    matches[0].len = (uint)mlen;
                    mnum = 1;
                    if (mlen > sufficientLen || ip + mlen == iLimit)
                    {
                        ms->nextToUpdate = curr + 1;
                        return 1;
                    }
                }
            }
        }

        hashTable[h] = curr;
        for (; nbCompares != 0 && matchIndex >= matchLow; --nbCompares)
        {
            var nextPtr = bt + 2 * (matchIndex & btMask);
            byte* match;
            /* guaranteed minimum nb of common bytes */
            var matchLength =
                commonLengthSmaller < commonLengthLarger ? commonLengthSmaller : commonLengthLarger;
            assert(curr > matchIndex);
            if (
                dictMode == ZstdDictModeE.ZstdNoDict
                || dictMode == ZstdDictModeE.ZstdDictMatchState
                || matchIndex + matchLength >= dictLimit
            )
            {
                assert(matchIndex + matchLength >= dictLimit);
                match = @base + matchIndex;
#if DEBUG
                if (matchIndex >= dictLimit)
                    assert(memcmp(match, ip, matchLength) == 0);
#endif
                matchLength += ZSTD_count(ip + matchLength, match + matchLength, iLimit);
            }
            else
            {
                match = dictBase + matchIndex;
                assert(memcmp(match, ip, matchLength) == 0);
                matchLength += ZSTD_count_2segments(
                    ip + matchLength,
                    match + matchLength,
                    iLimit,
                    dictEnd,
                    prefixStart
                );
                if (matchIndex + matchLength >= dictLimit)
                    match = @base + matchIndex;
            }

            if (matchLength > bestLength)
            {
                assert(matchEndIdx > matchIndex);
                if (matchLength > matchEndIdx - matchIndex)
                    matchEndIdx = matchIndex + (uint)matchLength;
                bestLength = matchLength;
                assert(curr - matchIndex > 0);
                matches[mnum].off = curr - matchIndex + 3;
                matches[mnum].len = (uint)matchLength;
                mnum++;
                if (matchLength > 1 << 12 || ip + matchLength == iLimit)
                {
                    if (dictMode == ZstdDictModeE.ZstdDictMatchState)
                        nbCompares = 0;
                    break;
                }
            }

            if (match[matchLength] < ip[matchLength])
            {
                *smallerPtr = matchIndex;
                commonLengthSmaller = matchLength;
                if (matchIndex <= btLow)
                {
                    smallerPtr = &dummy32;
                    break;
                }

                smallerPtr = nextPtr + 1;
                matchIndex = nextPtr[1];
            }
            else
            {
                *largerPtr = matchIndex;
                commonLengthLarger = matchLength;
                if (matchIndex <= btLow)
                {
                    largerPtr = &dummy32;
                    break;
                }

                largerPtr = nextPtr;
                matchIndex = nextPtr[0];
            }
        }

        *smallerPtr = *largerPtr = 0;
        assert(nbCompares <= 1U << ((sizeof(nuint) == 4 ? 30 : 31) - 1));
        if (dictMode == ZstdDictModeE.ZstdDictMatchState && nbCompares != 0)
        {
            var dmsH = ZSTD_hashPtr(ip, dmsHashLog, mls);
            var dictMatchIndex = dms->hashTable[dmsH];
            var dmsBt = dms->chainTable;
            commonLengthSmaller = commonLengthLarger = 0;
            for (; nbCompares != 0 && dictMatchIndex > dmsLowLimit; --nbCompares)
            {
                var nextPtr = dmsBt + 2 * (dictMatchIndex & dmsBtMask);
                /* guaranteed minimum nb of common bytes */
                var matchLength =
                    commonLengthSmaller < commonLengthLarger
                        ? commonLengthSmaller
                        : commonLengthLarger;
                var match = dmsBase + dictMatchIndex;
                matchLength += ZSTD_count_2segments(
                    ip + matchLength,
                    match + matchLength,
                    iLimit,
                    dmsEnd,
                    prefixStart
                );
                if (dictMatchIndex + matchLength >= dmsHighLimit)
                    match = @base + dictMatchIndex + dmsIndexDelta;
                if (matchLength > bestLength)
                {
                    matchIndex = dictMatchIndex + dmsIndexDelta;
                    if (matchLength > matchEndIdx - matchIndex)
                        matchEndIdx = matchIndex + (uint)matchLength;
                    bestLength = matchLength;
                    assert(curr - matchIndex > 0);
                    matches[mnum].off = curr - matchIndex + 3;
                    matches[mnum].len = (uint)matchLength;
                    mnum++;
                    if (matchLength > 1 << 12 || ip + matchLength == iLimit)
                        break;
                }

                if (dictMatchIndex <= dmsBtLow)
                    break;

                if (match[matchLength] < ip[matchLength])
                {
                    commonLengthSmaller = matchLength;
                    dictMatchIndex = nextPtr[1];
                }
                else
                {
                    commonLengthLarger = matchLength;
                    dictMatchIndex = nextPtr[0];
                }
            }
        }

        assert(matchEndIdx > curr + 8);
        ms->nextToUpdate = matchEndIdx - 8;
        return mnum;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint ZSTD_btGetAllMatches_internal(
        ZstdMatchT* matches,
        ZstdMatchStateT* ms,
        uint* nextToUpdate3,
        byte* ip,
        byte* iHighLimit,
        uint* rep,
        uint ll0,
        uint lengthToBeat,
        ZstdDictModeE dictMode,
        uint mls
    )
    {
        assert(
            (
                ms->cParams.minMatch <= 3 ? 3
                : ms->cParams.minMatch <= 6 ? ms->cParams.minMatch
                : 6
            ) == mls
        );
        if (ip < ms->window.@base + ms->nextToUpdate)
            return 0;
        ZSTD_updateTree_internal(ms, ip, iHighLimit, mls, dictMode);
        return ZSTD_insertBtAndGetAllMatches(
            matches,
            ms,
            nextToUpdate3,
            ip,
            iHighLimit,
            dictMode,
            rep,
            ll0,
            lengthToBeat,
            mls
        );
    }

    private static uint ZSTD_btGetAllMatches_noDict_3(
        ZstdMatchT* matches,
        ZstdMatchStateT* ms,
        uint* nextToUpdate3,
        byte* ip,
        byte* iHighLimit,
        uint* rep,
        uint ll0,
        uint lengthToBeat
    )
    {
        return ZSTD_btGetAllMatches_internal(
            matches,
            ms,
            nextToUpdate3,
            ip,
            iHighLimit,
            rep,
            ll0,
            lengthToBeat,
            ZstdDictModeE.ZstdNoDict,
            3
        );
    }

    private static uint ZSTD_btGetAllMatches_noDict_4(
        ZstdMatchT* matches,
        ZstdMatchStateT* ms,
        uint* nextToUpdate3,
        byte* ip,
        byte* iHighLimit,
        uint* rep,
        uint ll0,
        uint lengthToBeat
    )
    {
        return ZSTD_btGetAllMatches_internal(
            matches,
            ms,
            nextToUpdate3,
            ip,
            iHighLimit,
            rep,
            ll0,
            lengthToBeat,
            ZstdDictModeE.ZstdNoDict,
            4
        );
    }

    private static uint ZSTD_btGetAllMatches_noDict_5(
        ZstdMatchT* matches,
        ZstdMatchStateT* ms,
        uint* nextToUpdate3,
        byte* ip,
        byte* iHighLimit,
        uint* rep,
        uint ll0,
        uint lengthToBeat
    )
    {
        return ZSTD_btGetAllMatches_internal(
            matches,
            ms,
            nextToUpdate3,
            ip,
            iHighLimit,
            rep,
            ll0,
            lengthToBeat,
            ZstdDictModeE.ZstdNoDict,
            5
        );
    }

    private static uint ZSTD_btGetAllMatches_noDict_6(
        ZstdMatchT* matches,
        ZstdMatchStateT* ms,
        uint* nextToUpdate3,
        byte* ip,
        byte* iHighLimit,
        uint* rep,
        uint ll0,
        uint lengthToBeat
    )
    {
        return ZSTD_btGetAllMatches_internal(
            matches,
            ms,
            nextToUpdate3,
            ip,
            iHighLimit,
            rep,
            ll0,
            lengthToBeat,
            ZstdDictModeE.ZstdNoDict,
            6
        );
    }

    private static uint ZSTD_btGetAllMatches_extDict_3(
        ZstdMatchT* matches,
        ZstdMatchStateT* ms,
        uint* nextToUpdate3,
        byte* ip,
        byte* iHighLimit,
        uint* rep,
        uint ll0,
        uint lengthToBeat
    )
    {
        return ZSTD_btGetAllMatches_internal(
            matches,
            ms,
            nextToUpdate3,
            ip,
            iHighLimit,
            rep,
            ll0,
            lengthToBeat,
            ZstdDictModeE.ZstdExtDict,
            3
        );
    }

    private static uint ZSTD_btGetAllMatches_extDict_4(
        ZstdMatchT* matches,
        ZstdMatchStateT* ms,
        uint* nextToUpdate3,
        byte* ip,
        byte* iHighLimit,
        uint* rep,
        uint ll0,
        uint lengthToBeat
    )
    {
        return ZSTD_btGetAllMatches_internal(
            matches,
            ms,
            nextToUpdate3,
            ip,
            iHighLimit,
            rep,
            ll0,
            lengthToBeat,
            ZstdDictModeE.ZstdExtDict,
            4
        );
    }

    private static uint ZSTD_btGetAllMatches_extDict_5(
        ZstdMatchT* matches,
        ZstdMatchStateT* ms,
        uint* nextToUpdate3,
        byte* ip,
        byte* iHighLimit,
        uint* rep,
        uint ll0,
        uint lengthToBeat
    )
    {
        return ZSTD_btGetAllMatches_internal(
            matches,
            ms,
            nextToUpdate3,
            ip,
            iHighLimit,
            rep,
            ll0,
            lengthToBeat,
            ZstdDictModeE.ZstdExtDict,
            5
        );
    }

    private static uint ZSTD_btGetAllMatches_extDict_6(
        ZstdMatchT* matches,
        ZstdMatchStateT* ms,
        uint* nextToUpdate3,
        byte* ip,
        byte* iHighLimit,
        uint* rep,
        uint ll0,
        uint lengthToBeat
    )
    {
        return ZSTD_btGetAllMatches_internal(
            matches,
            ms,
            nextToUpdate3,
            ip,
            iHighLimit,
            rep,
            ll0,
            lengthToBeat,
            ZstdDictModeE.ZstdExtDict,
            6
        );
    }

    private static uint ZSTD_btGetAllMatches_dictMatchState_3(
        ZstdMatchT* matches,
        ZstdMatchStateT* ms,
        uint* nextToUpdate3,
        byte* ip,
        byte* iHighLimit,
        uint* rep,
        uint ll0,
        uint lengthToBeat
    )
    {
        return ZSTD_btGetAllMatches_internal(
            matches,
            ms,
            nextToUpdate3,
            ip,
            iHighLimit,
            rep,
            ll0,
            lengthToBeat,
            ZstdDictModeE.ZstdDictMatchState,
            3
        );
    }

    private static uint ZSTD_btGetAllMatches_dictMatchState_4(
        ZstdMatchT* matches,
        ZstdMatchStateT* ms,
        uint* nextToUpdate3,
        byte* ip,
        byte* iHighLimit,
        uint* rep,
        uint ll0,
        uint lengthToBeat
    )
    {
        return ZSTD_btGetAllMatches_internal(
            matches,
            ms,
            nextToUpdate3,
            ip,
            iHighLimit,
            rep,
            ll0,
            lengthToBeat,
            ZstdDictModeE.ZstdDictMatchState,
            4
        );
    }

    private static uint ZSTD_btGetAllMatches_dictMatchState_5(
        ZstdMatchT* matches,
        ZstdMatchStateT* ms,
        uint* nextToUpdate3,
        byte* ip,
        byte* iHighLimit,
        uint* rep,
        uint ll0,
        uint lengthToBeat
    )
    {
        return ZSTD_btGetAllMatches_internal(
            matches,
            ms,
            nextToUpdate3,
            ip,
            iHighLimit,
            rep,
            ll0,
            lengthToBeat,
            ZstdDictModeE.ZstdDictMatchState,
            5
        );
    }

    private static uint ZSTD_btGetAllMatches_dictMatchState_6(
        ZstdMatchT* matches,
        ZstdMatchStateT* ms,
        uint* nextToUpdate3,
        byte* ip,
        byte* iHighLimit,
        uint* rep,
        uint ll0,
        uint lengthToBeat
    )
    {
        return ZSTD_btGetAllMatches_internal(
            matches,
            ms,
            nextToUpdate3,
            ip,
            iHighLimit,
            rep,
            ll0,
            lengthToBeat,
            ZstdDictModeE.ZstdDictMatchState,
            6
        );
    }

    private static readonly void*[][] GetAllMatchesFns = new void*[3][]
    {
        new void*[4]
        {
            (delegate* managed<
                ZstdMatchT*,
                ZstdMatchStateT*,
                uint*,
                byte*,
                byte*,
                uint*,
                uint,
                uint,
                uint>)(&ZSTD_btGetAllMatches_noDict_3),
            (delegate* managed<
                ZstdMatchT*,
                ZstdMatchStateT*,
                uint*,
                byte*,
                byte*,
                uint*,
                uint,
                uint,
                uint>)(&ZSTD_btGetAllMatches_noDict_4),
            (delegate* managed<
                ZstdMatchT*,
                ZstdMatchStateT*,
                uint*,
                byte*,
                byte*,
                uint*,
                uint,
                uint,
                uint>)(&ZSTD_btGetAllMatches_noDict_5),
            (delegate* managed<
                ZstdMatchT*,
                ZstdMatchStateT*,
                uint*,
                byte*,
                byte*,
                uint*,
                uint,
                uint,
                uint>)(&ZSTD_btGetAllMatches_noDict_6)
        },
        new void*[4]
        {
            (delegate* managed<
                ZstdMatchT*,
                ZstdMatchStateT*,
                uint*,
                byte*,
                byte*,
                uint*,
                uint,
                uint,
                uint>)(&ZSTD_btGetAllMatches_extDict_3),
            (delegate* managed<
                ZstdMatchT*,
                ZstdMatchStateT*,
                uint*,
                byte*,
                byte*,
                uint*,
                uint,
                uint,
                uint>)(&ZSTD_btGetAllMatches_extDict_4),
            (delegate* managed<
                ZstdMatchT*,
                ZstdMatchStateT*,
                uint*,
                byte*,
                byte*,
                uint*,
                uint,
                uint,
                uint>)(&ZSTD_btGetAllMatches_extDict_5),
            (delegate* managed<
                ZstdMatchT*,
                ZstdMatchStateT*,
                uint*,
                byte*,
                byte*,
                uint*,
                uint,
                uint,
                uint>)(&ZSTD_btGetAllMatches_extDict_6)
        },
        new void*[4]
        {
            (delegate* managed<
                ZstdMatchT*,
                ZstdMatchStateT*,
                uint*,
                byte*,
                byte*,
                uint*,
                uint,
                uint,
                uint>)(&ZSTD_btGetAllMatches_dictMatchState_3),
            (delegate* managed<
                ZstdMatchT*,
                ZstdMatchStateT*,
                uint*,
                byte*,
                byte*,
                uint*,
                uint,
                uint,
                uint>)(&ZSTD_btGetAllMatches_dictMatchState_4),
            (delegate* managed<
                ZstdMatchT*,
                ZstdMatchStateT*,
                uint*,
                byte*,
                byte*,
                uint*,
                uint,
                uint,
                uint>)(&ZSTD_btGetAllMatches_dictMatchState_5),
            (delegate* managed<
                ZstdMatchT*,
                ZstdMatchStateT*,
                uint*,
                byte*,
                byte*,
                uint*,
                uint,
                uint,
                uint>)(&ZSTD_btGetAllMatches_dictMatchState_6)
        }
    };

    private static void* ZSTD_selectBtGetAllMatches(ZstdMatchStateT* ms, ZstdDictModeE dictMode)
    {
        var mls =
            ms->cParams.minMatch <= 3 ? 3
            : ms->cParams.minMatch <= 6 ? ms->cParams.minMatch
            : 6;
        assert((uint)dictMode < 3);
        assert(mls - 3 < 4);
        return GetAllMatchesFns[(int)dictMode][mls - 3];
    }

    /* ZSTD_optLdm_skipRawSeqStoreBytes():
     * Moves forward in @rawSeqStore by @nbBytes,
     * which will update the fields 'pos' and 'posInSequence'.
     */
    private static void ZSTD_optLdm_skipRawSeqStoreBytes(RawSeqStoreT* rawSeqStore, nuint nbBytes)
    {
        var currPos = (uint)(rawSeqStore->posInSequence + nbBytes);
        while (currPos != 0 && rawSeqStore->pos < rawSeqStore->size)
        {
            var currSeq = rawSeqStore->seq[rawSeqStore->pos];
            if (currPos >= currSeq.litLength + currSeq.matchLength)
            {
                currPos -= currSeq.litLength + currSeq.matchLength;
                rawSeqStore->pos++;
            }
            else
            {
                rawSeqStore->posInSequence = currPos;
                break;
            }
        }

        if (currPos == 0 || rawSeqStore->pos == rawSeqStore->size)
            rawSeqStore->posInSequence = 0;
    }

    /* ZSTD_opt_getNextMatchAndUpdateSeqStore():
     * Calculates the beginning and end of the next match in the current block.
     * Updates 'pos' and 'posInSequence' of the ldmSeqStore.
     */
    private static void ZSTD_opt_getNextMatchAndUpdateSeqStore(
        ZstdOptLdmT* optLdm,
        uint currPosInBlock,
        uint blockBytesRemaining
    )
    {
        RawSeq currSeq;
        uint currBlockEndPos;
        uint literalsBytesRemaining;
        uint matchBytesRemaining;
        if (optLdm->seqStore.size == 0 || optLdm->seqStore.pos >= optLdm->seqStore.size)
        {
            optLdm->startPosInBlock = 0xffffffff;
            optLdm->endPosInBlock = 0xffffffff;
            return;
        }

        currSeq = optLdm->seqStore.seq[optLdm->seqStore.pos];
        assert(optLdm->seqStore.posInSequence <= currSeq.litLength + currSeq.matchLength);
        currBlockEndPos = currPosInBlock + blockBytesRemaining;
        literalsBytesRemaining =
            optLdm->seqStore.posInSequence < currSeq.litLength
                ? currSeq.litLength - (uint)optLdm->seqStore.posInSequence
                : 0;
        matchBytesRemaining =
            literalsBytesRemaining == 0
                ? currSeq.matchLength - ((uint)optLdm->seqStore.posInSequence - currSeq.litLength)
                : currSeq.matchLength;
        if (literalsBytesRemaining >= blockBytesRemaining)
        {
            optLdm->startPosInBlock = 0xffffffff;
            optLdm->endPosInBlock = 0xffffffff;
            ZSTD_optLdm_skipRawSeqStoreBytes(&optLdm->seqStore, blockBytesRemaining);
            return;
        }

        optLdm->startPosInBlock = currPosInBlock + literalsBytesRemaining;
        optLdm->endPosInBlock = optLdm->startPosInBlock + matchBytesRemaining;
        optLdm->offset = currSeq.offset;
        if (optLdm->endPosInBlock > currBlockEndPos)
        {
            optLdm->endPosInBlock = currBlockEndPos;
            ZSTD_optLdm_skipRawSeqStoreBytes(&optLdm->seqStore, currBlockEndPos - currPosInBlock);
        }
        else
        {
            ZSTD_optLdm_skipRawSeqStoreBytes(
                &optLdm->seqStore,
                literalsBytesRemaining + matchBytesRemaining
            );
        }
    }

    /* ZSTD_optLdm_maybeAddMatch():
     * Adds a match if it's long enough,
     * based on it's 'matchStartPosInBlock' and 'matchEndPosInBlock',
     * into 'matches'. Maintains the correct ordering of 'matches'.
     */
    private static void ZSTD_optLdm_maybeAddMatch(
        ZstdMatchT* matches,
        uint* nbMatches,
        ZstdOptLdmT* optLdm,
        uint currPosInBlock
    )
    {
        var posDiff = currPosInBlock - optLdm->startPosInBlock;
        /* Note: ZSTD_match_t actually contains offBase and matchLength (before subtracting MINMATCH) */
        var candidateMatchLength = optLdm->endPosInBlock - optLdm->startPosInBlock - posDiff;
        if (
            currPosInBlock < optLdm->startPosInBlock
            || currPosInBlock >= optLdm->endPosInBlock
            || candidateMatchLength < 3
        )
            return;

        if (
            *nbMatches == 0
            || (candidateMatchLength > matches[*nbMatches - 1].len && *nbMatches < 1 << 12)
        )
        {
            assert(optLdm->offset > 0);
            var candidateOffBase = optLdm->offset + 3;
            matches[*nbMatches].len = candidateMatchLength;
            matches[*nbMatches].off = candidateOffBase;
            (*nbMatches)++;
        }
    }

    /* ZSTD_optLdm_processMatchCandidate():
     * Wrapper function to update ldm seq store and call ldm functions as necessary.
     */
    private static void ZSTD_optLdm_processMatchCandidate(
        ZstdOptLdmT* optLdm,
        ZstdMatchT* matches,
        uint* nbMatches,
        uint currPosInBlock,
        uint remainingBytes
    )
    {
        if (optLdm->seqStore.size == 0 || optLdm->seqStore.pos >= optLdm->seqStore.size)
            return;

        if (currPosInBlock >= optLdm->endPosInBlock)
        {
            if (currPosInBlock > optLdm->endPosInBlock)
            {
                /* The position at which ZSTD_optLdm_processMatchCandidate() is called is not necessarily
                 * at the end of a match from the ldm seq store, and will often be some bytes
                 * over beyond matchEndPosInBlock. As such, we need to correct for these "overshoots"
                 */
                var posOvershoot = currPosInBlock - optLdm->endPosInBlock;
                ZSTD_optLdm_skipRawSeqStoreBytes(&optLdm->seqStore, posOvershoot);
            }

            ZSTD_opt_getNextMatchAndUpdateSeqStore(optLdm, currPosInBlock, remainingBytes);
        }

        ZSTD_optLdm_maybeAddMatch(matches, nbMatches, optLdm, currPosInBlock);
    }

    /*-*******************************
     *  Optimal parser
     *********************************/
    private static uint ZSTD_totalLen(ZstdOptimalT sol)
    {
        return sol.litlen + sol.mlen;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static nuint ZSTD_compressBlock_opt_generic(
        ZstdMatchStateT* ms,
        SeqStoreT* seqStore,
        uint* rep,
        void* src,
        nuint srcSize,
        int optLevel,
        ZstdDictModeE dictMode
    )
    {
        var optStatePtr = &ms->opt;
        var istart = (byte*)src;
        var ip = istart;
        var anchor = istart;
        var iend = istart + srcSize;
        var ilimit = iend - 8;
        var @base = ms->window.@base;
        var prefixStart = @base + ms->window.dictLimit;
        var cParams = &ms->cParams;
        var getAllMatches = ZSTD_selectBtGetAllMatches(ms, dictMode);
        var sufficientLen =
            cParams->targetLength < (1 << 12) - 1 ? cParams->targetLength : (1 << 12) - 1;
        var minMatch = (uint)(cParams->minMatch == 3 ? 3 : 4);
        var nextToUpdate3 = ms->nextToUpdate;
        var opt = optStatePtr->priceTable;
        var matches = optStatePtr->matchTable;
        ZstdOptimalT lastSequence;
        ZstdOptLdmT optLdm;
        memset(&lastSequence, 0, (uint)sizeof(ZstdOptimalT));
        optLdm.seqStore = ms->ldmSeqStore != null ? *ms->ldmSeqStore : KNullRawSeqStore;
        optLdm.endPosInBlock = optLdm.startPosInBlock = optLdm.offset = 0;
        ZSTD_opt_getNextMatchAndUpdateSeqStore(&optLdm, (uint)(ip - istart), (uint)(iend - ip));
        assert(optLevel <= 2);
        ZSTD_rescaleFreqs(optStatePtr, (byte*)src, srcSize, optLevel);
        ip += ip == prefixStart ? 1 : 0;
        while (ip < ilimit)
        {
            uint cur,
                lastPos = 0;
            {
                var litlen = (uint)(ip - anchor);
                var ll0 = litlen == 0 ? 1U : 0U;
                var nbMatches = (
                    (delegate* managed<
                        ZstdMatchT*,
                        ZstdMatchStateT*,
                        uint*,
                        byte*,
                        byte*,
                        uint*,
                        uint,
                        uint,
                        uint>)getAllMatches
                )(matches, ms, &nextToUpdate3, ip, iend, rep, ll0, minMatch);
                ZSTD_optLdm_processMatchCandidate(
                    &optLdm,
                    matches,
                    &nbMatches,
                    (uint)(ip - istart),
                    (uint)(iend - ip)
                );
                if (nbMatches == 0)
                {
                    ip++;
                    continue;
                }

                {
                    uint i;
                    for (i = 0; i < 3; i++)
                        opt[0].rep[i] = rep[i];
                }

                opt[0].mlen = 0;
                opt[0].litlen = litlen;
                opt[0].price = (int)ZSTD_litLengthPrice(litlen, optStatePtr, optLevel);
                {
                    var maxMl = matches[nbMatches - 1].len;
                    var maxOffBase = matches[nbMatches - 1].off;
                    if (maxMl > sufficientLen)
                    {
                        lastSequence.litlen = litlen;
                        lastSequence.mlen = maxMl;
                        lastSequence.off = maxOffBase;
                        cur = 0;
                        lastPos = ZSTD_totalLen(lastSequence);
                        goto _shortestPath;
                    }
                }

                assert(opt[0].price >= 0);
                {
                    var literalsPrice =
                        (uint)opt[0].price + ZSTD_litLengthPrice(0, optStatePtr, optLevel);
                    uint pos;
                    uint matchNb;
                    for (pos = 1; pos < minMatch; pos++)
                        opt[pos].price = 1 << 30;

                    for (matchNb = 0; matchNb < nbMatches; matchNb++)
                    {
                        var offBase = matches[matchNb].off;
                        var end = matches[matchNb].len;
                        for (; pos <= end; pos++)
                        {
                            var matchPrice = ZSTD_getMatchPrice(
                                offBase,
                                pos,
                                optStatePtr,
                                optLevel
                            );
                            var sequencePrice = literalsPrice + matchPrice;
                            opt[pos].mlen = pos;
                            opt[pos].off = offBase;
                            opt[pos].litlen = litlen;
                            opt[pos].price = (int)sequencePrice;
                        }
                    }

                    lastPos = pos - 1;
                }
            }

            for (cur = 1; cur <= lastPos; cur++)
            {
                var inr = ip + cur;
                assert(cur < 1 << 12);
                {
                    var litlen = opt[cur - 1].mlen == 0 ? opt[cur - 1].litlen + 1 : 1;
                    var price =
                        opt[cur - 1].price
                        + (int)ZSTD_rawLiteralsCost(ip + cur - 1, 1, optStatePtr, optLevel)
                        + (int)ZSTD_litLengthPrice(litlen, optStatePtr, optLevel)
                        - (int)ZSTD_litLengthPrice(litlen - 1, optStatePtr, optLevel);
                    assert(price < 1000000000);
                    if (price <= opt[cur].price)
                    {
                        opt[cur].mlen = 0;
                        opt[cur].off = 0;
                        opt[cur].litlen = litlen;
                        opt[cur].price = price;
                    }
                }

                assert(cur >= opt[cur].mlen);
                if (opt[cur].mlen != 0)
                {
                    var prev = cur - opt[cur].mlen;
                    var newReps = ZSTD_newRep(
                        opt[prev].rep,
                        opt[cur].off,
                        opt[cur].litlen == 0 ? 1U : 0U
                    );
                    memcpy(opt[cur].rep, &newReps, (uint)sizeof(RepcodesS));
                }
                else
                {
                    memcpy(opt[cur].rep, opt[cur - 1].rep, (uint)sizeof(RepcodesS));
                }

                if (inr > ilimit)
                    continue;
                if (cur == lastPos)
                    break;
                if (optLevel == 0 && opt[cur + 1].price <= opt[cur].price + (1 << 8) / 2)
                    continue;

                assert(opt[cur].price >= 0);
                {
                    var ll0 = opt[cur].mlen != 0 ? 1U : 0U;
                    var litlen = opt[cur].mlen == 0 ? opt[cur].litlen : 0;
                    var previousPrice = (uint)opt[cur].price;
                    var basePrice = previousPrice + ZSTD_litLengthPrice(0, optStatePtr, optLevel);
                    var nbMatches = (
                        (delegate* managed<
                            ZstdMatchT*,
                            ZstdMatchStateT*,
                            uint*,
                            byte*,
                            byte*,
                            uint*,
                            uint,
                            uint,
                            uint>)getAllMatches
                    )(matches, ms, &nextToUpdate3, inr, iend, opt[cur].rep, ll0, minMatch);
                    uint matchNb;
                    ZSTD_optLdm_processMatchCandidate(
                        &optLdm,
                        matches,
                        &nbMatches,
                        (uint)(inr - istart),
                        (uint)(iend - inr)
                    );
                    if (nbMatches == 0)
                        continue;

                    {
                        var maxMl = matches[nbMatches - 1].len;
                        if (maxMl > sufficientLen || cur + maxMl >= 1 << 12)
                        {
                            lastSequence.mlen = maxMl;
                            lastSequence.off = matches[nbMatches - 1].off;
                            lastSequence.litlen = litlen;
                            cur -= opt[cur].mlen == 0 ? opt[cur].litlen : 0;
                            lastPos = cur + ZSTD_totalLen(lastSequence);
                            if (cur > 1 << 12)
                                cur = 0;
                            goto _shortestPath;
                        }
                    }

                    for (matchNb = 0; matchNb < nbMatches; matchNb++)
                    {
                        var offset = matches[matchNb].off;
                        var lastMl = matches[matchNb].len;
                        var startMl = matchNb > 0 ? matches[matchNb - 1].len + 1 : minMatch;
                        uint mlen;
                        for (mlen = lastMl; mlen >= startMl; mlen--)
                        {
                            var pos = cur + mlen;
                            var price =
                                (int)basePrice
                                + (int)ZSTD_getMatchPrice(offset, mlen, optStatePtr, optLevel);
                            if (pos > lastPos || price < opt[pos].price)
                            {
                                while (lastPos < pos)
                                {
                                    opt[lastPos + 1].price = 1 << 30;
                                    lastPos++;
                                }

                                opt[pos].mlen = mlen;
                                opt[pos].off = offset;
                                opt[pos].litlen = litlen;
                                opt[pos].price = price;
                            }
                            else
                            {
                                if (optLevel == 0)
                                    break;
                            }
                        }
                    }
                }
            }

            lastSequence = opt[lastPos];
            cur =
                lastPos > ZSTD_totalLen(lastSequence) ? lastPos - ZSTD_totalLen(lastSequence) : 0;
            assert(cur < 1 << 12);
            _shortestPath:
            assert(opt[0].mlen == 0);
            if (lastSequence.mlen != 0)
            {
                var reps = ZSTD_newRep(
                    opt[cur].rep,
                    lastSequence.off,
                    lastSequence.litlen == 0 ? 1U : 0U
                );
                memcpy(rep, &reps, (uint)sizeof(RepcodesS));
            }
            else
            {
                memcpy(rep, opt[cur].rep, (uint)sizeof(RepcodesS));
            }

            {
                var storeEnd = cur + 1;
                var storeStart = storeEnd;
                var seqPos = cur;
                assert(storeEnd < 1 << 12);
                opt[storeEnd] = lastSequence;
                while (seqPos > 0)
                {
                    var backDist = ZSTD_totalLen(opt[seqPos]);
                    storeStart--;
                    opt[storeStart] = opt[seqPos];
                    seqPos = seqPos > backDist ? seqPos - backDist : 0;
                }

                {
                    uint storePos;
                    for (storePos = storeStart; storePos <= storeEnd; storePos++)
                    {
                        var llen = opt[storePos].litlen;
                        var mlen = opt[storePos].mlen;
                        var offBase = opt[storePos].off;
                        var advance = llen + mlen;
                        if (mlen == 0)
                        {
                            assert(storePos == storeEnd);
                            ip = anchor + llen;
                            continue;
                        }

                        assert(anchor + llen <= iend);
                        ZSTD_updateStats(optStatePtr, llen, anchor, offBase, mlen);
                        ZSTD_storeSeq(seqStore, llen, anchor, iend, offBase, mlen);
                        anchor += advance;
                        ip = anchor;
                    }
                }

                ZSTD_setBasePrices(optStatePtr, optLevel);
            }
        }

        return (nuint)(iend - anchor);
    }

    private static nuint ZSTD_compressBlock_opt0(
        ZstdMatchStateT* ms,
        SeqStoreT* seqStore,
        uint* rep,
        void* src,
        nuint srcSize,
        ZstdDictModeE dictMode
    )
    {
        return ZSTD_compressBlock_opt_generic(ms, seqStore, rep, src, srcSize, 0, dictMode);
    }

    private static nuint ZSTD_compressBlock_opt2(
        ZstdMatchStateT* ms,
        SeqStoreT* seqStore,
        uint* rep,
        void* src,
        nuint srcSize,
        ZstdDictModeE dictMode
    )
    {
        return ZSTD_compressBlock_opt_generic(ms, seqStore, rep, src, srcSize, 2, dictMode);
    }

    private static nuint ZSTD_compressBlock_btopt(
        ZstdMatchStateT* ms,
        SeqStoreT* seqStore,
        uint* rep,
        void* src,
        nuint srcSize
    )
    {
        return ZSTD_compressBlock_opt0(
            ms,
            seqStore,
            rep,
            src,
            srcSize,
            ZstdDictModeE.ZstdNoDict
        );
    }

    /* ZSTD_initStats_ultra():
     * make a first compression pass, just to seed stats with more accurate starting values.
     * only works on first block, with no dictionary and no ldm.
     * this function cannot error out, its narrow contract must be respected.
     */
    private static void ZSTD_initStats_ultra(
        ZstdMatchStateT* ms,
        SeqStoreT* seqStore,
        uint* rep,
        void* src,
        nuint srcSize
    )
    {
        /* updated rep codes will sink here */
        var tmpRep = stackalloc uint[3];
        memcpy(tmpRep, rep, sizeof(uint) * 3);
        assert(ms->opt.litLengthSum == 0);
        assert(seqStore->sequences == seqStore->sequencesStart);
        assert(ms->window.dictLimit == ms->window.lowLimit);
        assert(ms->window.dictLimit - ms->nextToUpdate <= 1);
        ZSTD_compressBlock_opt2(ms, seqStore, tmpRep, src, srcSize, ZstdDictModeE.ZstdNoDict);
        ZSTD_resetSeqStore(seqStore);
        ms->window.@base -= srcSize;
        ms->window.dictLimit += (uint)srcSize;
        ms->window.lowLimit = ms->window.dictLimit;
        ms->nextToUpdate = ms->window.dictLimit;
    }

    private static nuint ZSTD_compressBlock_btultra(
        ZstdMatchStateT* ms,
        SeqStoreT* seqStore,
        uint* rep,
        void* src,
        nuint srcSize
    )
    {
        return ZSTD_compressBlock_opt2(
            ms,
            seqStore,
            rep,
            src,
            srcSize,
            ZstdDictModeE.ZstdNoDict
        );
    }

    private static nuint ZSTD_compressBlock_btultra2(
        ZstdMatchStateT* ms,
        SeqStoreT* seqStore,
        uint* rep,
        void* src,
        nuint srcSize
    )
    {
        var curr = (uint)((byte*)src - ms->window.@base);
        assert(srcSize <= 1 << 17);
        if (
            ms->opt.litLengthSum == 0
            && seqStore->sequences == seqStore->sequencesStart
            && ms->window.dictLimit == ms->window.lowLimit
            && curr == ms->window.dictLimit
            && srcSize > 8
        )
            ZSTD_initStats_ultra(ms, seqStore, rep, src, srcSize);

        return ZSTD_compressBlock_opt2(
            ms,
            seqStore,
            rep,
            src,
            srcSize,
            ZstdDictModeE.ZstdNoDict
        );
    }

    private static nuint ZSTD_compressBlock_btopt_dictMatchState(
        ZstdMatchStateT* ms,
        SeqStoreT* seqStore,
        uint* rep,
        void* src,
        nuint srcSize
    )
    {
        return ZSTD_compressBlock_opt0(
            ms,
            seqStore,
            rep,
            src,
            srcSize,
            ZstdDictModeE.ZstdDictMatchState
        );
    }

    private static nuint ZSTD_compressBlock_btultra_dictMatchState(
        ZstdMatchStateT* ms,
        SeqStoreT* seqStore,
        uint* rep,
        void* src,
        nuint srcSize
    )
    {
        return ZSTD_compressBlock_opt2(
            ms,
            seqStore,
            rep,
            src,
            srcSize,
            ZstdDictModeE.ZstdDictMatchState
        );
    }

    private static nuint ZSTD_compressBlock_btopt_extDict(
        ZstdMatchStateT* ms,
        SeqStoreT* seqStore,
        uint* rep,
        void* src,
        nuint srcSize
    )
    {
        return ZSTD_compressBlock_opt0(
            ms,
            seqStore,
            rep,
            src,
            srcSize,
            ZstdDictModeE.ZstdExtDict
        );
    }

    private static nuint ZSTD_compressBlock_btultra_extDict(
        ZstdMatchStateT* ms,
        SeqStoreT* seqStore,
        uint* rep,
        void* src,
        nuint srcSize
    )
    {
        return ZSTD_compressBlock_opt2(
            ms,
            seqStore,
            rep,
            src,
            srcSize,
            ZstdDictModeE.ZstdExtDict
        );
    }
}