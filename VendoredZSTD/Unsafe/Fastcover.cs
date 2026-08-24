using static VendoredZSTD.UnsafeHelper;

namespace VendoredZSTD.Unsafe;

public static unsafe partial class Methods
{
    /*-*************************************
     * Hash Functions
     ***************************************/
    /**
     * Hash the d-byte value pointed to by p and mod 2^f into the frequency vector
     */
    private static nuint FASTCOVER_hashPtrToIndex(void* p, uint f, uint d)
    {
        if (d == 6)
        {
            return ZSTD_hash6Ptr(p, f);
        }

        return ZSTD_hash8Ptr(p, f);
    }

    private static readonly FASTCOVER_accel_t* FastcoverDefaultAccelParameters = GetArrayPointer(new FASTCOVER_accel_t[] { new(finalize: 100, skip: 0), new(finalize: 100, skip: 0), new(finalize: 50, skip: 1), new(finalize: 34, skip: 2), new(finalize: 25, skip: 3), new(finalize: 20, skip: 4), new(finalize: 17, skip: 5), new(finalize: 14, skip: 6), new(finalize: 13, skip: 7), new(finalize: 11, skip: 8), new(finalize: 10, skip: 9) });

    /*-*************************************
     *  Helper functions
     ***************************************/
    /**
     * Selects the best segment in an epoch.
     * Segments of are scored according to the function:
     *
     * Let F(d) be the frequency of all dmers with hash value d.
     * Let S_i be hash value of the dmer at position i of segment S which has length k.
     *
     *     Score(S) = F(S_1) + F(S_2) + ... + F(S_{k-d+1})
     *
     * Once the dmer with hash value d is in the dictionary we set F(d) = 0.
     */
    private static CoverSegmentT FASTCOVER_selectSegment(FASTCOVER_ctx_t* ctx, uint* freqs, uint begin, uint end, ZDICT_cover_params_t parameters, ushort* segmentFreqs)
    {
        /* Constants */
        var k = parameters.k;
        var d = parameters.d;
        var f = ctx->f;
        var dmersInK = k - d + 1;
        /* Try each segment (activeSegment) and save the best (bestSegment) */
        var bestSegment = new CoverSegmentT
        {
            begin = 0,
            end = 0,
            score = 0
        };
        CoverSegmentT activeSegment;
        activeSegment.begin = begin;
        activeSegment.end = begin;
        activeSegment.score = 0;
        while (activeSegment.end < end)
        {
            /* Get hash value of current dmer */
            var idx = FASTCOVER_hashPtrToIndex(ctx->samples + activeSegment.end, f, d);
            if (segmentFreqs[idx] == 0)
            {
                activeSegment.score += freqs[idx];
            }

            activeSegment.end += 1;
            segmentFreqs[idx] += 1;
            if (activeSegment.end - activeSegment.begin == dmersInK + 1)
            {
                /* Get hash value of the dmer to be eliminated from active segment */
                var delIndex = FASTCOVER_hashPtrToIndex(ctx->samples + activeSegment.begin, f, d);
                segmentFreqs[delIndex] -= 1;
                if (segmentFreqs[delIndex] == 0)
                {
                    activeSegment.score -= freqs[delIndex];
                }

                activeSegment.begin += 1;
            }

            if (activeSegment.score > bestSegment.score)
            {
                bestSegment = activeSegment;
            }
        }

        while (activeSegment.begin < end)
        {
            var delIndex = FASTCOVER_hashPtrToIndex(ctx->samples + activeSegment.begin, f, d);
            segmentFreqs[delIndex] -= 1;
            activeSegment.begin += 1;
        }

        {
            /*  Zero the frequency of hash value of each dmer covered by the chosen segment. */
            uint pos;
            for (pos = bestSegment.begin; pos != bestSegment.end; ++pos)
            {
                var i = FASTCOVER_hashPtrToIndex(ctx->samples + pos, f, d);
                freqs[i] = 0;
            }
        }

        return bestSegment;
    }

    private static int FASTCOVER_checkParameters(ZDICT_cover_params_t parameters, nuint maxDictSize, uint f, uint accel)
    {
        if (parameters.d == 0 || parameters.k == 0)
        {
            return 0;
        }

        if (parameters.d != 6 && parameters.d != 8)
        {
            return 0;
        }

        if (parameters.k > maxDictSize)
        {
            return 0;
        }

        if (parameters.d > parameters.k)
        {
            return 0;
        }

        if (f is > 31 or 0)
        {
            return 0;
        }

        if (parameters.splitPoint is <= 0 or > 1)
        {
            return 0;
        }

        if (accel is > 10 or 0)
        {
            return 0;
        }

        return 1;
    }

    /**
     * Clean up a context initialized with `FASTCOVER_ctx_init()`.
     */
    private static void FASTCOVER_ctx_destroy(FASTCOVER_ctx_t* ctx)
    {
        if (ctx == null)
            return;

        free(ctx->freqs);
        ctx->freqs = null;
        free(ctx->offsets);
        ctx->offsets = null;
    }

    /**
     * Calculate for frequency of hash value of each dmer in ctx->samples
     */
    private static void FASTCOVER_computeFrequency(uint* freqs, FASTCOVER_ctx_t* ctx)
    {
        var f = ctx->f;
        var d = ctx->d;
        var skip = ctx->accelParams.skip;
        var readLength = d > 8 ? d : 8;
        nuint i;
        assert(ctx->nbTrainSamples >= 5);
        assert(ctx->nbTrainSamples <= ctx->nbSamples);
        for (i = 0; i < ctx->nbTrainSamples; i++)
        {
            /* start of current dmer */
            var start = ctx->offsets[i];
            var currSampleEnd = ctx->offsets[i + 1];
            while (start + readLength <= currSampleEnd)
            {
                var dmerIndex = FASTCOVER_hashPtrToIndex(ctx->samples + start, f, d);
                freqs[dmerIndex]++;
                start = start + skip + 1;
            }
        }
    }

    /**
     * Prepare a context for dictionary building.
     * The context is only dependent on the parameter `d` and can be used multiple
     * times.
     * Returns 0 on success or error code on error.
     * The context must be destroyed with `FASTCOVER_ctx_destroy()`.
     */
    private static nuint FASTCOVER_ctx_init(FASTCOVER_ctx_t* ctx, void* samplesBuffer, nuint* samplesSizes, uint nbSamples, uint d, double splitPoint, uint f, FASTCOVER_accel_t accelParams)
    {
        var samples = (byte*)samplesBuffer;
        var totalSamplesSize = COVER_sum(samplesSizes, nbSamples);
        /* Split samples into testing and training sets */
        var nbTrainSamples = splitPoint < 1 ? (uint)(nbSamples * splitPoint) : nbSamples;
        var nbTestSamples = splitPoint < 1 ? nbSamples - nbTrainSamples : nbSamples;
        var trainingSamplesSize = splitPoint < 1 ? COVER_sum(samplesSizes, nbTrainSamples) : totalSamplesSize;
        var testSamplesSize = splitPoint < 1 ? COVER_sum(samplesSizes + nbTrainSamples, nbTestSamples) : totalSamplesSize;
        if (totalSamplesSize < (d > sizeof(ulong) ? d : sizeof(ulong)) || totalSamplesSize >= (sizeof(nuint) == 8 ? unchecked((uint)-1) : 1 * (1U << 30)) || nbTrainSamples < 5 || nbTestSamples < 1)
        {
            return unchecked((nuint)(-(int)ZSTD_ErrorCode.ZSTD_error_srcSize_wrong));
        }

        *ctx = new FASTCOVER_ctx_t
        {
            samples = samples,
            samplesSizes = samplesSizes,
            nbSamples = nbSamples,
            nbTrainSamples = nbTrainSamples,
            nbTestSamples = nbTestSamples,
            nbDmers = trainingSamplesSize - (d > sizeof(ulong) ? d : sizeof(ulong)) + 1,
            d = d,
            f = f,
            accelParams = accelParams,
            offsets = (nuint*)calloc(nbSamples + 1, (ulong)sizeof(nuint))
        };
        if (ctx->offsets == null)
        {
            FASTCOVER_ctx_destroy(ctx);
            return unchecked((nuint)(-(int)ZSTD_ErrorCode.ZSTD_error_memory_allocation));
        }

        {
            uint i;
            ctx->offsets[0] = 0;
            assert(nbSamples >= 5);
            for (i = 1; i <= nbSamples; ++i)
            {
                ctx->offsets[i] = ctx->offsets[i - 1] + samplesSizes[i - 1];
            }
        }

        ctx->freqs = (uint*)calloc((ulong)1 << (int)f, sizeof(uint));
        if (ctx->freqs == null)
        {
            FASTCOVER_ctx_destroy(ctx);
            return unchecked((nuint)(-(int)ZSTD_ErrorCode.ZSTD_error_memory_allocation));
        }

        FASTCOVER_computeFrequency(ctx->freqs, ctx);
        return 0;
    }

    /**
     * Given the prepared context build the dictionary.
     */
    private static nuint FASTCOVER_buildDictionary(FASTCOVER_ctx_t* ctx, uint* freqs, void* dictBuffer, nuint dictBufferCapacity, ZDICT_cover_params_t parameters, ushort* segmentFreqs)
    {
        var dict = (byte*)dictBuffer;
        var tail = dictBufferCapacity;
        /* Divide the data into epochs. We will select one segment from each epoch. */
        var epochs = COVER_computeEpochs((uint)dictBufferCapacity, (uint)ctx->nbDmers, parameters.k, 1);
        const nuint maxZeroScoreRun = 10;
        nuint zeroScoreRun = 0;
        nuint epoch;
        for (epoch = 0; tail > 0; epoch = (epoch + 1) % epochs.num)
        {
            var epochBegin = (uint)(epoch * epochs.size);
            var epochEnd = epochBegin + epochs.size;
            /* Select a segment */
            var segment = FASTCOVER_selectSegment(ctx, freqs, epochBegin, epochEnd, parameters, segmentFreqs);
            if (segment.score == 0)
            {
                if (++zeroScoreRun >= maxZeroScoreRun)
                {
                    break;
                }

                continue;
            }

            zeroScoreRun = 0;
            var segmentSize = segment.end - segment.begin + parameters.d - 1 < tail ? segment.end - segment.begin + parameters.d - 1 : tail;
            if (segmentSize < parameters.d)
            {
                break;
            }

            tail -= segmentSize;
            memcpy(dict + tail, ctx->samples + segment.begin, (uint)segmentSize);
        }

        return tail;
    }

    /**
     * Tries a set of parameters and updates the COVER_best_t with the results.
     * This function is thread safe if zstd is compiled with multithreaded support.
     * It takes its parameters as an *OWNING* opaque pointer to support threading.
     */
    private static void FASTCOVER_tryParameters(void* opaque)
    {
        /* Save parameters as local variables */
        var data = (FastcoverTryParametersDataS*)opaque;
        var ctx = data->ctx;
        var parameters = data->parameters;
        var dictBufferCapacity = data->dictBufferCapacity;
        var totalCompressedSize = unchecked((nuint)(-(int)ZSTD_ErrorCode.ZSTD_error_GENERIC));
        /* Initialize array to keep track of frequency of dmer within activeSegment */
        var segmentFreqs = (ushort*)calloc((ulong)1 << (int)ctx->f, sizeof(ushort));
        /* Allocate space for hash table, dict, and freqs */
        var dict = (byte*)malloc(dictBufferCapacity);
        var selection = COVER_dictSelectionError(unchecked((nuint)(-(int)ZSTD_ErrorCode.ZSTD_error_GENERIC)));
        var freqs = (uint*)malloc(((ulong)1 << (int)ctx->f) * sizeof(uint));
        if (segmentFreqs == null || dict == null || freqs == null)
        {
            goto _cleanup;
        }

        memcpy(freqs, ctx->freqs, (uint)(((ulong)1 << (int)ctx->f) * sizeof(uint)));
        {
            var tail = FASTCOVER_buildDictionary(ctx, freqs, dict, dictBufferCapacity, parameters, segmentFreqs);
            var nbFinalizeSamples = (uint)(ctx->nbTrainSamples * ctx->accelParams.finalize / 100);
            selection = COVER_selectDict(dict + tail, dictBufferCapacity, dictBufferCapacity - tail, ctx->samples, ctx->samplesSizes, nbFinalizeSamples, ctx->nbTrainSamples, ctx->nbSamples, parameters, ctx->offsets, totalCompressedSize);
            if (COVER_dictSelectionIsError(selection) != 0)
            {
                goto _cleanup;
            }
        }

        _cleanup:
        free(dict);
        COVER_best_finish(data->best, parameters, selection);
        free(data);
        free(segmentFreqs);
        COVER_dictSelectionFree(selection);
        free(freqs);
    }

    private static void FASTCOVER_convertToCoverParams(ZDICT_fastCover_params_t fastCoverParams, ZDICT_cover_params_t* coverParams)
    {
        coverParams->k = fastCoverParams.k;
        coverParams->d = fastCoverParams.d;
        coverParams->steps = fastCoverParams.steps;
        coverParams->nbThreads = fastCoverParams.nbThreads;
        coverParams->splitPoint = fastCoverParams.splitPoint;
        coverParams->zParams = fastCoverParams.zParams;
        coverParams->shrinkDict = fastCoverParams.shrinkDict;
    }

    private static void FASTCOVER_convertToFastCoverParams(ZDICT_cover_params_t coverParams, ZDICT_fastCover_params_t* fastCoverParams, uint f, uint accel)
    {
        fastCoverParams->k = coverParams.k;
        fastCoverParams->d = coverParams.d;
        fastCoverParams->steps = coverParams.steps;
        fastCoverParams->nbThreads = coverParams.nbThreads;
        fastCoverParams->splitPoint = coverParams.splitPoint;
        fastCoverParams->f = f;
        fastCoverParams->accel = accel;
        fastCoverParams->zParams = coverParams.zParams;
        fastCoverParams->shrinkDict = coverParams.shrinkDict;
    }

    /*! ZDICT_trainFromBuffer_fastCover():
     *  Train a dictionary from an array of samples using a modified version of COVER algorithm.
     *  Samples must be stored concatenated in a single flat buffer `samplesBuffer`,
     *  supplied with an array of sizes `samplesSizes`, providing the size of each sample, in order.
     *  d and k are required.
     *  All other parameters are optional, will use default values if not provided
     *  The resulting dictionary will be saved into `dictBuffer`.
     * @return: size of dictionary stored into `dictBuffer` (<= `dictBufferCapacity`)
     *          or an error code, which can be tested with ZDICT_isError().
     *          See ZDICT_trainFromBuffer() for details on failure modes.
     *  Note: ZDICT_trainFromBuffer_fastCover() requires 6 * 2^f bytes of memory.
     *  Tips: In general, a reasonable dictionary has a size of ~ 100 KB.
     *        It's possible to select smaller or larger size, just by specifying `dictBufferCapacity`.
     *        In general, it's recommended to provide a few thousands samples, though this can vary a lot.
     *        It's recommended that total size of all samples be about ~x100 times the target size of dictionary.
     */
    public static nuint ZDICT_trainFromBuffer_fastCover(void* dictBuffer, nuint dictBufferCapacity, void* samplesBuffer, nuint* samplesSizes, uint nbSamples, ZDICT_fastCover_params_t parameters)
    {
        var dict = (byte*)dictBuffer;
        FASTCOVER_ctx_t ctx;
        _gDisplayLevel = (int)parameters.zParams.notificationLevel;
        parameters.splitPoint = 1;
        parameters.f = parameters.f == 0 ? 20 : parameters.f;
        parameters.accel = parameters.accel == 0 ? 1 : parameters.accel;
        var coverParams = new ZDICT_cover_params_t();
        FASTCOVER_convertToCoverParams(parameters, &coverParams);
        if (FASTCOVER_checkParameters(coverParams, dictBufferCapacity, parameters.f, parameters.accel) == 0)
        {
            return unchecked((nuint)(-(int)ZSTD_ErrorCode.ZSTD_error_parameter_outOfBound));
        }

        if (nbSamples == 0)
        {
            return unchecked((nuint)(-(int)ZSTD_ErrorCode.ZSTD_error_srcSize_wrong));
        }

        if (dictBufferCapacity < 256)
        {
            return unchecked((nuint)(-(int)ZSTD_ErrorCode.ZSTD_error_dstSize_tooSmall));
        }

        var accelParams = FastcoverDefaultAccelParameters[parameters.accel];
        {
            var initVal = FASTCOVER_ctx_init(&ctx, samplesBuffer, samplesSizes, nbSamples, coverParams.d, parameters.splitPoint, parameters.f, accelParams);
            if (ERR_isError(initVal))
            {
                return initVal;
            }
        }

        COVER_warnOnSmallCorpus(dictBufferCapacity, ctx.nbDmers, _gDisplayLevel);
        {
            /* Initialize array to keep track of frequency of dmer within activeSegment */
            var segmentFreqs = (ushort*)calloc((ulong)1 << (int)parameters.f, sizeof(ushort));
            var tail = FASTCOVER_buildDictionary(&ctx, ctx.freqs, dictBuffer, dictBufferCapacity, coverParams, segmentFreqs);
            var nbFinalizeSamples = (uint)(ctx.nbTrainSamples * ctx.accelParams.finalize / 100);
            var dictionarySize = ZDICT_finalizeDictionary(dict, dictBufferCapacity, dict + tail, dictBufferCapacity - tail, samplesBuffer, samplesSizes, nbFinalizeSamples, coverParams.zParams);
            if (!ERR_isError(dictionarySize))
            {
            }

            FASTCOVER_ctx_destroy(&ctx);
            free(segmentFreqs);
            return dictionarySize;
        }
    }

    /*! ZDICT_optimizeTrainFromBuffer_fastCover():
     * The same requirements as above hold for all the parameters except `parameters`.
     * This function tries many parameter combinations (specifically, k and d combinations)
     * and picks the best parameters. `*parameters` is filled with the best parameters found,
     * dictionary constructed with those parameters is stored in `dictBuffer`.
     * All of the parameters d, k, steps, f, and accel are optional.
     * If d is non-zero then we don't check multiple values of d, otherwise we check d = {6, 8}.
     * if steps is zero it defaults to its default value.
     * If k is non-zero then we don't check multiple values of k, otherwise we check steps values in [50, 2000].
     * If f is zero, default value of 20 is used.
     * If accel is zero, default value of 1 is used.
     *
     * @return: size of dictionary stored into `dictBuffer` (<= `dictBufferCapacity`)
     *          or an error code, which can be tested with ZDICT_isError().
     *          On success `*parameters` contains the parameters selected.
     *          See ZDICT_trainFromBuffer() for details on failure modes.
     * Note: ZDICT_optimizeTrainFromBuffer_fastCover() requires about 6 * 2^f bytes of memory for each thread.
     */
    public static nuint ZDICT_optimizeTrainFromBuffer_fastCover(void* dictBuffer, nuint dictBufferCapacity, void* samplesBuffer, nuint* samplesSizes, uint nbSamples, ZDICT_fastCover_params_t* parameters)
    {
        /* constants */
        var nbThreads = parameters->nbThreads;
        var splitPoint = parameters->splitPoint <= 0 ? 0.75 : parameters->splitPoint;
        var kMinD = parameters->d == 0 ? 6 : parameters->d;
        var kMaxD = parameters->d == 0 ? 8 : parameters->d;
        var kMinK = parameters->k == 0 ? 50 : parameters->k;
        var kMaxK = parameters->k == 0 ? 2000 : parameters->k;
        var kSteps = parameters->steps == 0 ? 40 : parameters->steps;
        var kStepSize = (kMaxK - kMinK) / kSteps > 1 ? (kMaxK - kMinK) / kSteps : 1;
        var kIterations = (1 + (kMaxD - kMinD) / 2) * (1 + (kMaxK - kMinK) / kStepSize);
        var f = parameters->f == 0 ? 20 : parameters->f;
        var accel = parameters->accel == 0 ? 1 : parameters->accel;
        const uint shrinkDict = 0;
        /* Local variables */
        var displayLevel = (int)parameters->zParams.notificationLevel;
        uint iteration = 1;
        uint d;
        uint k;
        CoverBestS best;
        void* pool = null;
        var warned = 0;
        if (splitPoint is <= 0 or > 1 || accel is 0 or > 10 || kMinK < kMaxD || kMaxK < kMinK)
        {
            return unchecked((nuint)(-(int)ZSTD_ErrorCode.ZSTD_error_parameter_outOfBound));
        }

        if (nbSamples == 0)
        {
            return unchecked((nuint)(-(int)ZSTD_ErrorCode.ZSTD_error_srcSize_wrong));
        }

        if (dictBufferCapacity < 256)
        {
            return unchecked((nuint)(-(int)ZSTD_ErrorCode.ZSTD_error_dstSize_tooSmall));
        }

        if (nbThreads > 1)
        {
            pool = POOL_create(nbThreads, 1);
            if (pool == null)
            {
                return unchecked((nuint)(-(int)ZSTD_ErrorCode.ZSTD_error_memory_allocation));
            }
        }

        COVER_best_init(&best);
        var coverParams = new ZDICT_cover_params_t();
        FASTCOVER_convertToCoverParams(*parameters, &coverParams);
        var accelParams = FastcoverDefaultAccelParameters[accel];
        _gDisplayLevel = displayLevel == 0 ? 0 : displayLevel - 1;
        for (d = kMinD; d <= kMaxD; d += 2)
        {
            /* Initialize the context for this value of d */
            FASTCOVER_ctx_t ctx;
            {
                var initVal = FASTCOVER_ctx_init(&ctx, samplesBuffer, samplesSizes, nbSamples, d, splitPoint, f, accelParams);
                if (ERR_isError(initVal))
                {
                    COVER_best_destroy(&best);
                    POOL_free(pool);
                    return initVal;
                }
            }

            if (warned == 0)
            {
                COVER_warnOnSmallCorpus(dictBufferCapacity, ctx.nbDmers, displayLevel);
                warned = 1;
            }

            for (k = kMinK; k <= kMaxK; k += kStepSize)
            {
                /* Prepare the arguments */
                var data = (FastcoverTryParametersDataS*)malloc((ulong)sizeof(FastcoverTryParametersDataS));
                if (data == null)
                {
                    COVER_best_destroy(&best);
                    FASTCOVER_ctx_destroy(&ctx);
                    POOL_free(pool);
                    return unchecked((nuint)(-(int)ZSTD_ErrorCode.ZSTD_error_memory_allocation));
                }

                data->ctx = &ctx;
                data->best = &best;
                data->dictBufferCapacity = dictBufferCapacity;
                data->parameters = coverParams;
                data->parameters.k = k;
                data->parameters.d = d;
                data->parameters.splitPoint = splitPoint;
                data->parameters.steps = kSteps;
                data->parameters.shrinkDict = shrinkDict;
                data->parameters.zParams.notificationLevel = (uint)_gDisplayLevel;
                if (FASTCOVER_checkParameters(data->parameters, dictBufferCapacity, data->ctx->f, accel) == 0)
                {
                    free(data);
                    continue;
                }

                COVER_best_start(&best);
                if (pool != null)
                {
                    POOL_add(pool, (delegate* managed<void*, void>)(&FASTCOVER_tryParameters), data);
                }
                else
                {
                    FASTCOVER_tryParameters(data);
                }

                ++iteration;
            }

            COVER_best_wait(&best);
            FASTCOVER_ctx_destroy(&ctx);
        }

        {
            var dictSize = best.dictSize;
            if (ERR_isError(best.compressedSize))
            {
                var compressedSize = best.compressedSize;
                COVER_best_destroy(&best);
                POOL_free(pool);
                return compressedSize;
            }

            FASTCOVER_convertToFastCoverParams(best.parameters, parameters, f, accel);
            memcpy(dictBuffer, best.dict, (uint)dictSize);
            COVER_best_destroy(&best);
            POOL_free(pool);
            return dictSize;
        }
    }
}