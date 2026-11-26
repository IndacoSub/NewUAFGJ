using System.Diagnostics;

namespace NewUAFGJ;

internal sealed class PerformanceProfile(long started)
{
    internal const string Initialization = "Process initialization";
    internal const string PayloadLoading = "Replacement payload loading";
    internal const string PngDecoding = "PNG decoding";
    internal const string TextureEncoding = "Texture encoding (including mipmaps)";
    internal const string TypeTreeResolution = "TypeTree resolution";
    internal const string BundleLoading = "Bundle loading";
    internal const string BundleDecompression = "Bundle decompression / LZ4 reads";
    internal const string PathIdSearch = "PathID search";
    internal const string AssetSerialization = "Asset serialization";
    internal const string BundleWriting = "Bundle writing";
    internal const string BundleCompression = "Bundle compression";
    internal const string Validation = "Final validation";
    internal const string Applying = "Applying replacements";
    internal const string Cooldown = "Same-source cooldown";

    private static readonly string[] Stages =
    [
        Initialization, PayloadLoading, PngDecoding, TextureEncoding, TypeTreeResolution,
        BundleLoading, BundleDecompression, PathIdSearch, AssetSerialization, BundleWriting,
        BundleCompression, Validation, Applying, Cooldown
    ];

    [ThreadStatic]
    internal static PerformanceProfile? Current;

    private readonly Dictionary<string, long> elapsed = new();
    private readonly List<string> details = new();

    internal static Scope Measure(string stage) => new(Current, stage);

    internal void Record(string stage, long timestamp)
    {
        elapsed[stage] = elapsed.GetValueOrDefault(stage) + Stopwatch.GetTimestamp() - timestamp;
    }

    internal void Describe(string detail) => details.Add(detail);
    internal IReadOnlyList<string> Details => details;
    internal double TotalMilliseconds => Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    internal Dictionary<string, double> Timings => Stages.ToDictionary(stage => stage,
        stage => TimeSpan.FromSeconds((double)elapsed.GetValueOrDefault(stage) / Stopwatch.Frequency).TotalMilliseconds);

    internal void Print()
    {
        Console.WriteLine("PERFORMANCE REPORT");
        foreach (string detail in details)
            Console.WriteLine(detail);
        foreach (var (stage, milliseconds) in Timings)
            Console.WriteLine(FormattableString.Invariant($"{stage,-39} {milliseconds,10:F2} ms"));
        Console.WriteLine(FormattableString.Invariant($"{"TOTAL",-39} {TotalMilliseconds,10:F2} ms"));
    }

    internal readonly struct Scope(PerformanceProfile? profile, string stage) : IDisposable
    {
        private readonly long timestamp = profile is null ? 0 : Stopwatch.GetTimestamp();

        public void Dispose() => profile?.Record(stage, timestamp);
    }

    // AssetsTools.NET decompresses LZ4 blocks on demand inside its seekable stream.
    // Time spent in those reads includes I/O and cache hits, and can overlap other stages.
    internal sealed class ProfiledLz4Stream(Stream stream) : Stream
    {
        public override bool CanRead => stream.CanRead;
        public override bool CanSeek => stream.CanSeek;
        public override bool CanWrite => false;
        public override long Length => stream.Length;
        public override long Position { get => stream.Position; set => stream.Position = value; }
        public override long Seek(long offset, SeekOrigin origin) => stream.Seek(offset, origin);
        public override void Flush() => stream.Flush();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count)
        {
            using var timing = Measure(BundleDecompression);
            return stream.Read(buffer, offset, count);
        }

        public override int Read(Span<byte> buffer)
        {
            using var timing = Measure(BundleDecompression);
            return stream.Read(buffer);
        }

        public override int ReadByte()
        {
            using var timing = Measure(BundleDecompression);
            return stream.ReadByte();
        }
    }
}
