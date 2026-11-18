using System.Collections.Concurrent;

namespace Radio.Windows.Tests;

// Awaitable non-UI groups still stay on the entry STA thread. No hidden Form,
// foreground activation or nested Application.Run is needed for Fast checks.
internal sealed class StaTestContext : SynchronizationContext, IDisposable
{
    private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();

    public override void Post(SendOrPostCallback callback, object? state) => _queue.Add((callback, state));

    internal static void Run(Func<Task> run)
    {
        var previous = Current;
        using var context = new StaTestContext();
        SetSynchronizationContext(context);
        try
        {
            var task = run();
            _ = task.ContinueWith(_ => context._queue.CompleteAdding(), CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            foreach (var work in context._queue.GetConsumingEnumerable())
                work.Callback(work.State);
            task.GetAwaiter().GetResult();
        }
        finally { SetSynchronizationContext(previous); }
    }

    public void Dispose() => _queue.Dispose();
}
