namespace Radio.Core;

public enum AudioState { Stopped, Opening, Buffering, Playing, Failed }
public enum PlaybackFailureKind { Unavailable, Transient }
public sealed record PlaybackStatus(AudioState State, string Message, PlaybackFailureKind Failure = PlaybackFailureKind.Unavailable);

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
    // Ordered, bounded backups, freshly resolved after the previous source fails to start.
    Task<ResolvedStream?> ResolveFallbackAsync(Station station, CancellationToken cancellationToken, int index = 0) =>
        Task.FromResult<ResolvedStream?>(null);
}
