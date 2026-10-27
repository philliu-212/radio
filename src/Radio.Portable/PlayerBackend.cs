using Radio.Core;

namespace Radio.Windows;

// The portable build never loads or searches for mpv.
internal static class PlayerBackend
{
    public static string Option(string[] args, string name, string fallback)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
    }

    public static IAudioPlayer Create(string[] args)
    {
        if (Option(args, "--backend", "system") != "system")
            throw new ArgumentException("Portable 版只使用 Windows 系統音訊引擎。");
        return new SystemAudioPlayer();
    }
}
