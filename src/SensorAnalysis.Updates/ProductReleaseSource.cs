using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using SensorAnalysis.Distribution;
using Velopack;
using Velopack.Logging;
using Velopack.Sources;

namespace SensorAnalysis.Updates;

/// <summary>
/// Update feed for one product in a repository that publishes several products.
/// Velopack's <see cref="GithubSource"/> only inspects the newest releases of the
/// whole repository, so another product's tags would hide this product's feed.
/// </summary>
public sealed class ProductReleaseSource : IUpdateSource
{
    private const int PageSize = 100;
    private const int MaxPages = 10;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(30) };

    private readonly string _productId;
    private readonly string _packId;
    private readonly string _releasesUrl;
    private readonly Dictionary<string, string> _downloadUrls = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates a source that reads stable releases for one product id.</summary>
    public ProductReleaseSource(string repositoryUrl, string productId, string packId)
    {
        if (!Uri.TryCreate(repositoryUrl, UriKind.Absolute, out var uri) ||
            !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Repository URL must be an https://github.com/owner/name URL.", nameof(repositoryUrl));

        var parts = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
            throw new ArgumentException("Repository URL must be an https://github.com/owner/name URL.", nameof(repositoryUrl));

        _productId = productId;
        _packId = packId;
        _releasesUrl = $"https://api.github.com/repos/{parts[0]}/{parts[1]}/releases";
    }

    /// <inheritdoc />
    public async Task<VelopackAssetFeed> GetReleaseFeed(
        IVelopackLogger logger,
        string? appId,
        string channel,
        Guid? stagingId = null,
        VelopackAsset? latestLocalRelease = null)
    {
        _downloadUrls.Clear();
        if (string.IsNullOrWhiteSpace(channel))
            throw new InvalidOperationException("The installed application has no Velopack channel.");

        var feedName = ReleaseTags.FeedFileName(channel);
        var releases = await ListProductReleases(logger).ConfigureAwait(false);
        var assets = new Dictionary<string, VelopackAsset>(StringComparer.OrdinalIgnoreCase);

        foreach (var release in releases.OrderBy(release => release.Version))
        {
            var feedAsset = release.Assets.FirstOrDefault(asset =>
                asset.Name?.Equals(feedName, StringComparison.OrdinalIgnoreCase) == true);
            if (feedAsset?.BrowserDownloadUrl is null)
            {
                logger.Warn($"Release {release.Tag} has no {feedName}.");
                continue;
            }

            var json = await DownloadString(feedAsset.BrowserDownloadUrl).ConfigureAwait(false);
            var feed = VelopackAssetFeed.FromJson(json);
            foreach (var asset in feed.Assets)
            {
                if (string.IsNullOrWhiteSpace(asset.FileName))
                    continue;
                if (!string.IsNullOrEmpty(_packId) &&
                    !string.Equals(asset.PackageId, _packId, StringComparison.OrdinalIgnoreCase))
                {
                    logger.Warn($"Ignoring {asset.FileName} because its package id is {asset.PackageId}.");
                    continue;
                }

                var download = release.Assets.FirstOrDefault(item =>
                    item.Name?.Equals(asset.FileName, StringComparison.OrdinalIgnoreCase) == true);
                if (download?.BrowserDownloadUrl is null)
                {
                    logger.Warn($"Release {release.Tag} feed lists {asset.FileName}, but that file is not attached.");
                    continue;
                }

                assets[asset.FileName] = asset;
                _downloadUrls[asset.FileName] = download.BrowserDownloadUrl;
            }
        }

        logger.Info($"Found {assets.Count} {_productId} package(s) on channel {channel}.");
        return new VelopackAssetFeed { Assets = assets.Values.ToArray() };
    }

    /// <inheritdoc />
    public async Task DownloadReleaseEntry(
        IVelopackLogger logger,
        VelopackAsset releaseEntry,
        string localFile,
        Action<int> progress,
        CancellationToken cancelToken = default)
    {
        if (!_downloadUrls.TryGetValue(releaseEntry.FileName ?? "", out var url))
            throw new FileNotFoundException($"No download URL is known for '{releaseEntry.FileName}'.");

        logger.Info($"Downloading {releaseEntry.FileName}.");
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("SensorAnalysis-Updater");
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancelToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new FileNotFoundException($"GitHub has no public file at {url}. A private repository cannot be an end-user update source.");
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength;
        await using var input = await response.Content.ReadAsStreamAsync(cancelToken).ConfigureAwait(false);
        await using var output = new FileStream(localFile, FileMode.Create, FileAccess.Write, FileShare.None);
        var buffer = new byte[81920];
        long transferred = 0;
        var reported = -1;
        int read;
        while ((read = await input.ReadAsync(buffer, cancelToken).ConfigureAwait(false)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), cancelToken).ConfigureAwait(false);
            transferred += read;
            if (total is not > 0)
                continue;
            var percent = (int)Math.Clamp(transferred * 100 / total.Value, 0, 100);
            if (percent == reported)
                continue;
            reported = percent;
            progress(percent);
        }

        progress(100);
    }

    private async Task<List<ListedRelease>> ListProductReleases(IVelopackLogger logger)
    {
        var selected = new List<ListedRelease>();
        for (var page = 1; page <= MaxPages; page++)
        {
            var json = await DownloadString($"{_releasesUrl}?per_page={PageSize}&page={page}").ConfigureAwait(false);
            var pageReleases = JsonSerializer.Deserialize<List<ReleaseDto>>(json, JsonOptions) ?? [];
            foreach (var release in pageReleases)
            {
                if (release.Draft || release.Prerelease)
                    continue;
                if (!ReleaseTags.TryParse(release.TagName, out var productId, out var version) || productId != _productId)
                    continue;
                selected.Add(new ListedRelease(release.TagName!, version, release.Assets));
            }

            if (pageReleases.Count < PageSize)
                return selected;
        }

        logger.Warn($"Stopped after {MaxPages * PageSize} GitHub releases while looking for {_productId}.");
        return selected;
    }

    private static async Task<string> DownloadString(string url)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("SensorAnalysis-Updater");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var response = await Http.SendAsync(request).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
            return body;

        var detail = body.Length > 180 ? body[..180] : body;
        throw new InvalidOperationException($"GitHub returned {(int)response.StatusCode} for {url}. {detail}");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed record ListedRelease(string Tag, ReleaseVersion Version, List<AssetDto> Assets);

    private sealed class ReleaseDto
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("draft")]
        public bool Draft { get; set; }

        [JsonPropertyName("prerelease")]
        public bool Prerelease { get; set; }

        [JsonPropertyName("assets")]
        public List<AssetDto> Assets { get; set; } = [];
    }

    private sealed class AssetDto
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("browser_download_url")]
        public string? BrowserDownloadUrl { get; set; }
    }
}
