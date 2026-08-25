using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics.X86;
using InlineMethod;
using static VendoredZSTD.UnsafeHelper;

namespace VendoredZSTD.Unsafe;

public static unsafe partial class Methods
{
    private static void ZSTD_fillHashTableForCDict(
        ZstdMatchStateT* ms,
        void* end,
        ZstdDictTableLoadMethodE dtlm
    )
    {
        var cParams = &ms->cParams;
        var hashTable = ms->hashTable;
        var hBits = cParams->hashLog + 8;
        var mls = cParams->minMatch;
        var @base = ms->window.@base;
        var ip = @base + ms->nextToUpdate;
        var iend = (byte*)end - 8;
        const uint fastHashFillStep = 3;
        assert(dtlm == ZstdDictTableLoadMethodE.ZstdDtlmFull);
        for (; ip + fastHashFillStep < iend + 2; ip += fastHashFillStep)
        {
            var curr = (uint)(ip - @base);
            {
                var hashAndTag = ZSTD_hashPtr(ip, hBits, mls);
                ZSTD_writeTaggedIndex(hashTable, hashAndTag, curr);
            }

            if (dtlm == ZstdDictTableLoadMethodE.ZstdDtlmFast)
                continue;
            {
                uint p;
                for (p = 1; p < fastHashFillStep; ++p)
                {
                    var hashAndTag = ZSTD_hashPtr(ip + p, hBits, mls);
                    if (hashTable[hashAndTag >> 8] == 0)
                        ZSTD_writeTaggedIndex(hashTable, hashAndTag, curr + p);
                }
            }
        }
    }

    private static void ZSTD_fillHashTableForCCtx(
        ZstdMatchStateT* ms,
        void* end,
        ZstdDictTableLoadMethodE dtlm
    )
    {
        var cParams = &ms->cParams;
        var hashTable = ms->hashTable;
        var hBits = cParams->hashLog;
        var mls = cParams->minMatch;
        var @base = ms->window.@base;
        var ip = @base + ms->nextToUpdate;
        var iend = (byte*)end - 8;
        const uint fastHashFillStep = 3;
        assert(dtlm == ZstdDictTableLoadMethodE.ZstdDtlmFast);
        for (; ip + fastHashFillStep < iend + 2; ip += fastHashFillStep)
        {
            var curr = (uint)(ip - @base);
            var hash0 = ZSTD_hashPtr(ip, hBits, mls);
            hashTable[hash0] = curr;
            if (dtlm == ZstdDictTableLoadMethodE.ZstdDtlmFast)
                continue;
            {
                uint p;
                for (p = 1; p < fastHashFillStep; ++p)
                {
                    var hash = ZSTD_hashPtr(ip + p, hBits, mls);
                    if (hashTable[hash] == 0)
                        hashTable[hash] = curr + p;
                }
            }
        }
    }

    private static void ZSTD_fillHashTable(
        ZstdMatchStateT* ms,
        void* end,
        ZstdDictTableLoadMethodE dtlm,
        ZstdTableFillPurposeE tfp
    )
    {
        if (tfp == ZstdTableFillPurposeE.ZstdTfpForCDict)
            ZSTD_fillHashTableForCDict(ms, end, dtlm);
        else
            ZSTD_fillHashTableForCCtx(ms, end, dtlm);
    }

    /*
     * If you squint hard enough (and ignore repcodes), the search operation at any
     * given position is broken into 4 stages:
     *
     * 1. Hash   (map position to hash value via input read)
     * 2. Lookup (map hash val to index via hashtable read)
     * 3. Load   (map index to value at that position via input read)
     * 4. Compare
     *
     * Each of these steps involves a memory read at an address which is computed
     * from the previous step. This means these steps must be sequenced and their
     * latencies are cumulative.
     *
     * Rather than do 1->2->3->4 sequentially for a single position before moving
     * onto the next, this implementation interleaves these operations across the
     * next few positions:
     *
     * R = Repcode Read & Compare
     * H = Hash
     * T = Table Lookup
     * M = Match Read & Compare
     *
     * Pos | Time -->
     * ----+-------------------
     * N   | ... M
     * N+1 | ...   TM
     * N+2 |    R H   T M
     * N+3 |         H    TM
     * N+4 |           R H   T M
     * N+5 |                H   ...
     * N+6 |                  R ...
     *
     * This is very much analogous to the pipelining of execution in a CPU. And just
     * like a CPU, we have to dump the pipeline when we find a match (i.e., take a
     * branch).
     *
     * When this happens, we throw away our current state, and do the following prep
     * to re-enter the loop:
     *
     * Pos | Time -->
     * ----+-------------------
     * N   | H T
     * N+1 |  H
     *
     * This is also the work we do at the beginning to enter the loop initially.
     */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [Inline]
    private static nuint ZSTD_compressBlock_fast_noDict_generic(
        ZstdMatchStateT* ms,
        SeqStoreT* seqStore,
        uint* rep,
        void* src,
        nuint srcSize,
        uint mls,
        uint hasStep
    )
    {
        var cParams = &ms->cParams;
        var hashTable = ms->hashTable;
        var hlog = cParams->hashLog;
        /* support stepSize of 0 */
        nuint stepSize =
            hasStep != 0
                ? cParams->targetLength + (uint)(cParams->targetLength == 0 ? 1 : 0) + 1
                : 2;
        var @base = ms->window.@base;
        var istart = (byte*)src;
        var endIndex = (uint)((nuint)(istart - @base) + srcSize);
        var prefixStartIndex = ZSTD_getLowestPrefixIndex(ms, endIndex, cParams->windowLog);
        var prefixStart = @base + prefixStartIndex;
        var iend = istart + srcSize;
        var ilimit = iend - 8;
        var anchor = istart;
        var ip0 = istart;
        byte* ip1;
        byte* ip2;
        byte* ip3;
        uint current0;
        var repOffset1 = rep[0];
        var repOffset2 = rep[1];
        uint offsetSaved1 = 0,
            offsetSaved2 = 0;
        /* hash for ip0 */
        nuint hash0;
        /* hash for ip1 */
        nuint hash1;
        /* match idx for ip0 */
        uint idx;
        /* src value at match idx */
        uint mval;
        uint offcode;
        byte* match0;
        nuint mLength;
        /* ip0 and ip1 are always adjacent. The targetLength skipping and
         * uncompressibility acceleration is applied to every other position,
         * matching the behavior of #1562. step therefore represents the gap
         * between pairs of positions, from ip0 to ip2 or ip1 to ip3. */
        nuint step;
        byte* nextStep;
        const nuint kStepIncr = 1 << (8 - 1);
        ip0 += ip0 == prefixStart ? 1 : 0;
        {
            var curr = (uint)(ip0 - @base);
            var windowLow = ZSTD_getLowestPrefixIndex(ms, curr, cParams->windowLog);
            var maxRep = curr - windowLow;
            if (repOffset2 > maxRep)
            {
                offsetSaved2 = repOffset2;
                repOffset2 = 0;
            }

            if (repOffset1 > maxRep)
            {
                offsetSaved1 = repOffset1;
                repOffset1 = 0;
            }
        }

        _start:
        step = stepSize;
        nextStep = ip0 + kStepIncr;
        ip1 = ip0 + 1;
        ip2 = ip0 + step;
        ip3 = ip2 + 1;
        if (ip3 >= ilimit)
            goto _cleanup;

        hash0 = ZSTD_hashPtr(ip0, hlog, mls);
        hash1 = ZSTD_hashPtr(ip1, hlog, mls);
        idx = hashTable[hash0];
        do
        {
            /* load repcode match for ip[2]*/
            var rval = MEM_read32(ip2 - repOffset1);
            current0 = (uint)(ip0 - @base);
            hashTable[hash0] = current0;
            if (MEM_read32(ip2) == rval && repOffset1 > 0)
            {
                ip0 = ip2;
                match0 = ip0 - repOffset1;
                mLength = ip0[-1] == match0[-1] ? 1U : 0U;
                ip0 -= mLength;
                match0 -= mLength;
                assert(1 >= 1);
                assert(1 <= 3);
                offcode = 1;
                mLength += 4;
                hashTable[hash1] = (uint)(ip1 - @base);
                goto _match;
            }

            if (idx >= prefixStartIndex)
                mval = MEM_read32(@base + idx);
            else
                mval = MEM_read32(ip0) ^ 1;

            if (MEM_read32(ip0) == mval)
            {
                hashTable[hash1] = (uint)(ip1 - @base);
                goto _offset;
            }

            idx = hashTable[hash1];
            hash0 = hash1;
            hash1 = ZSTD_hashPtr(ip2, hlog, mls);
            ip0 = ip1;
            ip1 = ip2;
            ip2 = ip3;
            current0 = (uint)(ip0 - @base);
            hashTable[hash0] = current0;
            if (idx >= prefixStartIndex)
                mval = MEM_read32(@base + idx);
            else
                mval = MEM_read32(ip0) ^ 1;

            if (MEM_read32(ip0) == mval)
            {
                if (step <= 4)
                    hashTable[hash1] = (uint)(ip1 - @base);

                goto _offset;
            }

            idx = hashTable[hash1];
            hash0 = hash1;
            hash1 = ZSTD_hashPtr(ip2, hlog, mls);
            ip0 = ip1;
            ip1 = ip2;
            ip2 = ip0 + step;
            ip3 = ip1 + step;
            if (ip2 >= nextStep)
            {
                step++;
#if NETCOREAPP3_0_OR_GREATER
                if (Sse.IsSupported)
                {
                    Sse.Prefetch0(ip1 + 64);
                    Sse.Prefetch0(ip1 + 128);
                }
#endif

                nextStep += kStepIncr;
            }
        } while (ip3 < ilimit);

        _cleanup:
        offsetSaved2 = offsetSaved1 != 0 && repOffset1 != 0 ? offsetSaved1 : offsetSaved2;
        rep[0] = repOffset1 != 0 ? repOffset1 : offsetSaved1;
        rep[1] = repOffset2 != 0 ? repOffset2 : offsetSaved2;
        return (nuint)(iend - anchor);
        _offset:
        match0 = @base + idx;
        repOffset2 = repOffset1;
        repOffset1 = (uint)(ip0 - match0);
        assert(repOffset1 > 0);
        offcode = repOffset1 + 3;
        mLength = 4;
        while (ip0 > anchor && match0 > prefixStart && ip0[-1] == match0[-1])
        {
            ip0--;
            match0--;
            mLength++;
        }

        _match:
        mLength += ZSTD_count(ip0 + mLength, match0 + mLength, iend);
        ZSTD_storeSeq(seqStore, (nuint)(ip0 - anchor), anchor, iend, offcode, mLength);
        ip0 += mLength;
        anchor = ip0;
        if (ip0 <= ilimit)
        {
            assert(@base + current0 + 2 > istart);
            hashTable[ZSTD_hashPtr(@base + current0 + 2, hlog, mls)] = current0 + 2;
            hashTable[ZSTD_hashPtr(ip0 - 2, hlog, mls)] = (uint)(ip0 - 2 - @base);
            if (repOffset2 > 0)
                while (ip0 <= ilimit && MEM_read32(ip0) == MEM_read32(ip0 - repOffset2))
                {
                    /* store sequence */
                    var rLength = ZSTD_count(ip0 + 4, ip0 + 4 - repOffset2, iend) + 4;
                    {
                        /* swap rep_offset2 <=> rep_offset1 */
                        var tmpOff = repOffset2;
                        repOffset2 = repOffset1;
                        repOffset1 = tmpOff;
                    }

                    hashTable[ZSTD_hashPtr(ip0, hlog, mls)] = (uint)(ip0 - @base);
                    ip0 += rLength;
                    assert(1 >= 1);
                    assert(1 <= 3);
                    ZSTD_storeSeq(seqStore, 0, anchor, iend, 1, rLength);
                    anchor = ip0;
                }
        }

        goto _start;
    }

    private static nuint ZSTD_compressBlock_fast_noDict_4_1(
        ZstdMatchStateT* ms,
        SeqStoreT* seqStore,
        uint* rep,
        void* src,
        nuint srcSize
    )
    {
        return ZSTD_compressBlock_fast_noDict_generic(ms, seqStore, rep, src, srcSize, 4, 1);
    }

    private static nuint ZSTD_compressBlock_fast_noDict_5_1(
        ZstdMatchStateT* ms,
        SeqStoreT* seqStore,
        uint* rep,
        void* src,
        nuint srcSize
    )
    {
        return ZSTD_compressBlock_fast_noDict_generic(ms, seqStore, rep, src, srcSize, 5, 1);
    }

    private static nuint ZSTD_compressBlock_fast_noDict_6_1(
        ZstdMatchStateT* ms,
        SeqStoreT* seqStore,
        uint* rep,
        void* src,
        nuint srcSize
    )
    {
        return ZSTD_compressBlock_fast_noDict_generic(ms, seqStore, rep, src, srcSize, 6, 1);
    }

    private static nuint ZSTD_compressBlock_fast_noDict_7_1(
        ZstdMatchStateT* ms,
        SeqStoreT* seqStore,
        uint* rep,
        void* src,
        nuint srcSize
    )
    {
        return ZSTD_compressBlock_fast_noDict_generic(ms, seqStore, rep, src, srcSize, 7, 1);
    }

    private static nuint ZSTD_compressBlock_fast_noDict_4_0(
        ZstdMatchStateT* ms,
        SeqStoreT* seqStore,
        uint* rep,
        void* src,
        nuint srcSize
    )
    {
        return ZSTD_compressBlock_fast_noDict_generic(ms, seqStore, rep, src, srcSize, 4, 0);
    }

    private static nuint ZSTD_compressBlock_fast_noDict_5_0(
        ZstdMatchStateT* ms,
        SeqStoreT* seqStore,
        uint* rep,
        void* src,
        nuint srcSize
    )
    {
        return ZSTD_compressBlock_fast_noDict_generic(ms, seqStore, rep, src, srcSize, 5, 0);
    }

    private static nuint ZSTD_compressBlock_fast_noDict_6_0(
        ZstdMatchStateT* ms,
        SeqStoreT* seqStore,
        uint* rep,
        void* src,
        nuint srcSize
    )
    {
        return ZSTD_compressBlock_fast_noDict_generic(ms, seqStore, rep, src, srcSize, 6, 0);
    }

    private static nuint ZSTD_compressBlock_fast_noDict_7_0(
        ZstdMatchStateT* ms,
        SeqStoreT* seqStore,
        uint* rep,
        void* src,
        nuint srcSize
    )
    {
        return ZSTD_compressBlock_fast_noDict_generic(ms, seqStore, rep, src, srcSize, 7, 0);
    }

    private static nuint ZSTD_compressBlock_fast(
        ZstdMatchStateT* ms,
        SeqStoreT* seqStore,
        uint* rep,
        void* src,
        nuint srcSize
    )
    {
        var mls = ms->cParams.minMatch;
        assert(ms->dictMatchState == null);
        if (ms->cParams.targetLength > 1)
            switch (mls)
            {
                default:
                case 4:
                    return ZSTD_compressBlock_fast_noDict_4_1(ms, seqStore, rep, src, srcSize);
                case 5:
                    return ZSTD_compressBlock_fast_noDict_5_1(ms, seqStore, rep, src, srcSize);
                case 6:
                    return ZSTD_compressBlock_fast_noDict_6_1(ms, seqStore, rep, src, srcSize);
                case 7:
                    return ZSTD_compressBlock_fast_noDict_7_1(ms, seqStore, rep, src, srcSize);
            }

        switch (mls)
        {
            default:
            case 4:
                return ZSTD_compressBlock_fast_noDict_4_0(ms, seqStore, rep, src, srcSize);
            case 5:
                return ZSTD_compressBlock_fast_noDict_5_0(ms, seqStore, rep, src, srcSize);
            case 6:
                return ZSTD_compressBlock_fast_noDict_6_0(ms, seqStore, rep, src, srcSize);
            case 7:
                return ZSTD_compressBlock_fast_noDict_7_0(ms, seqStore, rep, src, srcSize);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static nuint ZSTD_compressBlock_fast_dictMatchState_generic(
        ZstdMatchStateT* ms,
        SeqStoreT* seqStore,
        uint* rep,
        void* src,
        nuint srcSize,
        uint mls,
        uint hasStep
    )
    {
        var cParams = &ms->cParams;
        var hashTable = ms->hashTable;
        var hlog = cParams->hashLog;
        /* support stepSize of 0 */
        var stepSize = cParams->targetLength + (uint)(cParams->targetLength == 0 ? 1 : 0);
        var @base = ms->window.@base;
        var istart = (byte*)src;
        var ip0 = istart;
        /* we assert below that stepSize >= 1 */
        var ip1 = ip0 + stepSize;
        var anchor = istart;
        var prefixStartIndex = ms->window.dictLimit;
        var prefixStart = @base + prefixStartIndex;
        var iend = istart + srcSize;
        var ilimit = iend - 8;
        uint offset1 = rep[0],
            offset2 = rep[1];
        var dms = ms->dictMatchState;
        var dictCParams = &dms->cParams;
        var dictHashTable = dms->hashTable;
        var dictStartIndex = dms->window.dictLimit;
        var dictBase = dms->window.@base;
        var dictStart = dictBase + dictStartIndex;
        var dictEnd = dms->window.nextSrc;
        var dictIndexDelta = prefixStartIndex - (uint)(dictEnd - dictBase);
        var dictAndPrefixLength = (uint)(istart - prefixStart + dictEnd - dictStart);
        var dictHBits = dictCParams->hashLog + 8;
        /* if a dictionary is still attached, it necessarily means that
         * it is within window size. So we just check it. */
        var maxDistance = 1U << (int)cParams->windowLog;
        var endIndex = (uint)((nuint)(istart - @base) + srcSize);
        assert(endIndex - prefixStartIndex <= maxDistance);
        assert(prefixStartIndex >= (uint)(dictEnd - dictBase));
        if (ms->prefetchCDictTables != 0)
        {
            var hashTableBytes = ((nuint)1 << (int)dictCParams->hashLog) * sizeof(uint);
            {
                var ptr = (sbyte*)dictHashTable;
                var size = hashTableBytes;
                nuint pos;
                for (pos = 0; pos < size; pos += 64)
                {
#if NETCOREAPP3_0_OR_GREATER
                    if (Sse.IsSupported)
                        Sse.Prefetch1(ptr + pos);
#endif
                }
            }
        }

        ip0 += dictAndPrefixLength == 0 ? 1 : 0;
        assert(offset1 <= dictAndPrefixLength);
        assert(offset2 <= dictAndPrefixLength);
        assert(stepSize >= 1);
        while (ip1 <= ilimit)
        {
            nuint mLength;
            var hash0 = ZSTD_hashPtr(ip0, hlog, mls);
            var dictHashAndTag0 = ZSTD_hashPtr(ip0, dictHBits, mls);
            var dictMatchIndexAndTag = dictHashTable[dictHashAndTag0 >> 8];
            var dictTagsMatch = ZSTD_comparePackedTags(dictMatchIndexAndTag, dictHashAndTag0);
            var matchIndex = hashTable[hash0];
            var curr = (uint)(ip0 - @base);
            nuint step = stepSize;
            const nuint kStepIncr = 1 << 8;
            var nextStep = ip0 + kStepIncr;
            while (true)
            {
                var match = @base + matchIndex;
                var repIndex = curr + 1 - offset1;
                var repMatch =
                    repIndex < prefixStartIndex
                        ? dictBase + (repIndex - dictIndexDelta)
                        : @base + repIndex;
                var hash1 = ZSTD_hashPtr(ip1, hlog, mls);
                var dictHashAndTag1 = ZSTD_hashPtr(ip1, dictHBits, mls);
                hashTable[hash0] = curr;
                if (
                    prefixStartIndex - 1 - repIndex >= 3
                    && MEM_read32(repMatch) == MEM_read32(ip0 + 1)
                )
                {
                    var repMatchEnd = repIndex < prefixStartIndex ? dictEnd : iend;
                    mLength =
                        ZSTD_count_2segments(
                            ip0 + 1 + 4,
                            repMatch + 4,
                            iend,
                            repMatchEnd,
                            prefixStart
                        ) + 4;
                    ip0++;
                    assert(1 >= 1);
                    assert(1 <= 3);
                    ZSTD_storeSeq(seqStore, (nuint)(ip0 - anchor), anchor, iend, 1, mLength);
                    break;
                }

                if (dictTagsMatch != 0)
                {
                    /* Found a possible dict match */
                    var dictMatchIndex = dictMatchIndexAndTag >> 8;
                    var dictMatch = dictBase + dictMatchIndex;
                    if (dictMatchIndex > dictStartIndex && MEM_read32(dictMatch) == MEM_read32(ip0))
                        if (matchIndex <= prefixStartIndex)
                        {
                            var offset = curr - dictMatchIndex - dictIndexDelta;
                            mLength =
                                ZSTD_count_2segments(
                                    ip0 + 4,
                                    dictMatch + 4,
                                    iend,
                                    dictEnd,
                                    prefixStart
                                ) + 4;
                            while (
                                ip0 > anchor && dictMatch > dictStart && ip0[-1] == dictMatch[-1]
                            )
                            {
                                ip0--;
                                dictMatch--;
                                mLength++;
                            }

                            offset2 = offset1;
                            offset1 = offset;
                            assert(offset > 0);
                            ZSTD_storeSeq(
                                seqStore,
                                (nuint)(ip0 - anchor),
                                anchor,
                                iend,
                                offset + 3,
                                mLength
                            );
                            break;
                        }
                }

                if (matchIndex > prefixStartIndex && MEM_read32(match) == MEM_read32(ip0))
                {
                    /* found a regular match */
                    var offset = (uint)(ip0 - match);
                    mLength = ZSTD_count(ip0 + 4, match + 4, iend) + 4;
                    while (ip0 > anchor && match > prefixStart && ip0[-1] == match[-1])
                    {
                        ip0--;
                        match--;
                        mLength++;
                    }

                    offset2 = offset1;
                    offset1 = offset;
                    assert(offset > 0);
                    ZSTD_storeSeq(
                        seqStore,
                        (nuint)(ip0 - anchor),
                        anchor,
                        iend,
                        offset + 3,
                        mLength
                    );
                    break;
                }

                dictMatchIndexAndTag = dictHashTable[dictHashAndTag1 >> 8];
                dictTagsMatch = ZSTD_comparePackedTags(dictMatchIndexAndTag, dictHashAndTag1);
                matchIndex = hashTable[hash1];
                if (ip1 >= nextStep)
                {
                    step++;
                    nextStep += kStepIncr;
                }

                ip0 = ip1;
                ip1 = ip1 + step;
                if (ip1 > ilimit)
                    goto _cleanup;
                curr = (uint)(ip0 - @base);
                hash0 = hash1;
            }

            assert(mLength != 0);
            ip0 += mLength;
            anchor = ip0;
            if (ip0 <= ilimit)
            {
                assert(@base + curr + 2 > istart);
                hashTable[ZSTD_hashPtr(@base + curr + 2, hlog, mls)] = curr + 2;
                hashTable[ZSTD_hashPtr(ip0 - 2, hlog, mls)] = (uint)(ip0 - 2 - @base);
                while (ip0 <= ilimit)
                {
                    var current2 = (uint)(ip0 - @base);
                    var repIndex2 = current2 - offset2;
                    var repMatch2 =
                        repIndex2 < prefixStartIndex
                            ? dictBase - dictIndexDelta + repIndex2
                            : @base + repIndex2;
                    if (
                        prefixStartIndex - 1 - repIndex2 >= 3
                        && MEM_read32(repMatch2) == MEM_read32(ip0)
                    )
                    {
                        var repEnd2 = repIndex2 < prefixStartIndex ? dictEnd : iend;
                        var repLength2 =
                            ZSTD_count_2segments(ip0 + 4, repMatch2 + 4, iend, repEnd2, prefixStart)
                            + 4;
                        /* swap offset_2 <=> offset_1 */
                        var tmpOffset = offset2;
                        offset2 = offset1;
                        offset1 = tmpOffset;
                        assert(1 >= 1);
                        assert(1 <= 3);
                        ZSTD_storeSeq(seqStore, 0, anchor, iend, 1, repLength2);
                        hashTable[ZSTD_hashPtr(ip0, hlog, mls)] = current2;
                        ip0 += repLength2;
                        anchor = ip0;
                        continue;
                    }

                    break;
                }
            }

            assert(ip0 == anchor);
            ip1 = ip0 + stepSize;
        }

        _cleanup:
        rep[0] = offset1;
        rep[1] = offset2;
        return (nuint)(iend - anchor);
    }

    private static nuint ZSTD_compressBlock_fast_dictMatchState_4_0(
        ZstdMatchStateT* ms,
        SeqStoreT* seqStore,
        uint* rep,
        void* src,
        nuint srcSize
    )
    {
        return ZSTD_compressBlock_fast_dictMatchState_generic(
            ms,
            seqStore,
            rep,
            src,
            srcSize,
            4,
            0
        );
    }

    private static nuint ZSTD_compressBlock_fast_dictMatchState_5_0(
        ZstdMatchStateT* ms,
        SeqStoreT* seqStore,
        uint* rep,
        void* src,
        nuint srcSize
    )
    {
        return ZSTD_compressBlock_fast_dictMatchState_generic(
            ms,
            seqStore,
            rep,
            src,
            srcSize,
            5,
            0
        );
    }

    private static nuint ZSTD_compressBlock_fast_dictMatchState_6_0(
        ZstdMatchStateT* ms,
        SeqStoreT* seqStore,
        uint* rep,
        void* src,
        nuint srcSize
    )
    {
        return ZSTD_compressBlock_fast_dictMatchState_generic(
            ms,
            seqStore,
            rep,
            src,
            srcSize,
            6,
            0
        );
    }

    private static nuint ZSTD_compressBlock_fast_dictMatchState_7_0(
        ZstdMatchStateT* ms,
        SeqStoreT* seqStore,
        uint* rep,
        void* src,
        nuint srcSize
    )
    {
        return ZSTD_compressBlock_fast_dictMatchState_generic(
            ms,
            seqStore,
            rep,
            src,
            srcSize,
            7,
            0
        );
    }

    private static nuint ZSTD_compressBlock_fast_dictMatchState(
        ZstdMatchStateT* ms,
        SeqStoreT* seqStore,
        uint* rep,
        void* src,
        nuint srcSize
    )
    {
        var mls = ms->cParams.minMatch;
        assert(ms->dictMatchState != null);
        switch (mls)
        {
            default:
            case 4:
                return ZSTD_compressBlock_fast_dictMatchState_4_0(ms, seqStore, rep, src, srcSize);
            case 5:
                return ZSTD_compressBlock_fast_dictMatchState_5_0(ms, seqStore, rep, src, srcSize);
            case 6:
                return ZSTD_compressBlock_fast_dictMatchState_6_0(ms, seqStore, rep, src, srcSize);
            case 7:
                return ZSTD_compressBlock_fast_dictMatchState_7_0(ms, seqStore, rep, src, srcSize);
        }
    }

    private static nuint ZSTD_compressBlock_fast_extDict_generic(
        ZstdMatchStateT* ms,
        SeqStoreT* seqStore,
        uint* rep,
        void* src,
        nuint srcSize,
        uint mls,
        uint hasStep
    )
    {
        var cParams = &ms->cParams;
        var hashTable = ms->hashTable;
        var hlog = cParams->hashLog;
        /* support stepSize of 0 */
        nuint stepSize = cParams->targetLength + (uint)(cParams->targetLength == 0 ? 1 : 0) + 1;
        var @base = ms->window.@base;
        var dictBase = ms->window.dictBase;
        var istart = (byte*)src;
        var anchor = istart;
        var endIndex = (uint)((nuint)(istart - @base) + srcSize);
        var lowLimit = ZSTD_getLowestMatchIndex(ms, endIndex, cParams->windowLog);
        var dictStartIndex = lowLimit;
        var dictStart = dictBase + dictStartIndex;
        var dictLimit = ms->window.dictLimit;
        var prefixStartIndex = dictLimit < lowLimit ? lowLimit : dictLimit;
        var prefixStart = @base + prefixStartIndex;
        var dictEnd = dictBase + prefixStartIndex;
        var iend = istart + srcSize;
        var ilimit = iend - 8;
        uint offset1 = rep[0],
            offset2 = rep[1];
        uint offsetSaved1 = 0,
            offsetSaved2 = 0;
        var ip0 = istart;
        byte* ip1;
        byte* ip2;
        byte* ip3;
        uint current0;
        /* hash for ip0 */
        nuint hash0;
        /* hash for ip1 */
        nuint hash1;
        /* match idx for ip0 */
        uint idx;
        /* base pointer for idx */
        byte* idxBase;
        uint offcode;
        byte* match0;
        nuint mLength;
        /* initialize to avoid warning, assert != 0 later */
        byte* matchEnd = null;
        nuint step;
        byte* nextStep;
        const nuint kStepIncr = 1 << (8 - 1);
        if (prefixStartIndex == dictStartIndex)
            return ZSTD_compressBlock_fast(ms, seqStore, rep, src, srcSize);
        {
            var curr = (uint)(ip0 - @base);
            var maxRep = curr - dictStartIndex;
            if (offset2 >= maxRep)
            {
                offsetSaved2 = offset2;
                offset2 = 0;
            }

            if (offset1 >= maxRep)
            {
                offsetSaved1 = offset1;
                offset1 = 0;
            }
        }

        _start:
        step = stepSize;
        nextStep = ip0 + kStepIncr;
        ip1 = ip0 + 1;
        ip2 = ip0 + step;
        ip3 = ip2 + 1;
        if (ip3 >= ilimit)
            goto _cleanup;

        hash0 = ZSTD_hashPtr(ip0, hlog, mls);
        hash1 = ZSTD_hashPtr(ip1, hlog, mls);
        idx = hashTable[hash0];
        idxBase = idx < prefixStartIndex ? dictBase : @base;
        do
        {
            {
                var current2 = (uint)(ip2 - @base);
                var repIndex = current2 - offset1;
                var repBase = repIndex < prefixStartIndex ? dictBase : @base;
                uint rval;
                if (prefixStartIndex - repIndex >= 4 && offset1 > 0)
                    rval = MEM_read32(repBase + repIndex);
                else
                    rval = MEM_read32(ip2) ^ 1;

                current0 = (uint)(ip0 - @base);
                hashTable[hash0] = current0;
                if (MEM_read32(ip2) == rval)
                {
                    ip0 = ip2;
                    match0 = repBase + repIndex;
                    matchEnd = repIndex < prefixStartIndex ? dictEnd : iend;
                    assert(match0 != prefixStart && match0 != dictStart);
                    mLength = ip0[-1] == match0[-1] ? 1U : 0U;
                    ip0 -= mLength;
                    match0 -= mLength;
                    assert(1 >= 1);
                    assert(1 <= 3);
                    offcode = 1;
                    mLength += 4;
                    goto _match;
                }
            }

            {
                var mval = idx >= dictStartIndex ? MEM_read32(idxBase + idx) : MEM_read32(ip0) ^ 1;
                if (MEM_read32(ip0) == mval)
                    goto _offset;
            }

            idx = hashTable[hash1];
            idxBase = idx < prefixStartIndex ? dictBase : @base;
            hash0 = hash1;
            hash1 = ZSTD_hashPtr(ip2, hlog, mls);
            ip0 = ip1;
            ip1 = ip2;
            ip2 = ip3;
            current0 = (uint)(ip0 - @base);
            hashTable[hash0] = current0;
            {
                var mval = idx >= dictStartIndex ? MEM_read32(idxBase + idx) : MEM_read32(ip0) ^ 1;
                if (MEM_read32(ip0) == mval)
                    goto _offset;
            }

            idx = hashTable[hash1];
            idxBase = idx < prefixStartIndex ? dictBase : @base;
            hash0 = hash1;
            hash1 = ZSTD_hashPtr(ip2, hlog, mls);
            ip0 = ip1;
            ip1 = ip2;
            ip2 = ip0 + step;
            ip3 = ip1 + step;
            if (ip2 >= nextStep)
            {
                step++;
#if NETCOREAPP3_0_OR_GREATER
                if (Sse.IsSupported)
                {
                    Sse.Prefetch0(ip1 + 64);
                    Sse.Prefetch0(ip1 + 128);
                }
#endif

                nextStep += kStepIncr;
            }
        } while (ip3 < ilimit);

        _cleanup:
        offsetSaved2 = offsetSaved1 != 0 && offset1 != 0 ? offsetSaved1 : offsetSaved2;
        rep[0] = offset1 != 0 ? offset1 : offsetSaved1;
        rep[1] = offset2 != 0 ? offset2 : offsetSaved2;
        return (nuint)(iend - anchor);
        _offset:
        {
            var offset = current0 - idx;
            var lowMatchPtr = idx < prefixStartIndex ? dictStart : prefixStart;
            matchEnd = idx < prefixStartIndex ? dictEnd : iend;
            match0 = idxBase + idx;
            offset2 = offset1;
            offset1 = offset;
            assert(offset > 0);
            offcode = offset + 3;
            mLength = 4;
            while (ip0 > anchor && match0 > lowMatchPtr && ip0[-1] == match0[-1])
            {
                ip0--;
                match0--;
                mLength++;
            }
        }

        _match:
        assert(matchEnd != null);
        mLength += ZSTD_count_2segments(
            ip0 + mLength,
            match0 + mLength,
            iend,
            matchEnd,
            prefixStart
        );
        ZSTD_storeSeq(seqStore, (nuint)(ip0 - anchor), anchor, iend, offcode, mLength);
        ip0 += mLength;
        anchor = ip0;
        if (ip1 < ip0)
            hashTable[hash1] = (uint)(ip1 - @base);

        if (ip0 <= ilimit)
        {
            assert(@base + current0 + 2 > istart);
            hashTable[ZSTD_hashPtr(@base + current0 + 2, hlog, mls)] = current0 + 2;
            hashTable[ZSTD_hashPtr(ip0 - 2, hlog, mls)] = (uint)(ip0 - 2 - @base);
            while (ip0 <= ilimit)
            {
                var repIndex2 = (uint)(ip0 - @base) - offset2;
                var repMatch2 =
                    repIndex2 < prefixStartIndex ? dictBase + repIndex2 : @base + repIndex2;
                if (
                    prefixStartIndex - 1 - repIndex2 >= 3
                    && offset2 > 0
                    && MEM_read32(repMatch2) == MEM_read32(ip0)
                )
                {
                    var repEnd2 = repIndex2 < prefixStartIndex ? dictEnd : iend;
                    var repLength2 =
                        ZSTD_count_2segments(ip0 + 4, repMatch2 + 4, iend, repEnd2, prefixStart)
                        + 4;
                    {
                        /* swap offset_2 <=> offset_1 */
                        var tmpOffset = offset2;
                        offset2 = offset1;
                        offset1 = tmpOffset;
                    }

                    assert(1 >= 1);
                    assert(1 <= 3);
                    ZSTD_storeSeq(seqStore, 0, anchor, iend, 1, repLength2);
                    hashTable[ZSTD_hashPtr(ip0, hlog, mls)] = (uint)(ip0 - @base);
                    ip0 += repLength2;
                    anchor = ip0;
                    continue;
                }

                break;
            }
        }

        goto _start;
    }

    private static nuint ZSTD_compressBlock_fast_extDict_4_0(
        ZstdMatchStateT* ms,
        SeqStoreT* seqStore,
        uint* rep,
        void* src,
        nuint srcSize
    )
    {
        return ZSTD_compressBlock_fast_extDict_generic(ms, seqStore, rep, src, srcSize, 4, 0);
    }

    private static nuint ZSTD_compressBlock_fast_extDict_5_0(
        ZstdMatchStateT* ms,
        SeqStoreT* seqStore,
        uint* rep,
        void* src,
        nuint srcSize
    )
    {
        return ZSTD_compressBlock_fast_extDict_generic(ms, seqStore, rep, src, srcSize, 5, 0);
    }

    private static nuint ZSTD_compressBlock_fast_extDict_6_0(
        ZstdMatchStateT* ms,
        SeqStoreT* seqStore,
        uint* rep,
        void* src,
        nuint srcSize
    )
    {
        return ZSTD_compressBlock_fast_extDict_generic(ms, seqStore, rep, src, srcSize, 6, 0);
    }

    private static nuint ZSTD_compressBlock_fast_extDict_7_0(
        ZstdMatchStateT* ms,
        SeqStoreT* seqStore,
        uint* rep,
        void* src,
        nuint srcSize
    )
    {
        return ZSTD_compressBlock_fast_extDict_generic(ms, seqStore, rep, src, srcSize, 7, 0);
    }

    private static nuint ZSTD_compressBlock_fast_extDict(
        ZstdMatchStateT* ms,
        SeqStoreT* seqStore,
        uint* rep,
        void* src,
        nuint srcSize
    )
    {
        var mls = ms->cParams.minMatch;
        assert(ms->dictMatchState == null);
        switch (mls)
        {
            default:
            case 4:
                return ZSTD_compressBlock_fast_extDict_4_0(ms, seqStore, rep, src, srcSize);
            case 5:
                return ZSTD_compressBlock_fast_extDict_5_0(ms, seqStore, rep, src, srcSize);
            case 6:
                return ZSTD_compressBlock_fast_extDict_6_0(ms, seqStore, rep, src, srcSize);
            case 7:
                return ZSTD_compressBlock_fast_extDict_7_0(ms, seqStore, rep, src, srcSize);
        }
    }
}