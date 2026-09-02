using System;
using System.IO;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace UAFGJ;

internal enum DetectedFileType
{
    Invalid,
    AssetsFile,
    BundleFile
}

internal static class FileTypeDetector
{
    public static DetectedFileType DetectFileType(string path)
    {
        if (!File.Exists(path))
            return DetectedFileType.Invalid;

        try
        {
            if (AssetsFile.IsAssetsFile(path))
                return DetectedFileType.AssetsFile;
        }
        catch
        {
            // Fall through to bundle signature detection.
        }

        using FileStream fs = File.OpenRead(path);
        using BinaryReader br = new BinaryReader(fs);
        if (fs.Length < 4)
            return DetectedFileType.Invalid;

        byte[] sigBytes = br.ReadBytes(7);
        string sig = System.Text.Encoding.ASCII.GetString(sigBytes);
        if (sig.StartsWith("UnityFS", StringComparison.Ordinal) ||
            sig.StartsWith("UnityRaw", StringComparison.Ordinal) ||
            sig.StartsWith("UnityWeb", StringComparison.Ordinal))
        {
            return DetectedFileType.BundleFile;
        }

        return DetectedFileType.Invalid;
    }
}
