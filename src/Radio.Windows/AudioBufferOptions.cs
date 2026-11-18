namespace Radio.Windows;

// Queue capacity, not a startup preload or a guarantee of that much reserve.
internal sealed record AudioBufferOptions
{
    internal TimeSpan Capacity { get; }
    internal int MaxPayloadBytes { get; }
    internal int MaxFrames { get; }

    internal AudioBufferOptions(TimeSpan capacity, int maxPayloadBytes, int maxFrames = 4096)
    {
        if (capacity <= TimeSpan.Zero || capacity > TimeSpan.FromMinutes(1))
            throw new ArgumentOutOfRangeException(nameof(capacity));
        if (maxPayloadBytes is < 1 or > 8 * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(maxPayloadBytes));
        if (maxFrames is < 1 or > 8192)
            throw new ArgumentOutOfRangeException(nameof(maxFrames));
        Capacity = capacity;
        MaxPayloadBytes = maxPayloadBytes;
        MaxFrames = maxFrames;
    }
}
