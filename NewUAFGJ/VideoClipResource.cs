using AssetsTools.NET;
using AssetsTools.NET.Extra;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace UAFGJ
{
    partial class Program
    {
        private const int VideoClipTypeId = 329;

        private static bool IsVideoClipAsResourceKind(string fileKind)
        {
            return string.Equals(
                fileKind,
                "VIDEOCLIP_AS_RESOURCE",
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool FindVideoClipAsResourceFile(
            string inputFile,
            ref AssetsFileInstance assetInst,
            ref AssetFileInfo afie,
            ref AssetsTools.NET.AssetTypeValueField atvf,
            ref AssetsManager am,
            BundleFileInstance bundleInst,
            ref string assetfile_name,
            string specificPathId,
            string specificFileId,
            string fileKind,
            out byte[] rawReplacementData,
            out byte[] originalSerializedData,
            out string resourceEntryName)
        {
            rawReplacementData = Array.Empty<byte>();
            originalSerializedData = Array.Empty<byte>();
            resourceEntryName = string.Empty;

            if (!IsVideoClipAsResourceKind(fileKind))
            {
                DisplayStr(
                    $"[VIDEO] Invalid fileKind '{fileKind}'. " +
                    "Expected VIDEOCLIP_AS_RESOURCE.");
                return false;
            }

            if (assetInst == null || am == null || bundleInst == null)
            {
                DisplayStr("[VIDEO] AssetsManager/AssetsFileInstance/BundleFileInstance is null.");
                return false;
            }

            if (!File.Exists(inputFile))
            {
                DisplayStr(
                    $"[VIDEO] Replacement MP4 does not exist: '{inputFile}'.");
                return false;
            }

            if (!string.Equals(
                    Path.GetExtension(inputFile),
                    ".mp4",
                    StringComparison.OrdinalIgnoreCase))
            {
                DisplayStr(
                    $"[VIDEO] VIDEOCLIP_AS_RESOURCE requires an .mp4 input, " +
                    $"received '{inputFile}'.");
                return false;
            }

            long replacementLength =
                new FileInfo(inputFile).Length;

            if (replacementLength <= 0)
            {
                DisplayStr(
                    $"[VIDEO] Replacement MP4 is empty: '{inputFile}'.");
                return false;
            }

            long wantedPathId;
            bool hasWantedPathId =
                TryParsePathId(
                    specificPathId,
                    out wantedPathId);

            AssetsFileInstance targetFile = null;
            AssetFileInfo targetInfo = null;

            if (hasWantedPathId)
            {
                DebugStr(
                    $"[VIDEO] Resolving VideoClip target: " +
                    $"PID={wantedPathId}, TypeID={VideoClipTypeId}, " +
                    $"FileID='{specificFileId}'.");

                List<TargetAssetCandidate> candidates =
                    FindPathIdCandidates(
                        am,
                        assetInst,
                        wantedPathId,
                        VideoClipTypeId);

                int selectedFileId;

                if (!SelectTargetCandidate(
                        candidates,
                        specificFileId,
                        wantedPathId,
                        VideoClipTypeId,
                        out targetFile,
                        out targetInfo,
                        out selectedFileId))
                {
                    return false;
                }

                DebugStr(
                    $"[VIDEO] Selected VideoClip: " +
                    $"FileID={selectedFileId}, " +
                    $"PID={targetInfo.PathId}, " +
                    $"TypeID={targetInfo.TypeId}, " +
                    $"File='{targetFile.name}'.");
            }
            else
            {
                string targetName =
                    Path.GetFileNameWithoutExtension(
                        inputFile).Trim();

                DebugStr(
                    $"[VIDEO] No valid PathID supplied. " +
                    $"Searching VideoClip by name '{targetName}'.");

                int candidatesScanned = 0;

                foreach (AssetFileInfo candidateInfo
                         in assetInst.file.GetAssetsOfType(VideoClipTypeId))
                {
                    candidatesScanned++;

                    AssetsTools.NET.AssetTypeValueField candidateField;

                    try
                    {
                        candidateField =
                            am.GetBaseField(
                                assetInst,
                                candidateInfo);
                    }
                    catch (Exception ex)
                    {
                        DebugStr(
                            $"[VIDEO] Failed reading candidate PID " +
                            $"{candidateInfo.PathId}: " +
                            $"{ex.GetType().Name}: {ex.Message}");
                        continue;
                    }

                    if (candidateField == null ||
                        candidateField.IsDummy)
                    {
                        continue;
                    }

                    string candidateName =
                        GetAssetName(candidateField);

                    if (!string.Equals(
                            candidateName?.Trim(),
                            targetName,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (targetInfo != null)
                    {
                        DisplayStr(
                            $"[VIDEO] Multiple VideoClip assets named " +
                            $"'{targetName}' were found; PathID is required.");
                        return false;
                    }

                    targetFile = assetInst;
                    targetInfo = candidateInfo;
                    atvf = candidateField;
                }

                if (targetInfo == null)
                {
                    DisplayStr(
                        $"[VIDEO] Could not find VideoClip '{targetName}'. " +
                        $"Candidates scanned: {candidatesScanned}.");
                    return false;
                }

                DebugStr(
                    $"[VIDEO] Found VideoClip by name: " +
                    $"PID={targetInfo.PathId}, " +
                    $"Name='{GetAssetName(atvf)}'.");
            }

            if (targetInfo == null ||
                targetFile == null ||
                targetInfo.TypeId != VideoClipTypeId)
            {
                DisplayStr(
                    "[VIDEO] Selected target is not a VideoClip (TypeID=329).");
                return false;
            }

            try
            {
                if (atvf == null ||
                    atvf.IsDummy ||
                    !ReferenceEquals(targetFile, assetInst))
                {
                    atvf =
                        am.GetBaseField(
                            targetFile,
                            targetInfo);
                }
            }
            catch (Exception ex)
            {
                DisplayStr(
                    $"[VIDEO] Failed reading VideoClip PID={targetInfo.PathId}: " +
                    $"{ex.GetType().Name}: {ex.Message}");
                DebugStr(ex.ToString());
                return false;
            }

            if (atvf == null ||
                atvf.IsDummy)
            {
                DisplayStr(
                    $"[VIDEO] VideoClip PID={targetInfo.PathId} " +
                    "returned a null/dummy BaseField.");
                return false;
            }

            AssetsTools.NET.AssetTypeValueField externalResources;

            try
            {
                externalResources =
                    atvf["m_ExternalResources"];
            }
            catch (Exception ex)
            {
                DisplayStr(
                    $"[VIDEO] Could not access m_ExternalResources " +
                    $"for PID={targetInfo.PathId}: {ex.Message}");
                return false;
            }

            if (externalResources == null ||
                externalResources.IsDummy)
            {
                DisplayStr(
                    $"[VIDEO] VideoClip PID={targetInfo.PathId} " +
                    "does not contain a usable m_ExternalResources field.");
                return false;
            }

            string source;
            long offset;
            long oldSize;

            try
            {
                source =
                    externalResources["m_Source"].AsString;

                offset =
                    externalResources["m_Offset"].AsLong;

                oldSize =
                    externalResources["m_Size"].AsLong;
            }
            catch (Exception ex)
            {
                DisplayStr(
                    $"[VIDEO] Could not read m_Source/m_Offset/m_Size " +
                    $"for PID={targetInfo.PathId}: {ex.Message}");
                return false;
            }

            if (string.IsNullOrWhiteSpace(source))
            {
                DisplayStr(
                    $"[VIDEO] VideoClip PID={targetInfo.PathId} has empty " +
                    "m_ExternalResources.m_Source.");
                return false;
            }

            if (offset != 0)
            {
                DisplayStr(
                    $"[VIDEO] VideoClip PID={targetInfo.PathId} has " +
                    $"m_Offset={offset}. VIDEOCLIP_AS_RESOURCE requires " +
                    "m_Offset=0 because the entire resource entry is replaced.");
                return false;
            }

            resourceEntryName =
                ResolveResourceEntryNameFromSource(
                    bundleInst,
                    source);

            if (string.IsNullOrWhiteSpace(resourceEntryName))
            {
                DisplayStr(
                    $"[VIDEO] Resource entry could not be resolved from " +
                    $"m_Source='{source}'.");
                return false;
            }

            DebugStr(
                $"[VIDEO] External resource resolved: " +
                $"source='{source}', " +
                $"entry='{resourceEntryName}', " +
                $"oldSize={oldSize}, " +
                $"newSize={replacementLength}.");

            try
            {
                originalSerializedData =
                    atvf.WriteToByteArray();
            }
            catch (Exception ex)
            {
                DisplayStr(
                    $"[VIDEO] Could not serialize original VideoClip " +
                    $"PID={targetInfo.PathId}: {ex.Message}");
                return false;
            }

            externalResources["m_Size"].AsLong =
                replacementLength;

            try
            {
                rawReplacementData =
                    atvf.WriteToByteArray();
            }
            catch (Exception ex)
            {
                DisplayStr(
                    $"[VIDEO] Could not serialize modified VideoClip " +
                    $"PID={targetInfo.PathId}: {ex.Message}");
                return false;
            }

            if (rawReplacementData.Length == 0)
            {
                DisplayStr(
                    $"[VIDEO] Modified VideoClip PID={targetInfo.PathId} " +
                    "serialized to zero bytes.");
                return false;
            }

            assetInst = targetFile;
            afie = targetInfo;
            assetfile_name = targetFile.name;

            DebugStr(
                $"[VIDEO] VideoClip import prepared: " +
                $"PID={targetInfo.PathId}, " +
                $"TypeID={targetInfo.TypeId}, " +
                $"oldSerializedBytes={originalSerializedData.Length}, " +
                $"newSerializedBytes={rawReplacementData.Length}, " +
                $"m_Size={replacementLength}, " +
                $"resource='{resourceEntryName}'.");

            return true;
        }

        private static void SaveAssetBundleVideoClipAsResource(
            AssetsTools.NET.AssetTypeValueField modifiedBaseField,
            AssetFileInfo afie,
            AssetsFileInstance assetInst,
            BundleFileInstance bundleInst,
            string assetfile_name,
            string tempBundle,
            string inputFile)
        {
            if (modifiedBaseField == null ||
                modifiedBaseField.IsDummy)
            {
                throw new InvalidDataException(
                    "VideoClip BaseField is null/dummy.");
            }

            if (afie == null ||
                afie.TypeId != VideoClipTypeId)
            {
                throw new InvalidDataException(
                    $"VIDEOCLIP_AS_RESOURCE requires TypeID={VideoClipTypeId}, " +
                    $"received TypeID={afie?.TypeId.ToString() ?? "<null>"}.");
            }

            if (assetInst == null ||
                bundleInst == null)
            {
                throw new InvalidDataException(
                    "Asset/bundle state is null.");
            }

            if (!File.Exists(inputFile))
            {
                throw new FileNotFoundException(
                    "Video replacement file does not exist.",
                    inputFile);
            }

            long replacementLength =
                new FileInfo(inputFile).Length;

            if (replacementLength <= 0)
            {
                throw new InvalidDataException(
                    "Video replacement file is empty.");
            }

            if (replacementLength > int.MaxValue)
            {
                throw new InvalidDataException(
                    $"Video replacement file is too large for the current " +
                    $"bundle directory API: {replacementLength} bytes.");
            }

            AssetsTools.NET.AssetTypeValueField externalResources =
                modifiedBaseField["m_ExternalResources"];

            if (externalResources == null ||
                externalResources.IsDummy)
            {
                throw new InvalidDataException(
                    "VideoClip does not expose a valid m_ExternalResources field.");
            }

            string source =
                externalResources["m_Source"].AsString;

            long offset =
                externalResources["m_Offset"].AsLong;

            long size =
                externalResources["m_Size"].AsLong;

            if (string.IsNullOrWhiteSpace(source))
            {
                throw new InvalidDataException(
                    "VideoClip m_Source is empty.");
            }

            if (offset != 0)
            {
                throw new InvalidDataException(
                    $"VideoClip m_Offset={offset}; expected 0.");
            }

            if (size != replacementLength)
            {
                throw new InvalidDataException(
                    $"VideoClip m_Size={size} does not match MP4 length " +
                    $"{replacementLength}.");
            }

            string resourceEntryName =
                ResolveResourceEntryNameFromSource(
                    bundleInst,
                    source);

            if (string.IsNullOrWhiteSpace(resourceEntryName))
            {
                throw new InvalidDataException(
                    $"Bundle resource entry not found for m_Source='{source}'.");
            }

            int resourceIndex =
                bundleInst.file.GetFileIndex(resourceEntryName);

            if (resourceIndex < 0)
            {
                throw new InvalidDataException(
                    $"Bundle resource entry not found: '{resourceEntryName}'.");
            }

            if (bundleInst.file.IsAssetsFile(resourceIndex))
            {
                throw new InvalidDataException(
                    $"Resolved resource entry '{resourceEntryName}' is an assets file; " +
                    "expected a raw .resource entry.");
            }

            int assetIndex =
                bundleInst.file.GetFileIndex(assetfile_name);

            if (assetIndex < 0)
            {
                throw new InvalidDataException(
                    $"Bundle assets entry not found: '{assetfile_name}'.");
            }

            // ------------------------------------------------------------
            // 1) Install the modified VideoClip into the AssetsFile.
            //
            // Use the same API already used by the existing project.
            // This uses the same SetNewData APIs already used by the project.
            // ------------------------------------------------------------

            byte[] replacementData =
                modifiedBaseField.WriteToByteArray();

            if (replacementData == null ||
                replacementData.Length == 0)
            {
                throw new InvalidDataException(
                    "Modified VideoClip serialized to an empty payload.");
            }

            afie.SetNewData(
                replacementData);

            if (afie.Replacer == null)
            {
                throw new InvalidDataException(
                    $"SetNewData did not create a replacer for VideoClip " +
                    $"PID={afie.PathId}.");
            }

            byte[] newAssetData;

            using (var stream =
                   new MemoryStream())
            using (var writer =
                   new AssetsFileWriter(stream))
            {
                assetInst.file.Write(
                    writer);

                newAssetData =
                    stream.ToArray();
            }

            if (newAssetData.Length == 0)
            {
                throw new InvalidDataException(
                    $"Serialized assets file '{assetfile_name}' is empty.");
            }

            // ------------------------------------------------------------
            // 2) Replace the assets entry in the bundle.
            // ------------------------------------------------------------

            bundleInst.file
                .BlockAndDirInfo
                .DirectoryInfos[assetIndex]
                .SetNewData(
                    newAssetData);

            // ------------------------------------------------------------
            // 3) Replace the ENTIRE external .resource entry with the MP4.
            //
            // The current project API exposes DirectoryInfo.SetNewData(byte[]),
            // so the resource is loaded as raw bytes and assigned directly.
            // This preserves the original bundle entry name while changing only
            // its payload.
            // ------------------------------------------------------------

            byte[] replacementResourceData =
                File.ReadAllBytes(inputFile);

            if (replacementResourceData.Length != replacementLength)
            {
                throw new InvalidDataException(
                    $"MP4 size changed while reading: " +
                    $"expected={replacementLength}, " +
                    $"actual={replacementResourceData.Length}.");
            }

            bundleInst.file
                .BlockAndDirInfo
                .DirectoryInfos[resourceIndex]
                .SetNewData(
                    replacementResourceData);

            DebugStr(
                $"[VIDEO][SAVE] VideoClip entry updated: " +
                $"asset='{assetfile_name}', " +
                $"PID={afie.PathId}, " +
                $"serializedBytes={replacementData.Length}, " +
                $"assetsEntryBytes={newAssetData.Length}.");

            DebugStr(
                $"[VIDEO][SAVE] Resource entry updated: " +
                $"name='{resourceEntryName}', " +
                $"bytes={replacementResourceData.Length}, " +
                $"SHA256={Sha256Hex(replacementResourceData)}.");

            // ------------------------------------------------------------
            // 4) Write the bundle with the project's existing API.
            // ------------------------------------------------------------

            DeleteFileIfExists(
                tempBundle);

            using (var fileStream =
                   new FileStream(
                       tempBundle,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None))
            using (var bunWriter =
                   new AssetsFileWriter(fileStream))
            {
                bundleInst.file.Write(
                    bunWriter);
            }

            DebugStr(
                $"[VIDEO][SAVE] Stage1 bundle write completed: '{tempBundle}'.");
        }

        private static string ResolveResourceEntryNameFromSource(
            BundleFileInstance bundleInst,
            string source)
        {
            if (bundleInst == null ||
                bundleInst.file == null ||
                string.IsNullOrWhiteSpace(source))
            {
                return string.Empty;
            }

            string requested =
                GetResourceFileNameFromSource(source);

            if (string.IsNullOrWhiteSpace(requested))
                return string.Empty;

            int directIndex =
                bundleInst.file.GetFileIndex(requested);

            if (directIndex >= 0)
            {
                return bundleInst.file
                    .BlockAndDirInfo
                    .DirectoryInfos[directIndex]
                    .Name;
            }

            foreach (var directoryInfo
                     in bundleInst.file.BlockAndDirInfo.DirectoryInfos)
            {
                string entryName =
                    directoryInfo.Name ?? string.Empty;

                if (string.Equals(
                        entryName,
                        requested,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return entryName;
                }

                string normalizedEntry =
                    entryName.Replace('\\', '/');

                int slash =
                    normalizedEntry.LastIndexOf('/');

                string entryFileName =
                    slash >= 0
                        ? normalizedEntry[(slash + 1)..]
                        : normalizedEntry;

                if (string.Equals(
                        entryFileName,
                        requested,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return entryName;
                }
            }

            return string.Empty;
        }

        private static string GetResourceFileNameFromSource(
            string source)
        {
            string normalized =
                source.Replace('\\', '/').Trim();

            string[] parts =
                normalized.Split(
                    '/',
                    StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 0)
                return string.Empty;

            string fileName =
                parts[^1].Trim();

            if (fileName.StartsWith(
                    "archive:",
                    StringComparison.OrdinalIgnoreCase))
            {
                fileName =
                    fileName["archive:".Length..];
            }

            return fileName.Trim();
        }

        private static void ValidateVideoClipAsResourceFinal(
            AssetsManager am,
            BundleFileInstance bundle,
            AssetsFileInstance inst,
            AssetFileInfo targetInfo,
            AssetsTools.NET.AssetTypeValueField targetField,
            string replacementMp4Path)
        {
            if (targetInfo == null ||
                targetInfo.TypeId != VideoClipTypeId)
            {
                throw new InvalidDataException(
                    "Final VideoClip validation received a non-VideoClip target.");
            }

            if (targetField == null ||
                targetField.IsDummy)
            {
                throw new InvalidDataException(
                    "Final VideoClip validation requires a valid BaseField.");
            }

            if (!File.Exists(replacementMp4Path))
            {
                throw new FileNotFoundException(
                    "Original MP4 replacement file is missing during validation.",
                    replacementMp4Path);
            }

            AssetsTools.NET.AssetTypeValueField externalResources =
                targetField["m_ExternalResources"];

            if (externalResources == null ||
                externalResources.IsDummy)
            {
                throw new InvalidDataException(
                    "Final VideoClip is missing m_ExternalResources.");
            }

            string source =
                externalResources["m_Source"].AsString;

            long offset =
                externalResources["m_Offset"].AsLong;

            long size =
                externalResources["m_Size"].AsLong;

            long expectedSize =
                new FileInfo(replacementMp4Path).Length;

            if (string.IsNullOrWhiteSpace(source))
            {
                throw new InvalidDataException(
                    "Final VideoClip m_Source is empty.");
            }

            if (offset != 0)
            {
                throw new InvalidDataException(
                    $"Final VideoClip m_Offset changed to {offset}; expected 0.");
            }

            if (size != expectedSize)
            {
                throw new InvalidDataException(
                    $"Final VideoClip m_Size mismatch: " +
                    $"asset={size}, mp4={expectedSize}.");
            }

            string resourceEntryName =
                ResolveResourceEntryNameFromSource(
                    bundle,
                    source);

            if (string.IsNullOrWhiteSpace(resourceEntryName))
            {
                throw new InvalidDataException(
                    $"Final bundle resource entry could not be resolved " +
                    $"from m_Source='{source}'.");
            }

            int resourceIndex =
                bundle.file.GetFileIndex(resourceEntryName);

            if (resourceIndex < 0)
            {
                throw new InvalidDataException(
                    $"Final bundle resource entry is missing: " +
                    $"'{resourceEntryName}'.");
            }

            if (bundle.file.IsAssetsFile(resourceIndex))
            {
                throw new InvalidDataException(
                    $"Final resource entry '{resourceEntryName}' is serialized; " +
                    "expected a raw resource file.");
            }

            bundle.file.GetFileRange(
                resourceIndex,
                out long resourceOffset,
                out long resourceLength);

            if (resourceLength != expectedSize)
            {
                throw new InvalidDataException(
                    $"Final .resource length mismatch: " +
                    $"entry={resourceLength}, mp4={expectedSize}.");
            }

            AssetsFileReader reader =
                bundle.file.DataReader;

            if (reader == null)
            {
                throw new InvalidDataException(
                    "Final bundle DataReader is null.");
            }

            string expectedSha =
                Sha256File(replacementMp4Path);

            string actualSha =
                ComputeBundleEntrySha256(
                    reader,
                    resourceOffset,
                    resourceLength);

            DebugStr(
                $"[VIDEO][CHECK] Final resource entry: " +
                $"name='{resourceEntryName}', " +
                $"bytes={resourceLength}, " +
                $"SHA256={actualSha}, " +
                $"expectedSHA256={expectedSha}.");

            if (!string.Equals(
                    actualSha,
                    expectedSha,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Final .resource payload does not match the MP4. " +
                    $"SHA256={actualSha}, expected={expectedSha}.");
            }

            DebugStr(
                "[VIDEO][CHECK] VideoClip m_Size and raw .resource payload " +
                "match the replacement MP4.");
        }

        private static string ComputeBundleEntrySha256(
            AssetsFileReader reader,
            long offset,
            long length)
        {
            if (reader == null)
                throw new ArgumentNullException(nameof(reader));

            if (offset < 0 || length < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(offset),
                    $"Invalid bundle entry range: offset={offset}, length={length}.");
            }

            const int BufferSize = 1024 * 1024;

            byte[] buffer =
                new byte[BufferSize];

            using (SHA256 sha = SHA256.Create())
            {
                reader.Position = offset;

                long remaining = length;

                while (remaining > 0)
                {
                    int requested =
                        (int)Math.Min(
                            buffer.Length,
                            remaining);

                    int read =
                        reader.Read(
                            buffer,
                            0,
                            requested);

                    if (read <= 0)
                    {
                        throw new EndOfStreamException(
                            "Unexpected end of bundle entry while hashing.");
                    }

                    sha.TransformBlock(
                        buffer,
                        0,
                        read,
                        null,
                        0);

                    remaining -= read;
                }

                sha.TransformFinalBlock(
                    Array.Empty<byte>(),
                    0,
                    0);

                return Convert.ToHexString(
                    sha.Hash ?? Array.Empty<byte>());
            }
        }
    }
}
