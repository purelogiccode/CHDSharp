using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace VendoredZSTD;

internal unsafe class JobThreadPool : IDisposable
{
    private readonly BlockingCollection<Job> _queue;
    private readonly List<JobThread> _threads;
    private int _numThreads;

    public JobThreadPool(int num, int queueSize)
    {
        _numThreads = num;
        _queue = new BlockingCollection<Job>(queueSize + 1);
        _threads = new List<JobThread>(num);
        for (var i = 0; i < _numThreads; i++)
            CreateThread();
    }

    public void Dispose()
    {
        _queue.Dispose();
    }

    private void Worker(object? obj)
    {
        if (obj is not JobThread poolThread)
            return;

        var cancellationToken = poolThread.CancellationTokenSource.Token;
        while (!_queue.IsCompleted && !cancellationToken.IsCancellationRequested)
            try
            {
                if (_queue.TryTake(out var job, -1, cancellationToken))
                    ((delegate* managed<void*, void>)job.function)(job.opaque);
            }
            catch (InvalidOperationException)
            {
            }
            catch (OperationCanceledException)
            {
            }
    }

    private void CreateThread()
    {
        var poolThread = new JobThread(new Thread(Worker));
        _threads.Add(poolThread);
        poolThread.Start();
    }

    public void Resize(int num)
    {
        lock (_threads)
        {
            if (num < _numThreads)
                for (var i = _numThreads - 1; i >= num; i--)
                {
                    _threads[i].Cancel();
                    _threads.RemoveAt(i);
                }
            else
                for (var i = _numThreads; i < num; i++)
                    CreateThread();
        }

        _numThreads = num;
    }

    public void Add(void* function, void* opaque)
    {
        _queue.Add(new Job { function = function, opaque = opaque });
    }

    public bool TryAdd(void* function, void* opaque)
    {
        return _queue.TryAdd(new Job { function = function, opaque = opaque });
    }

    public void Join(bool cancel = true)
    {
        _queue.CompleteAdding();
        List<JobThread> jobThreads;
        lock (_threads)
        {
            jobThreads = new List<JobThread>(_threads);
        }

        if (cancel)
            foreach (var thread in jobThreads)
                thread.Cancel();

        foreach (var thread in jobThreads)
            thread.Join();
    }

    public static int Size()
    {
        // todo not implemented
        // https://github.com/dotnet/runtime/issues/24200
        return 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Job
    {
        public void* function;
        public void* opaque;
    }

    private class JobThread
    {
        public JobThread(Thread thread)
        {
            CancellationTokenSource = new CancellationTokenSource();
            Thread = thread;
        }

        private Thread Thread { get; }
        public CancellationTokenSource CancellationTokenSource { get; }

        public void Start()
        {
            Thread.Start(this);
        }

        public void Cancel()
        {
            CancellationTokenSource.Cancel();
        }

        public void Join()
        {
            Thread.Join();
        }
    }
}