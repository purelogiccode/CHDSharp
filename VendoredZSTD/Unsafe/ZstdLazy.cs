using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;
using static VendoredZSTD.UnsafeHelper;

namespace VendoredZSTD.Unsafe;

public static unsafe partial class Methods
{
    /*-*************************************
     *  Binary Tree search
     ***************************************/
    private static void ZSTD_updateDUBT(ZstdMatchStateT* ms, byte* ip, byte* iend, uint mls)
    {
        var cParams = &ms->cParams;
        var hashTable = ms->hashTable;
        var hashLog = cParams->hashLog;
        var bt = ms->chainTable;
        var btLog = cParams->chainLog - 1;
        var btMask = (uint)((1 << (int)btLog) - 1);
        var @base = ms->window.@base;
        var target = (uint)(ip - @base);
        var idx = ms->nextToUpdate;
        assert(ip + 8 <= iend);
        assert(idx >= ms->window.dictLimit);
        for (; idx < target; idx++)
        {
            /* assumption : ip + 8 <= iend */
            var h = ZSTD_hashPtr(@base + idx, hashLog, mls);
            var matchIndex = hashTable[h];
            var nextCandidatePtr = bt + 2 * (idx & btMask);
            var sortMarkPtr = nextCandidatePtr + 1;
            hashTable[h] = idx;
            *nextCandidatePtr = matchIndex;
            *sortMarkPtr = 1;
        }

        ms->nextToUpdate = target;
    }

    /** ZSTD_insertDUBT1() :
     *  sort one already inserted but unsorted position
     *  assumption : curr >= btlow == (curr - btmask)
     *  doesn't fail */
    private static void ZSTD_insertDUBT1(ZstdMatchStateT* ms, uint curr, byte* inputEnd, uint nbCompares, uint btLow, ZstdDictModeE dictMode)
    {
        var cParams = &ms->cParams;
        var bt = ms->chainTable;
        var btLog = cParams->chainLog - 1;
        var btMask = (uint)((1 << (int)btLog) - 1);
        nuint commonLengthSmaller = 0, commonLengthLarger = 0;
        var @base = ms->window.@base;
        var dictBase = ms->window.dictBase;
        var dictLimit = ms->window.dictLimit;
        var ip = curr >= dictLimit ? @base + curr : dictBase + curr;
        var iend = curr >= dictLimit ? inputEnd : dictBase + dictLimit;
        var dictEnd = dictBase + dictLimit;
        var prefixStart = @base + dictLimit;
        byte* match;
        var smallerPtr = bt + 2 * (curr & btMask);
        var largerPtr = smallerPtr + 1;
        /* this candidate is unsorted : next sorted candidate is reached through *smallerPtr, while *largerPtr contains previous unsorted candidate (which is already saved and can be overwritten) */
        var matchIndex = *smallerPtr;
        /* to be nullified at the end */
        uint dummy32;
        var windowValid = ms->window.lowLimit;
        var maxDistance = 1U << (int)cParams->windowLog;
        var windowLow = curr - windowValid > maxDistance ? curr - maxDistance : windowValid;
        assert(curr >= btLow);
        assert(ip < iend);
        for (; nbCompares != 0 && matchIndex > windowLow; --nbCompares)
        {
            var nextPtr = bt + 2 * (matchIndex & btMask);
            /* guaranteed minimum nb of common bytes */
            var matchLength = commonLengthSmaller < commonLengthLarger ? commonLengthSmaller : commonLengthLarger;
            assert(matchIndex < curr);
            if (dictMode != ZstdDictModeE.ZstdExtDict || matchIndex + matchLength >= dictLimit || curr < dictLimit)
            {
                var mBase = dictMode != ZstdDictModeE.ZstdExtDict || matchIndex + matchLength >= dictLimit ? @base : dictBase;
                assert(matchIndex + matchLength >= dictLimit || curr < dictLimit);
                match = mBase + matchIndex;
                matchLength += ZSTD_count(ip + matchLength, match + matchLength, iend);
            }
            else
            {
                match = dictBase + matchIndex;
                matchLength += ZSTD_count_2segments(ip + matchLength, match + matchLength, iend, dictEnd, prefixStart);
                if (matchIndex + matchLength >= dictLimit)
                {
                    match = @base + matchIndex;
                }
            }

            if (ip + matchLength == iend)
            {
                break;
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
    }

    private static nuint ZSTD_DUBT_findBetterDictMatch(ZstdMatchStateT* ms, byte* ip, byte* iend, nuint* offsetPtr, nuint bestLength, uint nbCompares, uint mls, ZstdDictModeE dictMode)
    {
        var dms = ms->dictMatchState;
        var dmsCParams = &dms->cParams;
        var dictHashTable = dms->hashTable;
        var hashLog = dmsCParams->hashLog;
        var h = ZSTD_hashPtr(ip, hashLog, mls);
        var dictMatchIndex = dictHashTable[h];
        var @base = ms->window.@base;
        var prefixStart = @base + ms->window.dictLimit;
        var curr = (uint)(ip - @base);
        var dictBase = dms->window.@base;
        var dictEnd = dms->window.nextSrc;
        var dictHighLimit = (uint)(dms->window.nextSrc - dms->window.@base);
        var dictLowLimit = dms->window.lowLimit;
        var dictIndexDelta = ms->window.lowLimit - dictHighLimit;
        var dictBt = dms->chainTable;
        var btLog = dmsCParams->chainLog - 1;
        var btMask = (uint)((1 << (int)btLog) - 1);
        var btLow = btMask >= dictHighLimit - dictLowLimit ? dictLowLimit : dictHighLimit - btMask;
        nuint commonLengthSmaller = 0, commonLengthLarger = 0;
        assert(dictMode == ZstdDictModeE.ZstdDictMatchState);
        for (; nbCompares != 0 && dictMatchIndex > dictLowLimit; --nbCompares)
        {
            var nextPtr = dictBt + 2 * (dictMatchIndex & btMask);
            /* guaranteed minimum nb of common bytes */
            var matchLength = commonLengthSmaller < commonLengthLarger ? commonLengthSmaller : commonLengthLarger;
            var match = dictBase + dictMatchIndex;
            matchLength += ZSTD_count_2segments(ip + matchLength, match + matchLength, iend, dictEnd, prefixStart);
            if (dictMatchIndex + matchLength >= dictHighLimit)
            {
                match = @base + dictMatchIndex + dictIndexDelta;
            }

            if (matchLength > bestLength)
            {
                var matchIndex = dictMatchIndex + dictIndexDelta;
                if (4 * (int)(matchLength - bestLength) > (int)(ZSTD_highbit32(curr - matchIndex + 1) - ZSTD_highbit32((uint)offsetPtr[0] + 1)))
                {
                    bestLength = matchLength;
                    assert(curr - matchIndex > 0);
                    *offsetPtr = curr - matchIndex + 3;
                }

                if (ip + matchLength == iend)
                {
                    break;
                }
            }

            if (match[matchLength] < ip[matchLength])
            {
                if (dictMatchIndex <= btLow)
                {
                    break;
                }

                commonLengthSmaller = matchLength;
                dictMatchIndex = nextPtr[1];
            }
            else
            {
                if (dictMatchIndex <= btLow)
                {
                    break;
                }

                commonLengthLarger = matchLength;
                dictMatchIndex = nextPtr[0];
            }
        }

        if (bestLength >= 3)
        {
            assert(*offsetPtr > 3);
            var mIndex = curr - (uint)(*offsetPtr - 3);
        }

        return bestLength;
    }

    private static nuint ZSTD_DUBT_findBestMatch(ZstdMatchStateT* ms, byte* ip, byte* iend, nuint* offBasePtr, uint mls, ZstdDictModeE dictMode)
    {
        var cParams = &ms->cParams;
        var hashTable = ms->hashTable;
        var hashLog = cParams->hashLog;
        var h = ZSTD_hashPtr(ip, hashLog, mls);
        var matchIndex = hashTable[h];
        var @base = ms->window.@base;
        var curr = (uint)(ip - @base);
        var windowLow = ZSTD_getLowestMatchIndex(ms, curr, cParams->windowLog);
        var bt = ms->chainTable;
        var btLog = cParams->chainLog - 1;
        var btMask = (uint)((1 << (int)btLog) - 1);
        var btLow = btMask >= curr ? 0 : curr - btMask;
        var unsortLimit = btLow > windowLow ? btLow : windowLow;
        var nextCandidate = bt + 2 * (matchIndex & btMask);
        var unsortedMark = bt + 2 * (matchIndex & btMask) + 1;
        var nbCompares = 1U << (int)cParams->searchLog;
        var nbCandidates = nbCompares;
        uint previousCandidate = 0;
        assert(ip <= iend - 8);
        assert(dictMode != ZstdDictModeE.ZstdDedicatedDictSearch);
        while (matchIndex > unsortLimit && *unsortedMark == 1 && nbCandidates > 1)
        {
            *unsortedMark = previousCandidate;
            previousCandidate = matchIndex;
            matchIndex = *nextCandidate;
            nextCandidate = bt + 2 * (matchIndex & btMask);
            unsortedMark = bt + 2 * (matchIndex & btMask) + 1;
            nbCandidates--;
        }

        if (matchIndex > unsortLimit && *unsortedMark == 1)
        {
            *nextCandidate = *unsortedMark = 0;
        }

        matchIndex = previousCandidate;
        while (matchIndex != 0)
        {
            var nextCandidateIdxPtr = bt + 2 * (matchIndex & btMask) + 1;
            var nextCandidateIdx = *nextCandidateIdxPtr;
            ZSTD_insertDUBT1(ms, matchIndex, iend, nbCandidates, unsortLimit, dictMode);
            matchIndex = nextCandidateIdx;
            nbCandidates++;
        }

        {
            nuint commonLengthSmaller = 0, commonLengthLarger = 0;
            var dictBase = ms->window.dictBase;
            var dictLimit = ms->window.dictLimit;
            var dictEnd = dictBase + dictLimit;
            var prefixStart = @base + dictLimit;
            var smallerPtr = bt + 2 * (curr & btMask);
            var largerPtr = bt + 2 * (curr & btMask) + 1;
            var matchEndIdx = curr + 8 + 1;
            /* to be nullified at the end */
            uint dummy32;
            nuint bestLength = 0;
            matchIndex = hashTable[h];
            hashTable[h] = curr;
            for (; nbCompares != 0 && matchIndex > windowLow; --nbCompares)
            {
                var nextPtr = bt + 2 * (matchIndex & btMask);
                /* guaranteed minimum nb of common bytes */
                var matchLength = commonLengthSmaller < commonLengthLarger ? commonLengthSmaller : commonLengthLarger;
                byte* match;
                if (dictMode != ZstdDictModeE.ZstdExtDict || matchIndex + matchLength >= dictLimit)
                {
                    match = @base + matchIndex;
                    matchLength += ZSTD_count(ip + matchLength, match + matchLength, iend);
                }
                else
                {
                    match = dictBase + matchIndex;
                    matchLength += ZSTD_count_2segments(ip + matchLength, match + matchLength, iend, dictEnd, prefixStart);
                    if (matchIndex + matchLength >= dictLimit)
                    {
                        match = @base + matchIndex;
                    }
                }

                if (matchLength > bestLength)
                {
                    if (matchLength > matchEndIdx - matchIndex)
                    {
                        matchEndIdx = matchIndex + (uint)matchLength;
                    }

                    if (4 * (int)(matchLength - bestLength) > (int)(ZSTD_highbit32(curr - matchIndex + 1) - ZSTD_highbit32((uint)*offBasePtr)))
                    {
                        bestLength = matchLength;
                        assert(curr - matchIndex > 0);
                        *offBasePtr = curr - matchIndex + 3;
                    }

                    if (ip + matchLength == iend)
                    {
                        if (dictMode == ZstdDictModeE.ZstdDictMatchState)
                        {
                            nbCompares = 0;
                        }

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
                bestLength = ZSTD_DUBT_findBetterDictMatch(ms, ip, iend, offBasePtr, bestLength, nbCompares, mls, dictMode);
            }

            assert(matchEndIdx > curr + 8);
            ms->nextToUpdate = matchEndIdx - 8;
            if (bestLength >= 3)
            {
                assert(*offBasePtr > 3);
                var mIndex = curr - (uint)(*offBasePtr - 3);
            }

            return bestLength;
        }
    }

    /** ZSTD_BtFindBestMatch() : Tree updater, providing best match */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static nuint ZSTD_BtFindBestMatch(ZstdMatchStateT* ms, byte* ip, byte* iLimit, nuint* offBasePtr, uint mls, ZstdDictModeE dictMode)
    {
        if (ip < ms->window.@base + ms->nextToUpdate)
            return 0;

        ZSTD_updateDUBT(ms, ip, iLimit, mls);
        return ZSTD_DUBT_findBestMatch(ms, ip, iLimit, offBasePtr, mls, dictMode);
    }

    /***********************************
     * Dedicated dict search
     ***********************************/
    private static void ZSTD_dedicatedDictSearch_lazy_loadDictionary(ZstdMatchStateT* ms, byte* ip)
    {
        var @base = ms->window.@base;
        var target = (uint)(ip - @base);
        var hashTable = ms->hashTable;
        var chainTable = ms->chainTable;
        var chainSize = (uint)(1 << (int)ms->cParams.chainLog);
        var idx = ms->nextToUpdate;
        var minChain = chainSize < target - idx ? target - chainSize : idx;
        const uint bucketSize = 1 << 2;
        var cacheSize = bucketSize - 1;
        var chainAttempts = (uint)(1 << (int)ms->cParams.searchLog) - cacheSize;
        var chainLimit = chainAttempts > 255 ? 255 : chainAttempts;
        /* We know the hashtable is oversized by a factor of `bucketSize`.
         * We are going to temporarily pretend `bucketSize == 1`, keeping only a
         * single entry. We will use the rest of the space to construct a temporary
         * chaintable.
         */
        var hashLog = ms->cParams.hashLog - 2;
        var tmpHashTable = hashTable;
        var tmpChainTable = hashTable + ((nuint)1 << (int)hashLog);
        var tmpChainSize = (uint)((1 << 2) - 1) << (int)hashLog;
        var tmpMinChain = tmpChainSize < target ? target - tmpChainSize : idx;
        uint hashIdx;
        assert(ms->cParams.chainLog <= 24);
        assert(ms->cParams.hashLog > ms->cParams.chainLog);
        assert(idx != 0);
        assert(tmpMinChain <= minChain);
        for (; idx < target; idx++)
        {
            var h = (uint)ZSTD_hashPtr(@base + idx, hashLog, ms->cParams.minMatch);
            if (idx >= tmpMinChain)
            {
                tmpChainTable[idx - tmpMinChain] = hashTable[h];
            }

            tmpHashTable[h] = idx;
        }

        {
            uint chainPos = 0;
            for (hashIdx = 0; hashIdx < 1U << (int)hashLog; hashIdx++)
            {
                uint count;
                uint countBeyondMinChain = 0;
                var i = tmpHashTable[hashIdx];
                for (count = 0; i >= tmpMinChain && count < cacheSize; count++)
                {
                    if (i < minChain)
                    {
                        countBeyondMinChain++;
                    }

                    i = tmpChainTable[i - tmpMinChain];
                }

                if (count == cacheSize)
                {
                    for (count = 0; count < chainLimit;)
                    {
                        if (i < minChain)
                        {
                            if (i == 0 || ++countBeyondMinChain > cacheSize)
                            {
                                break;
                            }
                        }

                        chainTable[chainPos++] = i;
                        count++;
                        if (i < tmpMinChain)
                        {
                            break;
                        }

                        i = tmpChainTable[i - tmpMinChain];
                    }
                }
                else
                {
                    count = 0;
                }

                if (count != 0)
                {
                    tmpHashTable[hashIdx] = ((chainPos - count) << 8) + count;
                }
                else
                {
                    tmpHashTable[hashIdx] = 0;
                }
            }

            assert(chainPos <= chainSize);
        }

        for (hashIdx = (uint)(1 << (int)hashLog); hashIdx != 0;)
        {
            var bucketIdx = --hashIdx << 2;
            var chainPackedPointer = tmpHashTable[hashIdx];
            uint i;
            for (i = 0; i < cacheSize; i++)
            {
                hashTable[bucketIdx + i] = 0;
            }

            hashTable[bucketIdx + bucketSize - 1] = chainPackedPointer;
        }

        for (idx = ms->nextToUpdate; idx < target; idx++)
        {
            var h = (uint)ZSTD_hashPtr(@base + idx, hashLog, ms->cParams.minMatch) << 2;
            uint i;
            for (i = cacheSize - 1; i != 0; i--)
            {
                hashTable[h + i] = hashTable[h + i - 1];
            }

            hashTable[h] = idx;
        }

        ms->nextToUpdate = target;
    }

    /* Returns the longest match length found in the dedicated dict search structure.
     * If none are longer than the argument ml, then ml will be returned.
     */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static nuint ZSTD_dedicatedDictSearch_lazy_search(nuint* offsetPtr, nuint ml, uint nbAttempts, ZstdMatchStateT* dms, byte* ip, byte* iLimit, byte* prefixStart, uint curr, uint dictLimit, nuint ddsIdx)
    {
        var ddsLowestIndex = dms->window.dictLimit;
        var ddsBase = dms->window.@base;
        var ddsEnd = dms->window.nextSrc;
        var ddsSize = (uint)(ddsEnd - ddsBase);
        var ddsIndexDelta = dictLimit - ddsSize;
        const uint bucketSize = 1 << 2;
        var bucketLimit = nbAttempts < bucketSize - 1 ? nbAttempts : bucketSize - 1;
        uint ddsAttempt;
        uint matchIndex;
        for (ddsAttempt = 0; ddsAttempt < bucketSize - 1; ddsAttempt++)
        {
#if NETCOREAPP3_0_OR_GREATER
            if (Sse.IsSupported)
            {
                Sse.Prefetch0(ddsBase + dms->hashTable[ddsIdx + ddsAttempt]);
            }
#endif
        }

        {
            var chainPackedPointer = dms->hashTable[ddsIdx + bucketSize - 1];
            var chainIndex = chainPackedPointer >> 8;
#if NETCOREAPP3_0_OR_GREATER
            if (Sse.IsSupported)
            {
                Sse.Prefetch0(&dms->chainTable[chainIndex]);
            }
#endif
        }

        for (ddsAttempt = 0; ddsAttempt < bucketLimit; ddsAttempt++)
        {
            nuint currentMl = 0;
            matchIndex = dms->hashTable[ddsIdx + ddsAttempt];
            var match = ddsBase + matchIndex;
            if (matchIndex == 0)
            {
                return ml;
            }

            assert(matchIndex >= ddsLowestIndex);
            assert(match + 4 <= ddsEnd);
            if (MEM_read32(match) == MEM_read32(ip))
            {
                currentMl = ZSTD_count_2segments(ip + 4, match + 4, iLimit, ddsEnd, prefixStart) + 4;
            }

            if (currentMl > ml)
            {
                ml = currentMl;
                assert(curr - (matchIndex + ddsIndexDelta) > 0);
                *offsetPtr = curr - (matchIndex + ddsIndexDelta) + 3;
                if (ip + currentMl == iLimit)
                {
                    return ml;
                }
            }
        }

        {
            var chainPackedPointer = dms->hashTable[ddsIdx + bucketSize - 1];
            var chainIndex = chainPackedPointer >> 8;
            var chainLength = chainPackedPointer & 0xFF;
            var chainAttempts = nbAttempts - ddsAttempt;
            var chainLimit = chainAttempts > chainLength ? chainLength : chainAttempts;
            uint chainAttempt;
            for (chainAttempt = 0; chainAttempt < chainLimit; chainAttempt++)
            {
#if NETCOREAPP3_0_OR_GREATER
                if (Sse.IsSupported)
                {
                    Sse.Prefetch0(ddsBase + dms->chainTable[chainIndex + chainAttempt]);
                }
#endif
            }

            for (chainAttempt = 0; chainAttempt < chainLimit; chainAttempt++, chainIndex++)
            {
                nuint currentMl = 0;
                matchIndex = dms->chainTable[chainIndex];
                var match = ddsBase + matchIndex;
                assert(matchIndex >= ddsLowestIndex);
                assert(match + 4 <= ddsEnd);
                if (MEM_read32(match) == MEM_read32(ip))
                {
                    currentMl = ZSTD_count_2segments(ip + 4, match + 4, iLimit, ddsEnd, prefixStart) + 4;
                }

                if (currentMl > ml)
                {
                    ml = currentMl;
                    assert(curr - (matchIndex + ddsIndexDelta) > 0);
                    *offsetPtr = curr - (matchIndex + ddsIndexDelta) + 3;
                    if (ip + currentMl == iLimit)
                        break;
                }
            }
        }

        return ml;
    }

    /* Update chains up to ip (excluded)
    Assumption : always within prefix (i.e. not within extDict) */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint ZSTD_insertAndFindFirstIndex_internal(ZstdMatchStateT* ms, ZstdCompressionParameters* cParams, byte* ip, uint mls, uint lazySkipping)
    {
        var hashTable = ms->hashTable;
        var hashLog = cParams->hashLog;
        var chainTable = ms->chainTable;
        var chainMask = (uint)((1 << (int)cParams->chainLog) - 1);
        var @base = ms->window.@base;
        var target = (uint)(ip - @base);
        var idx = ms->nextToUpdate;
        while (idx < target)
        {
            var h = ZSTD_hashPtr(@base + idx, hashLog, mls);
            chainTable[idx & chainMask] = hashTable[h];
            hashTable[h] = idx;
            idx++;
            if (lazySkipping != 0)
                break;
        }

        ms->nextToUpdate = target;
        return hashTable[ZSTD_hashPtr(ip, hashLog, mls)];
    }

    private static uint ZSTD_insertAndFindFirstIndex(ZstdMatchStateT* ms, byte* ip)
    {
        var cParams = &ms->cParams;
        return ZSTD_insertAndFindFirstIndex_internal(ms, cParams, ip, ms->cParams.minMatch, 0);
    }

    /* inlining is important to hardwire a hot branch (template emulation) */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static nuint ZSTD_HcFindBestMatch(ZstdMatchStateT* ms, byte* ip, byte* iLimit, nuint* offsetPtr, uint mls, ZstdDictModeE dictMode)
    {
        var cParams = &ms->cParams;
        var chainTable = ms->chainTable;
        var chainSize = (uint)(1 << (int)cParams->chainLog);
        var chainMask = chainSize - 1;
        var @base = ms->window.@base;
        var dictBase = ms->window.dictBase;
        var dictLimit = ms->window.dictLimit;
        var prefixStart = @base + dictLimit;
        var dictEnd = dictBase + dictLimit;
        var curr = (uint)(ip - @base);
        var maxDistance = 1U << (int)cParams->windowLog;
        var lowestValid = ms->window.lowLimit;
        var withinMaxDistance = curr - lowestValid > maxDistance ? curr - maxDistance : lowestValid;
        var isDictionary = ms->loadedDictEnd != 0 ? 1U : 0U;
        var lowLimit = isDictionary != 0 ? lowestValid : withinMaxDistance;
        var minChain = curr > chainSize ? curr - chainSize : 0;
        var nbAttempts = 1U << (int)cParams->searchLog;
        nuint ml = 4 - 1;
        var dms = ms->dictMatchState;
        var ddsHashLog = dictMode == ZstdDictModeE.ZstdDedicatedDictSearch ? dms->cParams.hashLog - 2 : 0;
        var ddsIdx = dictMode == ZstdDictModeE.ZstdDedicatedDictSearch ? ZSTD_hashPtr(ip, ddsHashLog, mls) << 2 : 0;
        if (dictMode == ZstdDictModeE.ZstdDedicatedDictSearch)
        {
            var entry = &dms->hashTable[ddsIdx];
#if NETCOREAPP3_0_OR_GREATER
            if (Sse.IsSupported)
            {
                Sse.Prefetch0(entry);
            }
#endif
        }

        var matchIndex = ZSTD_insertAndFindFirstIndex_internal(ms, cParams, ip, mls, (uint)ms->lazySkipping);
        for (; matchIndex >= lowLimit && nbAttempts > 0; nbAttempts--)
        {
            nuint currentMl = 0;
            if (dictMode != ZstdDictModeE.ZstdExtDict || matchIndex >= dictLimit)
            {
                var match = @base + matchIndex;
                assert(matchIndex >= dictLimit);
                if (MEM_read32(match + ml - 3) == MEM_read32(ip + ml - 3))
                {
                    currentMl = ZSTD_count(ip, match, iLimit);
                }
            }
            else
            {
                var match = dictBase + matchIndex;
                assert(match + 4 <= dictEnd);
                if (MEM_read32(match) == MEM_read32(ip))
                {
                    currentMl = ZSTD_count_2segments(ip + 4, match + 4, iLimit, dictEnd, prefixStart) + 4;
                }
            }

            if (currentMl > ml)
            {
                ml = currentMl;
                assert(curr - matchIndex > 0);
                *offsetPtr = curr - matchIndex + 3;
                if (ip + currentMl == iLimit)
                    break;
            }

            if (matchIndex <= minChain)
                break;

            matchIndex = chainTable[matchIndex & chainMask];
        }

        assert(nbAttempts <= 1U << ((sizeof(nuint) == 4 ? 30 : 31) - 1));
        if (dictMode == ZstdDictModeE.ZstdDedicatedDictSearch)
        {
            ml = ZSTD_dedicatedDictSearch_lazy_search(offsetPtr, ml, nbAttempts, dms, ip, iLimit, prefixStart, curr, dictLimit, ddsIdx);
        }
        else if (dictMode == ZstdDictModeE.ZstdDictMatchState)
        {
            var dmsChainTable = dms->chainTable;
            var dmsChainSize = (uint)(1 << (int)dms->cParams.chainLog);
            var dmsChainMask = dmsChainSize - 1;
            var dmsLowestIndex = dms->window.dictLimit;
            var dmsBase = dms->window.@base;
            var dmsEnd = dms->window.nextSrc;
            var dmsSize = (uint)(dmsEnd - dmsBase);
            var dmsIndexDelta = dictLimit - dmsSize;
            var dmsMinChain = dmsSize > dmsChainSize ? dmsSize - dmsChainSize : 0;
            matchIndex = dms->hashTable[ZSTD_hashPtr(ip, dms->cParams.hashLog, mls)];
            for (; matchIndex >= dmsLowestIndex && nbAttempts > 0; nbAttempts--)
            {
                nuint currentMl = 0;
                var match = dmsBase + matchIndex;
                assert(match + 4 <= dmsEnd);
                if (MEM_read32(match) == MEM_read32(ip))
                {
                    currentMl = ZSTD_count_2segments(ip + 4, match + 4, iLimit, dmsEnd, prefixStart) + 4;
                }

                if (currentMl > ml)
                {
                    ml = currentMl;
                    assert(curr > matchIndex + dmsIndexDelta);
                    assert(curr - (matchIndex + dmsIndexDelta) > 0);
                    *offsetPtr = curr - (matchIndex + dmsIndexDelta) + 3;
                    if (ip + currentMl == iLimit)
                        break;
                }

                if (matchIndex <= dmsMinChain)
                    break;

                matchIndex = dmsChainTable[matchIndex & dmsChainMask];
            }
        }

        return ml;
    }

    /* ZSTD_VecMask_next():
     * Starting from the LSB, returns the idx of the next non-zero bit.
     * Basically counting the nb of trailing zeroes.
     */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [InlineMethod.Inline]
    private static uint ZSTD_VecMask_next(ulong val)
    {
        assert(val != 0);
        return (uint)BitOperations.TrailingZeroCount(val);
    }

    /* ZSTD_row_nextIndex():
     * Returns the next index to insert at within a tagTable row, and updates the "head"
     * value to reflect the update. Essentially cycles backwards from [1, {entries per row})
     */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint ZSTD_row_nextIndex(byte* tagRow, uint rowMask)
    {
        var next = (uint)(*tagRow - 1) & rowMask;
        next += next == 0 ? rowMask : 0;
        *tagRow = (byte)next;
        return next;
    }

    /* ZSTD_isAligned():
     * Checks that a pointer is aligned to "align" bytes which must be a power of 2.
     */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ZSTD_isAligned(void* ptr, nuint align)
    {
        assert((align & (align - 1)) == 0);
        return ((nuint)ptr & (align - 1)) == 0 ? 1 : 0;
    }

    /* ZSTD_row_prefetch():
     * Performs prefetching for the hashTable and tagTable at a given row.
     */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ZSTD_row_prefetch(uint* hashTable, byte* tagTable, uint relRow, uint rowLog)
    {
#if NETCOREAPP3_0_OR_GREATER
        if (Sse.IsSupported)
        {
            Sse.Prefetch0(hashTable + relRow);
        }
#endif

        if (rowLog >= 5)
        {
#if NETCOREAPP3_0_OR_GREATER
            if (Sse.IsSupported)
            {
                Sse.Prefetch0(hashTable + relRow + 16);
            }
#endif
        }

#if NETCOREAPP3_0_OR_GREATER
        if (Sse.IsSupported)
        {
            Sse.Prefetch0(tagTable + relRow);
        }
#endif

        if (rowLog == 6)
        {
#if NETCOREAPP3_0_OR_GREATER
            if (Sse.IsSupported)
            {
                Sse.Prefetch0(tagTable + relRow + 32);
            }
#endif
        }

        assert(rowLog is 4 or 5 or 6);
        assert(ZSTD_isAligned(hashTable + relRow, 64) != 0);
        assert(ZSTD_isAligned(tagTable + relRow, (nuint)1 << (int)rowLog) != 0);
    }

    /* ZSTD_row_fillHashCache():
     * Fill up the hash cache starting at idx, prefetching up to ZSTD_ROW_HASH_CACHE_SIZE entries,
     * but not beyond iLimit.
     */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ZSTD_row_fillHashCache(ZstdMatchStateT* ms, byte* @base, uint rowLog, uint mls, uint idx, byte* iLimit)
    {
        var hashTable = ms->hashTable;
        var tagTable = ms->tagTable;
        var hashLog = ms->rowHashLog;
        var maxElemsToPrefetch = @base + idx > iLimit ? 0 : (uint)(iLimit - (@base + idx) + 1);
        var lim = idx + (8 < maxElemsToPrefetch ? 8 : maxElemsToPrefetch);
        for (; idx < lim; ++idx)
        {
            var hash = (uint)ZSTD_hashPtrSalted(@base + idx, hashLog + 8, mls, ms->hashSalt);
            var row = (hash >> 8) << (int)rowLog;
            ZSTD_row_prefetch(hashTable, tagTable, row, rowLog);
            ms->hashCache[idx & (8 - 1)] = hash;
        }
    }

    /* ZSTD_row_nextCachedHash():
     * Returns the hash of base + idx, and replaces the hash in the hash cache with the byte at
     * base + idx + ZSTD_ROW_HASH_CACHE_SIZE. Also prefetches the appropriate rows from hashTable and tagTable.
     */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint ZSTD_row_nextCachedHash(uint* cache, uint* hashTable, byte* tagTable, byte* @base, uint idx, uint hashLog, uint rowLog, uint mls, ulong hashSalt)
    {
        var newHash = (uint)ZSTD_hashPtrSalted(@base + idx + 8, hashLog + 8, mls, hashSalt);
        var row = (newHash >> 8) << (int)rowLog;
        ZSTD_row_prefetch(hashTable, tagTable, row, rowLog);
        {
            var hash = cache[idx & (8 - 1)];
            cache[idx & (8 - 1)] = newHash;
            return hash;
        }
    }

    /* ZSTD_row_update_internalImpl():
     * Updates the hash table with positions starting from updateStartIdx until updateEndIdx.
     */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ZSTD_row_update_internalImpl(ZstdMatchStateT* ms, uint updateStartIdx, uint updateEndIdx, uint mls, uint rowLog, uint rowMask, uint useCache)
    {
        var hashTable = ms->hashTable;
        var tagTable = ms->tagTable;
        var hashLog = ms->rowHashLog;
        var @base = ms->window.@base;
        for (; updateStartIdx < updateEndIdx; ++updateStartIdx)
        {
            var hash = useCache != 0 ? ZSTD_row_nextCachedHash(ms->hashCache, hashTable, tagTable, @base, updateStartIdx, hashLog, rowLog, mls, ms->hashSalt) : (uint)ZSTD_hashPtrSalted(@base + updateStartIdx, hashLog + 8, mls, ms->hashSalt);
            var relRow = (hash >> 8) << (int)rowLog;
            var row = hashTable + relRow;
            var tagRow = tagTable + relRow;
            var pos = ZSTD_row_nextIndex(tagRow, rowMask);
            assert(hash == ZSTD_hashPtrSalted(@base + updateStartIdx, hashLog + 8, mls, ms->hashSalt));
            tagRow[pos] = (byte)(hash & ((1U << 8) - 1));
            row[pos] = updateStartIdx;
        }
    }

    /* ZSTD_row_update_internal():
     * Inserts the byte at ip into the appropriate position in the hash table, and updates ms->nextToUpdate.
     * Skips sections of long matches as is necessary.
     */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ZSTD_row_update_internal(ZstdMatchStateT* ms, byte* ip, uint mls, uint rowLog, uint rowMask, uint useCache)
    {
        var idx = ms->nextToUpdate;
        var @base = ms->window.@base;
        var target = (uint)(ip - @base);
        const uint kSkipThreshold = 384;
        const uint kMaxMatchStartPositionsToUpdate = 96;
        const uint kMaxMatchEndPositionsToUpdate = 32;
        if (useCache != 0)
        {
            if (target - idx > kSkipThreshold)
            {
                var bound = idx + kMaxMatchStartPositionsToUpdate;
                ZSTD_row_update_internalImpl(ms, idx, bound, mls, rowLog, rowMask, useCache);
                idx = target - kMaxMatchEndPositionsToUpdate;
                ZSTD_row_fillHashCache(ms, @base, rowLog, mls, idx, ip + 1);
            }
        }

        assert(target >= idx);
        ZSTD_row_update_internalImpl(ms, idx, target, mls, rowLog, rowMask, useCache);
        ms->nextToUpdate = target;
    }

    /* ZSTD_row_update():
     * External wrapper for ZSTD_row_update_internal(). Used for filling the hashtable during dictionary
     * processing.
     */
    private static void ZSTD_row_update(ZstdMatchStateT* ms, byte* ip)
    {
        var rowLog = ms->cParams.searchLog <= 4 ? 4 : ms->cParams.searchLog <= 6 ? ms->cParams.searchLog : 6;
        var rowMask = (1U << (int)rowLog) - 1;
        /* mls caps out at 6 */
        var mls = ms->cParams.minMatch < 6 ? ms->cParams.minMatch : 6;
        ZSTD_row_update_internal(ms, ip, mls, rowLog, rowMask, 0);
    }

    /* Returns the mask width of bits group of which will be set to 1. Given not all
     * architectures have easy movemask instruction, this helps to iterate over
     * groups of bits easier and faster.
     */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint ZSTD_row_matchMaskGroupWidth(uint rowEntries)
    {
        assert(rowEntries is 16 or 32 or 64);
        assert(rowEntries <= 64);
#if NET5_0_OR_GREATER
        if (AdvSimd.IsSupported && BitConverter.IsLittleEndian)
        {
            if (rowEntries == 16)
                return 4;
#if NET9_0_OR_GREATER
            if (AdvSimd.Arm64.IsSupported)
            {
                if (rowEntries == 32)
                    return 2;
                if (rowEntries == 64)
                    return 1;
            }
#endif
        }
#endif
        return 1;
    }

#if NETCOREAPP3_0_OR_GREATER
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong ZSTD_row_getSSEMask(int nbChunks, byte* src, byte tag, uint head)
    {
        var comparisonMask = Vector128.Create(tag);
        assert(nbChunks is 1 or 2 or 4);
        if (nbChunks == 1)
        {
            var chunk0 = Sse2.LoadVector128(src);
            var equalMask0 = Sse2.CompareEqual(chunk0, comparisonMask);
            var matches0 = Sse2.MoveMask(equalMask0);
            return BitOperations.RotateRight((ushort)matches0, (int)head);
        }

        if (nbChunks == 2)
        {
            var chunk0 = Sse2.LoadVector128(src);
            var equalMask0 = Sse2.CompareEqual(chunk0, comparisonMask);
            var matches0 = Sse2.MoveMask(equalMask0);
            var chunk1 = Sse2.LoadVector128(src + 16);
            var equalMask1 = Sse2.CompareEqual(chunk1, comparisonMask);
            var matches1 = Sse2.MoveMask(equalMask1);
            return BitOperations.RotateRight(((uint)matches1 << 16) | (uint)matches0, (int)head);
        }

        {
            var chunk0 = Sse2.LoadVector128(src);
            var equalMask0 = Sse2.CompareEqual(chunk0, comparisonMask);
            var matches0 = Sse2.MoveMask(equalMask0);
            var chunk1 = Sse2.LoadVector128(src + 16 * 1);
            var equalMask1 = Sse2.CompareEqual(chunk1, comparisonMask);
            var matches1 = Sse2.MoveMask(equalMask1);
            var chunk2 = Sse2.LoadVector128(src + 16 * 2);
            var equalMask2 = Sse2.CompareEqual(chunk2, comparisonMask);
            var matches2 = Sse2.MoveMask(equalMask2);
            var chunk3 = Sse2.LoadVector128(src + 16 * 3);
            var equalMask3 = Sse2.CompareEqual(chunk3, comparisonMask);
            var matches3 = Sse2.MoveMask(equalMask3);
            return BitOperations.RotateRight(((ulong)matches3 << 48) | ((ulong)matches2 << 32) | ((ulong)matches1 << 16) | (uint)matches0, (int)head);
        }
    }
#endif

    /* Returns a ZSTD_VecMask (U64) that has the nth group (determined by
     * ZSTD_row_matchMaskGroupWidth) of bits set to 1 if the newly-computed "tag"
     * matches the hash at the nth position in a row of the tagTable.
     * Each row is a circular buffer beginning at the value of "headGrouped". So we
     * must rotate the "matches" bitfield to match up with the actual layout of the
     * entries within the hashTable */
#pragma warning disable MA0140
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong ZSTD_row_getMatchMask(byte* tagRow, byte tag, uint headGrouped, uint rowEntries)
    {
        var src = tagRow;
        assert(rowEntries is 16 or 32 or 64);
        assert(rowEntries <= 64);
        assert(ZSTD_row_matchMaskGroupWidth(rowEntries) * rowEntries <= sizeof(ulong) * 8);
#if NETCOREAPP3_0_OR_GREATER
        if (Sse2.IsSupported)
        {
            return ZSTD_row_getSSEMask((int)(rowEntries / 16), src, tag, headGrouped);
        }
#endif

#if NET5_0_OR_GREATER
        if (AdvSimd.IsSupported && BitConverter.IsLittleEndian)
        {
            if (rowEntries == 16)
            {
                /* vshrn_n_u16 shifts by 4 every u16 and narrows to 8 lower bits.
                 * After that groups of 4 bits represent the equalMask. We lower
                 * all bits except the highest in these groups by doing AND with
                 * 0x88 = 0b10001000.
                 */
                var chunk = AdvSimd.LoadVector128(src);
                var equalMask = AdvSimd.CompareEqual(chunk, AdvSimd.DuplicateToVector128(tag)).As<byte, ushort>();
                var res = AdvSimd.ShiftRightLogicalNarrowingLower(equalMask, 4);
                var matches = res.As<byte, ulong>().GetElement(0);
                return BitOperations.RotateRight(matches, (int)headGrouped) & 0x8888888888888888;
            }
            else if (rowEntries == 32)
            {
#if NET9_0_OR_GREATER
                if (AdvSimd.Arm64.IsSupported)
                {
                    /* Same idea as with rowEntries == 16 but doing AND with
                     * 0x55 = 0b01010101.
                     */
                    (var chunk0, var chunk1) = AdvSimd.Arm64.Load2xVector128AndUnzip((ushort*)src);
                    var dup = AdvSimd.DuplicateToVector128(tag);
                    var t0 = AdvSimd.ShiftRightLogicalNarrowingLower(AdvSimd.CompareEqual(chunk0.As<ushort, byte>(), dup).As<byte, ushort>(), 6);
                    var t1 = AdvSimd.ShiftRightLogicalNarrowingLower(AdvSimd.CompareEqual(chunk1.As<ushort, byte>(), dup).As<byte, ushort>(), 6);
                    var res = AdvSimd.ShiftLeftAndInsert(t0, t1, 4);
                    var matches = res.As<byte, ulong>().GetElement(0);
                    return BitOperations.RotateRight(matches, (int)headGrouped) & 0x5555555555555555;
                }
#endif
            }
            else
            { /* rowEntries == 64 */
#if NET9_0_OR_GREATER
                if (AdvSimd.Arm64.IsSupported)
                {
                    (var chunk0, var chunk1, var chunk2, var chunk3) = AdvSimd.Arm64.Load4xVector128AndUnzip(src);
                    var dup = AdvSimd.DuplicateToVector128(tag);
                    var cmp0 = AdvSimd.CompareEqual(chunk0, dup);
                    var cmp1 = AdvSimd.CompareEqual(chunk1, dup);
                    var cmp2 = AdvSimd.CompareEqual(chunk2, dup);
                    var cmp3 = AdvSimd.CompareEqual(chunk3, dup);

                    var t0 = AdvSimd.ShiftRightAndInsert(cmp1, cmp0, 1);
                    var t1 = AdvSimd.ShiftRightAndInsert(cmp3, cmp2, 1);
                    var t2 = AdvSimd.ShiftRightAndInsert(t1, t0, 2);
                    var t3 = AdvSimd.ShiftRightAndInsert(t2, t2, 4);
                    var t4 = AdvSimd.ShiftRightLogicalNarrowingLower(t3.As<byte, ushort>(), 4);
                    var matches = t4.As<byte, ulong>().GetElement(0);
                    return BitOperations.RotateRight(matches, (int) headGrouped);
                }
#endif
            }
        }
#endif

        {
            var chunkSize = (nuint)sizeof(nuint);
            var shiftAmount = chunkSize * 8 - chunkSize;
            var xFf = ~(nuint)0;
            var x01 = xFf / 0xFF;
            var x80 = x01 << 7;
            var splatChar = tag * x01;
            ulong matches = 0;
            var i = (int)(rowEntries - chunkSize);
            assert(sizeof(nuint) == 4 || sizeof(nuint) == 8);
            if (BitConverter.IsLittleEndian)
            {
                var extractMagic = (xFf / 0x7F) >> (int)chunkSize;
                do
                {
                    var chunk = MEM_readST(&src[i]);
                    chunk ^= splatChar;
                    chunk = (((chunk | x80) - x01) | chunk) & x80;
                    matches <<= (int)chunkSize;
                    matches |= (chunk * extractMagic) >> (int)shiftAmount;
                    i -= (int)chunkSize;
                }
                while (i >= 0);
            }
            else
            {
                var msb = xFf ^ (xFf >> 1);
                var extractMagic = (msb / 0x1FF) | msb;
                do
                {
                    var chunk = MEM_readST(&src[i]);
                    chunk ^= splatChar;
                    chunk = (((chunk | x80) - x01) | chunk) & x80;
                    matches <<= (int)chunkSize;
                    matches |= ((chunk >> 7) * extractMagic) >> (int)shiftAmount;
                    i -= (int)chunkSize;
                }
                while (i >= 0);
            }

            matches = ~matches;
            if (rowEntries == 16)
            {
                return BitOperations.RotateRight((ushort)matches, (int)headGrouped);
            }
            else if (rowEntries == 32)
            {
                return BitOperations.RotateRight((uint)matches, (int)headGrouped);
            }
            else
            {
                return BitOperations.RotateRight(matches, (int)headGrouped);
            }
        }
    }
#pragma warning restore MA0140

    /* The high-level approach of the SIMD row based match finder is as follows:
     * - Figure out where to insert the new entry:
     *      - Generate a hash for current input position and split it into a one byte of tag and `rowHashLog` bits of index.
     *           - The hash is salted by a value that changes on every context reset, so when the same table is used
     *             we will avoid collisions that would otherwise slow us down by introducing phantom matches.
     *      - The hashTable is effectively split into groups or "rows" of 15 or 31 entries of U32, and the index determines
     *        which row to insert into.
     *      - Determine the correct position within the row to insert the entry into. Each row of 15 or 31 can
     *        be considered as a circular buffer with a "head" index that resides in the tagTable (overall 16 or 32 bytes
     *        per row).
     * - Use SIMD to efficiently compare the tags in the tagTable to the 1-byte tag calculated for the position and
     *   generate a bitfield that we can cycle through to check the collisions in the hash table.
     * - Pick the longest match.
     * - Insert the tag into the equivalent row and position in the tagTable.
     */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [InlineMethod.Inline]
    private static nuint ZSTD_RowFindBestMatch(ZstdMatchStateT* ms, byte* ip, byte* iLimit, nuint* offsetPtr, uint mls, ZstdDictModeE dictMode, uint rowLog)
    {
        var hashTable = ms->hashTable;
        var tagTable = ms->tagTable;
        var hashCache = ms->hashCache;
        var hashLog = ms->rowHashLog;
        var cParams = &ms->cParams;
        var @base = ms->window.@base;
        var dictBase = ms->window.dictBase;
        var dictLimit = ms->window.dictLimit;
        var prefixStart = @base + dictLimit;
        var dictEnd = dictBase + dictLimit;
        var curr = (uint)(ip - @base);
        var maxDistance = 1U << (int)cParams->windowLog;
        var lowestValid = ms->window.lowLimit;
        var withinMaxDistance = curr - lowestValid > maxDistance ? curr - maxDistance : lowestValid;
        var isDictionary = ms->loadedDictEnd != 0 ? 1U : 0U;
        var lowLimit = isDictionary != 0 ? lowestValid : withinMaxDistance;
        var rowEntries = 1U << (int)rowLog;
        var rowMask = rowEntries - 1;
        /* nb of searches is capped at nb entries per row */
        var cappedSearchLog = cParams->searchLog < rowLog ? cParams->searchLog : rowLog;
        var groupWidth = ZSTD_row_matchMaskGroupWidth(rowEntries);
        var hashSalt = ms->hashSalt;
        var nbAttempts = 1U << (int)cappedSearchLog;
        nuint ml = 4 - 1;
        uint hash;
        /* DMS/DDS variables that may be referenced laster */
        var dms = ms->dictMatchState;
        /* Initialize the following variables to satisfy static analyzer */
        nuint ddsIdx = 0;
        /* cctx hash tables are limited in searches, but allow extra searches into DDS */
        uint ddsExtraAttempts = 0;
        uint dmsTag = 0;
        uint* dmsRow = null;
        byte* dmsTagRow = null;
        if (dictMode == ZstdDictModeE.ZstdDedicatedDictSearch)
        {
            var ddsHashLog = dms->cParams.hashLog - 2;
            {
                ddsIdx = ZSTD_hashPtr(ip, ddsHashLog, mls) << 2;
#if NETCOREAPP3_0_OR_GREATER
                if (Sse.IsSupported)
                {
                    Sse.Prefetch0(&dms->hashTable[ddsIdx]);
                }
#endif
            }

            ddsExtraAttempts = cParams->searchLog > rowLog ? 1U << (int)(cParams->searchLog - rowLog) : 0;
        }

        if (dictMode == ZstdDictModeE.ZstdDictMatchState)
        {
            /* Prefetch DMS rows */
            var dmsHashTable = dms->hashTable;
            var dmsTagTable = dms->tagTable;
            var dmsHash = (uint)ZSTD_hashPtr(ip, dms->rowHashLog + 8, mls);
            var dmsRelRow = (dmsHash >> 8) << (int)rowLog;
            dmsTag = dmsHash & ((1U << 8) - 1);
            dmsTagRow = dmsTagTable + dmsRelRow;
            dmsRow = dmsHashTable + dmsRelRow;
            ZSTD_row_prefetch(dmsHashTable, dmsTagTable, dmsRelRow, rowLog);
        }

        if (ms->lazySkipping == 0)
        {
            ZSTD_row_update_internal(ms, ip, mls, rowLog, rowMask, 1);
            hash = ZSTD_row_nextCachedHash(hashCache, hashTable, tagTable, @base, curr, hashLog, rowLog, mls, hashSalt);
        }
        else
        {
            hash = (uint)ZSTD_hashPtrSalted(ip, hashLog + 8, mls, hashSalt);
            ms->nextToUpdate = curr;
        }

        ms->hashSaltEntropy += hash;
        {
            var relRow = (hash >> 8) << (int)rowLog;
            var tag = hash & ((1U << 8) - 1);
            var row = hashTable + relRow;
            var tagRow = tagTable + relRow;
            var headGrouped = (*tagRow & rowMask) * groupWidth;
            var matchBuffer = stackalloc uint[64];
            nuint numMatches = 0;
            nuint currMatch = 0;
            var matches = ZSTD_row_getMatchMask(tagRow, (byte)tag, headGrouped, rowEntries);
            for (; matches > 0 && nbAttempts > 0; matches &= matches - 1)
            {
                var matchPos = ((headGrouped + ZSTD_VecMask_next(matches)) / groupWidth) & rowMask;
                var matchIndex = row[matchPos];
                if (matchPos == 0)
                    continue;

                assert(numMatches < rowEntries);
                if (matchIndex < lowLimit)
                    break;

                if (dictMode != ZstdDictModeE.ZstdExtDict || matchIndex >= dictLimit)
                {
#if NETCOREAPP3_0_OR_GREATER
                    if (Sse.IsSupported)
                    {
                        Sse.Prefetch0(@base + matchIndex);
                    }
#endif
                }
                else
                {
#if NETCOREAPP3_0_OR_GREATER
                    if (Sse.IsSupported)
                    {
                        Sse.Prefetch0(dictBase + matchIndex);
                    }
#endif
                }

                matchBuffer[numMatches++] = matchIndex;
                --nbAttempts;
            }

            {
                var pos = ZSTD_row_nextIndex(tagRow, rowMask);
                tagRow[pos] = (byte)tag;
                row[pos] = ms->nextToUpdate++;
            }

            for (; currMatch < numMatches; ++currMatch)
            {
                var matchIndex = matchBuffer[currMatch];
                nuint currentMl = 0;
                assert(matchIndex < curr);
                assert(matchIndex >= lowLimit);
                if (dictMode != ZstdDictModeE.ZstdExtDict || matchIndex >= dictLimit)
                {
                    var match = @base + matchIndex;
                    assert(matchIndex >= dictLimit);
                    if (MEM_read32(match + ml - 3) == MEM_read32(ip + ml - 3))
                    {
                        currentMl = ZSTD_count(ip, match, iLimit);
                    }
                }
                else
                {
                    var match = dictBase + matchIndex;
                    assert(match + 4 <= dictEnd);
                    if (MEM_read32(match) == MEM_read32(ip))
                    {
                        currentMl = ZSTD_count_2segments(ip + 4, match + 4, iLimit, dictEnd, prefixStart) + 4;
                    }
                }

                if (currentMl > ml)
                {
                    ml = currentMl;
                    assert(curr - matchIndex > 0);
                    *offsetPtr = curr - matchIndex + 3;
                    if (ip + currentMl == iLimit)
                        break;
                }
            }
        }

        assert(nbAttempts <= 1U << ((sizeof(nuint) == 4 ? 30 : 31) - 1));
        if (dictMode == ZstdDictModeE.ZstdDedicatedDictSearch)
        {
            ml = ZSTD_dedicatedDictSearch_lazy_search(offsetPtr, ml, nbAttempts + ddsExtraAttempts, dms, ip, iLimit, prefixStart, curr, dictLimit, ddsIdx);
        }
        else if (dictMode == ZstdDictModeE.ZstdDictMatchState)
        {
            /* TODO: Measure and potentially add prefetching to DMS */
            var dmsLowestIndex = dms->window.dictLimit;
            var dmsBase = dms->window.@base;
            var dmsEnd = dms->window.nextSrc;
            var dmsSize = (uint)(dmsEnd - dmsBase);
            var dmsIndexDelta = dictLimit - dmsSize;
            {
                var headGrouped = (*dmsTagRow & rowMask) * groupWidth;
                var matchBuffer = stackalloc uint[64];
                nuint numMatches = 0;
                nuint currMatch = 0;
                var matches = ZSTD_row_getMatchMask(dmsTagRow, (byte)dmsTag, headGrouped, rowEntries);
                for (; matches > 0 && nbAttempts > 0; matches &= matches - 1)
                {
                    var matchPos = ((headGrouped + ZSTD_VecMask_next(matches)) / groupWidth) & rowMask;
                    var matchIndex = dmsRow[matchPos];
                    if (matchPos == 0)
                        continue;

                    if (matchIndex < dmsLowestIndex)
                        break;
#if NETCOREAPP3_0_OR_GREATER
                    if (Sse.IsSupported)
                    {
                        Sse.Prefetch0(dmsBase + matchIndex);
                    }
#endif

                    matchBuffer[numMatches++] = matchIndex;
                    --nbAttempts;
                }

                for (; currMatch < numMatches; ++currMatch)
                {
                    var matchIndex = matchBuffer[currMatch];
                    nuint currentMl = 0;
                    assert(matchIndex >= dmsLowestIndex);
                    assert(matchIndex < curr);
                    {
                        var match = dmsBase + matchIndex;
                        assert(match + 4 <= dmsEnd);
                        if (MEM_read32(match) == MEM_read32(ip))
                        {
                            currentMl = ZSTD_count_2segments(ip + 4, match + 4, iLimit, dmsEnd, prefixStart) + 4;
                        }
                    }

                    if (currentMl > ml)
                    {
                        ml = currentMl;
                        assert(curr > matchIndex + dmsIndexDelta);
                        assert(curr - (matchIndex + dmsIndexDelta) > 0);
                        *offsetPtr = curr - (matchIndex + dmsIndexDelta) + 3;
                        if (ip + currentMl == iLimit)
                            break;
                    }
                }
            }
        }

        return ml;
    }

    /* Generate row search fns for each combination of (dictMode, mls, rowLog) */
    private static nuint ZSTD_RowFindBestMatch_noDict_4_4(ZstdMatchStateT* ms, byte* ip, byte* iLimit, nuint* offsetPtr)
    {
        assert((4 > (6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) ? 4 : 6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) == 4);
        assert((4 > (6 < ms->cParams.searchLog ? 6 : ms->cParams.searchLog) ? 4 : 6 < ms->cParams.searchLog ? 6 : ms->cParams.searchLog) == 4);
        return ZSTD_RowFindBestMatch(ms, ip, iLimit, offsetPtr, 4, ZstdDictModeE.ZstdNoDict, 4);
    }

    private static nuint ZSTD_RowFindBestMatch_noDict_4_5(ZstdMatchStateT* ms, byte* ip, byte* iLimit, nuint* offsetPtr)
    {
        assert((4 > (6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) ? 4 : 6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) == 4);
        assert((4 > (6 < ms->cParams.searchLog ? 6 : ms->cParams.searchLog) ? 4 : 6 < ms->cParams.searchLog ? 6 : ms->cParams.searchLog) == 5);
        return ZSTD_RowFindBestMatch(ms, ip, iLimit, offsetPtr, 4, ZstdDictModeE.ZstdNoDict, 5);
    }

    private static nuint ZSTD_RowFindBestMatch_noDict_4_6(ZstdMatchStateT* ms, byte* ip, byte* iLimit, nuint* offsetPtr)
    {
        assert((4 > (6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) ? 4 : 6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) == 4);
        assert((4 > (6 < ms->cParams.searchLog ? 6 : ms->cParams.searchLog) ? 4 : 6 < ms->cParams.searchLog ? 6 : ms->cParams.searchLog) == 6);
        return ZSTD_RowFindBestMatch(ms, ip, iLimit, offsetPtr, 4, ZstdDictModeE.ZstdNoDict, 6);
    }

    private static nuint ZSTD_RowFindBestMatch_noDict_5_4(ZstdMatchStateT* ms, byte* ip, byte* iLimit, nuint* offsetPtr)
    {
        assert((4 > (6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) ? 4 : 6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) == 5);
        assert((4 > (6 < ms->cParams.searchLog ? 6 : ms->cParams.searchLog) ? 4 : 6 < ms->cParams.searchLog ? 6 : ms->cParams.searchLog) == 4);
        return ZSTD_RowFindBestMatch(ms, ip, iLimit, offsetPtr, 5, ZstdDictModeE.ZstdNoDict, 4);
    }

    private static nuint ZSTD_RowFindBestMatch_noDict_5_5(ZstdMatchStateT* ms, byte* ip, byte* iLimit, nuint* offsetPtr)
    {
        assert((4 > (6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) ? 4 : 6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) == 5);
        assert((4 > (6 < ms->cParams.searchLog ? 6 : ms->cParams.searchLog) ? 4 : 6 < ms->cParams.searchLog ? 6 : ms->cParams.searchLog) == 5);
        return ZSTD_RowFindBestMatch(ms, ip, iLimit, offsetPtr, 5, ZstdDictModeE.ZstdNoDict, 5);
    }

    private static nuint ZSTD_RowFindBestMatch_noDict_5_6(ZstdMatchStateT* ms, byte* ip, byte* iLimit, nuint* offsetPtr)
    {
        assert((4 > (6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) ? 4 : 6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) == 5);
        assert((4 > (6 < ms->cParams.searchLog ? 6 : ms->cParams.searchLog) ? 4 : 6 < ms->cParams.searchLog ? 6 : ms->cParams.searchLog) == 6);
        return ZSTD_RowFindBestMatch(ms, ip, iLimit, offsetPtr, 5, ZstdDictModeE.ZstdNoDict, 6);
    }

    private static nuint ZSTD_RowFindBestMatch_noDict_6_4(ZstdMatchStateT* ms, byte* ip, byte* iLimit, nuint* offsetPtr)
    {
        assert((4 > (6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) ? 4 : 6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) == 6);
        assert((4 > (6 < ms->cParams.searchLog ? 6 : ms->cParams.searchLog) ? 4 : 6 < ms->cParams.searchLog ? 6 : ms->cParams.searchLog) == 4);
        return ZSTD_RowFindBestMatch(ms, ip, iLimit, offsetPtr, 6, ZstdDictModeE.ZstdNoDict, 4);
    }

    private static nuint ZSTD_RowFindBestMatch_noDict_6_5(ZstdMatchStateT* ms, byte* ip, byte* iLimit, nuint* offsetPtr)
    {
        assert((4 > (6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) ? 4 : 6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) == 6);
        assert((4 > (6 < ms->cParams.searchLog ? 6 : ms->cParams.searchLog) ? 4 : 6 < ms->cParams.searchLog ? 6 : ms->cParams.searchLog) == 5);
        return ZSTD_RowFindBestMatch(ms, ip, iLimit, offsetPtr, 6, ZstdDictModeE.ZstdNoDict, 5);
    }

    private static nuint ZSTD_RowFindBestMatch_noDict_6_6(ZstdMatchStateT* ms, byte* ip, byte* iLimit, nuint* offsetPtr)
    {
        assert((4 > (6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) ? 4 : 6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) == 6);
        assert((4 > (6 < ms->cParams.searchLog ? 6 : ms->cParams.searchLog) ? 4 : 6 < ms->cParams.searchLog ? 6 : ms->cParams.searchLog) == 6);
        return ZSTD_RowFindBestMatch(ms, ip, iLimit, offsetPtr, 6, ZstdDictModeE.ZstdNoDict, 6);
    }

    private static nuint ZSTD_RowFindBestMatch_extDict_4_4(ZstdMatchStateT* ms, byte* ip, byte* iLimit, nuint* offsetPtr)
    {
        assert((4 > (6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) ? 4 : 6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) == 4);
        assert((4 > (6 < ms->cParams.searchLog ? 6 : ms->cParams.searchLog) ? 4 : 6 < ms->cParams.searchLog ? 6 : ms->cParams.searchLog) == 4);
        return ZSTD_RowFindBestMatch(ms, ip, iLimit, offsetPtr, 4, ZstdDictModeE.ZstdExtDict, 4);
    }

    private static nuint ZSTD_RowFindBestMatch_extDict_4_5(ZstdMatchStateT* ms, byte* ip, byte* iLimit, nuint* offsetPtr)
    {
        assert((4 > (6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) ? 4 : 6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) == 4);
        assert((4 > (6 < ms->cParams.searchLog ? 6 : ms->cParams.searchLog) ? 4 : 6 < ms->cParams.searchLog ? 6 : ms->cParams.searchLog) == 5);
        return ZSTD_RowFindBestMatch(ms, ip, iLimit, offsetPtr, 4, ZstdDictModeE.ZstdExtDict, 5);
    }

    private static nuint ZSTD_RowFindBestMatch_extDict_4_6(ZstdMatchStateT* ms, byte* ip, byte* iLimit, nuint* offsetPtr)
    {
        assert((4 > (6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) ? 4 : 6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) == 4);
        assert((4 > (6 < ms->cParams.searchLog ? 6 : ms->cParams.searchLog) ? 4 : 6 < ms->cParams.searchLog ? 6 : ms->cParams.searchLog) == 6);
        return ZSTD_RowFindBestMatch(ms, ip, iLimit, offsetPtr, 4, ZstdDictModeE.ZstdExtDict, 6);
    }

    private static nuint ZSTD_RowFindBestMatch_extDict_5_4(ZstdMatchStateT* ms, byte* ip, byte* iLimit, nuint* offsetPtr)
    {
        assert((4 > (6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) ? 4 : 6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) == 5);
        assert((4 > (6 < ms->cParams.searchLog ? 6 : ms->cParams.searchLog) ? 4 : 6 < ms->cParams.searchLog ? 6 : ms->cParams.searchLog) == 4);
        return ZSTD_RowFindBestMatch(ms, ip, iLimit, offsetPtr, 5, ZstdDictModeE.ZstdExtDict, 4);
    }

    private static nuint ZSTD_RowFindBestMatch_extDict_5_5(ZstdMatchStateT* ms, byte* ip, byte* iLimit, nuint* offsetPtr)
    {
        assert((4 > (6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) ? 4 : 6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) == 5);
        assert((4 > (6 < ms->cParams.searchLog ? 6 : ms->cParams.searchLog) ? 4 : 6 < ms->cParams.searchLog ? 6 : ms->cParams.searchLog) == 5);
        return ZSTD_RowFindBestMatch(ms, ip, iLimit, offsetPtr, 5, ZstdDictModeE.ZstdExtDict, 5);
    }

    private static nuint ZSTD_RowFindBestMatch_extDict_5_6(ZstdMatchStateT* ms, byte* ip, byte* iLimit, nuint* offsetPtr)
    {
        assert((4 > (6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) ? 4 : 6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) == 5);
        assert((4 > (6 < ms->cParams.searchLog ? 6 : ms->cParams.searchLog) ? 4 : 6 < ms->cParams.searchLog ? 6 : ms->cParams.searchLog) == 6);
        return ZSTD_RowFindBestMatch(ms, ip, iLimit, offsetPtr, 5, ZstdDictModeE.ZstdExtDict, 6);
    }

    private static nuint ZSTD_RowFindBestMatch_extDict_6_4(ZstdMatchStateT* ms, byte* ip, byte* iLimit, nuint* offsetPtr)
    {
        assert((4 > (6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) ? 4 : 6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) == 6);
        assert((4 > (6 < ms->cParams.searchLog ? 6 : ms->cParams.searchLog) ? 4 : 6 < ms->cParams.searchLog ? 6 : ms->cParams.searchLog) == 4);
        return ZSTD_RowFindBestMatch(ms, ip, iLimit, offsetPtr, 6, ZstdDictModeE.ZstdExtDict, 4);
    }

    private static nuint ZSTD_RowFindBestMatch_extDict_6_5(ZstdMatchStateT* ms, byte* ip, byte* iLimit, nuint* offsetPtr)
    {
        assert((4 > (6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) ? 4 : 6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) == 6);
        assert((4 > (6 < ms->cParams.searchLog ? 6 : ms->cParams.searchLog) ? 4 : 6 < ms->cParams.searchLog ? 6 : ms->cParams.searchLog) == 5);
        return ZSTD_RowFindBestMatch(ms, ip, iLimit, offsetPtr, 6, ZstdDictModeE.ZstdExtDict, 5);
    }

    private static nuint ZSTD_RowFindBestMatch_extDict_6_6(ZstdMatchStateT* ms, byte* ip, byte* iLimit, nuint* offsetPtr)
    {
        assert((4 > (6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) ? 4 : 6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) == 6);
        assert((4 > (6 < ms->cParams.searchLog ? 6 : ms->cParams.searchLog) ? 4 : 6 < ms->cParams.searchLog ? 6 : ms->cParams.searchLog) == 6);
        return ZSTD_RowFindBestMatch(ms, ip, iLimit, offsetPtr, 6, ZstdDictModeE.ZstdExtDict, 6);
    }

    private static nuint ZSTD_RowFindBestMatch_dictMatchState_4_4(ZstdMatchStateT* ms, byte* ip, byte* iLimit, nuint* offsetPtr)
    {
        assert((4 > (6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) ? 4 : 6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) == 4);
        assert((4 > (6 < ms->cParams.searchLog ? 6 : ms->cParams.searchLog) ? 4 : 6 < ms->cParams.searchLog ? 6 : ms->cParams.searchLog) == 4);
        return ZSTD_RowFindBestMatch(ms, ip, iLimit, offsetPtr, 4, ZstdDictModeE.ZstdDictMatchState, 4);
    }

    private static nuint ZSTD_RowFindBestMatch_dictMatchState_4_5(ZstdMatchStateT* ms, byte* ip, byte* iLimit, nuint* offsetPtr)
    {
        assert((4 > (6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) ? 4 : 6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) == 4);
        assert((4 > (6 < ms->cParams.searchLog ? 6 : ms->cParams.searchLog) ? 4 : 6 < ms->cParams.searchLog ? 6 : ms->cParams.searchLog) == 5);
        return ZSTD_RowFindBestMatch(ms, ip, iLimit, offsetPtr, 4, ZstdDictModeE.ZstdDictMatchState, 5);
    }

    private static nuint ZSTD_RowFindBestMatch_dictMatchState_4_6(ZstdMatchStateT* ms, byte* ip, byte* iLimit, nuint* offsetPtr)
    {
        assert((4 > (6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) ? 4 : 6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) == 4);
        assert((4 > (6 < ms->cParams.searchLog ? 6 : ms->cParams.searchLog) ? 4 : 6 < ms->cParams.searchLog ? 6 : ms->cParams.searchLog) == 6);
        return ZSTD_RowFindBestMatch(ms, ip, iLimit, offsetPtr, 4, ZstdDictModeE.ZstdDictMatchState, 6);
    }

    private static nuint ZSTD_RowFindBestMatch_dictMatchState_5_4(ZstdMatchStateT* ms, byte* ip, byte* iLimit, nuint* offsetPtr)
    {
        assert((4 > (6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) ? 4 : 6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) == 5);
        assert((4 > (6 < ms->cParams.searchLog ? 6 : ms->cParams.searchLog) ? 4 : 6 < ms->cParams.searchLog ? 6 : ms->cParams.searchLog) == 4);
        return ZSTD_RowFindBestMatch(ms, ip, iLimit, offsetPtr, 5, ZstdDictModeE.ZstdDictMatchState, 4);
    }

    private static nuint ZSTD_RowFindBestMatch_dictMatchState_5_5(ZstdMatchStateT* ms, byte* ip, byte* iLimit, nuint* offsetPtr)
    {
        assert((4 > (6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) ? 4 : 6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) == 5);
        assert((4 > (6 < ms->cParams.searchLog ? 6 : ms->cParams.searchLog) ? 4 : 6 < ms->cParams.searchLog ? 6 : ms->cParams.searchLog) == 5);
        return ZSTD_RowFindBestMatch(ms, ip, iLimit, offsetPtr, 5, ZstdDictModeE.ZstdDictMatchState, 5);
    }

    private static nuint ZSTD_RowFindBestMatch_dictMatchState_5_6(ZstdMatchStateT* ms, byte* ip, byte* iLimit, nuint* offsetPtr)
    {
        assert((4 > (6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) ? 4 : 6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) == 5);
        assert((4 > (6 < ms->cParams.searchLog ? 6 : ms->cParams.searchLog) ? 4 : 6 < ms->cParams.searchLog ? 6 : ms->cParams.searchLog) == 6);
        return ZSTD_RowFindBestMatch(ms, ip, iLimit, offsetPtr, 5, ZstdDictModeE.ZstdDictMatchState, 6);
    }

    private static nuint ZSTD_RowFindBestMatch_dictMatchState_6_4(ZstdMatchStateT* ms, byte* ip, byte* iLimit, nuint* offsetPtr)
    {
        assert((4 > (6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) ? 4 : 6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) == 6);
        assert((4 > (6 < ms->cParams.searchLog ? 6 : ms->cParams.searchLog) ? 4 : 6 < ms->cParams.searchLog ? 6 : ms->cParams.searchLog) == 4);
        return ZSTD_RowFindBestMatch(ms, ip, iLimit, offsetPtr, 6, ZstdDictModeE.ZstdDictMatchState, 4);
    }

    private static nuint ZSTD_RowFindBestMatch_dictMatchState_6_5(ZstdMatchStateT* ms, byte* ip, byte* iLimit, nuint* offsetPtr)
    {
        assert((4 > (6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) ? 4 : 6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) == 6);
        assert((4 > (6 < ms->cParams.searchLog ? 6 : ms->cParams.searchLog) ? 4 : 6 < ms->cParams.searchLog ? 6 : ms->cParams.searchLog) == 5);
        return ZSTD_RowFindBestMatch(ms, ip, iLimit, offsetPtr, 6, ZstdDictModeE.ZstdDictMatchState, 5);
    }

    private static nuint ZSTD_RowFindBestMatch_dictMatchState_6_6(ZstdMatchStateT* ms, byte* ip, byte* iLimit, nuint* offsetPtr)
    {
        assert((4 > (6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) ? 4 : 6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) == 6);
        assert((4 > (6 < ms->cParams.searchLog ? 6 : ms->cParams.searchLog) ? 4 : 6 < ms->cParams.searchLog ? 6 : ms->cParams.searchLog) == 6);
        return ZSTD_RowFindBestMatch(ms, ip, iLimit, offsetPtr, 6, ZstdDictModeE.ZstdDictMatchState, 6);
    }

    private static nuint ZSTD_RowFindBestMatch_dedicatedDictSearch_4_4(ZstdMatchStateT* ms, byte* ip, byte* iLimit, nuint* offsetPtr)
    {
        assert((4 > (6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) ? 4 : 6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) == 4);
        assert((4 > (6 < ms->cParams.searchLog ? 6 : ms->cParams.searchLog) ? 4 : 6 < ms->cParams.searchLog ? 6 : ms->cParams.searchLog) == 4);
        return ZSTD_RowFindBestMatch(ms, ip, iLimit, offsetPtr, 4, ZstdDictModeE.ZstdDedicatedDictSearch, 4);
    }

    private static nuint ZSTD_RowFindBestMatch_dedicatedDictSearch_4_5(ZstdMatchStateT* ms, byte* ip, byte* iLimit, nuint* offsetPtr)
    {
        assert((4 > (6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) ? 4 : 6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) == 4);
        assert((4 > (6 < ms->cParams.searchLog ? 6 : ms->cParams.searchLog) ? 4 : 6 < ms->cParams.searchLog ? 6 : ms->cParams.searchLog) == 5);
        return ZSTD_RowFindBestMatch(ms, ip, iLimit, offsetPtr, 4, ZstdDictModeE.ZstdDedicatedDictSearch, 5);
    }

    private static nuint ZSTD_RowFindBestMatch_dedicatedDictSearch_4_6(ZstdMatchStateT* ms, byte* ip, byte* iLimit, nuint* offsetPtr)
    {
        assert((4 > (6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) ? 4 : 6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) == 4);
        assert((4 > (6 < ms->cParams.searchLog ? 6 : ms->cParams.searchLog) ? 4 : 6 < ms->cParams.searchLog ? 6 : ms->cParams.searchLog) == 6);
        return ZSTD_RowFindBestMatch(ms, ip, iLimit, offsetPtr, 4, ZstdDictModeE.ZstdDedicatedDictSearch, 6);
    }

    private static nuint ZSTD_RowFindBestMatch_dedicatedDictSearch_5_4(ZstdMatchStateT* ms, byte* ip, byte* iLimit, nuint* offsetPtr)
    {
        assert((4 > (6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) ? 4 : 6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) == 5);
        assert((4 > (6 < ms->cParams.searchLog ? 6 : ms->cParams.searchLog) ? 4 : 6 < ms->cParams.searchLog ? 6 : ms->cParams.searchLog) == 4);
        return ZSTD_RowFindBestMatch(ms, ip, iLimit, offsetPtr, 5, ZstdDictModeE.ZstdDedicatedDictSearch, 4);
    }

    private static nuint ZSTD_RowFindBestMatch_dedicatedDictSearch_5_5(ZstdMatchStateT* ms, byte* ip, byte* iLimit, nuint* offsetPtr)
    {
        assert((4 > (6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) ? 4 : 6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) == 5);
        assert((4 > (6 < ms->cParams.searchLog ? 6 : ms->cParams.searchLog) ? 4 : 6 < ms->cParams.searchLog ? 6 : ms->cParams.searchLog) == 5);
        return ZSTD_RowFindBestMatch(ms, ip, iLimit, offsetPtr, 5, ZstdDictModeE.ZstdDedicatedDictSearch, 5);
    }

    private static nuint ZSTD_RowFindBestMatch_dedicatedDictSearch_5_6(ZstdMatchStateT* ms, byte* ip, byte* iLimit, nuint* offsetPtr)
    {
        assert((4 > (6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) ? 4 : 6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) == 5);
        assert((4 > (6 < ms->cParams.searchLog ? 6 : ms->cParams.searchLog) ? 4 : 6 < ms->cParams.searchLog ? 6 : ms->cParams.searchLog) == 6);
        return ZSTD_RowFindBestMatch(ms, ip, iLimit, offsetPtr, 5, ZstdDictModeE.ZstdDedicatedDictSearch, 6);
    }

    private static nuint ZSTD_RowFindBestMatch_dedicatedDictSearch_6_4(ZstdMatchStateT* ms, byte* ip, byte* iLimit, nuint* offsetPtr)
    {
        assert((4 > (6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) ? 4 : 6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) == 6);
        assert((4 > (6 < ms->cParams.searchLog ? 6 : ms->cParams.searchLog) ? 4 : 6 < ms->cParams.searchLog ? 6 : ms->cParams.searchLog) == 4);
        return ZSTD_RowFindBestMatch(ms, ip, iLimit, offsetPtr, 6, ZstdDictModeE.ZstdDedicatedDictSearch, 4);
    }

    private static nuint ZSTD_RowFindBestMatch_dedicatedDictSearch_6_5(ZstdMatchStateT* ms, byte* ip, byte* iLimit, nuint* offsetPtr)
    {
        assert((4 > (6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) ? 4 : 6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) == 6);
        assert((4 > (6 < ms->cParams.searchLog ? 6 : ms->cParams.searchLog) ? 4 : 6 < ms->cParams.searchLog ? 6 : ms->cParams.searchLog) == 5);
        return ZSTD_RowFindBestMatch(ms, ip, iLimit, offsetPtr, 6, ZstdDictModeE.ZstdDedicatedDictSearch, 5);
    }

    private static nuint ZSTD_RowFindBestMatch_dedicatedDictSearch_6_6(ZstdMatchStateT* ms, byte* ip, byte* iLimit, nuint* offsetPtr)
    {
        assert((4 > (6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) ? 4 : 6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) == 6);
        assert((4 > (6 < ms->cParams.searchLog ? 6 : ms->cParams.searchLog) ? 4 : 6 < ms->cParams.searchLog ? 6 : ms->cParams.searchLog) == 6);
        return ZSTD_RowFindBestMatch(ms, ip, iLimit, offsetPtr, 6, ZstdDictModeE.ZstdDedicatedDictSearch, 6);
    }

    /* Generate binary Tree search fns for each combination of (dictMode, mls) */
    private static nuint ZSTD_BtFindBestMatch_noDict_4(ZstdMatchStateT* ms, byte* ip, byte* iLimit, nuint* offBasePtr)
    {
        assert((4 > (6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) ? 4 : 6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) == 4);
        return ZSTD_BtFindBestMatch(ms, ip, iLimit, offBasePtr, 4, ZstdDictModeE.ZstdNoDict);
    }

    private static nuint ZSTD_BtFindBestMatch_noDict_5(ZstdMatchStateT* ms, byte* ip, byte* iLimit, nuint* offBasePtr)
    {
        assert((4 > (6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) ? 4 : 6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) == 5);
        return ZSTD_BtFindBestMatch(ms, ip, iLimit, offBasePtr, 5, ZstdDictModeE.ZstdNoDict);
    }

    private static nuint ZSTD_BtFindBestMatch_noDict_6(ZstdMatchStateT* ms, byte* ip, byte* iLimit, nuint* offBasePtr)
    {
        assert((4 > (6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) ? 4 : 6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) == 6);
        return ZSTD_BtFindBestMatch(ms, ip, iLimit, offBasePtr, 6, ZstdDictModeE.ZstdNoDict);
    }

    private static nuint ZSTD_BtFindBestMatch_extDict_4(ZstdMatchStateT* ms, byte* ip, byte* iLimit, nuint* offBasePtr)
    {
        assert((4 > (6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) ? 4 : 6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) == 4);
        return ZSTD_BtFindBestMatch(ms, ip, iLimit, offBasePtr, 4, ZstdDictModeE.ZstdExtDict);
    }

    private static nuint ZSTD_BtFindBestMatch_extDict_5(ZstdMatchStateT* ms, byte* ip, byte* iLimit, nuint* offBasePtr)
    {
        assert((4 > (6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) ? 4 : 6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) == 5);
        return ZSTD_BtFindBestMatch(ms, ip, iLimit, offBasePtr, 5, ZstdDictModeE.ZstdExtDict);
    }

    private static nuint ZSTD_BtFindBestMatch_extDict_6(ZstdMatchStateT* ms, byte* ip, byte* iLimit, nuint* offBasePtr)
    {
        assert((4 > (6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) ? 4 : 6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) == 6);
        return ZSTD_BtFindBestMatch(ms, ip, iLimit, offBasePtr, 6, ZstdDictModeE.ZstdExtDict);
    }

    private static nuint ZSTD_BtFindBestMatch_dictMatchState_4(ZstdMatchStateT* ms, byte* ip, byte* iLimit, nuint* offBasePtr)
    {
        assert((4 > (6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) ? 4 : 6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) == 4);
        return ZSTD_BtFindBestMatch(ms, ip, iLimit, offBasePtr, 4, ZstdDictModeE.ZstdDictMatchState);
    }

    private static nuint ZSTD_BtFindBestMatch_dictMatchState_5(ZstdMatchStateT* ms, byte* ip, byte* iLimit, nuint* offBasePtr)
    {
        assert((4 > (6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) ? 4 : 6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) == 5);
        return ZSTD_BtFindBestMatch(ms, ip, iLimit, offBasePtr, 5, ZstdDictModeE.ZstdDictMatchState);
    }

    private static nuint ZSTD_BtFindBestMatch_dictMatchState_6(ZstdMatchStateT* ms, byte* ip, byte* iLimit, nuint* offBasePtr)
    {
        assert((4 > (6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) ? 4 : 6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) == 6);
        return ZSTD_BtFindBestMatch(ms, ip, iLimit, offBasePtr, 6, ZstdDictModeE.ZstdDictMatchState);
    }

    private static nuint ZSTD_BtFindBestMatch_dedicatedDictSearch_4(ZstdMatchStateT* ms, byte* ip, byte* iLimit, nuint* offBasePtr)
    {
        assert((4 > (6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) ? 4 : 6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) == 4);
        return ZSTD_BtFindBestMatch(ms, ip, iLimit, offBasePtr, 4, ZstdDictModeE.ZstdDedicatedDictSearch);
    }

    private static nuint ZSTD_BtFindBestMatch_dedicatedDictSearch_5(ZstdMatchStateT* ms, byte* ip, byte* iLimit, nuint* offBasePtr)
    {
        assert((4 > (6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) ? 4 : 6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) == 5);
        return ZSTD_BtFindBestMatch(ms, ip, iLimit, offBasePtr, 5, ZstdDictModeE.ZstdDedicatedDictSearch);
    }

    private static nuint ZSTD_BtFindBestMatch_dedicatedDictSearch_6(ZstdMatchStateT* ms, byte* ip, byte* iLimit, nuint* offBasePtr)
    {
        assert((4 > (6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) ? 4 : 6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) == 6);
        return ZSTD_BtFindBestMatch(ms, ip, iLimit, offBasePtr, 6, ZstdDictModeE.ZstdDedicatedDictSearch);
    }

    /* Generate hash chain search fns for each combination of (dictMode, mls) */
    private static nuint ZSTD_HcFindBestMatch_noDict_4(ZstdMatchStateT* ms, byte* ip, byte* iLimit, nuint* offsetPtr)
    {
        assert((4 > (6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) ? 4 : 6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) == 4);
        return ZSTD_HcFindBestMatch(ms, ip, iLimit, offsetPtr, 4, ZstdDictModeE.ZstdNoDict);
    }

    private static nuint ZSTD_HcFindBestMatch_noDict_5(ZstdMatchStateT* ms, byte* ip, byte* iLimit, nuint* offsetPtr)
    {
        assert((4 > (6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) ? 4 : 6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) == 5);
        return ZSTD_HcFindBestMatch(ms, ip, iLimit, offsetPtr, 5, ZstdDictModeE.ZstdNoDict);
    }

    private static nuint ZSTD_HcFindBestMatch_noDict_6(ZstdMatchStateT* ms, byte* ip, byte* iLimit, nuint* offsetPtr)
    {
        assert((4 > (6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) ? 4 : 6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) == 6);
        return ZSTD_HcFindBestMatch(ms, ip, iLimit, offsetPtr, 6, ZstdDictModeE.ZstdNoDict);
    }

    private static nuint ZSTD_HcFindBestMatch_extDict_4(ZstdMatchStateT* ms, byte* ip, byte* iLimit, nuint* offsetPtr)
    {
        assert((4 > (6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) ? 4 : 6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) == 4);
        return ZSTD_HcFindBestMatch(ms, ip, iLimit, offsetPtr, 4, ZstdDictModeE.ZstdExtDict);
    }

    private static nuint ZSTD_HcFindBestMatch_extDict_5(ZstdMatchStateT* ms, byte* ip, byte* iLimit, nuint* offsetPtr)
    {
        assert((4 > (6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) ? 4 : 6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) == 5);
        return ZSTD_HcFindBestMatch(ms, ip, iLimit, offsetPtr, 5, ZstdDictModeE.ZstdExtDict);
    }

    private static nuint ZSTD_HcFindBestMatch_extDict_6(ZstdMatchStateT* ms, byte* ip, byte* iLimit, nuint* offsetPtr)
    {
        assert((4 > (6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) ? 4 : 6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) == 6);
        return ZSTD_HcFindBestMatch(ms, ip, iLimit, offsetPtr, 6, ZstdDictModeE.ZstdExtDict);
    }

    private static nuint ZSTD_HcFindBestMatch_dictMatchState_4(ZstdMatchStateT* ms, byte* ip, byte* iLimit, nuint* offsetPtr)
    {
        assert((4 > (6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) ? 4 : 6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) == 4);
        return ZSTD_HcFindBestMatch(ms, ip, iLimit, offsetPtr, 4, ZstdDictModeE.ZstdDictMatchState);
    }

    private static nuint ZSTD_HcFindBestMatch_dictMatchState_5(ZstdMatchStateT* ms, byte* ip, byte* iLimit, nuint* offsetPtr)
    {
        assert((4 > (6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) ? 4 : 6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) == 5);
        return ZSTD_HcFindBestMatch(ms, ip, iLimit, offsetPtr, 5, ZstdDictModeE.ZstdDictMatchState);
    }

    private static nuint ZSTD_HcFindBestMatch_dictMatchState_6(ZstdMatchStateT* ms, byte* ip, byte* iLimit, nuint* offsetPtr)
    {
        assert((4 > (6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) ? 4 : 6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) == 6);
        return ZSTD_HcFindBestMatch(ms, ip, iLimit, offsetPtr, 6, ZstdDictModeE.ZstdDictMatchState);
    }

    private static nuint ZSTD_HcFindBestMatch_dedicatedDictSearch_4(ZstdMatchStateT* ms, byte* ip, byte* iLimit, nuint* offsetPtr)
    {
        assert((4 > (6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) ? 4 : 6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) == 4);
        return ZSTD_HcFindBestMatch(ms, ip, iLimit, offsetPtr, 4, ZstdDictModeE.ZstdDedicatedDictSearch);
    }

    private static nuint ZSTD_HcFindBestMatch_dedicatedDictSearch_5(ZstdMatchStateT* ms, byte* ip, byte* iLimit, nuint* offsetPtr)
    {
        assert((4 > (6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) ? 4 : 6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) == 5);
        return ZSTD_HcFindBestMatch(ms, ip, iLimit, offsetPtr, 5, ZstdDictModeE.ZstdDedicatedDictSearch);
    }

    private static nuint ZSTD_HcFindBestMatch_dedicatedDictSearch_6(ZstdMatchStateT* ms, byte* ip, byte* iLimit, nuint* offsetPtr)
    {
        assert((4 > (6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) ? 4 : 6 < ms->cParams.minMatch ? 6 : ms->cParams.minMatch) == 6);
        return ZSTD_HcFindBestMatch(ms, ip, iLimit, offsetPtr, 6, ZstdDictModeE.ZstdDedicatedDictSearch);
    }

    /**
     * Searches for the longest match at @p ip.
     * Dispatches to the correct implementation function based on the
     * (searchMethod, dictMode, mls, rowLog). We use switch statements
     * here instead of using an indirect function call through a function
     * pointer because after Spectre and Meltdown mitigations, indirect
     * function calls can be very costly, especially in the kernel.
     *
     * NOTE: dictMode and searchMethod should be templated, so those switch
     * statements should be optimized out. Only the mls & rowLog switches
     * should be left.
     *
     * @param ms The match state.
     * @param ip The position to search at.
     * @param iend The end of the input data.
     * @param[out] offsetPtr Stores the match offset into this pointer.
     * @param mls The minimum search length, in the range [4, 6].
     * @param rowLog The row log (if applicable), in the range [4, 6].
     * @param searchMethod The search method to use (templated).
     * @param dictMode The dictMode (templated).
     *
     * @returns The length of the longest match found, or < mls if no match is found.
     * If a match is found its offset is stored in @p offsetPtr.
     */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static nuint ZSTD_searchMax(ZstdMatchStateT* ms, byte* ip, byte* iend, nuint* offsetPtr, uint mls, uint rowLog, SearchMethodE searchMethod, ZstdDictModeE dictMode)
    {
        if (dictMode == ZstdDictModeE.ZstdNoDict)
        {
            if (searchMethod == SearchMethodE.SearchRowHash)
            {
                if (mls == 4)
                {
                    if (rowLog == 4)
                        return ZSTD_RowFindBestMatch_noDict_4_4(ms, ip, iend, offsetPtr);

                    return rowLog == 5 ? ZSTD_RowFindBestMatch_noDict_4_5(ms, ip, iend, offsetPtr) : ZSTD_RowFindBestMatch_noDict_4_6(ms, ip, iend, offsetPtr);
                }

                if (mls == 5)
                {
                    if (rowLog == 4)
                        return ZSTD_RowFindBestMatch_noDict_5_4(ms, ip, iend, offsetPtr);

                    return rowLog == 5 ? ZSTD_RowFindBestMatch_noDict_5_5(ms, ip, iend, offsetPtr) : ZSTD_RowFindBestMatch_noDict_5_6(ms, ip, iend, offsetPtr);
                }

                if (rowLog == 4)
                    return ZSTD_RowFindBestMatch_noDict_6_4(ms, ip, iend, offsetPtr);

                return rowLog == 5 ? ZSTD_RowFindBestMatch_noDict_6_5(ms, ip, iend, offsetPtr) : ZSTD_RowFindBestMatch_noDict_6_6(ms, ip, iend, offsetPtr);
            }

            if (searchMethod == SearchMethodE.SearchHashChain)
            {
                if (mls == 4)
                    return ZSTD_HcFindBestMatch_noDict_4(ms, ip, iend, offsetPtr);

                return mls == 5 ? ZSTD_HcFindBestMatch_noDict_5(ms, ip, iend, offsetPtr) : ZSTD_HcFindBestMatch_noDict_6(ms, ip, iend, offsetPtr);
            }

            // searchMethod_e.search_binaryTree
            if (mls == 4)
                return ZSTD_BtFindBestMatch_noDict_4(ms, ip, iend, offsetPtr);

            return mls == 5 ? ZSTD_BtFindBestMatch_noDict_5(ms, ip, iend, offsetPtr) : ZSTD_BtFindBestMatch_noDict_6(ms, ip, iend, offsetPtr);
        }

        if (dictMode == ZstdDictModeE.ZstdExtDict)
        {
            if (searchMethod == SearchMethodE.SearchRowHash)
            {
                if (mls == 4)
                {
                    if (rowLog == 4)
                        return ZSTD_RowFindBestMatch_extDict_4_4(ms, ip, iend, offsetPtr);
                    if (rowLog == 5)
                        return ZSTD_RowFindBestMatch_extDict_4_5(ms, ip, iend, offsetPtr);

                    return ZSTD_RowFindBestMatch_extDict_4_6(ms, ip, iend, offsetPtr);
                }

                if (mls == 5)
                {
                    if (rowLog == 4)
                        return ZSTD_RowFindBestMatch_extDict_5_4(ms, ip, iend, offsetPtr);
                    if (rowLog == 5)
                        return ZSTD_RowFindBestMatch_extDict_5_5(ms, ip, iend, offsetPtr);

                    return ZSTD_RowFindBestMatch_extDict_5_6(ms, ip, iend, offsetPtr);
                }

                if (mls == 6)
                {
                    if (rowLog == 4)
                        return ZSTD_RowFindBestMatch_extDict_6_4(ms, ip, iend, offsetPtr);
                    if (rowLog == 5)
                        return ZSTD_RowFindBestMatch_extDict_6_5(ms, ip, iend, offsetPtr);

                    return ZSTD_RowFindBestMatch_extDict_6_6(ms, ip, iend, offsetPtr);
                }
            }

            if (searchMethod == SearchMethodE.SearchHashChain)
            {
                if (mls == 4)
                    return ZSTD_HcFindBestMatch_extDict_4(ms, ip, iend, offsetPtr);
                if (mls == 5)
                    return ZSTD_HcFindBestMatch_extDict_5(ms, ip, iend, offsetPtr);

                return ZSTD_HcFindBestMatch_extDict_6(ms, ip, iend, offsetPtr);
            }

            // searchMethod_e.search_binaryTree
            if (mls == 4)
                return ZSTD_BtFindBestMatch_extDict_4(ms, ip, iend, offsetPtr);
            if (mls == 5)
                return ZSTD_BtFindBestMatch_extDict_5(ms, ip, iend, offsetPtr);

            return ZSTD_BtFindBestMatch_extDict_6(ms, ip, iend, offsetPtr);
        }

        if (dictMode == ZstdDictModeE.ZstdDictMatchState)
        {
            if (searchMethod == SearchMethodE.SearchRowHash)
            {
                if (mls == 4)
                {
                    if (rowLog == 4)
                        return ZSTD_RowFindBestMatch_dictMatchState_4_4(ms, ip, iend, offsetPtr);
                    if (rowLog == 5)
                        return ZSTD_RowFindBestMatch_dictMatchState_4_5(ms, ip, iend, offsetPtr);

                    return ZSTD_RowFindBestMatch_dictMatchState_4_6(ms, ip, iend, offsetPtr);
                }

                if (mls == 5)
                {
                    if (rowLog == 4)
                        return ZSTD_RowFindBestMatch_dictMatchState_5_4(ms, ip, iend, offsetPtr);
                    if (rowLog == 5)
                        return ZSTD_RowFindBestMatch_dictMatchState_5_5(ms, ip, iend, offsetPtr);

                    return ZSTD_RowFindBestMatch_dictMatchState_5_6(ms, ip, iend, offsetPtr);
                }

                if (mls == 6)
                {
                    if (rowLog == 4)
                        return ZSTD_RowFindBestMatch_dictMatchState_6_4(ms, ip, iend, offsetPtr);
                    if (rowLog == 5)
                        return ZSTD_RowFindBestMatch_dictMatchState_6_5(ms, ip, iend, offsetPtr);

                    return ZSTD_RowFindBestMatch_dictMatchState_6_6(ms, ip, iend, offsetPtr);
                }
            }

            if (searchMethod == SearchMethodE.SearchHashChain)
            {
                if (mls == 4)
                    return ZSTD_HcFindBestMatch_dictMatchState_4(ms, ip, iend, offsetPtr);
                if (mls == 5)
                    return ZSTD_HcFindBestMatch_dictMatchState_5(ms, ip, iend, offsetPtr);

                return ZSTD_HcFindBestMatch_dictMatchState_6(ms, ip, iend, offsetPtr);
            }

            // search_binaryTree
            if (mls == 4)
                return ZSTD_BtFindBestMatch_dictMatchState_4(ms, ip, iend, offsetPtr);
            if (mls == 5)
                return ZSTD_BtFindBestMatch_dictMatchState_5(ms, ip, iend, offsetPtr);

            return ZSTD_BtFindBestMatch_dictMatchState_6(ms, ip, iend, offsetPtr);
        }

        if (searchMethod == SearchMethodE.SearchRowHash)
        {
            if (mls == 4)
            {
                if (rowLog == 4)
                    return ZSTD_RowFindBestMatch_dedicatedDictSearch_4_4(ms, ip, iend, offsetPtr);
                if (rowLog == 5)
                    return ZSTD_RowFindBestMatch_dedicatedDictSearch_4_5(ms, ip, iend, offsetPtr);

                return ZSTD_RowFindBestMatch_dedicatedDictSearch_4_6(ms, ip, iend, offsetPtr);
            }

            if (mls == 5)
            {
                if (rowLog == 4)
                    return ZSTD_RowFindBestMatch_dedicatedDictSearch_5_4(ms, ip, iend, offsetPtr);
                if (rowLog == 5)
                    return ZSTD_RowFindBestMatch_dedicatedDictSearch_5_5(ms, ip, iend, offsetPtr);

                return ZSTD_RowFindBestMatch_dedicatedDictSearch_5_6(ms, ip, iend, offsetPtr);
            }

            if (mls == 6)
            {
                if (rowLog == 4)
                    return ZSTD_RowFindBestMatch_dedicatedDictSearch_6_4(ms, ip, iend, offsetPtr);
                if (rowLog == 5)
                    return ZSTD_RowFindBestMatch_dedicatedDictSearch_6_5(ms, ip, iend, offsetPtr);

                return ZSTD_RowFindBestMatch_dedicatedDictSearch_6_6(ms, ip, iend, offsetPtr);
            }
        }

        if (searchMethod == SearchMethodE.SearchHashChain)
        {
            if (mls == 4)
                return ZSTD_HcFindBestMatch_dedicatedDictSearch_4(ms, ip, iend, offsetPtr);
            if (mls == 5)
                return ZSTD_HcFindBestMatch_dedicatedDictSearch_5(ms, ip, iend, offsetPtr);

            return ZSTD_HcFindBestMatch_dedicatedDictSearch_6(ms, ip, iend, offsetPtr);
        }

        // searchMethod_e.search_binaryTree
        if (mls == 4)
            return ZSTD_BtFindBestMatch_dedicatedDictSearch_4(ms, ip, iend, offsetPtr);
        if (mls == 5)
            return ZSTD_BtFindBestMatch_dedicatedDictSearch_5(ms, ip, iend, offsetPtr);

        return ZSTD_BtFindBestMatch_dedicatedDictSearch_6(ms, ip, iend, offsetPtr);
    }

    /* *******************************
     *  Common parser - lazy strategy
     *********************************/
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static nuint ZSTD_compressBlock_lazy_generic(ZstdMatchStateT* ms, SeqStoreT* seqStore, uint* rep, void* src, nuint srcSize, SearchMethodE searchMethod, uint depth, ZstdDictModeE dictMode)
    {
        var istart = (byte*)src;
        var ip = istart;
        var anchor = istart;
        var iend = istart + srcSize;
        var ilimit = searchMethod == SearchMethodE.SearchRowHash ? iend - 8 - 8 : iend - 8;
        var @base = ms->window.@base;
        var prefixLowestIndex = ms->window.dictLimit;
        var prefixLowest = @base + prefixLowestIndex;
        var mls = ms->cParams.minMatch <= 4 ? 4 : ms->cParams.minMatch <= 6 ? ms->cParams.minMatch : 6;
        var rowLog = ms->cParams.searchLog <= 4 ? 4 : ms->cParams.searchLog <= 6 ? ms->cParams.searchLog : 6;
        uint offset1 = rep[0], offset2 = rep[1];
        uint offsetSaved1 = 0, offsetSaved2 = 0;
        var isDms = dictMode == ZstdDictModeE.ZstdDictMatchState ? 1 : 0;
        var isDds = dictMode == ZstdDictModeE.ZstdDedicatedDictSearch ? 1 : 0;
        var isDxS = isDms != 0 || isDds != 0 ? 1 : 0;
        var dms = ms->dictMatchState;
        var dictLowestIndex = isDxS != 0 ? dms->window.dictLimit : 0;
        var dictBase = isDxS != 0 ? dms->window.@base : null;
        var dictLowest = isDxS != 0 ? dictBase + dictLowestIndex : null;
        var dictEnd = isDxS != 0 ? dms->window.nextSrc : null;
        var dictIndexDelta = isDxS != 0 ? prefixLowestIndex - (uint)(dictEnd - dictBase) : 0;
        var dictAndPrefixLength = (uint)(ip - prefixLowest + (dictEnd - dictLowest));
        ip += dictAndPrefixLength == 0 ? 1 : 0;
        if (dictMode == ZstdDictModeE.ZstdNoDict)
        {
            var curr = (uint)(ip - @base);
            var windowLow = ZSTD_getLowestPrefixIndex(ms, curr, ms->cParams.windowLog);
            var maxRep = curr - windowLow;
            if (offset2 > maxRep)
            {
                offsetSaved2 = offset2;
                offset2 = 0;
            }

            if (offset1 > maxRep)
            {
                offsetSaved1 = offset1;
                offset1 = 0;
            }
        }

#if DEBUG
        if (isDxS != 0)
        {
            assert(offset1 <= dictAndPrefixLength);
            assert(offset2 <= dictAndPrefixLength);
        }
#endif

        ms->lazySkipping = 0;
        if (searchMethod == SearchMethodE.SearchRowHash)
        {
            ZSTD_row_fillHashCache(ms, @base, rowLog, mls, ms->nextToUpdate, ilimit);
        }

        while (ip < ilimit)
        {
            nuint matchLength = 0;
            assert(1 >= 1);
            assert(1 <= 3);
            nuint offBase = 1;
            var start = ip + 1;
            if (isDxS != 0)
            {
                var repIndex = (uint)(ip - @base) + 1 - offset1;
                var repMatch = dictMode is ZstdDictModeE.ZstdDictMatchState or ZstdDictModeE.ZstdDedicatedDictSearch && repIndex < prefixLowestIndex ? dictBase + (repIndex - dictIndexDelta) : @base + repIndex;
                if (ZSTD_index_overlap_check(prefixLowestIndex, repIndex) != 0 && MEM_read32(repMatch) == MEM_read32(ip + 1))
                {
                    var repMatchEnd = repIndex < prefixLowestIndex ? dictEnd : iend;
                    matchLength = ZSTD_count_2segments(ip + 1 + 4, repMatch + 4, iend, repMatchEnd, prefixLowest) + 4;
                    if (depth == 0)
                        goto _storeSequence;
                }
            }

            if (dictMode == ZstdDictModeE.ZstdNoDict && offset1 > 0 && MEM_read32(ip + 1 - offset1) == MEM_read32(ip + 1))
            {
                matchLength = ZSTD_count(ip + 1 + 4, ip + 1 + 4 - offset1, iend) + 4;
                if (depth == 0)
                    goto _storeSequence;
            }

            {
                nuint offbaseFound = 999999999;
                var ml2 = ZSTD_searchMax(ms, ip, iend, &offbaseFound, mls, rowLog, searchMethod, dictMode);
                if (ml2 > matchLength)
                {
                    matchLength = ml2;
                    start = ip;
                    offBase = offbaseFound;
                }
            }

            if (matchLength < 4)
            {
                /* jump faster over incompressible sections */
                var step = ((nuint)(ip - anchor) >> 8) + 1;
                ip += step;
                ms->lazySkipping = step > 8 ? 1 : 0;
                continue;
            }

            if (depth >= 1)
                while (ip < ilimit)
                {
                    ip++;
                    if (dictMode == ZstdDictModeE.ZstdNoDict && offBase != 0 && offset1 > 0 && MEM_read32(ip) == MEM_read32(ip - offset1))
                    {
                        var mlRep = ZSTD_count(ip + 4, ip + 4 - offset1, iend) + 4;
                        var gain2 = (int)(mlRep * 3);
                        var gain1 = (int)(matchLength * 3 - ZSTD_highbit32((uint)offBase) + 1);
                        if (mlRep >= 4 && gain2 > gain1)
                        {
                            matchLength = mlRep;
                            assert(1 >= 1);
                            assert(1 <= 3);
                            offBase = 1;
                            start = ip;
                        }
                    }

                    if (isDxS != 0)
                    {
                        var repIndex = (uint)(ip - @base) - offset1;
                        var repMatch = repIndex < prefixLowestIndex ? dictBase + (repIndex - dictIndexDelta) : @base + repIndex;
                        if (ZSTD_index_overlap_check(prefixLowestIndex, repIndex) != 0 && MEM_read32(repMatch) == MEM_read32(ip))
                        {
                            var repMatchEnd = repIndex < prefixLowestIndex ? dictEnd : iend;
                            var mlRep = ZSTD_count_2segments(ip + 4, repMatch + 4, iend, repMatchEnd, prefixLowest) + 4;
                            var gain2 = (int)(mlRep * 3);
                            var gain1 = (int)(matchLength * 3 - ZSTD_highbit32((uint)offBase) + 1);
                            if (mlRep >= 4 && gain2 > gain1)
                            {
                                matchLength = mlRep;
                                assert(1 >= 1);
                                assert(1 <= 3);
                                offBase = 1;
                                start = ip;
                            }
                        }
                    }

                    {
                        nuint ofbCandidate = 999999999;
                        var ml2 = ZSTD_searchMax(ms, ip, iend, &ofbCandidate, mls, rowLog, searchMethod, dictMode);
                        /* raw approx */
                        var gain2 = (int)(ml2 * 4 - ZSTD_highbit32((uint)ofbCandidate));
                        var gain1 = (int)(matchLength * 4 - ZSTD_highbit32((uint)offBase) + 4);
                        if (ml2 >= 4 && gain2 > gain1)
                        {
                            matchLength = ml2;
                            offBase = ofbCandidate;
                            start = ip;
                            continue;
                        }
                    }

                    if (depth == 2 && ip < ilimit)
                    {
                        ip++;
                        if (dictMode == ZstdDictModeE.ZstdNoDict && offBase != 0 && offset1 > 0 && MEM_read32(ip) == MEM_read32(ip - offset1))
                        {
                            var mlRep = ZSTD_count(ip + 4, ip + 4 - offset1, iend) + 4;
                            var gain2 = (int)(mlRep * 4);
                            var gain1 = (int)(matchLength * 4 - ZSTD_highbit32((uint)offBase) + 1);
                            if (mlRep >= 4 && gain2 > gain1)
                            {
                                matchLength = mlRep;
                                assert(1 >= 1);
                                assert(1 <= 3);
                                offBase = 1;
                                start = ip;
                            }
                        }

                        if (isDxS != 0)
                        {
                            var repIndex = (uint)(ip - @base) - offset1;
                            var repMatch = repIndex < prefixLowestIndex ? dictBase + (repIndex - dictIndexDelta) : @base + repIndex;
                            if (ZSTD_index_overlap_check(prefixLowestIndex, repIndex) != 0 && MEM_read32(repMatch) == MEM_read32(ip))
                            {
                                var repMatchEnd = repIndex < prefixLowestIndex ? dictEnd : iend;
                                var mlRep = ZSTD_count_2segments(ip + 4, repMatch + 4, iend, repMatchEnd, prefixLowest) + 4;
                                var gain2 = (int)(mlRep * 4);
                                var gain1 = (int)(matchLength * 4 - ZSTD_highbit32((uint)offBase) + 1);
                                if (mlRep >= 4 && gain2 > gain1)
                                {
                                    matchLength = mlRep;
                                    assert(1 >= 1);
                                    assert(1 <= 3);
                                    offBase = 1;
                                    start = ip;
                                }
                            }
                        }

                        {
                            nuint ofbCandidate = 999999999;
                            var ml2 = ZSTD_searchMax(ms, ip, iend, &ofbCandidate, mls, rowLog, searchMethod, dictMode);
                            /* raw approx */
                            var gain2 = (int)(ml2 * 4 - ZSTD_highbit32((uint)ofbCandidate));
                            var gain1 = (int)(matchLength * 4 - ZSTD_highbit32((uint)offBase) + 7);
                            if (ml2 >= 4 && gain2 > gain1)
                            {
                                matchLength = ml2;
                                offBase = ofbCandidate;
                                start = ip;
                                continue;
                            }
                        }
                    }

                    break;
                }

            if (offBase > 3)
            {
                if (dictMode == ZstdDictModeE.ZstdNoDict)
                {
                    assert(offBase > 3);
                    assert(offBase > 3);
                    while (start > anchor && start - (offBase - 3) > prefixLowest && start[-1] == (start - (offBase - 3))[-1])
                    {
                        start--;
                        matchLength++;
                    }
                }

                if (isDxS != 0)
                {
                    assert(offBase > 3);
                    var matchIndex = (uint)((nuint)(start - @base) - (offBase - 3));
                    var match = matchIndex < prefixLowestIndex ? dictBase + matchIndex - dictIndexDelta : @base + matchIndex;
                    var mStart = matchIndex < prefixLowestIndex ? dictLowest : prefixLowest;
                    while (start > anchor && match > mStart && start[-1] == match[-1])
                    {
                        start--;
                        match--;
                        matchLength++;
                    }
                }

                offset2 = offset1;
                assert(offBase > 3);
                offset1 = (uint)(offBase - 3);
            }

            _storeSequence:
            {
                var litLength = (nuint)(start - anchor);
                ZSTD_storeSeq(seqStore, litLength, anchor, iend, (uint)offBase, matchLength);
                anchor = ip = start + matchLength;
            }

            if (ms->lazySkipping != 0)
            {
                if (searchMethod == SearchMethodE.SearchRowHash)
                {
                    ZSTD_row_fillHashCache(ms, @base, rowLog, mls, ms->nextToUpdate, ilimit);
                }

                ms->lazySkipping = 0;
            }

            if (isDxS != 0)
            {
                while (ip <= ilimit)
                {
                    var current2 = (uint)(ip - @base);
                    var repIndex = current2 - offset2;
                    var repMatch = repIndex < prefixLowestIndex ? dictBase - dictIndexDelta + repIndex : @base + repIndex;
                    if (ZSTD_index_overlap_check(prefixLowestIndex, repIndex) != 0 && MEM_read32(repMatch) == MEM_read32(ip))
                    {
                        var repEnd2 = repIndex < prefixLowestIndex ? dictEnd : iend;
                        matchLength = ZSTD_count_2segments(ip + 4, repMatch + 4, iend, repEnd2, prefixLowest) + 4;
                        offBase = offset2;
                        offset2 = offset1;
                        offset1 = (uint)offBase;
                        assert(1 >= 1);
                        assert(1 <= 3);
                        ZSTD_storeSeq(seqStore, 0, anchor, iend, 1, matchLength);
                        ip += matchLength;
                        anchor = ip;
                        continue;
                    }

                    break;
                }
            }

            if (dictMode == ZstdDictModeE.ZstdNoDict)
            {
                while (ip <= ilimit && offset2 > 0 && MEM_read32(ip) == MEM_read32(ip - offset2))
                {
                    matchLength = ZSTD_count(ip + 4, ip + 4 - offset2, iend) + 4;
                    offBase = offset2;
                    offset2 = offset1;
                    offset1 = (uint)offBase;
                    assert(1 >= 1);
                    assert(1 <= 3);
                    ZSTD_storeSeq(seqStore, 0, anchor, iend, 1, matchLength);
                    ip += matchLength;
                    anchor = ip;
                }
            }
        }

        offsetSaved2 = offsetSaved1 != 0 && offset1 != 0 ? offsetSaved1 : offsetSaved2;
        rep[0] = offset1 != 0 ? offset1 : offsetSaved1;
        rep[1] = offset2 != 0 ? offset2 : offsetSaved2;
        return (nuint)(iend - anchor);
    }

    private static nuint ZSTD_compressBlock_greedy(ZstdMatchStateT* ms, SeqStoreT* seqStore, uint* rep, void* src, nuint srcSize)
    {
        return ZSTD_compressBlock_lazy_generic(ms, seqStore, rep, src, srcSize, SearchMethodE.SearchHashChain, 0, ZstdDictModeE.ZstdNoDict);
    }

    private static nuint ZSTD_compressBlock_greedy_dictMatchState(ZstdMatchStateT* ms, SeqStoreT* seqStore, uint* rep, void* src, nuint srcSize)
    {
        return ZSTD_compressBlock_lazy_generic(ms, seqStore, rep, src, srcSize, SearchMethodE.SearchHashChain, 0, ZstdDictModeE.ZstdDictMatchState);
    }

    private static nuint ZSTD_compressBlock_greedy_dedicatedDictSearch(ZstdMatchStateT* ms, SeqStoreT* seqStore, uint* rep, void* src, nuint srcSize)
    {
        return ZSTD_compressBlock_lazy_generic(ms, seqStore, rep, src, srcSize, SearchMethodE.SearchHashChain, 0, ZstdDictModeE.ZstdDedicatedDictSearch);
    }

    private static nuint ZSTD_compressBlock_greedy_row(ZstdMatchStateT* ms, SeqStoreT* seqStore, uint* rep, void* src, nuint srcSize)
    {
        return ZSTD_compressBlock_lazy_generic(ms, seqStore, rep, src, srcSize, SearchMethodE.SearchRowHash, 0, ZstdDictModeE.ZstdNoDict);
    }

    private static nuint ZSTD_compressBlock_greedy_dictMatchState_row(ZstdMatchStateT* ms, SeqStoreT* seqStore, uint* rep, void* src, nuint srcSize)
    {
        return ZSTD_compressBlock_lazy_generic(ms, seqStore, rep, src, srcSize, SearchMethodE.SearchRowHash, 0, ZstdDictModeE.ZstdDictMatchState);
    }

    private static nuint ZSTD_compressBlock_greedy_dedicatedDictSearch_row(ZstdMatchStateT* ms, SeqStoreT* seqStore, uint* rep, void* src, nuint srcSize)
    {
        return ZSTD_compressBlock_lazy_generic(ms, seqStore, rep, src, srcSize, SearchMethodE.SearchRowHash, 0, ZstdDictModeE.ZstdDedicatedDictSearch);
    }

    private static nuint ZSTD_compressBlock_lazy(ZstdMatchStateT* ms, SeqStoreT* seqStore, uint* rep, void* src, nuint srcSize)
    {
        return ZSTD_compressBlock_lazy_generic(ms, seqStore, rep, src, srcSize, SearchMethodE.SearchHashChain, 1, ZstdDictModeE.ZstdNoDict);
    }

    private static nuint ZSTD_compressBlock_lazy_dictMatchState(ZstdMatchStateT* ms, SeqStoreT* seqStore, uint* rep, void* src, nuint srcSize)
    {
        return ZSTD_compressBlock_lazy_generic(ms, seqStore, rep, src, srcSize, SearchMethodE.SearchHashChain, 1, ZstdDictModeE.ZstdDictMatchState);
    }

    private static nuint ZSTD_compressBlock_lazy_dedicatedDictSearch(ZstdMatchStateT* ms, SeqStoreT* seqStore, uint* rep, void* src, nuint srcSize)
    {
        return ZSTD_compressBlock_lazy_generic(ms, seqStore, rep, src, srcSize, SearchMethodE.SearchHashChain, 1, ZstdDictModeE.ZstdDedicatedDictSearch);
    }

    private static nuint ZSTD_compressBlock_lazy_row(ZstdMatchStateT* ms, SeqStoreT* seqStore, uint* rep, void* src, nuint srcSize)
    {
        return ZSTD_compressBlock_lazy_generic(ms, seqStore, rep, src, srcSize, SearchMethodE.SearchRowHash, 1, ZstdDictModeE.ZstdNoDict);
    }

    private static nuint ZSTD_compressBlock_lazy_dictMatchState_row(ZstdMatchStateT* ms, SeqStoreT* seqStore, uint* rep, void* src, nuint srcSize)
    {
        return ZSTD_compressBlock_lazy_generic(ms, seqStore, rep, src, srcSize, SearchMethodE.SearchRowHash, 1, ZstdDictModeE.ZstdDictMatchState);
    }

    private static nuint ZSTD_compressBlock_lazy_dedicatedDictSearch_row(ZstdMatchStateT* ms, SeqStoreT* seqStore, uint* rep, void* src, nuint srcSize)
    {
        return ZSTD_compressBlock_lazy_generic(ms, seqStore, rep, src, srcSize, SearchMethodE.SearchRowHash, 1, ZstdDictModeE.ZstdDedicatedDictSearch);
    }

    private static nuint ZSTD_compressBlock_lazy2(ZstdMatchStateT* ms, SeqStoreT* seqStore, uint* rep, void* src, nuint srcSize)
    {
        return ZSTD_compressBlock_lazy_generic(ms, seqStore, rep, src, srcSize, SearchMethodE.SearchHashChain, 2, ZstdDictModeE.ZstdNoDict);
    }

    private static nuint ZSTD_compressBlock_lazy2_dictMatchState(ZstdMatchStateT* ms, SeqStoreT* seqStore, uint* rep, void* src, nuint srcSize)
    {
        return ZSTD_compressBlock_lazy_generic(ms, seqStore, rep, src, srcSize, SearchMethodE.SearchHashChain, 2, ZstdDictModeE.ZstdDictMatchState);
    }

    private static nuint ZSTD_compressBlock_lazy2_dedicatedDictSearch(ZstdMatchStateT* ms, SeqStoreT* seqStore, uint* rep, void* src, nuint srcSize)
    {
        return ZSTD_compressBlock_lazy_generic(ms, seqStore, rep, src, srcSize, SearchMethodE.SearchHashChain, 2, ZstdDictModeE.ZstdDedicatedDictSearch);
    }

    private static nuint ZSTD_compressBlock_lazy2_row(ZstdMatchStateT* ms, SeqStoreT* seqStore, uint* rep, void* src, nuint srcSize)
    {
        return ZSTD_compressBlock_lazy_generic(ms, seqStore, rep, src, srcSize, SearchMethodE.SearchRowHash, 2, ZstdDictModeE.ZstdNoDict);
    }

    private static nuint ZSTD_compressBlock_lazy2_dictMatchState_row(ZstdMatchStateT* ms, SeqStoreT* seqStore, uint* rep, void* src, nuint srcSize)
    {
        return ZSTD_compressBlock_lazy_generic(ms, seqStore, rep, src, srcSize, SearchMethodE.SearchRowHash, 2, ZstdDictModeE.ZstdDictMatchState);
    }

    private static nuint ZSTD_compressBlock_lazy2_dedicatedDictSearch_row(ZstdMatchStateT* ms, SeqStoreT* seqStore, uint* rep, void* src, nuint srcSize)
    {
        return ZSTD_compressBlock_lazy_generic(ms, seqStore, rep, src, srcSize, SearchMethodE.SearchRowHash, 2, ZstdDictModeE.ZstdDedicatedDictSearch);
    }

    private static nuint ZSTD_compressBlock_btlazy2(ZstdMatchStateT* ms, SeqStoreT* seqStore, uint* rep, void* src, nuint srcSize)
    {
        return ZSTD_compressBlock_lazy_generic(ms, seqStore, rep, src, srcSize, SearchMethodE.SearchBinaryTree, 2, ZstdDictModeE.ZstdNoDict);
    }

    private static nuint ZSTD_compressBlock_btlazy2_dictMatchState(ZstdMatchStateT* ms, SeqStoreT* seqStore, uint* rep, void* src, nuint srcSize)
    {
        return ZSTD_compressBlock_lazy_generic(ms, seqStore, rep, src, srcSize, SearchMethodE.SearchBinaryTree, 2, ZstdDictModeE.ZstdDictMatchState);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static nuint ZSTD_compressBlock_lazy_extDict_generic(ZstdMatchStateT* ms, SeqStoreT* seqStore, uint* rep, void* src, nuint srcSize, SearchMethodE searchMethod, uint depth)
    {
        var istart = (byte*)src;
        var ip = istart;
        var anchor = istart;
        var iend = istart + srcSize;
        var ilimit = searchMethod == SearchMethodE.SearchRowHash ? iend - 8 - 8 : iend - 8;
        var @base = ms->window.@base;
        var dictLimit = ms->window.dictLimit;
        var prefixStart = @base + dictLimit;
        var dictBase = ms->window.dictBase;
        var dictEnd = dictBase + dictLimit;
        var dictStart = dictBase + ms->window.lowLimit;
        var windowLog = ms->cParams.windowLog;
        var mls = ms->cParams.minMatch <= 4 ? 4 : ms->cParams.minMatch <= 6 ? ms->cParams.minMatch : 6;
        var rowLog = ms->cParams.searchLog <= 4 ? 4 : ms->cParams.searchLog <= 6 ? ms->cParams.searchLog : 6;
        uint offset1 = rep[0], offset2 = rep[1];
        ms->lazySkipping = 0;
        ip += ip == prefixStart ? 1 : 0;
        if (searchMethod == SearchMethodE.SearchRowHash)
        {
            ZSTD_row_fillHashCache(ms, @base, rowLog, mls, ms->nextToUpdate, ilimit);
        }

        while (ip < ilimit)
        {
            nuint matchLength = 0;
            assert(1 >= 1);
            assert(1 <= 3);
            nuint offBase = 1;
            var start = ip + 1;
            var curr = (uint)(ip - @base);
            {
                var windowLow = ZSTD_getLowestMatchIndex(ms, curr + 1, windowLog);
                var repIndex = curr + 1 - offset1;
                var repBase = repIndex < dictLimit ? dictBase : @base;
                var repMatch = repBase + repIndex;
                if ((ZSTD_index_overlap_check(dictLimit, repIndex) & (offset1 <= curr + 1 - windowLow ? 1 : 0)) != 0)
                    if (MEM_read32(ip + 1) == MEM_read32(repMatch))
                    {
                        /* repcode detected we should take it */
                        var repEnd = repIndex < dictLimit ? dictEnd : iend;
                        matchLength = ZSTD_count_2segments(ip + 1 + 4, repMatch + 4, iend, repEnd, prefixStart) + 4;
                        if (depth == 0)
                            goto _storeSequence;
                    }
            }

            {
                nuint ofbCandidate = 999999999;
                var ml2 = ZSTD_searchMax(ms, ip, iend, &ofbCandidate, mls, rowLog, searchMethod, ZstdDictModeE.ZstdExtDict);
                if (ml2 > matchLength)
                {
                    matchLength = ml2;
                    start = ip;
                    offBase = ofbCandidate;
                }
            }

            if (matchLength < 4)
            {
                var step = (nuint)(ip - anchor) >> 8;
                ip += step + 1;
                ms->lazySkipping = step > 8 ? 1 : 0;
                continue;
            }

            if (depth >= 1)
                while (ip < ilimit)
                {
                    ip++;
                    curr++;
                    if (offBase != 0)
                    {
                        var windowLow = ZSTD_getLowestMatchIndex(ms, curr, windowLog);
                        var repIndex = curr - offset1;
                        var repBase = repIndex < dictLimit ? dictBase : @base;
                        var repMatch = repBase + repIndex;
                        if ((ZSTD_index_overlap_check(dictLimit, repIndex) & (offset1 <= curr - windowLow ? 1 : 0)) != 0)
                            if (MEM_read32(ip) == MEM_read32(repMatch))
                            {
                                /* repcode detected */
                                var repEnd = repIndex < dictLimit ? dictEnd : iend;
                                var repLength = ZSTD_count_2segments(ip + 4, repMatch + 4, iend, repEnd, prefixStart) + 4;
                                var gain2 = (int)(repLength * 3);
                                var gain1 = (int)(matchLength * 3 - ZSTD_highbit32((uint)offBase) + 1);
                                if (repLength >= 4 && gain2 > gain1)
                                {
                                    matchLength = repLength;
                                    assert(1 >= 1);
                                    assert(1 <= 3);
                                    offBase = 1;
                                    start = ip;
                                }
                            }
                    }

                    {
                        nuint ofbCandidate = 999999999;
                        var ml2 = ZSTD_searchMax(ms, ip, iend, &ofbCandidate, mls, rowLog, searchMethod, ZstdDictModeE.ZstdExtDict);
                        /* raw approx */
                        var gain2 = (int)(ml2 * 4 - ZSTD_highbit32((uint)ofbCandidate));
                        var gain1 = (int)(matchLength * 4 - ZSTD_highbit32((uint)offBase) + 4);
                        if (ml2 >= 4 && gain2 > gain1)
                        {
                            matchLength = ml2;
                            offBase = ofbCandidate;
                            start = ip;
                            continue;
                        }
                    }

                    if (depth == 2 && ip < ilimit)
                    {
                        ip++;
                        curr++;
                        if (offBase != 0)
                        {
                            var windowLow = ZSTD_getLowestMatchIndex(ms, curr, windowLog);
                            var repIndex = curr - offset1;
                            var repBase = repIndex < dictLimit ? dictBase : @base;
                            var repMatch = repBase + repIndex;
                            if ((ZSTD_index_overlap_check(dictLimit, repIndex) & (offset1 <= curr - windowLow ? 1 : 0)) != 0)
                                if (MEM_read32(ip) == MEM_read32(repMatch))
                                {
                                    /* repcode detected */
                                    var repEnd = repIndex < dictLimit ? dictEnd : iend;
                                    var repLength = ZSTD_count_2segments(ip + 4, repMatch + 4, iend, repEnd, prefixStart) + 4;
                                    var gain2 = (int)(repLength * 4);
                                    var gain1 = (int)(matchLength * 4 - ZSTD_highbit32((uint)offBase) + 1);
                                    if (repLength >= 4 && gain2 > gain1)
                                    {
                                        matchLength = repLength;
                                        assert(1 >= 1);
                                        assert(1 <= 3);
                                        offBase = 1;
                                        start = ip;
                                    }
                                }
                        }

                        {
                            nuint ofbCandidate = 999999999;
                            var ml2 = ZSTD_searchMax(ms, ip, iend, &ofbCandidate, mls, rowLog, searchMethod, ZstdDictModeE.ZstdExtDict);
                            /* raw approx */
                            var gain2 = (int)(ml2 * 4 - ZSTD_highbit32((uint)ofbCandidate));
                            var gain1 = (int)(matchLength * 4 - ZSTD_highbit32((uint)offBase) + 7);
                            if (ml2 >= 4 && gain2 > gain1)
                            {
                                matchLength = ml2;
                                offBase = ofbCandidate;
                                start = ip;
                                continue;
                            }
                        }
                    }

                    break;
                }

            if (offBase > 3)
            {
                assert(offBase > 3);
                var matchIndex = (uint)((nuint)(start - @base) - (offBase - 3));
                var match = matchIndex < dictLimit ? dictBase + matchIndex : @base + matchIndex;
                var mStart = matchIndex < dictLimit ? dictStart : prefixStart;
                while (start > anchor && match > mStart && start[-1] == match[-1])
                {
                    start--;
                    match--;
                    matchLength++;
                }

                offset2 = offset1;
                assert(offBase > 3);
                offset1 = (uint)(offBase - 3);
            }

            _storeSequence:
            {
                var litLength = (nuint)(start - anchor);
                ZSTD_storeSeq(seqStore, litLength, anchor, iend, (uint)offBase, matchLength);
                anchor = ip = start + matchLength;
            }

            if (ms->lazySkipping != 0)
            {
                if (searchMethod == SearchMethodE.SearchRowHash)
                {
                    ZSTD_row_fillHashCache(ms, @base, rowLog, mls, ms->nextToUpdate, ilimit);
                }

                ms->lazySkipping = 0;
            }

            while (ip <= ilimit)
            {
                var repCurrent = (uint)(ip - @base);
                var windowLow = ZSTD_getLowestMatchIndex(ms, repCurrent, windowLog);
                var repIndex = repCurrent - offset2;
                var repBase = repIndex < dictLimit ? dictBase : @base;
                var repMatch = repBase + repIndex;
                if ((ZSTD_index_overlap_check(dictLimit, repIndex) & (offset2 <= repCurrent - windowLow ? 1 : 0)) != 0)
                    if (MEM_read32(ip) == MEM_read32(repMatch))
                    {
                        /* repcode detected we should take it */
                        var repEnd = repIndex < dictLimit ? dictEnd : iend;
                        matchLength = ZSTD_count_2segments(ip + 4, repMatch + 4, iend, repEnd, prefixStart) + 4;
                        offBase = offset2;
                        offset2 = offset1;
                        offset1 = (uint)offBase;
                        assert(1 >= 1);
                        assert(1 <= 3);
                        ZSTD_storeSeq(seqStore, 0, anchor, iend, 1, matchLength);
                        ip += matchLength;
                        anchor = ip;
                        continue;
                    }

                break;
            }
        }

        rep[0] = offset1;
        rep[1] = offset2;
        return (nuint)(iend - anchor);
    }

    private static nuint ZSTD_compressBlock_greedy_extDict(ZstdMatchStateT* ms, SeqStoreT* seqStore, uint* rep, void* src, nuint srcSize)
    {
        return ZSTD_compressBlock_lazy_extDict_generic(ms, seqStore, rep, src, srcSize, SearchMethodE.SearchHashChain, 0);
    }

    private static nuint ZSTD_compressBlock_greedy_extDict_row(ZstdMatchStateT* ms, SeqStoreT* seqStore, uint* rep, void* src, nuint srcSize)
    {
        return ZSTD_compressBlock_lazy_extDict_generic(ms, seqStore, rep, src, srcSize, SearchMethodE.SearchRowHash, 0);
    }

    private static nuint ZSTD_compressBlock_lazy_extDict(ZstdMatchStateT* ms, SeqStoreT* seqStore, uint* rep, void* src, nuint srcSize)
    {
        return ZSTD_compressBlock_lazy_extDict_generic(ms, seqStore, rep, src, srcSize, SearchMethodE.SearchHashChain, 1);
    }

    private static nuint ZSTD_compressBlock_lazy_extDict_row(ZstdMatchStateT* ms, SeqStoreT* seqStore, uint* rep, void* src, nuint srcSize)
    {
        return ZSTD_compressBlock_lazy_extDict_generic(ms, seqStore, rep, src, srcSize, SearchMethodE.SearchRowHash, 1);
    }

    private static nuint ZSTD_compressBlock_lazy2_extDict(ZstdMatchStateT* ms, SeqStoreT* seqStore, uint* rep, void* src, nuint srcSize)
    {
        return ZSTD_compressBlock_lazy_extDict_generic(ms, seqStore, rep, src, srcSize, SearchMethodE.SearchHashChain, 2);
    }

    private static nuint ZSTD_compressBlock_lazy2_extDict_row(ZstdMatchStateT* ms, SeqStoreT* seqStore, uint* rep, void* src, nuint srcSize)
    {
        return ZSTD_compressBlock_lazy_extDict_generic(ms, seqStore, rep, src, srcSize, SearchMethodE.SearchRowHash, 2);
    }

    private static nuint ZSTD_compressBlock_btlazy2_extDict(ZstdMatchStateT* ms, SeqStoreT* seqStore, uint* rep, void* src, nuint srcSize)
    {
        return ZSTD_compressBlock_lazy_extDict_generic(ms, seqStore, rep, src, srcSize, SearchMethodE.SearchBinaryTree, 2);
    }
}