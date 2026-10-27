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
        "mpv" => new MpvAudioPlayer(FindMpv(Option(args, "--mpv-path", ""))),
        _ => throw new ArgumentException("播放引擎必須是 system、native、mediaengine 或 mpv。")
    };

    private static string FindMpv(string explicitPath)
    {
        if (explicitPath.Length > 0)
        {
            var path = Path.GetFullPath(explicitPath);
            return File.Exists(path) ? path : throw new FileNotFoundException("找不到指定的 mpv.exe。");
        }
        var adjacent = Path.Combine(AppContext.BaseDirectory, "tools", "mpv", "mpv.exe");
        if (File.Exists(adjacent)) return adjacent;
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            if (!File.Exists(Path.Combine(folder.FullName, "scripts", "mpv-package.json"))) continue;
            var path = Path.Combine(folder.FullName, "tools", "mpv", "mpv.exe");
            if (File.Exists(path)) return path;
            break;
        }
        throw new FileNotFoundException("找不到本機 mpv；請先執行 scripts/Install-Mpv.ps1 或指定 --mpv-path。");
    }
}
