namespace NewUAFGJ;

internal sealed class TextureEncodingCache(IEnumerable<string> repeatedPaths)
{
    private const long MaxBytes = 64 * 1024 * 1024;
    private readonly HashSet<string> repeated = new(repeatedPaths, BatchCommand.PathComparer);
    private readonly Dictionary<(string Path, TextureEncoding Encoding), (byte[] Png, byte[] Encoded)> entries = new();
    private readonly object gate = new();
    private long bytes;

    internal byte[] Encode(string path, byte[] png, (int Width, int Height) pngSize, TextureEncoding encoding)
    {
        // The encoder already uses multiple cores. Bound both encoder working memory and the retained cache.
        lock (gate)
        {
            if (!repeated.Contains(path))
                return TextureImage.Encode(png, pngSize, encoding);

            var key = (path, encoding);
            if (entries.TryGetValue(key, out var existing) && existing.Png.AsSpan().SequenceEqual(png))
            {
                PerformanceProfile.Current?.Describe("Reused the encoded texture for an identical PNG and texture layout.");
                return existing.Encoded;
            }

            byte[] encoded = TextureImage.Encode(png, pngSize, encoding);
            long size = png.LongLength + encoded.LongLength;
            if (size > MaxBytes)
                return encoded;
            if (entries.Remove(key, out existing))
                bytes -= existing.Png.LongLength + existing.Encoded.LongLength;
            if (size > MaxBytes - bytes)
            {
                entries.Clear();
                bytes = 0;
            }
            entries.Add(key, (png, encoded));
            bytes += size;
            return encoded;
        }
    }
}
