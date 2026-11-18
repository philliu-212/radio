using Radio.Core;

namespace Radio.Windows;

internal static class PlayerBackend
{
    public static string Option(string[] args, string name, string fallback)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
    }

    public static IAudioPlayer Create(string[] args) => Option(args, "--backend", "system") switch
    {
        "system" => new SystemAudioPlayer(),
        "native" => new WindowsAudioPlayer(),
        "mediaengine" => new MediaEngineAudioPlayer(),
        _ => throw new ArgumentException("播放引擎必須是 system、native 或 mediaengine。")
    };
}
