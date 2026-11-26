using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace NewUAFGJ;

internal sealed class BundleInput : IDisposable
{
    private readonly AssetsFileReader reader;
    private readonly AssetsFileReader? packedData;
    private readonly FileStream? unpacked;

    internal BundleInput(Stream stream, bool measureLoading = true, bool unpackData = true)
    {
        reader = new AssetsFileReader(new SegmentStream(stream, 0, stream.Length));
        try
        {
            using (measureLoading ? PerformanceProfile.Measure(PerformanceProfile.BundleLoading) : default)
            {
                var header = new AssetBundleHeader();
                header.Read(reader);
                if (header.Signature != "UnityFS")
                    throw new InvalidDataException("Expected a UnityFS bundle.");
                if (header.GetCompressionType() > 3)
                    throw new InvalidDataException($"Unsupported UnityFS directory compression ({header.GetCompressionType()}).");

                Bundle = new AssetBundleFile();
                Bundle.Read(reader);
                packedData = Bundle.DataReader;
                foreach (var block in Bundle.BlockAndDirInfo.BlockInfos)
                {
                    if (block.GetCompressionType() > 3)
                        throw new InvalidDataException($"Unsupported UnityFS block compression ({block.GetCompressionType()}).");
                }
                Compression = Bundle.GetCompressionType();
            }

            if (Bundle.DataIsCompressed && unpackData)
            {
                using var timing = PerformanceProfile.Measure(PerformanceProfile.BundleDecompression);
                unpacked = AssetReplacement.OpenTemporaryStream();
                Bundle = BundleHelper.UnpackBundleToStream(Bundle, unpacked, freeOriginalStream: false);
            }
            else if (PerformanceProfile.Current is not null && packedData!.BaseStream is LZ4BlockStream blocks)
            {
                Bundle.DataReader = new AssetsFileReader(new PerformanceProfile.ProfiledLz4Stream(blocks));
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal AssetBundleFile Bundle { get; }
    internal AssetBundleCompressionType Compression { get; }

    internal Dictionary<int, AssetsFile> ReadSerializedEntries()
    {
        var entries = new Dictionary<int, AssetsFile>();
        for (int index = 0; index < Bundle.BlockAndDirInfo.DirectoryInfos.Count; index++)
        {
            if (Bundle.IsAssetsFile(index))
            {
                AssetsFile file = BundleHelper.LoadAssetFromBundle(Bundle, index);
                AssetReplacement.CheckFileSize(file);
                entries.Add(index, file);
            }
        }
        return entries;
    }

    public void Dispose()
    {
        Bundle?.DataReader?.Dispose();
        if (!ReferenceEquals(packedData, Bundle?.DataReader))
            packedData?.Dispose();
        unpacked?.Dispose();
        reader.Dispose();
    }
}
