using AssetsTools.NET;
using AssetsTools.NET.Extra;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace UAFGJ
{
    partial class Program
    {
        /// ============================================================
        // MONOBEHAVIOUR TEXT-ONLY
        //
        // MONOBEHAVIOUR_TEXT
        //      -> sostituisce solo m_text
        //
        // MONOBEHAVIOUR_TEXT_CHECKED
        //      -> sostituisce solo m_text
        //      -> il controllo finale NON usa il BaseField completo,
        //         perch� per alcuni MonoBehaviour il TypeTree pu� essere dummy.
        //
        // ============================================================

        private static bool ImportMonoBehaviourTextOnly(
            string inputFile,
            AssetsManager am,
            AssetFileInfo afie,
            AssetsFileInstance assetInst,
            string assetName,
            out AssetTypeValueField modifiedBaseField,
            out byte[] replacementData,
            out byte[] originalSerializedData)
        {
            return ImportMonoBehaviourTextOnlyInternal(
                inputFile,
                am,
                afie,
                assetInst,
                assetName,
                false,
                out modifiedBaseField,
                out replacementData,
                out originalSerializedData);
        }

        private static bool ImportMonoBehaviourTextOnlyChecked(
            string inputFile,
            AssetsManager am,
            AssetFileInfo afie,
            AssetsFileInstance assetInst,
            string assetName,
            out AssetTypeValueField modifiedBaseField,
            out byte[] replacementData,
            out byte[] originalSerializedData)
        {
            return ImportMonoBehaviourTextOnlyInternal(
                inputFile,
                am,
                afie,
                assetInst,
                assetName,
                true,
                out modifiedBaseField,
                out replacementData,
                out originalSerializedData);
        }

        private static bool ImportMonoBehaviourTextOnlyInternal(
            string inputFile,
            AssetsManager am,
            AssetFileInfo afie,
            AssetsFileInstance assetInst,
            string assetName,
            bool checkedMode,
            out AssetsTools.NET.AssetTypeValueField modifiedBaseField,
            out byte[] replacementData,
            out byte[] originalSerializedData)
        {
            modifiedBaseField = null;
            replacementData = Array.Empty<byte>();
            originalSerializedData = Array.Empty<byte>();

            if (assetInst == null)
                throw new InvalidOperationException(
                    "assetInst is null.");

            if (afie == null)
                throw new InvalidOperationException(
                    "AssetFileInfo is null.");

            if (!File.Exists(inputFile))
                throw new FileNotFoundException(
                    "TXT input not found.",
                    inputFile);

            DebugStr(
                $"[TXT] Importing TEXT-ONLY MonoBehaviour " +
                $"PID={afie.PathId}, " +
                $"asset='{assetName}', " +
                $"checked={checkedMode}");

            LogPhase($"TEXT-only import starting PID={afie.PathId}, checked={checkedMode}.");

            // --------------------------------------------------------
            // RAW SERIALIZED PAYLOAD
            //
            // Non usiamo GetBaseField().
            //
            // Questo � fondamentale per i MonoBehaviour nei quali
            // AssetsTools.NET vede un TypeTree dummy/incompleto.
            // --------------------------------------------------------

            DebugStr($"[TXT] Reading raw serialized bytes for PID={afie.PathId}.");
            originalSerializedData =
                ReadRawAssetBytes(
                    assetInst,
                    afie);

            if (originalSerializedData == null ||
                originalSerializedData.Length == 0)
            {
                throw new InvalidDataException(
                    $"Raw asset data is empty for PID={afie.PathId}.");
            }

            DebugStr(
                $"[TXT] Original raw asset size=" +
                $"{originalSerializedData.Length} bytes " +
                $"SHA256={Sha256Hex(originalSerializedData)}");

            // --------------------------------------------------------
            // READ NEW m_text FROM DUMP
            // --------------------------------------------------------

            DebugStr($"[TXT] Reading replacement m_text from '{inputFile}'.");
            string newText =
                ReadDumpMText(
                    inputFile);

            DebugStr(
                $"[TXT] Replacement m_text length=" +
                $"{newText.Length}");

            DebugStr(
                $"[TXT] Replacement m_text='{newText}'");

            // --------------------------------------------------------
            // REPLACE ONLY THE TEXT STRING
            //
            // Non viene modificato nessun altro campo.
            // --------------------------------------------------------

            DebugStr("[TXT] Replacing first Unity string candidate.");
            replacementData =
                ReplaceFirstUnityString(
                    originalSerializedData,
                    newText);

            if (replacementData == null ||
                replacementData.Length == 0)
            {
                throw new InvalidDataException(
                    "Text-only replacement produced empty data.");
            }

            DebugStr(
                $"[TXT] TEXT-ONLY replacement complete. " +
                $"New serialized size={replacementData.Length} " +
                $"SHA256={Sha256Hex(replacementData)}");

            // --------------------------------------------------------
            // IMPORTANT:
            //
            // Rimane null.
            //
            // Il caller deve usare replacementData direttamente.
            // --------------------------------------------------------

            modifiedBaseField = null;

            return true;
        }


        // ============================================================
        // FULL MONOBEHAVIOUR
        //
        // Usato per:
        //
        //   FONT
        //   FONT_CHECKED
        //   MONOBEHAVIOUR_FULL_CHECKED
        //
        // FONT:
        //   importa tutto il dump.
        //   nessun controllo scalar prima dell'import.
        //
        // FONT_CHECKED:
        //   importa tutto il dump.
        //   controllo scalar.
        //
        // MONOBEHAVIOUR_FULL_CHECKED:
        //   stesso comportamento di FONT_CHECKED.
        // ============================================================

        private static bool ImportMonoBehaviourFull(
            string inputFile,
            AssetsManager am,
            AssetFileInfo afie,
            AssetsFileInstance assetInst,
            string assetName,
            out AssetTypeValueField modifiedBaseField,
            out byte[] replacementData,
            out byte[] originalSerializedData)
        {
            return ImportMonoBehaviourFullInternal(
                inputFile,
                am,
                afie,
                assetInst,
                assetName,
                false,
                out modifiedBaseField,
                out replacementData,
                out originalSerializedData);
        }

        private static bool ImportMonoBehaviourFullChecked(
            string inputFile,
            AssetsManager am,
            AssetFileInfo afie,
            AssetsFileInstance assetInst,
            string assetName,
            out AssetTypeValueField modifiedBaseField,
            out byte[] replacementData,
            out byte[] originalSerializedData)
        {
            return ImportMonoBehaviourFullInternal(
                inputFile,
                am,
                afie,
                assetInst,
                assetName,
                true,
                out modifiedBaseField,
                out replacementData,
                out originalSerializedData);
        }

        private static bool ImportMonoBehaviourFullInternal(
            string inputFile,
            AssetsManager am,
            AssetFileInfo afie,
            AssetsFileInstance assetInst,
            string assetName,
            bool checkedMode,
            out AssetTypeValueField modifiedBaseField,
            out byte[] replacementData,
            out byte[] originalSerializedData)
        {
            modifiedBaseField = null;
            replacementData = Array.Empty<byte>();
            originalSerializedData = Array.Empty<byte>();

            if (assetInst == null)
            {
                throw new InvalidOperationException(
                    "assetInst is null.");
            }

            if (am == null)
            {
                throw new InvalidOperationException(
                    "AssetsManager is null.");
            }

            if (afie == null)
            {
                throw new InvalidOperationException(
                    "AssetFileInfo is null.");
            }

            if (!File.Exists(inputFile))
            {
                throw new FileNotFoundException(
                    "TXT input not found.",
                    inputFile);
            }

            LogPhase($"FULL import starting PID={afie.PathId}, checked={checkedMode}.");
            DebugStr(
                $"[TXT] Importing FULL MonoBehaviour " +
                $"PID={afie.PathId}, " +
                $"asset='{assetName}', " +
                $"checked={checkedMode}");

            // --------------------------------------------------------
            // Load BaseField.
            // --------------------------------------------------------

            DebugStr($"[TXT] Requesting BaseField from AssetsTools.NET for PID={afie.PathId}.");
            AssetTypeValueField baseField =
                am.GetBaseField(
                    assetInst,
                    afie);

            if (baseField == null ||
                baseField.IsDummy)
            {
                throw new InvalidDataException(
                    "AssetsTools.NET returned a null/dummy BaseField.");
            }

            // --------------------------------------------------------
            // Preserve original serialized data.
            // --------------------------------------------------------

            originalSerializedData =
                baseField.WriteToByteArray();

            DebugStr(
                $"[CHECK] Original target base field " +
                $"serialized size={originalSerializedData.Length} " +
                $"SHA256={Sha256Hex(originalSerializedData)}");

            // --------------------------------------------------------
            // FULL CHECKED
            //
            // Delegate completamente alla funzione che gi� conosciamo:
            //
            //   ApplyTextDumpToBaseField
            //
            // Questa esegue il controllo:
            //
            //   count
            //   field name
            //   field order
            //   field type
            //   value
            //
            // e poi applica il dump.
            // --------------------------------------------------------

            DebugStr($"[TXT] FULL mode preflight selected: checked={checkedMode}.");

            if (checkedMode)
            {
                DebugStr(
                    "[TXT] FULL checked mode: " +
                    "applying dump with scalar validation.");

                ApplyTextDumpToBaseField(
                    inputFile,
                    baseField);
            }
            else
            {
                // ----------------------------------------------------
                // FULL UNCHECKED
                //
                // Applica il dump senza richiedere che il numero
                // di scalari corrisponda esattamente.
                //
                // I campi vengono applicati in ordine posizionale
                // finch� entrambi i lati hanno dati.
                //
                // ATTENZIONE:
                // questo � volutamente "unchecked".
                // Il chiamante ha dichiarato che sa che il dump
                // appartiene a quel tipo di asset.
                // ----------------------------------------------------

                ApplyTextDumpToBaseFieldUnchecked(
                    inputFile,
                    baseField);
            }

            // --------------------------------------------------------
            // Serialize modified asset.
            // --------------------------------------------------------

            DebugStr("[TXT] Serializing modified FULL BaseField.");
            replacementData =
                baseField.WriteToByteArray();

            if (replacementData == null ||
                replacementData.Length == 0)
            {
                throw new InvalidDataException(
                    "Modified MonoBehaviour serialized to zero bytes.");
            }

            DebugStr(
                $"[TXT] FULL MonoBehaviour serialized: " +
                $"{replacementData.Length} bytes " +
                $"SHA256={Sha256Hex(replacementData)}");

            modifiedBaseField =
                baseField;

            return true;
        }


        // ============================================================
        // DUMP STRING FIELD
        //
        // Estrae:
        //
        //   1 string m_text = "..."
        //
        // senza fare affidamento sull'intero albero del dump.
        // ============================================================

        private static string ReadDumpStringField(
            string inputFile,
            string wantedFieldName)
        {
            using (var reader =
                new StreamReader(
                    inputFile,
                    Encoding.UTF8,
                    true))
            {
                int lineNumber = 0;

                while (true)
                {
                    string line =
                        reader.ReadLine();

                    if (line == null)
                        break;

                    lineNumber++;

                    string trimmed =
                        line.TrimStart();

                    if (!trimmed.StartsWith(
                        "1 string ",
                        StringComparison.Ordinal))
                    {
                        continue;
                    }

                    const string prefix =
                        "1 string ";

                    string rest =
                        trimmed.Substring(
                            prefix.Length);

                    int eq =
                        rest.IndexOf('=');

                    if (eq < 0)
                        continue;

                    string fieldName =
                        rest.Substring(
                            0,
                            eq).Trim();

                    if (!string.Equals(
                        fieldName,
                        wantedFieldName,
                        StringComparison.Ordinal))
                    {
                        continue;
                    }

                    string rawValue =
                        rest.Substring(
                            eq + 1).Trim();

                    string parsed =
                        ParseDumpString(
                            rawValue);

                    DebugStr(
                        $"[TXT] Found dump field " +
                        $"'{wantedFieldName}' " +
                        $"at line {lineNumber}, " +
                        $"length={parsed.Length}");

                    return parsed;
                }
            }

            throw new InvalidDataException(
                $"Dump does not contain " +
                $"a string field named '{wantedFieldName}'.");
        }


        // ============================================================
        // FULL UNCHECKED APPLY
        //
        // Versione volutamente senza controllo scalar.
        //
        // Non confronta:
        //
        //   - count
        //   - names
        //   - order
        //   - types
        //
        // Usa semplicemente i valori scalar del dump e quelli del
        // BaseField nella loro sequenza.
        //
        // Se il dump contiene meno campi del target, i rimanenti
        // campi del target vengono lasciati invariati.
        //
        // Se contiene pi� campi, quelli extra vengono ignorati.
        // ============================================================

        private static byte[] ApplyTextDumpToBaseFieldUnchecked(
            string inputFile,
            AssetTypeValueField baseField)
        {
            DebugStr(
                "[TXT] Applying FULL dump with structural path mapping (unchecked values).");

            // Structural preflight is mandatory even in unchecked mode.
            // Every dump scalar must map to exactly one target scalar of the
            // same type. Extra target scalars are allowed and remain unchanged.
            // Most importantly, no field is mutated until the complete mapping
            // has succeeded.
            var matches = BuildDumpTargetMatches(
                inputFile,
                baseField,
                false);

            DebugStr(
                $"[TXT] FULL structural preflight passed for {matches.Count} mapped fields.");

            for (int i = 0; i < matches.Count; i++)
            {
                var match = matches[i];

                try
                {
                    ApplyDumpValue(
                        match.Target.Field,
                        match.Dump);

                    if (i < 8 || i == matches.Count - 1)
                    {
                        DebugStr(
                            $"[TXT] FULL applied #{i + 1}: " +
                            $"path='{match.Dump.Path}' " +
                            $"field='{match.Dump.FieldName}' type='{match.Dump.Type}'");
                    }
                }
                catch (Exception ex)
                {
                    throw new InvalidDataException(
                        $"Unable to apply FULL field #{i + 1}: " +
                        $"path='{match.Dump.Path}', line={match.Dump.LineNumber}.",
                        ex);
                }
            }

            byte[] data = baseField.WriteToByteArray();

            DebugStr(
                $"[TXT] FULL BaseField reserialized: {data.Length} bytes SHA256={Sha256Hex(data)}");

            return data;
        }

        private static byte[] ReadRawAssetBytes(
    AssetsFileInstance assetInst,
    AssetFileInfo afie)
        {
            if (assetInst == null)
                throw new ArgumentNullException(
                    nameof(assetInst));

            if (afie == null)
                throw new ArgumentNullException(
                    nameof(afie));

            long absoluteOffset =
                afie.GetAbsoluteByteOffset(
                    assetInst.file);

            long byteSize =
                afie.ByteSize;

            if (absoluteOffset < 0)
            {
                throw new InvalidDataException(
                    $"Invalid absolute asset offset " +
                    $"for PID={afie.PathId}: {absoluteOffset}");
            }

            if (byteSize <= 0)
            {
                throw new InvalidDataException(
                    $"Invalid asset byte size " +
                    $"for PID={afie.PathId}: {byteSize}");
            }

            if (byteSize > int.MaxValue)
            {
                throw new InvalidDataException(
                    $"Asset PID={afie.PathId} is too large " +
                    $"to load into a byte array: {byteSize} bytes.");
            }

            AssetsFileReader reader =
                assetInst.file.Reader;

            long oldPosition =
                reader.Position;

            try
            {
                reader.Position =
                    absoluteOffset;

                byte[] data =
                    reader.ReadBytes(
                        (int)byteSize);

                if (data.Length != byteSize)
                {
                    throw new InvalidDataException(
                        $"Could not read complete raw asset " +
                        $"PID={afie.PathId}: " +
                        $"expected={byteSize}, " +
                        $"actual={data.Length}");
                }

                return data;
            }
            finally
            {
                reader.Position =
                    oldPosition;
            }
        }

        private static string ReadDumpMText(
            string inputFile)
        {
            if (!File.Exists(inputFile))
            {
                throw new FileNotFoundException(
                    "TXT dump not found.",
                    inputFile);
            }

            string found = null;
            using (var reader =
                new StreamReader(
                    inputFile,
                    Encoding.UTF8,
                    true))
            {
                int lineNumber = 0;

                while (true)
                {
                    string rawLine =
                        reader.ReadLine();

                    if (rawLine == null)
                        break;

                    lineNumber++;

                    string line =
                        rawLine.TrimStart('\uFEFF');

                    string trimmed =
                        line.TrimStart();

                    // ----------------------------------------------------
                    // Cerchiamo:
                    //
                    // 1 string m_text = "..."
                    // ----------------------------------------------------

                    if (!trimmed.StartsWith(
                        "1 string m_text = ",
                        StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (found != null)
                    {
                        throw new InvalidDataException(
                            "Dump contains multiple m_text fields.");
                    }

                    const string prefix =
                        "1 string m_text = ";

                    string rawValue =
                        trimmed.Substring(
                            prefix.Length)
                        .Trim();

                    found =
                        ParseDumpString(
                            rawValue);

                    DebugStr(
                        $"[TXT] Found m_text at line {lineNumber}: " +
                        $"length={found.Length}");
                }
            }

            if (found == null)
            {
                throw new InvalidDataException(
                    "The TXT dump does not contain " +
                    "'1 string m_text = ...'.");
            }

            return found;
        }

        private static byte[] ReplaceFirstUnityString(
    byte[] source,
    string replacement)
        {
            if (source == null ||
                source.Length < 4)
            {
                throw new InvalidDataException(
                    "Source asset data is too small.");
            }

            replacement ??= "";

            byte[] replacementUtf8 =
                Encoding.UTF8.GetBytes(
                    replacement);

            // Unity strings are:
            //
            //   int32 byteLength
            //   UTF-8 bytes
            //   padding to 4-byte alignment
            //
            // Search aligned string fields.
            //
            // For MONOBEHAVIOUR_TEXT the target objects used by
            // this importer contain the text string as the first
            // meaningful printable Unity string.
            // --------------------------------------------------------

            List<(int Offset, int Length, string Text)> candidates =
                new List<(int Offset, int Length, string Text)>();

            for (int offset = 0;
                 offset + 4 <= source.Length;
                 offset += 4)
            {
                int length =
                    BitConverter.ToInt32(
                        source,
                        offset);

                if (length <= 0)
                    continue;

                if (length > source.Length - offset - 4)
                    continue;

                int stringStart =
                    offset + 4;

                int stringEnd =
                    stringStart + length;

                string decoded;

                try
                {
                    decoded =
                        new UTF8Encoding(
                            false,
                            true)
                        .GetString(
                            source,
                            stringStart,
                            length);
                }
                catch
                {
                    continue;
                }

                if (string.IsNullOrEmpty(decoded))
                    continue;

                // Reject binary garbage.
                bool printable = true;

                foreach (char c in decoded)
                {
                    if (char.IsControl(c) &&
                        c != '\r' &&
                        c != '\n' &&
                        c != '\t')
                    {
                        printable = false;
                        break;
                    }
                }

                if (!printable)
                    continue;

                candidates.Add(
                    (
                        offset,
                        length,
                        decoded
                    ));
            }

            if (candidates.Count == 0)
            {
                throw new InvalidDataException(
                    "Could not find any plausible serialized " +
                    "UTF-8 string inside the target MonoBehaviour.");
            }

            DebugStr(
                $"[TXT] Raw string candidates found: " +
                $"{candidates.Count}");

            for (int i = 0;
                 i < candidates.Count;
                 i++)
            {
                var candidate =
                    candidates[i];

                DebugStr(
                    $"[TXT] Candidate #{i + 1}: " +
                    $"offset={candidate.Offset}, " +
                    $"length={candidate.Length}, " +
                    $"text='{candidate.Text}'");
            }

            // --------------------------------------------------------
            // These MonoBehaviours are specifically classified as
            // MONOBEHAVIOUR_TEXT.
            //
            // Their useful text field is the first meaningful string
            // in the serialized object.
            // --------------------------------------------------------

            var target =
                candidates[0];

            DebugStr(
                $"[TXT] Selected serialized string at " +
                $"offset={target.Offset}: '{target.Text}'");

            int oldTotalLength =
                4 + target.Length;

            int oldPaddedLength =
                Align4(
                    oldTotalLength);

            int newTotalLength =
                4 + replacementUtf8.Length;

            int newPaddedLength =
                Align4(
                    newTotalLength);

            int newSize =
                source.Length
                - oldPaddedLength
                + newPaddedLength;

            byte[] result =
                new byte[newSize];

            // Before string.
            Buffer.BlockCopy(
                source,
                0,
                result,
                0,
                target.Offset);

            // New string length.
            WriteInt32LittleEndian(
                result,
                target.Offset,
                replacementUtf8.Length);

            // New UTF-8 data.
            Buffer.BlockCopy(
                replacementUtf8,
                0,
                result,
                target.Offset + 4,
                replacementUtf8.Length);

            // Remaining bytes after old string.
            int sourceTailOffset =
                target.Offset
                + oldPaddedLength;

            int resultTailOffset =
                target.Offset
                + newPaddedLength;

            int tailLength =
                source.Length
                - sourceTailOffset;

            if (tailLength > 0)
            {
                Buffer.BlockCopy(
                    source,
                    sourceTailOffset,
                    result,
                    resultTailOffset,
                    tailLength);
            }

            // New padding is already zero-filled.

            return result;
        }

        private static int Align4(
    int value)
        {
            return (
                value + 3
            ) & ~3;
        }

        private static void WriteInt32LittleEndian(
            byte[] buffer,
            int offset,
            int value)
        {
            byte[] bytes =
                BitConverter.GetBytes(
                    value);

            Buffer.BlockCopy(
                bytes,
                0,
                buffer,
                offset,
                4);
        }
    }
}