using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Radio.Core;

// Anonymous playback only. No account credentials, persisted tokens or access-control workarounds.
internal static class TwitchStreamResolver
{
    internal static async Task<ResolvedStream> ResolveAsync(HttpClient client, Uri page, CancellationToken cancellationToken)
    {
        if (!page.IsAbsoluteUri || page.Scheme != "https" || page.Host != "www.twitch.tv" ||
            !page.IsDefaultPort || !string.IsNullOrEmpty(page.UserInfo) || page.Query.Length != 0 || page.Fragment.Length != 0 ||
            !Regex.IsMatch(page.AbsolutePath, "^/[a-z0-9_]{1,25}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
            throw new SourceResolutionException("Twitch 備援頻道網址格式不正確。");
        var channel = page.AbsolutePath[1..];
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://gql.twitch.tv/gql");
            // Public Twitch web-player client identifier; not an account secret.
            request.Headers.Add("Client-ID", "kimne78kx3ncx6brgo4mv6wki5h1ko");
            request.Headers.Referrer = page;
            request.Content = JsonContent.Create(new
            {
                operationName = "PlaybackAccessToken_Template",
                query = "query PlaybackAccessToken_Template($login: String!) { streamPlaybackAccessToken(channelName: $login, params: {platform: \"web\", playerBackend: \"mediaplayer\", playerType: \"site\"}) { value signature } }",
                variables = new { login = channel }
            });
            using var response = await client.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.TryGetProperty("errors", out _) ||
                !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object ||
                !data.TryGetProperty("streamPlaybackAccessToken", out var token) || token.ValueKind != JsonValueKind.Object ||
                !token.TryGetProperty("signature", out var signature) || signature.ValueKind != JsonValueKind.String ||
                !token.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(signature.GetString()) || string.IsNullOrWhiteSpace(value.GetString()))
                throw new SourceResolutionException("Twitch 備援未提供公開播放授權，請使用官方播放器。");

            var manifest = $"https://usher.ttvnw.net/api/channel/hls/{channel}.m3u8?allow_audio_only=true&allow_source=true&sig=" +
                Uri.EscapeDataString(signature.GetString()!) + "&token=" + Uri.EscapeDataString(value.GetString()!);
            var playlist = await client.GetStringAsync(manifest, cancellationToken);
            return ParseAudioPlaylist(playlist, page);
        }
        catch (HttpRequestException error)
        {
            throw new SourceResolutionException(error.StatusCode is { } status
                ? $"Twitch 備援回應 HTTP {(int)status}，可能離線或暫時無法播放。"
                : "無法連接 Twitch 備援，請稍後重試。");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new SourceResolutionException("取得 Twitch 備援逾時，請稍後重試。");
        }
        catch (JsonException)
        {
            throw new SourceResolutionException("Twitch 備援 API 格式已變更，請使用官方播放器。");
        }
    }

    internal static ResolvedStream ParseAudioPlaylist(string playlist, Uri page)
    {
        var lines = playlist.TrimStart('\uFEFF').Split('\n').Select(line => line.Trim()).ToArray();
        if (lines.FirstOrDefault() == "#EXTM3U")
            for (var index = 1; index + 1 < lines.Length; index++)
            {
                if (!lines[index].StartsWith("#EXT-X-STREAM-INF:", StringComparison.Ordinal) ||
                    !Regex.IsMatch(lines[index], "(?:[:,])VIDEO=\"audio_only\"(?:,|$)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))) continue;
                if (Uri.TryCreate(lines[index + 1], UriKind.Absolute, out var uri) &&
                    uri.Scheme == "https" && uri.IsDefaultPort && string.IsNullOrEmpty(uri.UserInfo) &&
                    (uri.Host.EndsWith(".ttvnw.net", StringComparison.OrdinalIgnoreCase) ||
                     uri.Host.EndsWith(".live-video.net", StringComparison.OrdinalIgnoreCase)) &&
                    uri.AbsolutePath.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase))
                    return new ResolvedStream(uri, page, "application/vnd.apple.mpegurl");
                break;
            }
        throw new SourceResolutionException("Twitch 備援未提供預期的純音訊直播，可能離線或格式已變更。");
    }
}
