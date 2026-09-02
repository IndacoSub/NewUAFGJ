using AssetsTools.NET;
using AssetsTools.NET.Extra;
using System;
using System.IO;
using System.Linq;
using System.Text;

namespace UAFGJ
{
    partial class Program
    {
        private static bool TryParsePathId(
            string specificPathId,
            out long pathId)
        {
            if (string.IsNullOrWhiteSpace(specificPathId))
            {
                pathId = 0;
                return false;
            }

            return long.TryParse(
                specificPathId,
                out pathId);
        }

        private static string GetAssetName(
            AssetsTools.NET.AssetTypeValueField field)
        {
            try
            {
                if (field == null ||
                    field.IsDummy)
                {
                    return "";
                }

                var nameField = field["m_Name"];

                if (nameField == null ||
                    nameField.IsDummy)
                {
                    return "";
                }

                return nameField.AsString ?? "";
            }
            catch
            {
                return "";
            }
        }

        private static bool ImportTextAssetRaw(
            string inputFile,
            AssetsTools.NET.AssetTypeValueField baseField,
            AssetFileInfo afie,
            string fileKind,
            out byte[] originalSerializedData,
            out byte[] replacementData)
        {

            originalSerializedData =
                Array.Empty<byte>();

            replacementData =
                Array.Empty<byte>();

            if (baseField == null ||
                baseField.IsDummy)
            {
                DebugStr(
                    "[TXT] TextAsset BaseField is null/dummy.");

                return false;
            }

            // --------------------------------------------------------
            // Preserve the original serialized asset.
            // --------------------------------------------------------

            try
            {
                originalSerializedData =
                    baseField.WriteToByteArray();
            }
            catch (Exception ex)
            {
                DebugStr(
                    $"[TXT] Could not serialize original TextAsset " +
                    $"PID={afie.PathId}: {ex}");

                return false;
            }

            DebugStr(
                $"[TXT] Original TextAsset serialized size=" +
                $"{originalSerializedData.Length} " +
                $"SHA256={Sha256Hex(originalSerializedData)}");

            // --------------------------------------------------------
            // TextAsset structure:
            //
            //   m_Name
            //   m_Script
            // --------------------------------------------------------

            AssetsTools.NET.AssetTypeValueField nameField;
            AssetsTools.NET.AssetTypeValueField scriptField;

            try
            {
                nameField = baseField["m_Name"];
                scriptField = baseField["m_Script"];
            }
            catch (Exception ex)
            {
                DebugStr(
                    $"[TXT] Could not access TextAsset fields " +
                    $"for PID={afie.PathId}: {ex}");

                return false;
            }

            if (nameField == null ||
                nameField.IsDummy ||
                scriptField == null ||
                scriptField.IsDummy)
            {
                DebugStr(
                    $"[TXT] TextAsset PID={afie.PathId} " +
                    "does not have valid m_Name/m_Script fields.");

                return false;
            }

            // --------------------------------------------------------
            // Read input.
            // --------------------------------------------------------

            string text;

            try
            {
                text = File.ReadAllText(
                    inputFile,
                    new UTF8Encoding(
                        encoderShouldEmitUTF8Identifier: false,
                        throwOnInvalidBytes: false));
            }
            catch (Exception ex)
            {
                DebugStr(
                    $"[TXT] Could not read TextAsset source " +
                    $"'{inputFile}': {ex}");

                return false;
            }

            // Remove BOM.
            if (text.Length > 0 &&
                text[0] == '\uFEFF')
            {
                text = text.Substring(1);
            }

            // --------------------------------------------------------
            // Detect UABEA Export Dump.
            //
            // Supported:
            //
            //   0 TextAsset Base
            //    1 string m_Name = "English"
            //    1 string m_Script = "...."
            //
            // Otherwise the file is treated as raw TextAsset text.
            // --------------------------------------------------------

            bool looksLikeExportDump =
                text.StartsWith(
                    "0 TextAsset Base",
                    StringComparison.Ordinal);

            if (looksLikeExportDump)
            {
                DebugStr(
                    "[TXT] Detected UABEA TextAsset Export Dump.");

                string[] lines =
                    text.Replace("\r\n", "\n")
                        .Replace("\r", "\n")
                        .Split('\n');

                string dumpedName = null;
                string dumpedScript = null;

                foreach (string rawLine in lines)
                {
                    string line = rawLine;

                    // ----------------------------------------------------
                    // m_Name
                    // ----------------------------------------------------

                    const string namePrefix =
                        " 1 string m_Name = ";

                    if (line.StartsWith(
                        namePrefix,
                        StringComparison.Ordinal))
                    {
                        string value =
                            line.Substring(
                                namePrefix.Length).Trim();

                        dumpedName =
                            ParseDumpString(value);

                        continue;
                    }

                    // ----------------------------------------------------
                    // m_Script
                    // ----------------------------------------------------

                    const string scriptPrefix =
                        " 1 string m_Script = ";

                    if (line.StartsWith(
                        scriptPrefix,
                        StringComparison.Ordinal))
                    {
                        string value =
                            line.Substring(
                                scriptPrefix.Length).Trim();

                        dumpedScript =
                            ParseDumpString(value);

                        continue;
                    }
                }

                if (dumpedScript == null)
                {
                    DisplayStr(
                        "[TXT] File looks like a UABEA TextAsset Export Dump " +
                        "but m_Script could not be parsed.");

                    return false;
                }

                if (dumpedName != null)
                {
                    try
                    {
                        nameField.AsString = dumpedName;

                        DebugStr(
                            $"[TXT] Imported m_Name from dump: '{dumpedName}'");
                    }
                    catch (Exception ex)
                    {
                        DebugStr(
                            $"[TXT] Failed assigning m_Name from dump: {ex}");

                        return false;
                    }
                }

                text = dumpedScript;

                DebugStr(
                    $"[TXT] Extracted m_Script from UABEA dump. " +
                    $"Length={text.Length}");
            }
            else
            {
                DebugStr(
                    "[TXT] Input is raw TextAsset text; no dump wrapper detected.");
            }

            // --------------------------------------------------------
            // Assign m_Script.
            // --------------------------------------------------------

            DebugStr(
                $"[TXT] Original TextAsset m_Script length=" +
                $"{scriptField.AsString?.Length ?? 0}");

            DebugStr(
                $"[TXT] New TextAsset m_Script length=" +
                $"{text.Length}");

            try
            {
                scriptField.AsString = text;
            }
            catch (Exception ex)
            {
                DebugStr(
                    $"[TXT] Failed assigning TextAsset.m_Script " +
                    $"for PID={afie.PathId}: {ex}");

                return false;
            }

            // --------------------------------------------------------
            // Serialize modified asset.
            // --------------------------------------------------------

            try
            {
                replacementData =
                    baseField.WriteToByteArray();
            }
            catch (Exception ex)
            {
                DebugStr(
                    $"[TXT] Could not serialize modified TextAsset " +
                    $"PID={afie.PathId}: {ex}");

                return false;
            }

            if (replacementData.Length == 0)
            {
                DebugStr(
                    $"[TXT] Modified TextAsset PID={afie.PathId} " +
                    "serialized to zero bytes.");

                return false;
            }

            DebugStr(
                $"[TXT] Modified TextAsset serialized size=" +
                $"{replacementData.Length} " +
                $"SHA256={Sha256Hex(replacementData)}");

            return true;
        }

        private static bool FindTXTFile(
            string inputFile,
            ref AssetsFileInstance assetInst,
            ref AssetFileInfo afie,
            ref AssetsTools.NET.AssetTypeValueField atvf,
            ref AssetsManager am,
            string asset,
            string assetfile_name,
            string specific_pathid,
            string fileKind,
            out byte[] rawReplacementData,
            out byte[] originalSerializedData)
        {
            rawReplacementData =
                Array.Empty<byte>();

            originalSerializedData =
                Array.Empty<byte>();

            // ============================================================
            // BASIC VALIDATION
            // ============================================================

            if (assetInst == null)
            {
                DebugStr(
                    "[TXT] AssetsFileInstance is null.");

                return false;
            }

            if (am == null)
            {
                DebugStr(
                    "[TXT] AssetsManager is null.");

                return false;
            }

            if (!File.Exists(inputFile))
            {
                DebugStr(
                    $"[TXT] Replacement file does not exist: {inputFile}");

                return false;
            }

            // ============================================================
            // DEFAULT KIND
            // ============================================================

            if (string.IsNullOrWhiteSpace(fileKind))
            {
                fileKind =
                    "MONOBEHAVIOUR_FULL_CHECKED";

                DebugStr(
                    "[TXT] fileKind is empty; " +
                    "defaulting to MONOBEHAVIOUR_FULL_CHECKED.");
            }

            // ============================================================
            // VALID KIND
            // ============================================================

            switch (fileKind)
            {
                case "MONOBEHAVIOUR_TEXT":
                case "MONOBEHAVIOUR_TEXT_CHECKED":
                case "MONOBEHAVIOUR_FONT":
                case "MONOBEHAVIOUR_FONT_CHECKED":
                case "MONOBEHAVIOUR_FULL":
                case "MONOBEHAVIOUR_FULL_CHECKED":
                    break;

                default:
                    DisplayStr(
                        $"[TXT] Unsupported fileKind '{fileKind}'.");

                    return false;
            }

            DebugStr(
                $"[TXT] Requested fileKind='{fileKind}'.");

            // ============================================================
            // PATH ID
            // ============================================================

            long wantedPathId;

            bool hasWantedPathId =
                TryParsePathId(
                    specific_pathid,
                    out wantedPathId);

            // ============================================================
            // ============================================================
            // PATH ID SEARCH
            //
            // PID supplied:
            // PID has absolute priority.
            //
            // IMPORTANT:
            //
            // For MONOBEHAVIOUR_TEXT / TEXT_CHECKED we MUST NOT call
            // GetBaseField() before checking TypeID.
            //
            // Some TypeID 114 objects have a dummy BaseField even though
            // their raw serialized payload is perfectly usable.
            // ============================================================
            // ============================================================

            if (hasWantedPathId)
            {
                DebugStr(
                    $"[TXT] Searching assets in '{assetfile_name}' " +
                    $"for exact PID {wantedPathId}");

                AssetFileInfo exactMatch =
                    assetInst.file.AssetInfos.FirstOrDefault(
                        a => a.PathId == wantedPathId);

                if (exactMatch == null)
                {
                    DisplayStr(
                        $"[TXT] Could not find any asset " +
                        $"with path ID {wantedPathId}.");

                    return false;
                }

                afie =
                    exactMatch;

                DebugStr(
                    $"[TXT] Exact PID found: " +
                    $"PID={afie.PathId}, " +
                    $"TypeID={afie.TypeId}");

                // ========================================================
                // TEXTASSET - TYPEID 49
                //
                // TextAsset still uses the normal BaseField importer.
                // ========================================================

                if (afie.TypeId == 49)
                {
                    DebugStr(
                        "[TXT] Target is TextAsset (TypeID=49).");

                    AssetsTools.NET.AssetTypeValueField textAssetField;

                    try
                    {
                        textAssetField =
                            am.GetBaseField(
                                assetInst,
                                afie);
                    }
                    catch (Exception ex)
                    {
                        DisplayStr(
                            $"[TXT] Failed reading TextAsset PID " +
                            $"{wantedPathId}: " +
                            $"{ex.GetType().Name}: {ex.Message}");

                        DebugStr(
                            ex.ToString());

                        return false;
                    }

                    if (textAssetField == null ||
                        textAssetField.IsDummy)
                    {
                        DisplayStr(
                            $"[TXT] TextAsset PID {wantedPathId} " +
                            "returned a null/dummy BaseField.");

                        return false;
                    }

                    atvf =
                        textAssetField;

                    string textAssetName =
                        GetAssetName(
                            textAssetField);

                    DebugStr(
                        $"[TXT] Found TextAsset by exact PID: " +
                        $"PID={afie.PathId}, " +
                        $"Name='{textAssetName}', " +
                        $"TypeID={afie.TypeId}");

                    return ImportTextAssetRaw(
                        inputFile,
                        atvf,
                        afie,
                        fileKind,
                        out originalSerializedData,
                        out rawReplacementData);
                }

                // ========================================================
                // MONOBEHAVIOUR - TYPEID 114
                // ========================================================

                if (afie.TypeId == 114)
                {
                    DebugStr(
                        $"[TXT] Target is MonoBehaviour " +
                        $"(TypeID=114), PID={afie.PathId}.");

                    // ----------------------------------------------------
                    // TEXT-ONLY RAW PATH
                    //
                    // NO GetBaseField()
                    //
                    // This is the critical fix for TypeTree-dummy
                    // MonoBehaviours.
                    // ----------------------------------------------------

                    if (fileKind == "MONOBEHAVIOUR_TEXT")
                    {
                        DebugStr(
                            "[TXT] Kind=MONOBEHAVIOUR_TEXT. " +
                            "Using RAW m_text replacement.");

                        AssetsTools.NET.AssetTypeValueField modifiedBaseField;

                        byte[] monoOriginalData;

                        bool success =
                            ImportMonoBehaviourTextOnly(
                                inputFile,
                                am,
                                afie,
                                assetInst,
                                assetfile_name,
                                out modifiedBaseField,
                                out rawReplacementData,
                                out monoOriginalData);

                        if (!success)
                        {
                            return false;
                        }

                        // Raw TEXT importer deliberately returns null BaseField.
                        atvf =
                            modifiedBaseField;

                        originalSerializedData =
                            monoOriginalData;

                        DebugStr(
                            $"[TXT] MONOBEHAVIOUR_TEXT import succeeded. " +
                            $"PID={afie.PathId}");

                        return true;
                    }

                    // ----------------------------------------------------
                    // TEXT-ONLY CHECKED
                    //
                    // Same RAW replacement path.
                    //
                    // Do NOT require a valid BaseField here.
                    // ----------------------------------------------------

                    if (fileKind == "MONOBEHAVIOUR_TEXT_CHECKED")
                    {
                        DebugStr(
                            "[TXT] Kind=MONOBEHAVIOUR_TEXT_CHECKED. " +
                            "Using RAW m_text replacement.");

                        AssetsTools.NET.AssetTypeValueField modifiedBaseField;

                        byte[] monoOriginalData;

                        bool success =
                            ImportMonoBehaviourTextOnlyChecked(
                                inputFile,
                                am,
                                afie,
                                assetInst,
                                assetfile_name,
                                out modifiedBaseField,
                                out rawReplacementData,
                                out monoOriginalData);

                        if (!success)
                        {
                            return false;
                        }

                        // Raw TEXT importer deliberately returns null BaseField.
                        atvf =
                            modifiedBaseField;

                        originalSerializedData =
                            monoOriginalData;

                        DebugStr(
                            $"[TXT] MONOBEHAVIOUR_TEXT_CHECKED import succeeded. " +
                            $"PID={afie.PathId}");

                        return true;
                    }

                    // ----------------------------------------------------
                    // FULL MODES
                    //
                    // These require a real BaseField / TypeTree.
                    // ----------------------------------------------------

                    AssetsTools.NET.AssetTypeValueField monoBaseField;

                    try
                    {
                        monoBaseField =
                            am.GetBaseField(
                                assetInst,
                                afie);
                    }
                    catch (Exception ex)
                    {
                        DisplayStr(
                            $"[TXT] Failed reading MonoBehaviour PID " +
                            $"{wantedPathId}: " +
                            $"{ex.GetType().Name}: {ex.Message}");

                        DebugStr(
                            ex.ToString());

                        return false;
                    }

                    if (monoBaseField == null ||
                        monoBaseField.IsDummy)
                    {
                        DisplayStr(
                            $"[TXT] MonoBehaviour PID {wantedPathId} " +
                            "returned a null/dummy BaseField. " +
                            $"fileKind='{fileKind}' requires the full TypeTree.");

                        return false;
                    }

                    atvf =
                        monoBaseField;

                    string monoName =
                        GetAssetName(
                            monoBaseField);

                    DebugStr(
                        $"[TXT] Found MonoBehaviour by exact PID: " +
                        $"PID={afie.PathId}, " +
                        $"Name='{monoName}', " +
                        $"TypeID={afie.TypeId}");

                    ushort monoId;

                    try
                    {
                        monoId =
                            assetInst.file.GetScriptIndex(
                                afie);
                    }
                    catch (Exception ex)
                    {
                        DebugStr(
                            $"[TXT] Could not read MonoScriptIndex " +
                            $"for PID={afie.PathId}: {ex}");

                        monoId = 0;
                    }

                    DebugStr(
                        $"[TXT] MonoScriptIndex={monoId} " +
                        $"(0x{monoId:X4}).");

                    // ====================================================
                    // FULL / FONT
                    // ====================================================

                    if (fileKind == "MONOBEHAVIOUR_FONT" ||
                        fileKind == "MONOBEHAVIOUR_FULL")
                    {
                        DebugStr(
                            $"[TXT] Kind={fileKind}. " +
                            "Using FULL unchecked MonoBehaviour import.");

                        AssetsTools.NET.AssetTypeValueField modifiedBaseField;

                        byte[] monoOriginalData;

                        bool success =
                            ImportMonoBehaviourFull(
                                inputFile,
                                am,
                                afie,
                                assetInst,
                                assetfile_name,
                                out modifiedBaseField,
                                out rawReplacementData,
                                out monoOriginalData);

                        if (!success)
                        {
                            return false;
                        }

                        atvf =
                            modifiedBaseField;

                        originalSerializedData =
                            monoOriginalData;

                        DebugStr(
                            $"[TXT] {fileKind} import succeeded. " +
                            $"PID={afie.PathId}");

                        return true;
                    }

                    // ====================================================
                    // FULL CHECKED / FONT CHECKED
                    // ====================================================

                    if (fileKind == "MONOBEHAVIOUR_FONT_CHECKED" ||
                        fileKind == "MONOBEHAVIOUR_FULL_CHECKED")
                    {
                        DebugStr(
                            $"[TXT] Kind={fileKind}. " +
                            "Using FULL checked MonoBehaviour import.");

                        AssetsTools.NET.AssetTypeValueField modifiedBaseField;

                        byte[] monoOriginalData;

                        bool success =
                            ImportMonoBehaviourFullChecked(
                                inputFile,
                                am,
                                afie,
                                assetInst,
                                assetfile_name,
                                out modifiedBaseField,
                                out rawReplacementData,
                                out monoOriginalData);

                        if (!success)
                        {
                            return false;
                        }

                        atvf =
                            modifiedBaseField;

                        originalSerializedData =
                            monoOriginalData;

                        DebugStr(
                            $"[TXT] {fileKind} import succeeded. " +
                            $"PID={afie.PathId}");

                        return true;
                    }

                    DisplayStr(
                        $"[TXT] Unsupported MonoBehaviour fileKind " +
                        $"'{fileKind}'.");

                    return false;
                }

                // ========================================================
                // UNSUPPORTED TYPE
                // ========================================================

                DisplayStr(
                    $"[TXT] Asset PID={afie.PathId} has unsupported " +
                    $"TypeID={afie.TypeId}. " +
                    "TXT import supports TextAsset (49) and " +
                    "MonoBehaviour (114).");

                return false;
            }

            // ============================================================
            // ============================================================
            // FALLBACK BY NAME
            //
            // Only used when no valid PID was supplied.
            //
            // For name lookup we necessarily need a BaseField because
            // m_Name is read from the serialized object structure.
            //
            // Therefore a TypeTree-dummy MonoBehaviour cannot be found
            // by name through this fallback. Such an object should be
            // addressed with its exact PID.
            // ============================================================
            // ============================================================

            string targetName =
                Path.GetFileNameWithoutExtension(
                    inputFile).Trim();

            DebugStr(
                $"[TXT] No valid PathID supplied. " +
                $"Searching supported text assets in " +
                $"'{assetfile_name}' by name '{targetName}'.");

            int candidatesScanned = 0;

            foreach (var inf in assetInst.file.AssetInfos)
            {
                // Only supported TXT types.
                if (inf.TypeId != 49 &&
                    inf.TypeId != 114)
                {
                    continue;
                }

                candidatesScanned++;

                AssetsTools.NET.AssetTypeValueField candidate;

                try
                {
                    candidate =
                        am.GetBaseField(
                            assetInst,
                            inf);
                }
                catch (Exception ex)
                {
                    DebugStr(
                        $"[TXT] Failed reading candidate PID " +
                        $"{inf.PathId}: " +
                        $"{ex.GetType().Name}: {ex.Message}");

                    continue;
                }

                if (candidate == null ||
                    candidate.IsDummy)
                {
                    DebugStr(
                        $"[TXT] Candidate PID={inf.PathId} " +
                        "has null/dummy BaseField; skipping name lookup.");

                    continue;
                }

                string name =
                    GetAssetName(
                        candidate);

                DebugStr(
                    $"[TXT] Candidate #{candidatesScanned}: " +
                    $"Name='{name}', " +
                    $"PID={inf.PathId}, " +
                    $"TypeID={inf.TypeId}");

                if (!string.Equals(
                    name?.Trim(),
                    targetName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // ========================================================
                // FOUND BY NAME
                // ========================================================

                afie =
                    inf;

                atvf =
                    candidate;

                DebugStr(
                    $"[TXT] Found TXT target by name: " +
                    $"'{name}', " +
                    $"PID={afie.PathId}, " +
                    $"TypeID={afie.TypeId}");

                // ========================================================
                // TEXTASSET
                // ========================================================

                if (inf.TypeId == 49)
                {
                    DebugStr(
                        "[TXT] Target is TextAsset (TypeID=49); " +
                        "using existing TextAsset importer.");

                    return ImportTextAssetRaw(
                        inputFile,
                        atvf,
                        afie,
                        fileKind,
                        out originalSerializedData,
                        out rawReplacementData);
                }

                // ========================================================
                // MONOBEHAVIOUR
                // ========================================================

                if (inf.TypeId == 114)
                {
                    DebugStr(
                        $"[TXT] Target is MonoBehaviour " +
                        $"(TypeID=114), PID={afie.PathId}.");

                    ushort monoId;

                    try
                    {
                        monoId =
                            assetInst.file.GetScriptIndex(
                                afie);
                    }
                    catch (Exception ex)
                    {
                        DebugStr(
                            $"[TXT] Could not read MonoScriptIndex " +
                            $"for PID={afie.PathId}: {ex}");

                        monoId = 0;
                    }

                    DebugStr(
                        $"[TXT] MonoScriptIndex={monoId} " +
                        $"(0x{monoId:X4}).");

                    // ----------------------------------------------------
                    // TEXT
                    //
                    // Here the BaseField was only needed to FIND the
                    // object by m_Name.
                    //
                    // The actual import is still RAW.
                    // ----------------------------------------------------

                    if (fileKind == "MONOBEHAVIOUR_TEXT")
                    {
                        DebugStr(
                            "[TXT] Kind=MONOBEHAVIOUR_TEXT. " +
                            "Using RAW m_text replacement.");

                        AssetsTools.NET.AssetTypeValueField modifiedBaseField;

                        byte[] monoOriginalData;

                        bool success =
                            ImportMonoBehaviourTextOnly(
                                inputFile,
                                am,
                                afie,
                                assetInst,
                                assetfile_name,
                                out modifiedBaseField,
                                out rawReplacementData,
                                out monoOriginalData);

                        if (!success)
                        {
                            return false;
                        }

                        atvf =
                            modifiedBaseField;

                        originalSerializedData =
                            monoOriginalData;

                        return true;
                    }

                    // ----------------------------------------------------
                    // TEXT CHECKED
                    // ----------------------------------------------------

                    if (fileKind == "MONOBEHAVIOUR_TEXT_CHECKED")
                    {
                        DebugStr(
                            "[TXT] Kind=MONOBEHAVIOUR_TEXT_CHECKED. " +
                            "Using RAW m_text replacement.");

                        AssetsTools.NET.AssetTypeValueField modifiedBaseField;

                        byte[] monoOriginalData;

                        bool success =
                            ImportMonoBehaviourTextOnlyChecked(
                                inputFile,
                                am,
                                afie,
                                assetInst,
                                assetfile_name,
                                out modifiedBaseField,
                                out rawReplacementData,
                                out monoOriginalData);

                        if (!success)
                        {
                            return false;
                        }

                        atvf =
                            modifiedBaseField;

                        originalSerializedData =
                            monoOriginalData;

                        return true;
                    }

                    // ----------------------------------------------------
                    // FULL UNCHECKED
                    // ----------------------------------------------------

                    if (fileKind == "MONOBEHAVIOUR_FONT" ||
                        fileKind == "MONOBEHAVIOUR_FULL")
                    {
                        DebugStr(
                            $"[TXT] Kind={fileKind}. " +
                            "Using FULL unchecked MonoBehaviour import.");

                        AssetsTools.NET.AssetTypeValueField modifiedBaseField;

                        byte[] monoOriginalData;

                        bool success =
                            ImportMonoBehaviourFull(
                                inputFile,
                                am,
                                afie,
                                assetInst,
                                assetfile_name,
                                out modifiedBaseField,
                                out rawReplacementData,
                                out monoOriginalData);

                        if (!success)
                        {
                            return false;
                        }

                        atvf =
                            modifiedBaseField;

                        originalSerializedData =
                            monoOriginalData;

                        return true;
                    }

                    // ----------------------------------------------------
                    // FULL CHECKED
                    // ----------------------------------------------------

                    if (fileKind == "MONOBEHAVIOUR_FONT_CHECKED" ||
                        fileKind == "MONOBEHAVIOUR_FULL_CHECKED")
                    {
                        DebugStr(
                            $"[TXT] Kind={fileKind}. " +
                            "Using FULL checked MonoBehaviour import.");

                        AssetsTools.NET.AssetTypeValueField modifiedBaseField;

                        byte[] monoOriginalData;

                        bool success =
                            ImportMonoBehaviourFullChecked(
                                inputFile,
                                am,
                                afie,
                                assetInst,
                                assetfile_name,
                                out modifiedBaseField,
                                out rawReplacementData,
                                out monoOriginalData);

                        if (!success)
                        {
                            return false;
                        }

                        atvf =
                            modifiedBaseField;

                        originalSerializedData =
                            monoOriginalData;

                        return true;
                    }

                    DisplayStr(
                        $"[TXT] Unsupported MonoBehaviour fileKind " +
                        $"'{fileKind}'.");

                    return false;
                }

                DisplayStr(
                    $"[TXT] Found asset by name but TypeID={inf.TypeId} " +
                    "is unsupported.");

                return false;
            }

            // ============================================================
            // NOT FOUND
            // ============================================================

            DisplayStr(
                $"[TXT] Could not find supported text asset " +
                $"'{targetName}' in '{assetfile_name}'. " +
                $"Candidates scanned: {candidatesScanned}.");

            return false;
        }


        private static bool FindPNGFile(
            string inputFile,
            ref AssetFileInfo afie,
            ref AssetsFileInstance assetInst,
            ref AssetsTools.NET.AssetTypeValueField atvf,
            ref AssetsManager am,
            string asset,
            string assetfile_name,
            string specificPathId,
            string fileKind)
        {

            if (assetInst == null)
            {
                DisplayStr(
                    "[PNG] AssetsFileInstance is null.");

                return false;
            }

            if (am == null)
            {
                DisplayStr(
                    "[PNG] AssetsManager is null.");

                return false;
            }

            if (!File.Exists(inputFile))
            {
                DisplayStr(
                    $"[PNG] Replacement file does not exist: {inputFile}");

                return false;
            }

            string targetName =
                Path.GetFileNameWithoutExtension(
                    inputFile).Trim();

            long wantedPathId;

            bool hasWantedPathId =
                TryParsePathId(
                    specificPathId,
                    out wantedPathId);

            // ========================================================
            // PATH ID SEARCH
            //
            // If a PathID was supplied, search the Texture2D assets
            // by PID FIRST.
            //
            // Do NOT require the texture name to match the PNG name.
            // ========================================================

            if (hasWantedPathId)
            {
                DebugStr(
                    $"[PNG] Searching Texture2D assets in " +
                    $"'{assetfile_name}' for exact PID " +
                    $"{wantedPathId}");

                AssetFileInfo exactMatch =
                    assetInst.file.AssetInfos.FirstOrDefault(
                        a =>
                            a.PathId == wantedPathId &&
                            a.TypeId == (int)AssetClassID.Texture2D);

                if (exactMatch == null)
                {
                    DisplayStr(
                        $"[PNG] Could not find Texture2D " +
                        $"with path ID {wantedPathId}.");

                    return false;
                }

                try
                {
                    var candidate =
                        am.GetBaseField(
                            assetInst,
                            exactMatch);

                    if (candidate == null ||
                        candidate.IsDummy)
                    {
                        DisplayStr(
                            $"[PNG] Texture2D PID {wantedPathId} " +
                            "returned a null/dummy BaseField.");

                        return false;
                    }

                    afie = exactMatch;
                    atvf = candidate;

                    string name =
                        GetAssetName(candidate);

                    DebugStr(
                        $"[PNG] Found Texture2D by exact PID: " +
                        $"PID={exactMatch.PathId}, " +
                        $"Name='{name}', " +
                        $"TypeID={exactMatch.TypeId}");

                    // The filename does NOT have to equal m_Name.
                    DebugStr(
                        $"[PNG] Importing '{inputFile}' into " +
                        $"Texture2D '{name}'.");

                    return true;
                }
                catch (Exception ex)
                {
                    DisplayStr(
                        $"[PNG] Failed reading Texture2D PID " +
                        $"{wantedPathId}: " +
                        $"{ex.GetType().Name}: {ex.Message}");

                    DebugStr(
                        ex.ToString());

                    return false;
                }
            }

            // ========================================================
            // FALLBACK BY NAME
            //
            // No PathID supplied.
            // Search Texture2D by m_Name.
            // ========================================================

            DebugStr(
                $"[PNG] No valid PathID supplied. " +
                $"Searching Texture2D by name '{targetName}'.");

            int candidatesScanned = 0;

            foreach (var inf in assetInst.file.GetAssetsOfType(
                (int)AssetClassID.Texture2D))
            {
                candidatesScanned++;

                try
                {
                    var candidate =
                        am.GetBaseField(
                            assetInst,
                            inf);

                    if (candidate == null ||
                        candidate.IsDummy)
                    {
                        continue;
                    }

                    string name =
                        GetAssetName(candidate);

                    DebugStr(
                        $"[PNG] Candidate #{candidatesScanned}: " +
                        $"Name='{name}', PID={inf.PathId}");

                    if (!string.Equals(
                        name?.Trim(),
                        targetName,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    afie = inf;
                    atvf = candidate;

                    DebugStr(
                        $"[PNG] Found Texture2D by name: " +
                        $"Name='{name}', PID={inf.PathId}");

                    return true;
                }
                catch (Exception ex)
                {
                    DebugStr(
                        $"[PNG] Failed reading candidate PID " +
                        $"{inf.PathId}: " +
                        $"{ex.GetType().Name}: {ex.Message}");

                    DebugStr(
                        ex.ToString());
                }
            }

            DisplayStr(
                $"[PNG] Couldn't find equivalent image for " +
                $"{asset} " +
                $"(Asset: {assetfile_name}, " +
                $"Texture: {targetName}). " +
                $"Texture2D candidates scanned: " +
                $"{candidatesScanned}");

            return false;
        }
    }
}