using System.Runtime.CompilerServices;
using static VendoredZSTD.UnsafeHelper;

namespace VendoredZSTD.Unsafe;

public static unsafe partial class Methods
{
    private static readonly BufferS GNullBuffer = new(start: null, capacity: 0);

    private static void ZSTDMT_freeBufferPool(ZstdmtBufferPoolS* bufPool)
    {
        if (bufPool == null)
            return;

        if (bufPool->buffers != null)
        {
            uint u;
            for (u = 0; u < bufPool->totalBuffers; u++)
            {
                ZSTD_customFree(bufPool->buffers[u].start, bufPool->cMem);
            }

            ZSTD_customFree(bufPool->buffers, bufPool->cMem);
        }

        SynchronizationWrapper.Free(&bufPool->poolMutex);
        ZSTD_customFree(bufPool, bufPool->cMem);
    }

    private static ZstdmtBufferPoolS* ZSTDMT_createBufferPool(uint maxNbBuffers, ZstdCustomMem cMem)
    {
        var bufPool = (ZstdmtBufferPoolS*)ZSTD_customCalloc((nuint)sizeof(ZstdmtBufferPoolS), cMem);
        if (bufPool == null)
            return null;

        SynchronizationWrapper.Init(&bufPool->poolMutex);
        bufPool->buffers = (BufferS*)ZSTD_customCalloc(maxNbBuffers * (uint)sizeof(BufferS), cMem);
        if (bufPool->buffers == null)
        {
            ZSTDMT_freeBufferPool(bufPool);
            return null;
        }

        bufPool->bufferSize = 64 * (1 << 10);
        bufPool->totalBuffers = maxNbBuffers;
        bufPool->nbBuffers = 0;
        bufPool->cMem = cMem;
        return bufPool;
    }

    /* only works at initialization, not during compression */
    private static nuint ZSTDMT_sizeof_bufferPool(ZstdmtBufferPoolS* bufPool)
    {
        var poolSize = (nuint)sizeof(ZstdmtBufferPoolS);
        nuint arraySize = bufPool->totalBuffers * (uint)sizeof(BufferS);
        uint u;
        nuint totalBufferSize = 0;
        SynchronizationWrapper.Enter(&bufPool->poolMutex);
        for (u = 0; u < bufPool->totalBuffers; u++)
        {
            totalBufferSize += bufPool->buffers[u].capacity;
        }

        SynchronizationWrapper.Exit(&bufPool->poolMutex);
        return poolSize + arraySize + totalBufferSize;
    }

    /* ZSTDMT_setBufferSize() :
     * all future buffers provided by this buffer pool will have _at least_ this size
     * note : it's better for all buffers to have same size,
     * as they become freely interchangeable, reducing malloc/free usages and memory fragmentation */
    private static void ZSTDMT_setBufferSize(ZstdmtBufferPoolS* bufPool, nuint bSize)
    {
        SynchronizationWrapper.Enter(&bufPool->poolMutex);
        bufPool->bufferSize = bSize;
        SynchronizationWrapper.Exit(&bufPool->poolMutex);
    }

    private static ZstdmtBufferPoolS* ZSTDMT_expandBufferPool(ZstdmtBufferPoolS* srcBufPool, uint maxNbBuffers)
    {
        if (srcBufPool == null)
            return null;
        if (srcBufPool->totalBuffers >= maxNbBuffers)
            return srcBufPool;

        {
            var cMem = srcBufPool->cMem;
            /* forward parameters */
            var bSize = srcBufPool->bufferSize;
            ZSTDMT_freeBufferPool(srcBufPool);
            var newBufPool = ZSTDMT_createBufferPool(maxNbBuffers, cMem);
            if (newBufPool == null)
                return newBufPool;

            ZSTDMT_setBufferSize(newBufPool, bSize);
            return newBufPool;
        }
    }

    /** ZSTDMT_getBuffer() :
     *  assumption : bufPool must be valid
     * @return : a buffer, with start pointer and size
     *  note: allocation may fail, in this case, start==NULL and size==0 */
    private static BufferS ZSTDMT_getBuffer(ZstdmtBufferPoolS* bufPool)
    {
        var bSize = bufPool->bufferSize;
        SynchronizationWrapper.Enter(&bufPool->poolMutex);
        if (bufPool->nbBuffers != 0)
        {
            var buf = bufPool->buffers[--bufPool->nbBuffers];
            var availBufferSize = buf.capacity;
            bufPool->buffers[bufPool->nbBuffers] = GNullBuffer;
            if (availBufferSize >= bSize && availBufferSize >> 3 <= bSize)
            {
                SynchronizationWrapper.Exit(&bufPool->poolMutex);
                return buf;
            }

            ZSTD_customFree(buf.start, bufPool->cMem);
        }

        SynchronizationWrapper.Exit(&bufPool->poolMutex);
        {
            BufferS buffer;
            var start = ZSTD_customMalloc(bSize, bufPool->cMem);
            buffer.start = start;
            buffer.capacity = start == null ? 0 : bSize;
            return buffer;
        }
    }

    /* store buffer for later re-use, up to pool capacity */
    private static void ZSTDMT_releaseBuffer(ZstdmtBufferPoolS* bufPool, BufferS buf)
    {
        if (buf.start == null)
            return;

        SynchronizationWrapper.Enter(&bufPool->poolMutex);
        if (bufPool->nbBuffers < bufPool->totalBuffers)
        {
            bufPool->buffers[bufPool->nbBuffers++] = buf;
            SynchronizationWrapper.Exit(&bufPool->poolMutex);
            return;
        }

        SynchronizationWrapper.Exit(&bufPool->poolMutex);
        ZSTD_customFree(buf.start, bufPool->cMem);
    }

    private static nuint ZSTDMT_sizeof_seqPool(ZstdmtBufferPoolS* seqPool)
    {
        return ZSTDMT_sizeof_bufferPool(seqPool);
    }

    private static RawSeqStoreT BufferToSeq(BufferS buffer)
    {
        var seq = KNullRawSeqStore;
        seq.seq = (RawSeq*)buffer.start;
        seq.capacity = buffer.capacity / (nuint)sizeof(RawSeq);
        return seq;
    }

    private static BufferS SeqToBuffer(RawSeqStoreT seq)
    {
        BufferS buffer;
        buffer.start = seq.seq;
        buffer.capacity = seq.capacity * (nuint)sizeof(RawSeq);
        return buffer;
    }

    private static RawSeqStoreT ZSTDMT_getSeq(ZstdmtBufferPoolS* seqPool)
    {
        if (seqPool->bufferSize == 0)
        {
            return KNullRawSeqStore;
        }

        return BufferToSeq(ZSTDMT_getBuffer(seqPool));
    }

    private static void ZSTDMT_releaseSeq(ZstdmtBufferPoolS* seqPool, RawSeqStoreT seq)
    {
        ZSTDMT_releaseBuffer(seqPool, SeqToBuffer(seq));
    }

    private static void ZSTDMT_setNbSeq(ZstdmtBufferPoolS* seqPool, nuint nbSeq)
    {
        ZSTDMT_setBufferSize(seqPool, nbSeq * (nuint)sizeof(RawSeq));
    }

    private static ZstdmtBufferPoolS* ZSTDMT_createSeqPool(uint nbWorkers, ZstdCustomMem cMem)
    {
        var seqPool = ZSTDMT_createBufferPool(nbWorkers, cMem);
        if (seqPool == null)
            return null;

        ZSTDMT_setNbSeq(seqPool, 0);
        return seqPool;
    }

    private static void ZSTDMT_freeSeqPool(ZstdmtBufferPoolS* seqPool)
    {
        ZSTDMT_freeBufferPool(seqPool);
    }

    private static ZstdmtBufferPoolS* ZSTDMT_expandSeqPool(ZstdmtBufferPoolS* pool, uint nbWorkers)
    {
        return ZSTDMT_expandBufferPool(pool, nbWorkers);
    }

    /* note : all CCtx borrowed from the pool must be reverted back to the pool _before_ freeing the pool */
    private static void ZSTDMT_freeCCtxPool(ZstdmtCCtxPool* pool)
    {
        if (pool == null)
            return;

        SynchronizationWrapper.Free(&pool->poolMutex);
        if (pool->cctxs != null)
        {
            int cid;
            for (cid = 0; cid < pool->totalCCtx; cid++)
                ZSTD_freeCCtx(pool->cctxs[cid]);
            ZSTD_customFree(pool->cctxs, pool->cMem);
        }

        ZSTD_customFree(pool, pool->cMem);
    }

    /* ZSTDMT_createCCtxPool() :
     * implies nbWorkers >= 1 , checked by caller ZSTDMT_createCCtx() */
    private static ZstdmtCCtxPool* ZSTDMT_createCCtxPool(int nbWorkers, ZstdCustomMem cMem)
    {
        var cctxPool = (ZstdmtCCtxPool*)ZSTD_customCalloc((nuint)sizeof(ZstdmtCCtxPool), cMem);
        assert(nbWorkers > 0);
        if (cctxPool == null)
            return null;

        SynchronizationWrapper.Init(&cctxPool->poolMutex);
        cctxPool->totalCCtx = nbWorkers;
        cctxPool->cctxs = (ZstdCCtxS**)ZSTD_customCalloc((nuint)(nbWorkers * sizeof(ZstdCCtxS*)), cMem);
        if (cctxPool->cctxs == null)
        {
            ZSTDMT_freeCCtxPool(cctxPool);
            return null;
        }

        cctxPool->cMem = cMem;
        cctxPool->cctxs[0] = ZSTD_createCCtx_advanced(cMem);
        if (cctxPool->cctxs[0] == null)
        {
            ZSTDMT_freeCCtxPool(cctxPool);
            return null;
        }

        cctxPool->availCCtx = 1;
        return cctxPool;
    }

    private static ZstdmtCCtxPool* ZSTDMT_expandCCtxPool(ZstdmtCCtxPool* srcPool, int nbWorkers)
    {
        if (srcPool == null)
            return null;
        if (nbWorkers <= srcPool->totalCCtx)
            return srcPool;

        {
            var cMem = srcPool->cMem;
            ZSTDMT_freeCCtxPool(srcPool);
            return ZSTDMT_createCCtxPool(nbWorkers, cMem);
        }
    }

    /* only works during initialization phase, not during compression */
    private static nuint ZSTDMT_sizeof_CCtxPool(ZstdmtCCtxPool* cctxPool)
    {
        SynchronizationWrapper.Enter(&cctxPool->poolMutex);
        {
            var nbWorkers = (uint)cctxPool->totalCCtx;
            var poolSize = (nuint)sizeof(ZstdmtCCtxPool);
            var arraySize = (nuint)(cctxPool->totalCCtx * sizeof(ZstdCCtxS*));
            nuint totalCCtxSize = 0;
            uint u;
            for (u = 0; u < nbWorkers; u++)
            {
                totalCCtxSize += ZSTD_sizeof_CCtx(cctxPool->cctxs[u]);
            }

            SynchronizationWrapper.Exit(&cctxPool->poolMutex);
            assert(nbWorkers > 0);
            return poolSize + arraySize + totalCCtxSize;
        }
    }

    private static ZstdCCtxS* ZSTDMT_getCCtx(ZstdmtCCtxPool* cctxPool)
    {
        SynchronizationWrapper.Enter(&cctxPool->poolMutex);
        if (cctxPool->availCCtx != 0)
        {
            cctxPool->availCCtx--;
            {
                var cctx = cctxPool->cctxs[cctxPool->availCCtx];
                SynchronizationWrapper.Exit(&cctxPool->poolMutex);
                return cctx;
            }
        }

        SynchronizationWrapper.Exit(&cctxPool->poolMutex);
        return ZSTD_createCCtx_advanced(cctxPool->cMem);
    }

    private static void ZSTDMT_releaseCCtx(ZstdmtCCtxPool* pool, ZstdCCtxS* cctx)
    {
        if (cctx == null)
            return;

        SynchronizationWrapper.Enter(&pool->poolMutex);
        if (pool->availCCtx < pool->totalCCtx)
        {
            pool->cctxs[pool->availCCtx++] = cctx;
        }
        else
        {
            ZSTD_freeCCtx(cctx);
        }

        SynchronizationWrapper.Exit(&pool->poolMutex);
    }

    private static int ZSTDMT_serialState_reset(SerialState* serialState, ZstdmtBufferPoolS* seqPool, ZstdCCtxParamsS @params, nuint jobSize, void* dict, nuint dictSize, ZstdDictContentTypeE dictContentType)
    {
        if (@params.ldmParams.enableLdm == ZstdParamSwitchE.ZstdPsEnable)
        {
            ZSTD_ldm_adjustParameters(&@params.ldmParams, &@params.cParams);
            assert(@params.ldmParams.hashLog >= @params.ldmParams.bucketSizeLog);
            assert(@params.ldmParams.hashRateLog < 32);
        }
        else
        {
            @params.ldmParams = new LdmParamsT();
        }

        serialState->nextJobID = 0;
        if (@params.fParams.checksumFlag != 0)
            ZSTD_XXH64_reset(&serialState->xxhState, 0);
        if (@params.ldmParams.enableLdm == ZstdParamSwitchE.ZstdPsEnable)
        {
            var cMem = @params.customMem;
            var hashLog = @params.ldmParams.hashLog;
            var hashSize = ((nuint)1 << (int)hashLog) * (nuint)sizeof(LdmEntryT);
            var bucketLog = @params.ldmParams.hashLog - @params.ldmParams.bucketSizeLog;
            var prevBucketLog = serialState->@params.ldmParams.hashLog - serialState->@params.ldmParams.bucketSizeLog;
            var numBuckets = (nuint)1 << (int)bucketLog;
            ZSTDMT_setNbSeq(seqPool, ZSTD_ldm_getMaxNbSeq(@params.ldmParams, jobSize));
            ZSTD_window_init(&serialState->ldmState.window);
            if (serialState->ldmState.hashTable == null || serialState->@params.ldmParams.hashLog < hashLog)
            {
                ZSTD_customFree(serialState->ldmState.hashTable, cMem);
                serialState->ldmState.hashTable = (LdmEntryT*)ZSTD_customMalloc(hashSize, cMem);
            }

            if (serialState->ldmState.bucketOffsets == null || prevBucketLog < bucketLog)
            {
                ZSTD_customFree(serialState->ldmState.bucketOffsets, cMem);
                serialState->ldmState.bucketOffsets = (byte*)ZSTD_customMalloc(numBuckets, cMem);
            }

            if (serialState->ldmState.hashTable == null || serialState->ldmState.bucketOffsets == null)
                return 1;

            memset(serialState->ldmState.hashTable, 0, (uint)hashSize);
            memset(serialState->ldmState.bucketOffsets, 0, (uint)numBuckets);
            serialState->ldmState.loadedDictEnd = 0;
            if (dictSize > 0)
            {
                if (dictContentType == ZstdDictContentTypeE.ZstdDctRawContent)
                {
                    var dictEnd = (byte*)dict + dictSize;
                    ZSTD_window_update(&serialState->ldmState.window, dict, dictSize, 0);
                    ZSTD_ldm_fillHashTable(&serialState->ldmState, (byte*)dict, dictEnd, &@params.ldmParams);
                    serialState->ldmState.loadedDictEnd = @params.forceWindow != 0 ? 0 : (uint)(dictEnd - serialState->ldmState.window.@base);
                }
            }

            serialState->ldmWindow = serialState->ldmState.window;
        }

        serialState->@params = @params;
        serialState->@params.jobSize = (uint)jobSize;
        return 0;
    }

    private static int ZSTDMT_serialState_init(SerialState* serialState)
    {
        var initError = 0;
        *serialState = new SerialState();
        SynchronizationWrapper.Init(&serialState->mutex);
        initError |= 0;
        initError |= 0;
        SynchronizationWrapper.Init(&serialState->ldmWindowMutex);
        initError |= 0;
        initError |= 0;
        return initError;
    }

    private static void ZSTDMT_serialState_free(SerialState* serialState)
    {
        var cMem = serialState->@params.customMem;
        SynchronizationWrapper.Free(&serialState->mutex);
        SynchronizationWrapper.Free(&serialState->ldmWindowMutex);
        ZSTD_customFree(serialState->ldmState.hashTable, cMem);
        ZSTD_customFree(serialState->ldmState.bucketOffsets, cMem);
    }

    private static void ZSTDMT_serialState_genSequences(SerialState* serialState, RawSeqStoreT* seqStore, Range src, uint jobId)
    {
        SynchronizationWrapper.Enter(&serialState->mutex);
        // ReSharper disable once LoopVariableIsNeverChangedInsideLoop
        while (serialState->nextJobID < jobId)
        {
            SynchronizationWrapper.Wait(&serialState->mutex);
        }

        if (serialState->nextJobID == jobId)
        {
            if (serialState->@params.ldmParams.enableLdm == ZstdParamSwitchE.ZstdPsEnable)
            {
                assert(seqStore->seq != null && seqStore->pos == 0 && seqStore->size == 0 && seqStore->capacity > 0);
                assert(src.size <= serialState->@params.jobSize);
                ZSTD_window_update(&serialState->ldmState.window, src.start, src.size, 0);
                var error = ZSTD_ldm_generateSequences(&serialState->ldmState, seqStore, &serialState->@params.ldmParams, src.start, src.size);
                assert(!ERR_isError(error));
                SynchronizationWrapper.Enter(&serialState->ldmWindowMutex);
                serialState->ldmWindow = serialState->ldmState.window;
                SynchronizationWrapper.Pulse(&serialState->ldmWindowMutex);
                SynchronizationWrapper.Exit(&serialState->ldmWindowMutex);
            }

            if (serialState->@params.fParams.checksumFlag != 0 && src.size > 0)
                ZSTD_XXH64_update(&serialState->xxhState, src.start, src.size);
        }

        serialState->nextJobID++;
        SynchronizationWrapper.PulseAll(&serialState->mutex);
        SynchronizationWrapper.Exit(&serialState->mutex);
    }

    private static void ZSTDMT_serialState_applySequences(SerialState* serialState, ZstdCCtxS* jobCCtx, RawSeqStoreT* seqStore)
    {
        if (seqStore->size > 0)
        {
            assert(serialState->@params.ldmParams.enableLdm == ZstdParamSwitchE.ZstdPsEnable);
            assert(jobCCtx != null);
            ZSTD_referenceExternalSequences(jobCCtx, seqStore->seq, seqStore->size);
        }
    }

    private static void ZSTDMT_serialState_ensureFinished(SerialState* serialState, uint jobId, nuint cSize)
    {
        SynchronizationWrapper.Enter(&serialState->mutex);
        if (serialState->nextJobID <= jobId)
        {
            assert(ERR_isError(cSize));
            serialState->nextJobID = jobId + 1;
            SynchronizationWrapper.PulseAll(&serialState->mutex);
            SynchronizationWrapper.Enter(&serialState->ldmWindowMutex);
            ZSTD_window_clear(&serialState->ldmWindow);
            SynchronizationWrapper.Pulse(&serialState->ldmWindowMutex);
            SynchronizationWrapper.Exit(&serialState->ldmWindowMutex);
        }

        SynchronizationWrapper.Exit(&serialState->mutex);
    }

    private static readonly Range KNullRange = new(start: null, size: 0);

    /* ZSTDMT_compressionJob() is a POOL_function type */
    private static void ZSTDMT_compressionJob(void* jobDescription)
    {
        var job = (ZstdmtJobDescription*)jobDescription;
        /* do not modify job->params ! copy it, modify the copy */
        var jobParams = job->@params;
        var cctx = ZSTDMT_getCCtx(job->cctxPool);
        var rawSeqStore = ZSTDMT_getSeq(job->seqPool);
        var dstBuff = job->dstBuff;
        nuint lastCBlockSize = 0;
        if (cctx == null)
        {
            SynchronizationWrapper.Enter(&job->job_mutex);
            job->cSize = unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorMemoryAllocation));
            SynchronizationWrapper.Exit(&job->job_mutex);
            goto _endJob;
        }

        if (dstBuff.start == null)
        {
            dstBuff = ZSTDMT_getBuffer(job->bufPool);
            if (dstBuff.start == null)
            {
                SynchronizationWrapper.Enter(&job->job_mutex);
                job->cSize = unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorMemoryAllocation));
                SynchronizationWrapper.Exit(&job->job_mutex);
                goto _endJob;
            }

            job->dstBuff = dstBuff;
        }

        if (jobParams.ldmParams.enableLdm == ZstdParamSwitchE.ZstdPsEnable && rawSeqStore.seq == null)
        {
            SynchronizationWrapper.Enter(&job->job_mutex);
            job->cSize = unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorMemoryAllocation));
            SynchronizationWrapper.Exit(&job->job_mutex);
            goto _endJob;
        }

        if (job->jobID != 0)
        {
            jobParams.fParams.checksumFlag = 0;
        }

        jobParams.ldmParams.enableLdm = ZstdParamSwitchE.ZstdPsDisable;
        jobParams.nbWorkers = 0;
        ZSTDMT_serialState_genSequences(job->serial, &rawSeqStore, job->src, job->jobID);
        if (job->cdict != null)
        {
            var initError = ZSTD_compressBegin_advanced_internal(cctx, null, 0, ZstdDictContentTypeE.ZstdDctAuto, ZstdDictTableLoadMethodE.ZstdDtlmFast, job->cdict, &jobParams, job->fullFrameSize);
            assert(job->firstJob != 0);
            if (ERR_isError(initError))
            {
                SynchronizationWrapper.Enter(&job->job_mutex);
                job->cSize = initError;
                SynchronizationWrapper.Exit(&job->job_mutex);
                goto _endJob;
            }
        }
        else
        {
            var pledgedSrcSize = job->firstJob != 0 ? job->fullFrameSize : job->src.size;
            {
                var forceWindowError = ZSTD_CCtxParams_setParameter(&jobParams, ZstdCParameter.ZstdCExperimentalParam3, job->firstJob == 0 ? 1 : 0);
                if (ERR_isError(forceWindowError))
                {
                    SynchronizationWrapper.Enter(&job->job_mutex);
                    job->cSize = forceWindowError;
                    SynchronizationWrapper.Exit(&job->job_mutex);
                    goto _endJob;
                }
            }

            if (job->firstJob == 0)
            {
                var err = ZSTD_CCtxParams_setParameter(&jobParams, ZstdCParameter.ZstdCExperimentalParam15, 0);
                if (ERR_isError(err))
                {
                    SynchronizationWrapper.Enter(&job->job_mutex);
                    job->cSize = err;
                    SynchronizationWrapper.Exit(&job->job_mutex);
                    goto _endJob;
                }
            }

            {
                var initError = ZSTD_compressBegin_advanced_internal(cctx, job->prefix.start, job->prefix.size, ZstdDictContentTypeE.ZstdDctRawContent, ZstdDictTableLoadMethodE.ZstdDtlmFast, null, &jobParams, pledgedSrcSize);
                if (ERR_isError(initError))
                {
                    SynchronizationWrapper.Enter(&job->job_mutex);
                    job->cSize = initError;
                    SynchronizationWrapper.Exit(&job->job_mutex);
                    goto _endJob;
                }
            }
        }

        ZSTDMT_serialState_applySequences(job->serial, cctx, &rawSeqStore);
        if (job->firstJob == 0)
        {
            var hSize = ZSTD_compressContinue_public(cctx, dstBuff.start, dstBuff.capacity, job->src.start, 0);
            if (ERR_isError(hSize))
            {
                SynchronizationWrapper.Enter(&job->job_mutex);
                job->cSize = hSize;
                SynchronizationWrapper.Exit(&job->job_mutex);
                goto _endJob;
            }

            ZSTD_invalidateRepCodes(cctx);
        }

        {
            const nuint chunkSize = 4 * (1 << 17);
            var nbChunks = (int)((job->src.size + (chunkSize - 1)) / chunkSize);
            var ip = (byte*)job->src.start;
            var ostart = (byte*)dstBuff.start;
            var op = ostart;
            var oend = op + dstBuff.capacity;
            int chunkNb;
#if DEBUG
            if (sizeof(nuint) > sizeof(int))
                assert(job->src.size < unchecked(2147483647 * chunkSize));
#endif
            assert(job->cSize == 0);
            for (chunkNb = 1; chunkNb < nbChunks; chunkNb++)
            {
                var cSize = ZSTD_compressContinue_public(cctx, op, (nuint)(oend - op), ip, chunkSize);
                if (ERR_isError(cSize))
                {
                    SynchronizationWrapper.Enter(&job->job_mutex);
                    job->cSize = cSize;
                    SynchronizationWrapper.Exit(&job->job_mutex);
                    goto _endJob;
                }

                ip += chunkSize;
                op += cSize;
                assert(op < oend);
                SynchronizationWrapper.Enter(&job->job_mutex);
                job->cSize += cSize;
                job->consumed = chunkSize * (nuint)chunkNb;
                SynchronizationWrapper.Pulse(&job->job_mutex);
                SynchronizationWrapper.Exit(&job->job_mutex);
            }

            assert(chunkSize > 0);
            assert((chunkSize & (chunkSize - 1)) == 0);
            if (((uint)(nbChunks > 0 ? 1 : 0) | job->lastJob) != 0)
            {
                var lastBlockSize1 = job->src.size & (chunkSize - 1);
                var lastBlockSize = lastBlockSize1 == 0 && job->src.size >= chunkSize ? chunkSize : lastBlockSize1;
                var cSize = job->lastJob != 0 ? ZSTD_compressEnd_public(cctx, op, (nuint)(oend - op), ip, lastBlockSize) : ZSTD_compressContinue_public(cctx, op, (nuint)(oend - op), ip, lastBlockSize);
                if (ERR_isError(cSize))
                {
                    SynchronizationWrapper.Enter(&job->job_mutex);
                    job->cSize = cSize;
                    SynchronizationWrapper.Exit(&job->job_mutex);
                    goto _endJob;
                }

                lastCBlockSize = cSize;
            }
        }

#if DEBUG
        if (job->firstJob == 0)
        {
            assert(ZSTD_window_hasExtDict(cctx->blockState.matchState.window) == 0);
        }
#endif

        ZSTD_CCtx_trace(cctx, 0);
        _endJob:
        ZSTDMT_serialState_ensureFinished(job->serial, job->jobID, job->cSize);
        ZSTDMT_releaseSeq(job->seqPool, rawSeqStore);
        ZSTDMT_releaseCCtx(job->cctxPool, cctx);
        SynchronizationWrapper.Enter(&job->job_mutex);
        if (ERR_isError(job->cSize))
            assert(lastCBlockSize == 0);
        job->cSize += lastCBlockSize;
        job->consumed = job->src.size;
        SynchronizationWrapper.Pulse(&job->job_mutex);
        SynchronizationWrapper.Exit(&job->job_mutex);
    }

    private static readonly RoundBuffT KNullRoundBuff = new(buffer: null, capacity: 0, pos: 0);

    private static void ZSTDMT_freeJobsTable(ZstdmtJobDescription* jobTable, uint nbJobs, ZstdCustomMem cMem)
    {
        uint jobNb;
        if (jobTable == null)
            return;

        for (jobNb = 0; jobNb < nbJobs; jobNb++)
        {
            SynchronizationWrapper.Free(&jobTable[jobNb].job_mutex);
        }

        ZSTD_customFree(jobTable, cMem);
    }

    /* ZSTDMT_allocJobsTable()
     * allocate and init a job table.
     * update *nbJobsPtr to next power of 2 value, as size of table */
    private static ZstdmtJobDescription* ZSTDMT_createJobsTable(uint* nbJobsPtr, ZstdCustomMem cMem)
    {
        var nbJobsLog2 = ZSTD_highbit32(*nbJobsPtr) + 1;
        var nbJobs = (uint)(1 << (int)nbJobsLog2);
        uint jobNb;
        var jobTable = (ZstdmtJobDescription*)ZSTD_customCalloc(nbJobs * (uint)sizeof(ZstdmtJobDescription), cMem);
        var initError = 0;
        if (jobTable == null)
            return null;

        *nbJobsPtr = nbJobs;
        for (jobNb = 0; jobNb < nbJobs; jobNb++)
        {
            SynchronizationWrapper.Init(&jobTable[jobNb].job_mutex);
            initError |= 0;
            initError |= 0;
        }

        if (initError != 0)
        {
            ZSTDMT_freeJobsTable(jobTable, nbJobs, cMem);
            return null;
        }

        return jobTable;
    }

    private static nuint ZSTDMT_expandJobsTable(ZstdmtCCtxS* mtctx, uint nbWorkers)
    {
        var nbJobs = nbWorkers + 2;
        if (nbJobs > mtctx->jobIDMask + 1)
        {
            ZSTDMT_freeJobsTable(mtctx->jobs, mtctx->jobIDMask + 1, mtctx->cMem);
            mtctx->jobIDMask = 0;
            mtctx->jobs = ZSTDMT_createJobsTable(&nbJobs, mtctx->cMem);
            if (mtctx->jobs == null)
                return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorMemoryAllocation));

            assert(nbJobs != 0 && (nbJobs & (nbJobs - 1)) == 0);
            mtctx->jobIDMask = nbJobs - 1;
        }

        return 0;
    }

    /* ZSTDMT_CCtxParam_setNbWorkers():
     * Internal use only */
    // ReSharper disable once UnusedMethodReturnValue.Local
    private static nuint ZSTDMT_CCtxParam_setNbWorkers(ZstdCCtxParamsS* @params, uint nbWorkers)
    {
        return ZSTD_CCtxParams_setParameter(@params, ZstdCParameter.ZstdCNbWorkers, (int)nbWorkers);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ZstdmtCCtxS* ZSTDMT_createCCtx_advanced_internal(uint nbWorkers, ZstdCustomMem cMem, void* pool)
    {
        var nbJobs = nbWorkers + 2;
        if (nbWorkers < 1)
            return null;

        nbWorkers = nbWorkers < (uint)(sizeof(void*) == 4 ? 64 : 256) ? nbWorkers : (uint)(sizeof(void*) == 4 ? 64 : 256);
        if (((cMem.customAlloc != null ? 1 : 0) ^ (cMem.customFree != null ? 1 : 0)) != 0)
            return null;

        var mtctx = (ZstdmtCCtxS*)ZSTD_customCalloc((nuint)sizeof(ZstdmtCCtxS), cMem);
        if (mtctx == null)
            return null;

        ZSTDMT_CCtxParam_setNbWorkers(&mtctx->@params, nbWorkers);
        mtctx->cMem = cMem;
        mtctx->allJobsCompleted = 1;
        if (pool != null)
        {
            mtctx->factory = pool;
            mtctx->providedFactory = 1;
        }
        else
        {
            mtctx->factory = POOL_create_advanced(nbWorkers, 0, cMem);
            mtctx->providedFactory = 0;
        }

        mtctx->jobs = ZSTDMT_createJobsTable(&nbJobs, cMem);
        assert(nbJobs > 0);
        assert((nbJobs & (nbJobs - 1)) == 0);
        mtctx->jobIDMask = nbJobs - 1;
        mtctx->bufPool = ZSTDMT_createBufferPool(2 * nbWorkers + 3, cMem);
        mtctx->cctxPool = ZSTDMT_createCCtxPool((int)nbWorkers, cMem);
        mtctx->seqPool = ZSTDMT_createSeqPool(nbWorkers, cMem);
        var initError = ZSTDMT_serialState_init(&mtctx->serial);
        mtctx->roundBuff = KNullRoundBuff;
        if (((mtctx->factory == null || mtctx->jobs == null || mtctx->bufPool == null || mtctx->cctxPool == null || mtctx->seqPool == null ? 1 : 0) | initError) != 0)
        {
            ZSTDMT_freeCCtx(mtctx);
            return null;
        }

        return mtctx;
    }

    /* Requires ZSTD_MULTITHREAD to be defined during compilation, otherwise it will return NULL. */
    private static ZstdmtCCtxS* ZSTDMT_createCCtx_advanced(uint nbWorkers, ZstdCustomMem cMem, void* pool)
    {
        return ZSTDMT_createCCtx_advanced_internal(nbWorkers, cMem, pool);
    }

    /* ZSTDMT_releaseAllJobResources() :
     * note : ensure all workers are killed first ! */
    private static void ZSTDMT_releaseAllJobResources(ZstdmtCCtxS* mtctx)
    {
        uint jobId;
        for (jobId = 0; jobId <= mtctx->jobIDMask; jobId++)
        {
            /* Copy the mutex/cond out */
            var mutex = mtctx->jobs[jobId].job_mutex;
            var cond = mtctx->jobs[jobId].job_cond;
            ZSTDMT_releaseBuffer(mtctx->bufPool, mtctx->jobs[jobId].dstBuff);
            mtctx->jobs[jobId] = new ZstdmtJobDescription
            {
                job_mutex = mutex,
                job_cond = cond
            };
        }

        mtctx->inBuff.buffer = GNullBuffer;
        mtctx->inBuff.filled = 0;
        mtctx->allJobsCompleted = 1;
    }

    private static void ZSTDMT_waitForAllJobsCompleted(ZstdmtCCtxS* mtctx)
    {
        while (mtctx->doneJobID < mtctx->nextJobID)
        {
            var jobId = mtctx->doneJobID & mtctx->jobIDMask;
            SynchronizationWrapper.Enter(&mtctx->jobs[jobId].job_mutex);
            while (mtctx->jobs[jobId].consumed < mtctx->jobs[jobId].src.size)
            {
                SynchronizationWrapper.Wait(&mtctx->jobs[jobId].job_mutex);
            }

            SynchronizationWrapper.Exit(&mtctx->jobs[jobId].job_mutex);
            mtctx->doneJobID++;
        }
    }

    // ReSharper disable once UnusedMethodReturnValue.Local
    private static nuint ZSTDMT_freeCCtx(ZstdmtCCtxS* mtctx)
    {
        if (mtctx == null)
            return 0;

        if (mtctx->providedFactory == 0)
            POOL_free(mtctx->factory);
        ZSTDMT_releaseAllJobResources(mtctx);
        ZSTDMT_freeJobsTable(mtctx->jobs, mtctx->jobIDMask + 1, mtctx->cMem);
        ZSTDMT_freeBufferPool(mtctx->bufPool);
        ZSTDMT_freeCCtxPool(mtctx->cctxPool);
        ZSTDMT_freeSeqPool(mtctx->seqPool);
        ZSTDMT_serialState_free(&mtctx->serial);
        ZSTD_freeCDict(mtctx->cdictLocal);
        if (mtctx->roundBuff.buffer != null)
            ZSTD_customFree(mtctx->roundBuff.buffer, mtctx->cMem);
        ZSTD_customFree(mtctx, mtctx->cMem);
        return 0;
    }

    private static nuint ZSTDMT_sizeof_CCtx(ZstdmtCCtxS* mtctx)
    {
        if (mtctx == null)
            return 0;

        return (nuint)sizeof(ZstdmtCCtxS) + POOL_sizeof(mtctx->factory) + ZSTDMT_sizeof_bufferPool(mtctx->bufPool) + (mtctx->jobIDMask + 1) * (uint)sizeof(ZstdmtJobDescription) + ZSTDMT_sizeof_CCtxPool(mtctx->cctxPool) + ZSTDMT_sizeof_seqPool(mtctx->seqPool) + ZSTD_sizeof_CDict(mtctx->cdictLocal) + mtctx->roundBuff.capacity;
    }

    /* ZSTDMT_resize() :
     * @return : error code if fails, 0 on success */
    private static nuint ZSTDMT_resize(ZstdmtCCtxS* mtctx, uint nbWorkers)
    {
        if (POOL_resize(mtctx->factory, nbWorkers) != 0)
            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorMemoryAllocation));

        {
            var errCode = ZSTDMT_expandJobsTable(mtctx, nbWorkers);
            if (ERR_isError(errCode))
            {
                return errCode;
            }
        }

        mtctx->bufPool = ZSTDMT_expandBufferPool(mtctx->bufPool, 2 * nbWorkers + 3);
        if (mtctx->bufPool == null)
            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorMemoryAllocation));

        mtctx->cctxPool = ZSTDMT_expandCCtxPool(mtctx->cctxPool, (int)nbWorkers);
        if (mtctx->cctxPool == null)
            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorMemoryAllocation));

        mtctx->seqPool = ZSTDMT_expandSeqPool(mtctx->seqPool, nbWorkers);
        if (mtctx->seqPool == null)
            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorMemoryAllocation));

        ZSTDMT_CCtxParam_setNbWorkers(&mtctx->@params, nbWorkers);
        return 0;
    }

    /*! ZSTDMT_updateCParams_whileCompressing() :
     *  Updates a selected set of compression parameters, remaining compatible with currently active frame.
     *  New parameters will be applied to next compression job. */
    private static void ZSTDMT_updateCParams_whileCompressing(ZstdmtCCtxS* mtctx, ZstdCCtxParamsS* cctxParams)
    {
        /* Do not modify windowLog while compressing */
        var savedWlog = mtctx->@params.cParams.windowLog;
        var compressionLevel = cctxParams->compressionLevel;
        mtctx->@params.compressionLevel = compressionLevel;
        {
            var cParams = ZSTD_getCParamsFromCCtxParams(cctxParams, unchecked(0UL - 1), 0, ZstdCParamModeE.ZstdCpmNoAttachDict);
            cParams.windowLog = savedWlog;
            mtctx->@params.cParams = cParams;
        }
    }

    /* ZSTDMT_getFrameProgression():
     * tells how much data has been consumed (input) and produced (output) for current frame.
     * able to count progression inside worker threads.
     * Note : mutex will be acquired during statistics collection inside workers. */
    private static ZstdFrameProgression ZSTDMT_getFrameProgression(ZstdmtCCtxS* mtctx)
    {
        ZstdFrameProgression fps;
        fps.ingested = mtctx->consumed + mtctx->inBuff.filled;
        fps.consumed = mtctx->consumed;
        fps.produced = fps.flushed = mtctx->produced;
        fps.currentJobID = mtctx->nextJobID;
        fps.nbActiveWorkers = 0;
        {
            uint jobNb;
            var lastJobNb = mtctx->nextJobID + (uint)mtctx->jobReady;
            assert(mtctx->jobReady <= 1);
            for (jobNb = mtctx->doneJobID; jobNb < lastJobNb; jobNb++)
            {
                var wJobId = jobNb & mtctx->jobIDMask;
                var jobPtr = &mtctx->jobs[wJobId];
                SynchronizationWrapper.Enter(&jobPtr->job_mutex);
                {
                    var cResult = jobPtr->cSize;
                    var produced = ERR_isError(cResult) ? 0 : cResult;
                    var flushed = ERR_isError(cResult) ? 0 : jobPtr->dstFlushed;
                    assert(flushed <= produced);
                    fps.ingested += jobPtr->src.size;
                    fps.consumed += jobPtr->consumed;
                    fps.produced += produced;
                    fps.flushed += flushed;
                    fps.nbActiveWorkers += jobPtr->consumed < jobPtr->src.size ? 1U : 0U;
                }

                SynchronizationWrapper.Exit(&mtctx->jobs[wJobId].job_mutex);
            }
        }

        return fps;
    }

    /*! ZSTDMT_toFlushNow()
     *  Tell how many bytes are ready to be flushed immediately.
     *  Probe the oldest active job (not yet entirely flushed) and check its output buffer.
     *  If return 0, it means there is no active job,
     *  or, it means oldest job is still active, but everything produced has been flushed so far,
     *  therefore flushing is limited by speed of oldest job. */
    private static nuint ZSTDMT_toFlushNow(ZstdmtCCtxS* mtctx)
    {
        nuint toFlush;
        var jobId = mtctx->doneJobID;
        assert(jobId <= mtctx->nextJobID);
        if (jobId == mtctx->nextJobID)
            return 0;

        {
            var wJobId = jobId & mtctx->jobIDMask;
            var jobPtr = &mtctx->jobs[wJobId];
            SynchronizationWrapper.Enter(&jobPtr->job_mutex);
            {
                var cResult = jobPtr->cSize;
                var produced = ERR_isError(cResult) ? 0 : cResult;
                var flushed = ERR_isError(cResult) ? 0 : jobPtr->dstFlushed;
                assert(flushed <= produced);
                assert(jobPtr->consumed <= jobPtr->src.size);
                toFlush = produced - flushed;
#if DEBUG
                if (toFlush == 0)
                {
                    assert(jobPtr->consumed < jobPtr->src.size);
                }
#endif
            }

            SynchronizationWrapper.Exit(&mtctx->jobs[wJobId].job_mutex);
        }

        return toFlush;
    }

    /* ------------------------------------------ */
    /* =====   Multi-threaded compression   ===== */
    /* ------------------------------------------ */
    private static uint ZSTDMT_computeTargetJobLog(ZstdCCtxParamsS* @params)
    {
        uint jobLog;
        if (@params->ldmParams.enableLdm == ZstdParamSwitchE.ZstdPsEnable)
        {
            jobLog = 21 > ZSTD_cycleLog(@params->cParams.chainLog, @params->cParams.strategy) + 3 ? 21 : ZSTD_cycleLog(@params->cParams.chainLog, @params->cParams.strategy) + 3;
        }
        else
        {
            jobLog = 20 > @params->cParams.windowLog + 2 ? 20 : @params->cParams.windowLog + 2;
        }

        return jobLog < (uint)(MEM_32bits ? 29 : 30) ? jobLog : (uint)(MEM_32bits ? 29 : 30);
    }

    private static int ZSTDMT_overlapLog_default(ZstdStrategy strat)
    {
        switch (strat)
        {
            case ZstdStrategy.ZstdBtultra2:
                return 9;
            case ZstdStrategy.ZstdBtultra:
            case ZstdStrategy.ZstdBtopt:
                return 8;
            case ZstdStrategy.ZstdBtlazy2:
            case ZstdStrategy.ZstdLazy2:
                return 7;
            case ZstdStrategy.ZstdLazy:
            case ZstdStrategy.ZstdGreedy:
            case ZstdStrategy.ZstdDfast:
            case ZstdStrategy.ZstdFast:
            default:
                break;
        }

        return 6;
    }

    private static int ZSTDMT_overlapLog(int ovlog, ZstdStrategy strat)
    {
        assert(ovlog is >= 0 and <= 9);
        if (ovlog == 0)
            return ZSTDMT_overlapLog_default(strat);

        return ovlog;
    }

    private static nuint ZSTDMT_computeOverlapSize(ZstdCCtxParamsS* @params)
    {
        var overlapRLog = 9 - ZSTDMT_overlapLog(@params->overlapLog, @params->cParams.strategy);
        var ovLog = (int)(overlapRLog >= 8 ? 0 : @params->cParams.windowLog - (uint)overlapRLog);
        assert(overlapRLog is >= 0 and <= 8);
        if (@params->ldmParams.enableLdm == ZstdParamSwitchE.ZstdPsEnable)
        {
            ovLog = (int)((@params->cParams.windowLog < ZSTDMT_computeTargetJobLog(@params) - 2 ? @params->cParams.windowLog : ZSTDMT_computeTargetJobLog(@params) - 2) - (uint)overlapRLog);
        }

        assert(0 <= ovLog && ovLog <= (sizeof(nuint) == 4 ? 30 : 31));
        return ovLog == 0 ? 0 : (nuint)1 << ovLog;
    }

    /* ====================================== */
    /* =======      Streaming API     ======= */
    /* ====================================== */
    private static nuint ZSTDMT_initCStream_internal(ZstdmtCCtxS* mtctx, void* dict, nuint dictSize, ZstdDictContentTypeE dictContentType, ZstdCDictS* cdict, ZstdCCtxParamsS @params, ulong pledgedSrcSize)
    {
        assert(!ERR_isError(ZSTD_checkCParams(@params.cParams)));
        assert(!(dict != null && cdict != null));
        if (@params.nbWorkers != mtctx->@params.nbWorkers)
        {
            /* init */
            var errCode = ZSTDMT_resize(mtctx, (uint)@params.nbWorkers);
            if (ERR_isError(errCode))
            {
                return errCode;
            }
        }

        if (@params.jobSize != 0 && @params.jobSize < 512 * (1 << 10))
        {
            @params.jobSize = 512 * (1 << 10);
        }

        if (@params.jobSize > (nuint)(MEM_32bits ? 512 * (1 << 20) : 1024 * (1 << 20)))
        {
            @params.jobSize = (nuint)(MEM_32bits ? 512 * (1 << 20) : 1024 * (1 << 20));
        }

        if (mtctx->allJobsCompleted == 0)
        {
            ZSTDMT_waitForAllJobsCompleted(mtctx);
            ZSTDMT_releaseAllJobResources(mtctx);
            mtctx->allJobsCompleted = 1;
        }

        mtctx->@params = @params;
        mtctx->frameContentSize = pledgedSrcSize;
        ZSTD_freeCDict(mtctx->cdictLocal);
        if (dict != null)
        {
            mtctx->cdictLocal = ZSTD_createCDict_advanced(dict, dictSize, ZstdDictLoadMethodE.ZstdDlmByCopy, dictContentType, @params.cParams, mtctx->cMem);
            mtctx->cdict = mtctx->cdictLocal;
            if (mtctx->cdictLocal == null)
                return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorMemoryAllocation));
        }
        else
        {
            mtctx->cdictLocal = null;
            mtctx->cdict = cdict;
        }

        mtctx->targetPrefixSize = ZSTDMT_computeOverlapSize(&@params);
        mtctx->targetSectionSize = @params.jobSize;
        if (mtctx->targetSectionSize == 0)
        {
            mtctx->targetSectionSize = (nuint)(1UL << (int)ZSTDMT_computeTargetJobLog(&@params));
        }

        assert(mtctx->targetSectionSize <= (nuint)(MEM_32bits ? 512 * (1 << 20) : 1024 * (1 << 20)));
        if (@params.rsyncable != 0)
        {
            /* Aim for the targetsectionSize as the average job size. */
            var jobSizeKb = (uint)(mtctx->targetSectionSize >> 10);
            assert(jobSizeKb >= 1);
            var rsyncBits = ZSTD_highbit32(jobSizeKb) + 10;
            assert(rsyncBits >= 17 + 2);
            mtctx->rsync.hash = 0;
            mtctx->rsync.hitMask = (1UL << (int)rsyncBits) - 1;
            mtctx->rsync.primePower = ZSTD_rollingHash_primePower(32);
        }

        if (mtctx->targetSectionSize < mtctx->targetPrefixSize)
        {
            mtctx->targetSectionSize = mtctx->targetPrefixSize;
        }

        ZSTDMT_setBufferSize(mtctx->bufPool, ZSTD_compressBound(mtctx->targetSectionSize));
        {
            /* If ldm is enabled we need windowSize space. */
            nuint windowSize = mtctx->@params.ldmParams.enableLdm == ZstdParamSwitchE.ZstdPsEnable ? 1U << (int)mtctx->@params.cParams.windowLog : 0;
            /* Two buffers of slack, plus extra space for the overlap
             * This is the minimum slack that LDM works with. One extra because
             * flush might waste up to targetSectionSize-1 bytes. Another extra
             * for the overlap (if > 0), then one to fill which doesn't overlap
             * with the LDM window.
             */
            var nbSlackBuffers = (nuint)(2 + (mtctx->targetPrefixSize > 0 ? 1 : 0));
            var slackSize = mtctx->targetSectionSize * nbSlackBuffers;
            /* Compute the total size, and always have enough slack */
            var nbWorkers = (nuint)(mtctx->@params.nbWorkers > 1 ? mtctx->@params.nbWorkers : 1);
            var sectionsSize = mtctx->targetSectionSize * nbWorkers;
            var capacity = (windowSize > sectionsSize ? windowSize : sectionsSize) + slackSize;
            if (mtctx->roundBuff.capacity < capacity)
            {
                if (mtctx->roundBuff.buffer != null)
                    ZSTD_customFree(mtctx->roundBuff.buffer, mtctx->cMem);
                mtctx->roundBuff.buffer = (byte*)ZSTD_customMalloc(capacity, mtctx->cMem);
                if (mtctx->roundBuff.buffer == null)
                {
                    mtctx->roundBuff.capacity = 0;
                    return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorMemoryAllocation));
                }

                mtctx->roundBuff.capacity = capacity;
            }
        }

        mtctx->roundBuff.pos = 0;
        mtctx->inBuff.buffer = GNullBuffer;
        mtctx->inBuff.filled = 0;
        mtctx->inBuff.prefix = KNullRange;
        mtctx->doneJobID = 0;
        mtctx->nextJobID = 0;
        mtctx->frameEnded = 0;
        mtctx->allJobsCompleted = 0;
        mtctx->consumed = 0;
        mtctx->produced = 0;
        ZSTD_freeCDict(mtctx->cdictLocal);
        mtctx->cdictLocal = null;
        mtctx->cdict = null;
        if (dict != null)
        {
            if (dictContentType == ZstdDictContentTypeE.ZstdDctRawContent)
            {
                mtctx->inBuff.prefix.start = (byte*)dict;
                mtctx->inBuff.prefix.size = dictSize;
            }
            else
            {
                mtctx->cdictLocal = ZSTD_createCDict_advanced(dict, dictSize, ZstdDictLoadMethodE.ZstdDlmByRef, dictContentType, @params.cParams, mtctx->cMem);
                mtctx->cdict = mtctx->cdictLocal;
                if (mtctx->cdictLocal == null)
                    return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorMemoryAllocation));
            }
        }
        else
        {
            mtctx->cdict = cdict;
        }

        if (ZSTDMT_serialState_reset(&mtctx->serial, mtctx->seqPool, @params, mtctx->targetSectionSize, dict, dictSize, dictContentType) != 0)
            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorMemoryAllocation));

        return 0;
    }

    /* ZSTDMT_writeLastEmptyBlock()
     * Write a single empty block with an end-of-frame to finish a frame.
     * Job must be created from streaming variant.
     * This function is always successful if expected conditions are fulfilled.
     */
    private static void ZSTDMT_writeLastEmptyBlock(ZstdmtJobDescription* job)
    {
        assert(job->lastJob == 1);
        assert(job->src.size == 0);
        assert(job->firstJob == 0);
        assert(job->dstBuff.start == null);
        job->dstBuff = ZSTDMT_getBuffer(job->bufPool);
        if (job->dstBuff.start == null)
        {
            job->cSize = unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorMemoryAllocation));
            return;
        }

        assert(job->dstBuff.capacity >= ZstdBlockHeaderSize);
        job->src = KNullRange;
        job->cSize = ZSTD_writeLastEmptyBlock(job->dstBuff.start, job->dstBuff.capacity);
        assert(!ERR_isError(job->cSize));
        assert(job->consumed == 0);
    }

    private static nuint ZSTDMT_createCompressionJob(ZstdmtCCtxS* mtctx, nuint srcSize, ZstdEndDirective endOp)
    {
        var jobId = mtctx->nextJobID & mtctx->jobIDMask;
        var endFrame = endOp == ZstdEndDirective.ZstdEEnd ? 1 : 0;
        if (mtctx->nextJobID > mtctx->doneJobID + mtctx->jobIDMask)
        {
            assert((mtctx->nextJobID & mtctx->jobIDMask) == (mtctx->doneJobID & mtctx->jobIDMask));
            return 0;
        }

        if (mtctx->jobReady == 0)
        {
            var src = (byte*)mtctx->inBuff.buffer.start;
            mtctx->jobs[jobId].src.start = src;
            mtctx->jobs[jobId].src.size = srcSize;
            assert(mtctx->inBuff.filled >= srcSize);
            mtctx->jobs[jobId].prefix = mtctx->inBuff.prefix;
            mtctx->jobs[jobId].consumed = 0;
            mtctx->jobs[jobId].cSize = 0;
            mtctx->jobs[jobId].@params = mtctx->@params;
            mtctx->jobs[jobId].cdict = mtctx->nextJobID == 0 ? mtctx->cdict : null;
            mtctx->jobs[jobId].fullFrameSize = mtctx->frameContentSize;
            mtctx->jobs[jobId].dstBuff = GNullBuffer;
            mtctx->jobs[jobId].cctxPool = mtctx->cctxPool;
            mtctx->jobs[jobId].bufPool = mtctx->bufPool;
            mtctx->jobs[jobId].seqPool = mtctx->seqPool;
            mtctx->jobs[jobId].serial = &mtctx->serial;
            mtctx->jobs[jobId].jobID = mtctx->nextJobID;
            mtctx->jobs[jobId].firstJob = mtctx->nextJobID == 0 ? 1U : 0U;
            mtctx->jobs[jobId].lastJob = (uint)endFrame;
            mtctx->jobs[jobId].frameChecksumNeeded = mtctx->@params.fParams.checksumFlag != 0 && endFrame != 0 && mtctx->nextJobID > 0 ? 1U : 0U;
            mtctx->jobs[jobId].dstFlushed = 0;
            mtctx->roundBuff.pos += srcSize;
            mtctx->inBuff.buffer = GNullBuffer;
            mtctx->inBuff.filled = 0;
            if (endFrame == 0)
            {
                var newPrefixSize = srcSize < mtctx->targetPrefixSize ? srcSize : mtctx->targetPrefixSize;
                mtctx->inBuff.prefix.start = src + srcSize - newPrefixSize;
                mtctx->inBuff.prefix.size = newPrefixSize;
            }
            else
            {
                mtctx->inBuff.prefix = KNullRange;
                mtctx->frameEnded = (uint)endFrame;
                if (mtctx->nextJobID == 0)
                {
                    mtctx->@params.fParams.checksumFlag = 0;
                }
            }

            if (srcSize == 0 && mtctx->nextJobID > 0)
            {
                assert(endOp == ZstdEndDirective.ZstdEEnd);
                ZSTDMT_writeLastEmptyBlock(mtctx->jobs + jobId);
                mtctx->nextJobID++;
                return 0;
            }
        }

        if (POOL_tryAdd(mtctx->factory, (delegate* managed<void*, void>)(&ZSTDMT_compressionJob), &mtctx->jobs[jobId]) != 0)
        {
            mtctx->nextJobID++;
            mtctx->jobReady = 0;
        }
        else
        {
            mtctx->jobReady = 1;
        }

        return 0;
    }

    /*! ZSTDMT_flushProduced() :
     *  flush whatever data has been produced but not yet flushed in current job.
     *  move to next job if current one is fully flushed.
     * `output` : `pos` will be updated with amount of data flushed .
     * `blockToFlush` : if >0, the function will block and wait if there is no data available to flush .
     * @return : amount of data remaining within internal buffer, 0 if no more, 1 if unknown but > 0, or an error code */
    private static nuint ZSTDMT_flushProduced(ZstdmtCCtxS* mtctx, ZstdOutBufferS* output, uint blockToFlush, ZstdEndDirective end)
    {
        var wJobId = mtctx->doneJobID & mtctx->jobIDMask;
        assert(output->size >= output->pos);
        SynchronizationWrapper.Enter(&mtctx->jobs[wJobId].job_mutex);
        if (blockToFlush != 0 && mtctx->doneJobID < mtctx->nextJobID)
        {
            assert(mtctx->jobs[wJobId].dstFlushed <= mtctx->jobs[wJobId].cSize);
            while (mtctx->jobs[wJobId].dstFlushed == mtctx->jobs[wJobId].cSize)
            {
                if (mtctx->jobs[wJobId].consumed == mtctx->jobs[wJobId].src.size)
                {
                    break;
                }

                SynchronizationWrapper.Wait(&mtctx->jobs[wJobId].job_mutex);
            }
        }

        {
            /* shared */
            var cSize = mtctx->jobs[wJobId].cSize;
            /* shared */
            var srcConsumed = mtctx->jobs[wJobId].consumed;
            /* read-only, could be done after mutex lock, but no-declaration-after-statement */
            var srcSize = mtctx->jobs[wJobId].src.size;
            SynchronizationWrapper.Exit(&mtctx->jobs[wJobId].job_mutex);
            if (ERR_isError(cSize))
            {
                ZSTDMT_waitForAllJobsCompleted(mtctx);
                ZSTDMT_releaseAllJobResources(mtctx);
                return cSize;
            }

            assert(srcConsumed <= srcSize);
            if (srcConsumed == srcSize && mtctx->jobs[wJobId].frameChecksumNeeded != 0)
            {
                var checksum = (uint)ZSTD_XXH64_digest(&mtctx->serial.xxhState);
                MEM_writeLE32((sbyte*)mtctx->jobs[wJobId].dstBuff.start + mtctx->jobs[wJobId].cSize, checksum);
                cSize += 4;
                mtctx->jobs[wJobId].cSize += 4;
                mtctx->jobs[wJobId].frameChecksumNeeded = 0;
            }

            if (cSize > 0)
            {
                var toFlush = cSize - mtctx->jobs[wJobId].dstFlushed < output->size - output->pos ? cSize - mtctx->jobs[wJobId].dstFlushed : output->size - output->pos;
                assert(mtctx->doneJobID < mtctx->nextJobID);
                assert(cSize >= mtctx->jobs[wJobId].dstFlushed);
                assert(mtctx->jobs[wJobId].dstBuff.start != null);
                if (toFlush > 0)
                {
                    memcpy((sbyte*)output->dst + output->pos, (sbyte*)mtctx->jobs[wJobId].dstBuff.start + mtctx->jobs[wJobId].dstFlushed, (uint)toFlush);
                }

                output->pos += toFlush;
                mtctx->jobs[wJobId].dstFlushed += toFlush;
                if (srcConsumed == srcSize && mtctx->jobs[wJobId].dstFlushed == cSize)
                {
                    ZSTDMT_releaseBuffer(mtctx->bufPool, mtctx->jobs[wJobId].dstBuff);
                    mtctx->jobs[wJobId].dstBuff = GNullBuffer;
                    mtctx->jobs[wJobId].cSize = 0;
                    mtctx->consumed += srcSize;
                    mtctx->produced += cSize;
                    mtctx->doneJobID++;
                }
            }

            if (cSize > mtctx->jobs[wJobId].dstFlushed)
                return cSize - mtctx->jobs[wJobId].dstFlushed;
            if (srcSize > srcConsumed)
                return 1;
        }

        if (mtctx->doneJobID < mtctx->nextJobID)
            return 1;
        if (mtctx->jobReady != 0)
            return 1;
        if (mtctx->inBuff.filled > 0)
            return 1;

        mtctx->allJobsCompleted = mtctx->frameEnded;
        if (end == ZstdEndDirective.ZstdEEnd)
            return mtctx->frameEnded == 0 ? 1U : 0U;

        return 0;
    }

    /**
     * Returns the range of data used by the earliest job that is not yet complete.
     * If the data of the first job is broken up into two segments, we cover both
     * sections.
     */
    private static Range ZSTDMT_getInputDataInUse(ZstdmtCCtxS* mtctx)
    {
        var firstJobId = mtctx->doneJobID;
        var lastJobId = mtctx->nextJobID;
        uint jobId;
        /* no need to check during first round */
        var roundBuffCapacity = mtctx->roundBuff.capacity;
        var nbJobs1StRoundMin = roundBuffCapacity / mtctx->targetSectionSize;
        if (lastJobId < nbJobs1StRoundMin)
            return KNullRange;

        for (jobId = firstJobId; jobId < lastJobId; ++jobId)
        {
            var wJobId = jobId & mtctx->jobIDMask;
            SynchronizationWrapper.Enter(&mtctx->jobs[wJobId].job_mutex);
            var consumed = mtctx->jobs[wJobId].consumed;
            SynchronizationWrapper.Exit(&mtctx->jobs[wJobId].job_mutex);
            if (consumed < mtctx->jobs[wJobId].src.size)
            {
                var range = mtctx->jobs[wJobId].prefix;
                if (range.size == 0)
                {
                    range = mtctx->jobs[wJobId].src;
                }

                assert(range.start <= mtctx->jobs[wJobId].src.start);
                return range;
            }
        }

        return KNullRange;
    }

    /**
     * Returns non-zero iff buffer and range overlap.
     */
    private static int ZSTDMT_isOverlapped(BufferS buffer, Range range)
    {
        var bufferStart = (byte*)buffer.start;
        var rangeStart = (byte*)range.start;
        if (rangeStart == null || bufferStart == null)
            return 0;

        {
            var bufferEnd = bufferStart + buffer.capacity;
            var rangeEnd = rangeStart + range.size;
            if (bufferStart == bufferEnd || rangeStart == rangeEnd)
                return 0;

            return bufferStart < rangeEnd && rangeStart < bufferEnd ? 1 : 0;
        }
    }

    private static int ZSTDMT_doesOverlapWindow(BufferS buffer, ZstdWindowT window)
    {
        Range extDict;
        Range prefix;
        extDict.start = window.dictBase + window.lowLimit;
        extDict.size = window.dictLimit - window.lowLimit;
        prefix.start = window.@base + window.dictLimit;
        prefix.size = (nuint)(window.nextSrc - (window.@base + window.dictLimit));
        return ZSTDMT_isOverlapped(buffer, extDict) != 0 || ZSTDMT_isOverlapped(buffer, prefix) != 0 ? 1 : 0;
    }

    private static void ZSTDMT_waitForLdmComplete(ZstdmtCCtxS* mtctx, BufferS buffer)
    {
        if (mtctx->@params.ldmParams.enableLdm == ZstdParamSwitchE.ZstdPsEnable)
        {
            var mutex = &mtctx->serial.ldmWindowMutex;
            SynchronizationWrapper.Enter(mutex);
            while (ZSTDMT_doesOverlapWindow(buffer, mtctx->serial.ldmWindow) != 0)
            {
                SynchronizationWrapper.Wait(mutex);
            }

            SynchronizationWrapper.Exit(mutex);
        }
    }

    /**
     * Attempts to set the inBuff to the next section to fill.
     * If any part of the new section is still in use we give up.
     * Returns non-zero if the buffer is filled.
     */
    private static int ZSTDMT_tryGetInputRange(ZstdmtCCtxS* mtctx)
    {
        var inUse = ZSTDMT_getInputDataInUse(mtctx);
        var spaceLeft = mtctx->roundBuff.capacity - mtctx->roundBuff.pos;
        var spaceNeeded = mtctx->targetSectionSize;
        BufferS buffer;
        assert(mtctx->inBuff.buffer.start == null);
        assert(mtctx->roundBuff.capacity >= spaceNeeded);
        if (spaceLeft < spaceNeeded)
        {
            /* ZSTD_invalidateRepCodes() doesn't work for extDict variants.
             * Simply copy the prefix to the beginning in that case.
             */
            var start = mtctx->roundBuff.buffer;
            var prefixSize = mtctx->inBuff.prefix.size;
            buffer.start = start;
            buffer.capacity = prefixSize;
            if (ZSTDMT_isOverlapped(buffer, inUse) != 0)
            {
                return 0;
            }

            ZSTDMT_waitForLdmComplete(mtctx, buffer);
            memmove(start, mtctx->inBuff.prefix.start, prefixSize);
            mtctx->inBuff.prefix.start = start;
            mtctx->roundBuff.pos = prefixSize;
        }

        buffer.start = mtctx->roundBuff.buffer + mtctx->roundBuff.pos;
        buffer.capacity = spaceNeeded;
        if (ZSTDMT_isOverlapped(buffer, inUse) != 0)
        {
            return 0;
        }

        assert(ZSTDMT_isOverlapped(buffer, mtctx->inBuff.prefix) == 0);
        ZSTDMT_waitForLdmComplete(mtctx, buffer);
        mtctx->inBuff.buffer = buffer;
        mtctx->inBuff.filled = 0;
        assert(mtctx->roundBuff.pos + buffer.capacity <= mtctx->roundBuff.capacity);
        return 1;
    }

    /**
     * Searches through the input for a synchronization point. If one is found, we
     * will instruct the caller to flush, and return the number of bytes to load.
     * Otherwise, we will load as many bytes as possible and instruct the caller
     * to continue as normal.
     */
    private static SyncPoint FindSynchronizationPoint(ZstdmtCCtxS* mtctx, ZstdInBufferS input)
    {
        var istart = (byte*)input.src + input.pos;
        var primePower = mtctx->rsync.primePower;
        var hitMask = mtctx->rsync.hitMask;
        SyncPoint syncPoint;
        ulong hash;
        byte* prev;
        nuint pos;
        syncPoint.toLoad = input.size - input.pos < mtctx->targetSectionSize - mtctx->inBuff.filled ? input.size - input.pos : mtctx->targetSectionSize - mtctx->inBuff.filled;
        syncPoint.flush = 0;
        if (mtctx->@params.rsyncable == 0 || mtctx->inBuff.filled + input.size - input.pos < 1 << 17 || mtctx->inBuff.filled + syncPoint.toLoad < 32)
            return syncPoint;

        if (mtctx->inBuff.filled < 1 << 17)
        {
            pos = (1 << 17) - mtctx->inBuff.filled;
            if (pos >= 32)
            {
                prev = istart + pos - 32;
                hash = ZSTD_rollingHash_compute(prev, 32);
            }
            else
            {
                assert(mtctx->inBuff.filled >= 32);
                prev = (byte*)mtctx->inBuff.buffer.start + mtctx->inBuff.filled - 32;
                hash = ZSTD_rollingHash_compute(prev + pos, 32 - pos);
                hash = ZSTD_rollingHash_append(hash, istart, pos);
            }
        }
        else
        {
            assert(mtctx->inBuff.filled >= 1 << 17);
            assert(1 << 17 >= 32);
            pos = 0;
            prev = (byte*)mtctx->inBuff.buffer.start + mtctx->inBuff.filled - 32;
            hash = ZSTD_rollingHash_compute(prev, 32);
            if ((hash & hitMask) == hitMask)
            {
                syncPoint.toLoad = 0;
                syncPoint.flush = 1;
                return syncPoint;
            }
        }

        assert(pos < 32 || ZSTD_rollingHash_compute(istart + pos - 32, 32) == hash);
        for (; pos < syncPoint.toLoad; ++pos)
        {
            var toRemove = pos < 32 ? prev[pos] : istart[pos - 32];
            hash = ZSTD_rollingHash_rotate(hash, toRemove, istart[pos], primePower);
            assert(mtctx->inBuff.filled + pos >= 1 << 17);
            if ((hash & hitMask) == hitMask)
            {
                syncPoint.toLoad = pos + 1;
                syncPoint.flush = 1;
                ++pos;
                break;
            }
        }

        assert(pos < 32 || ZSTD_rollingHash_compute(istart + pos - 32, 32) == hash);
        return syncPoint;
    }

    /* ===   Streaming functions   === */
    private static nuint ZSTDMT_nextInputSizeHint(ZstdmtCCtxS* mtctx)
    {
        var hintInSize = mtctx->targetSectionSize - mtctx->inBuff.filled;
        if (hintInSize == 0)
        {
            hintInSize = mtctx->targetSectionSize;
        }

        return hintInSize;
    }

    /** ZSTDMT_compressStream_generic() :
     *  internal use only - exposed to be invoked from zstd_compress.c
     *  assumption : output and input are valid (pos <= size)
     * @return : minimum amount of data remaining to flush, 0 if none */
    private static nuint ZSTDMT_compressStream_generic(ZstdmtCCtxS* mtctx, ZstdOutBufferS* output, ZstdInBufferS* input, ZstdEndDirective endOp)
    {
        uint forwardInputProgress = 0;
        assert(output->pos <= output->size);
        assert(input->pos <= input->size);
        if (mtctx->frameEnded != 0 && endOp == ZstdEndDirective.ZstdEContinue)
        {
            return unchecked((nuint)(-(int)ZstdErrorCode.ZstdErrorStageWrong));
        }

        if (mtctx->jobReady == 0 && input->size > input->pos)
        {
            if (mtctx->inBuff.buffer.start == null)
            {
                assert(mtctx->inBuff.filled == 0);
                if (ZSTDMT_tryGetInputRange(mtctx) == 0)
                {
                    assert(mtctx->doneJobID != mtctx->nextJobID);
                }
            }

            if (mtctx->inBuff.buffer.start != null)
            {
                var syncPoint = FindSynchronizationPoint(mtctx, *input);
                if (syncPoint.flush != 0 && endOp == ZstdEndDirective.ZstdEContinue)
                {
                    endOp = ZstdEndDirective.ZstdEFlush;
                }

                assert(mtctx->inBuff.buffer.capacity >= mtctx->targetSectionSize);
                memcpy((sbyte*)mtctx->inBuff.buffer.start + mtctx->inBuff.filled, (sbyte*)input->src + input->pos, (uint)syncPoint.toLoad);
                input->pos += syncPoint.toLoad;
                mtctx->inBuff.filled += syncPoint.toLoad;
                forwardInputProgress = syncPoint.toLoad > 0 ? 1U : 0U;
            }
        }

        if (input->pos < input->size && endOp == ZstdEndDirective.ZstdEEnd)
        {
            assert(mtctx->inBuff.filled == 0 || mtctx->inBuff.filled == mtctx->targetSectionSize || mtctx->@params.rsyncable != 0);
            endOp = ZstdEndDirective.ZstdEFlush;
        }

        if (mtctx->jobReady != 0 || mtctx->inBuff.filled >= mtctx->targetSectionSize || (endOp != ZstdEndDirective.ZstdEContinue && mtctx->inBuff.filled > 0) || (endOp == ZstdEndDirective.ZstdEEnd && mtctx->frameEnded == 0))
        {
            var jobSize = mtctx->inBuff.filled;
            assert(mtctx->inBuff.filled <= mtctx->targetSectionSize);
            {
                var errCode = ZSTDMT_createCompressionJob(mtctx, jobSize, endOp);
                if (ERR_isError(errCode))
                {
                    return errCode;
                }
            }
        }

        {
            /* block if there was no forward input progress */
            var remainingToFlush = ZSTDMT_flushProduced(mtctx, output, forwardInputProgress == 0 ? 1U : 0U, endOp);
            if (input->pos < input->size)
                return remainingToFlush > 1 ? remainingToFlush : 1;

            return remainingToFlush;
        }
    }
}