using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DueGooder.Infrastructure.Http;

/// <summary>
/// Development cache of successful responses: one gzipped JSON file per request under
/// <c>&lt;directory&gt;/&lt;host&gt;/</c>, keyed by method, URL and form body with per-session parameters
/// left out, so a re-run replays a school's responses instead of hitting the registrar again.
/// </summary>
internal sealed class ResponseCache(string directory, IReadOnlySet<string> ignoredParameters)
{
    #region Methods

    public async Task<string> KeyForAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!;
        var canonical = new StringBuilder()
                        .Append(request.Method.Method)
                        .Append(' ')
                        .Append(url.GetLeftPart(UriPartial.Path))
                        .Append('?')
                        .Append(WithoutIgnoredParameters(url.Query.TrimStart('?')));
        if (request.Content is not null)
        {
            canonical.Append('\n').Append(WithoutIgnoredParameters(await request.Content.ReadAsStringAsync(cancellationToken)));
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    public async Task<CachedResponse?> TryReadAsync(Uri url, string key, CancellationToken cancellationToken)
    {
        var path = PathFor(url, key);
        if (File.Exists(path) is false)
        {
            return null;
        }

        await using var file = File.OpenRead(path);
        await using var gzip = new GZipStream(file, CompressionMode.Decompress);
        return await JsonSerializer.DeserializeAsync<CachedResponse>(gzip, cancellationToken: cancellationToken);
    }

    // Written to a temporary file first, so a run killed mid-write never leaves a truncated entry behind.
    public async Task WriteAsync(Uri url, string key, CachedResponse response, CancellationToken cancellationToken)
    {
        var path = PathFor(url, key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + ".tmp";
        await using (var file = File.Create(temporaryPath))
        await using (var gzip = new GZipStream(file, CompressionLevel.Fastest))
        {
            await JsonSerializer.SerializeAsync(gzip, response, cancellationToken: cancellationToken);
        }

        File.Move(temporaryPath, path, overwrite: true);
    }

    private string PathFor(Uri url, string key) => Path.Combine(directory, url.Host, key + ".json.gz");

    private string WithoutIgnoredParameters(string encodedPairs) =>
        string.Join('&',
                    encodedPairs.Split('&', StringSplitOptions.RemoveEmptyEntries)
                                .Where(pair => ignoredParameters.Contains(NameOf(pair)) is false));

    private static string NameOf(string encodedPair) =>
        Uri.UnescapeDataString(encodedPair.Split('=')[0].Replace('+', ' '));

    #endregion Methods
}
