namespace Radio.Core;

public enum AudioState { Stopped, Opening, Buffering, Playing, Failed }
public sealed record PlaybackStatus(AudioState State, string Message);

public interface IAudioPlayer : IDisposable
{
    event Action<PlaybackStatus>? StatusChanged;
    double Volume { get; set; }
    bool IsMuted { get; set; }
    TimeSpan Position { get; }
    Task PlayAsync(ResolvedStream stream, CancellationToken cancellationToken);
    void Stop();
}

public interface IStreamResolver
{
    Task<ResolvedStream> ResolveAsync(Station station, CancellationToken cancellationToken);
    // A single optional backup, freshly resolved after primary resolution/startup fails.
    Task<ResolvedStream?> ResolveFallbackAsync(Station station, CancellationToken cancellationToken) =>
        Task.FromResult<ResolvedStream?>(null);
}
