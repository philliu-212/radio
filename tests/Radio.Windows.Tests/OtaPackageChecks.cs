using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.Json;
using Radio.Update;

namespace Radio.Windows.Tests;

// Explicit local HTTP integration transport, injected only in the test assembly.
// Production update URLs remain fixed to the public GitHub repository.
internal static class OtaPackageChecks
{
    internal static async Task StageAsync(string[] args)
    {
        string Arg(string name) => args[Array.IndexOf(args, name) + 1];
        var root = Path.GetFullPath(Arg("--root")); var zip = Path.GetFullPath(Arg("--zip")); var report = Path.GetFullPath(Arg("--report"));
        if (!root.StartsWith(Path.GetFullPath("artifacts") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Integration root must be isolated under artifacts.");
        Package candidate;
        using (var archive = ZipFile.OpenRead(zip))
        using (var input = archive.Entries.Single(e => e.Name == "package.json").Open())
            candidate = (Package)new DataContractJsonSerializer(typeof(Package)).ReadObject(input)!;
        candidate.Validate();
        var store = new Store(root); var state = store.Load();
        if (candidate.Mode != state.Current.Mode || Version.Parse(candidate.Version) <= Version.Parse(state.Current.Version)) throw new InvalidDataException("Candidate must be a newer same-mode integration build.");
        await using var server = new ZipServer(zip);
        var handler = new Transport(server.Url, zip, candidate);
        using var updater = new PortableUpdate(handler, store, Version.Parse(state.Current.Version));
        var before = UpdateChecks.DiskBytes(root); var watch = Stopwatch.StartNew(); double downloaded = 0;
        updater.Changed += view => { if (view.Phase == UpdatePhase.Preparing) downloaded = watch.Elapsed.TotalMilliseconds; };
        await updater.CheckNowAsync(); await updater.Preparation;
        if (updater.View.Phase != UpdatePhase.Ready) throw new Exception(File.ReadAllText(Path.Combine(store.Ota, "last-error.txt")));
        var ready = watch.Elapsed.TotalMilliseconds;
        var requests = handler.Downloads; await updater.CheckNowAsync();
        if (handler.Downloads != requests) throw new Exception("Prepared package downloaded twice");
        var result = new { Scope = "actual portable ZIP over local TCP/HTTP, not GitHub WAN", Passed = true, Mode = candidate.Mode,
            ZipBytes = new FileInfo(zip).Length, HttpBodyBytes = server.BytesSent, BackgroundDownloadAndVerifyMs = downloaded,
            BackgroundExtractAndPrepareMs = ready - downloaded, BackgroundTotalMs = ready, DiskBytesBefore = before,
            DiskBytesReady = UpdateChecks.DiskBytes(root), CurrentVersion = store.Load().Current.Version, PendingVersion = store.Load().Pending.Version,
            PendingCommit = candidate.Commit, DuplicateDownloads = handler.Downloads - requests };
        File.WriteAllText(report, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("PASS: actual portable prepared over local HTTP; current version unchanged; repeated check downloads nothing.");
    }
    private sealed class Transport(Uri server, string zip, Package package) : HttpMessageHandler
    {
        private readonly HttpClient _forward = new() { Timeout = Timeout.InfiniteTimeSpan };
        internal int Downloads;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/latest"))
            {
                var name = $"Radio-Portable-v{package.Version}-{package.Mode}-win-x64-20261029-123456.zip";
                var json = JsonSerializer.Serialize(new { tag_name = "v" + package.Version, draft = false, prerelease = false,
                    assets = new[] { new { name, size = new FileInfo(zip).Length, digest = "sha256:" + Store.Hash(zip), browser_download_url = $"{PortableUpdate.Repository}/releases/download/v{package.Version}/{name}" } } });
                return new(HttpStatusCode.OK) { Content = new StringContent(json) };
            }
            Downloads++; return await _forward.GetAsync(server, HttpCompletionOption.ResponseHeadersRead, token);
        }
        protected override void Dispose(bool disposing) { if (disposing) _forward.Dispose(); base.Dispose(disposing); }
    }
    private sealed class ZipServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _serve;
        internal long BytesSent;
        internal Uri Url { get; }
        internal ZipServer(string path)
        {
            _listener.Start(); Url = new Uri($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/package.zip");
            _serve = Task.Run(async () =>
            {
                try
                {
                    while (!_stop.IsCancellationRequested)
                    {
                        using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                        await using var output = client.GetStream();
                        using var reader = new StreamReader(output, Encoding.ASCII, false, 1024, true);
                        string? line; do { line = await reader.ReadLineAsync(_stop.Token); } while (!string.IsNullOrEmpty(line));
                        var headers = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Length: {new FileInfo(path).Length}\r\nConnection: close\r\n\r\n");
                        await output.WriteAsync(headers, _stop.Token);
                        await using var input = File.OpenRead(path); await input.CopyToAsync(output, _stop.Token); BytesSent += input.Length;
                    }
                }
                catch (OperationCanceledException) { }
                catch (SocketException) when (_stop.IsCancellationRequested) { }
                catch (ObjectDisposedException) when (_stop.IsCancellationRequested) { }
            });
        }
        public async ValueTask DisposeAsync() { _stop.Cancel(); _listener.Stop(); await _serve; _stop.Dispose(); }
    }
}
