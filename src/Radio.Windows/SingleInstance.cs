using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Radio.Windows;

// One player per executable directory/user/session, independent of package version.
// Ownership is acquired and released on the startup STA thread.
internal sealed class SingleInstance : IDisposable
{
    internal static string Scope
        // Portable's root apphost stays here while managed package directories change.
        => CreateScope(Path.GetDirectoryName(Environment.ProcessPath!)!);

    internal static string CreateScope(string executableDirectory)
    {
        var directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(executableDirectory)).ToUpperInvariant();
        var directoryHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(directory)));
        using var identity = WindowsIdentity.GetCurrent();
        using var process = Process.GetCurrentProcess();
        return $"RadioMini.{identity.User!.Value}.{process.SessionId}.{directoryHash}";
    }
    private const byte ActivateWindow = 1;
    private readonly Mutex _mutex;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _stop = new();
    private Task? _listener;
    internal bool IsPrimary { get; }

    internal SingleInstance(string? scope = null)
    {
        scope ??= Scope;
        _pipeName = scope + ".activation";
        _mutex = new Mutex(false, @"Local\" + scope);
        // A short bounded wait also covers the interval between a terminating
        // process being signaled and Windows abandoning its owning thread.
        try { IsPrimary = _mutex.WaitOne(250); }
        catch (AbandonedMutexException) { IsPrimary = true; }
    }

    internal void Listen(Form window, Action activate)
    {
        if (!IsPrimary || _listener is not null) throw new InvalidOperationException("Only the primary instance can listen once.");
        _ = window.Handle; // Create on the UI thread before accepting activation.
        _listener = ListenAsync(window, activate);
    }

    private async Task ListenAsync(Form window, Action activate)
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(_stop.Token).ConfigureAwait(false);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(1));
                var command = new byte[1];
                if (await server.ReadAsync(command, timeout.Token).ConfigureAwait(false) != 1 || command[0] != ActivateWindow) continue;
                // Never touch UI/audio state on the pipe thread. BeginInvoke also queues
                // requests received before Application.Run starts pumping messages.
                window.BeginInvoke(() => { if (!window.IsDisposed && !window.Disposing) activate(); });
                await server.WriteAsync(new byte[] { ActivateWindow }, timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (IOException) { } // Disconnected/malformed client must not stop the listener.
            catch (InvalidOperationException) when (window.IsDisposed || window.Disposing || _stop.IsCancellationRequested) { break; }
        }
    }

    internal async Task<bool> ActivateExistingAsync()
    {
        if (IsPrimary) throw new InvalidOperationException("The primary instance cannot activate itself through IPC.");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        for (var attempt = 0; attempt < 20 && !deadline.IsCancellationRequested; attempt++)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                await client.ConnectAsync(250, deadline.Token).ConfigureAwait(false);
                // Grant only the connected server permission, before asking it to activate.
                // Windows may deny foreground activation; never work around it with TopMost.
                if (GetNamedPipeServerProcessId(client.SafePipeHandle, out var processId)) AllowSetForegroundWindow(processId);
                using var exchange = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
                exchange.CancelAfter(TimeSpan.FromSeconds(1));
                await client.WriteAsync(new byte[] { ActivateWindow }, exchange.Token).ConfigureAwait(false);
                var reply = new byte[1];
                if (await client.ReadAsync(reply, exchange.Token).ConfigureAwait(false) == 1 && reply[0] == ActivateWindow) return true;
            }
            catch (TimeoutException) { }
            catch (IOException) { }
            catch (OperationCanceledException) { }
            try { await Task.Delay(100, deadline.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
        return false;
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener?.GetAwaiter().GetResult();
        _stop.Dispose();
        if (IsPrimary) _mutex.ReleaseMutex();
        _mutex.Dispose();
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint serverProcessId);
    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(uint processId);
}
