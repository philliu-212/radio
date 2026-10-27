using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Radio.Core;

public sealed class SourceResolutionException(string message) : Exception(message);

public sealed class OfficialStreamResolver(Func<HttpMessageHandler>? handlerFactory = null) : IStreamResolver
{
    public async Task<ResolvedStream> ResolveAsync(Station station, CancellationToken cancellationToken)
    {
        using var client = new HttpClient(handlerFactory?.Invoke() ?? new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            UseCookies = true
        });
        client.Timeout = TimeSpan.FromSeconds(20);
        client.MaxResponseContentBufferSize = 2 * 1024 * 1024;
        client.DefaultRequestHeaders.UserAgent.ParseAdd("LightweightRadio/0.1");
        client.DefaultRequestHeaders.Referrer = station.PageUrl;

        try
        {
            string url;
            switch (station.Resolver)
            {
                case ResolverKind.Html:
                    var html = await client.GetStringAsync(station.PageUrl, cancellationToken);
                    var match = Regex.Match(html, station.Match!, RegexOptions.CultureInvariant,
                        TimeSpan.FromSeconds(1));
                    if (!match.Success)
                        throw new SourceResolutionException("官方頁面的串流格式已變更，請稍後更新來源解析器。");
                    url = WebUtility.HtmlDecode(match.Groups[1].Value);
                    break;
                case ResolverKind.Form:
                    // These official players initialize a session before requesting a signed URL.
                    await client.GetStringAsync(station.PageUrl, cancellationToken);
                    using (var form = new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["channelID"] = "1",
                        ["action"] = "getLIVEURL"
                    }))
                    using (var response = await client.PostAsync(station.EndpointUrl, form, cancellationToken))
                    {
                        response.EnsureSuccessStatusCode();
                        url = WebUtility.HtmlDecode((await response.Content.ReadAsStringAsync(cancellationToken)).Trim());
                    }
                    break;
                case ResolverKind.BccJson:
                    var json = await client.GetStringAsync(station.EndpointUrl, cancellationToken);
                    using (var document = JsonDocument.Parse(json))
                    {
                        if (document.RootElement.ValueKind != JsonValueKind.Array)
                            throw new SourceResolutionException("官方 API 資料格式已變更。");
                        var channel = document.RootElement.EnumerateArray().FirstOrDefault(item =>
                            item.TryGetProperty("name", out var name) && name.GetString() == station.Match);
                        if (channel.ValueKind == JsonValueKind.Undefined ||
                            !channel.TryGetProperty("iosStream", out var source))
                            throw new SourceResolutionException("官方 API 找不到中廣流行網。");
                        if (source.ValueKind != JsonValueKind.String)
                            throw new SourceResolutionException("官方 API 未提供可用的串流網址。");
                        url = source.GetString() ?? "";
                    }
                    break;
                default:
                    throw new SourceResolutionException("不支援的來源解析方式。");
            }

            // Fail closed on HTML errors, unexpected hosts and non-HTTPS playback URLs.
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
                !string.Equals(uri.Host, station.StreamHost, StringComparison.OrdinalIgnoreCase) ||
                !string.IsNullOrEmpty(uri.UserInfo) || !uri.IsDefaultPort)
                throw new SourceResolutionException("官方未提供預期的 HTTPS 串流，可能暫停服務或已更換來源。");

            return new ResolvedStream(uri, station.PageUrl, station.MediaType);
        }
        catch (HttpRequestException error)
        {
            // Do not expose signed URLs from exception messages.
            throw new SourceResolutionException(error.StatusCode is { } status
                ? $"官方來源回應 HTTP {(int)status}，請稍後再試或使用官方播放器。"
                : "無法連接官方來源，請檢查網路或使用官方播放器。");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new SourceResolutionException("取得官方來源逾時，請稍後再試。");
        }
        catch (JsonException)
        {
            throw new SourceResolutionException("官方 API 資料格式已變更。");
        }
    }
}
