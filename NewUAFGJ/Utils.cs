using AssetsTools.NET;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace UAFGJ
{
    partial class Program
    {
        private static void LogPhase(string message)
        {
            DebugStr("[PHASE] " + message);
        }

        private static void LogException(string context, Exception ex)
        {
            DebugStr($"[ERROR] {context}: {ex.GetType().FullName}: {ex.Message}");
            DebugStr(ex.ToString());
        }

        private static void LogFileState(string label, string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    DebugStr($"{label}: path=<empty>");
                    return;
                }

                if (!File.Exists(path))
                {
                    DebugStr($"{label}: MISSING path='{path}'");
                    return;
                }

                var info = new FileInfo(path);
                DebugStr($"{label}: path='{path}', length={info.Length}, lastWriteUtc={info.LastWriteTimeUtc:O}, readOnly={info.IsReadOnly}");
                DebugStr($"{label}: SHA256={Sha256File(path)}");
            }
            catch (Exception ex)
            {
                DebugStr($"[ERROR] Could not log file state '{path}': {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static bool StartsWithSpace(string str, string value)
        {
            return str.StartsWith(
                value + " ",
                StringComparison.Ordinal);
        }

        private static string UnescapeDumpString(string str)
        {
            StringBuilder sb = new StringBuilder(str.Length);
            bool escaping = false;

            foreach (char c in str)
            {
                if (!escaping && c == '\\')
                {
                    escaping = true;
                    continue;
                }

                if (escaping)
                {
                    switch (c)
                    {
                        case '\\':
                            sb.Append('\\');
                            break;

                        case 'r':
                            sb.Append('\r');
                            break;

                        case 'n':
                            sb.Append('\n');
                            break;

                        case 't':
                            sb.Append('\t');
                            break;

                        default:
                            sb.Append(c);
                            break;
                    }

                    escaping = false;
                }
                else
                {
                    sb.Append(c);
                }
            }

            if (escaping)
                sb.Append('\\');

            return sb.ToString();
        }

        private static int LeadingSpaces(string line)
        {
            int depth = 0;

            while (depth < line.Length && line[depth] == ' ')
                depth++;

            return depth;
        }

        private static string ParseDumpString(string valueStr)
        {
            int firstQuote = valueStr.IndexOf('"');
            int lastQuote = valueStr.LastIndexOf('"');

            if (firstQuote < 0 || lastQuote <= firstQuote)
            {
                throw new FormatException(
                    "String field does not contain a valid quoted value: " +
                    valueStr);
            }

            return UnescapeDumpString(
                valueStr.Substring(
                    firstQuote + 1,
                    lastQuote - firstQuote - 1));
        }

        private static int ParseInt32(string s)
        {
            return int.Parse(
                s,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture);
        }

        private static long ParseInt64(string s)
        {
            return long.Parse(
                s,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture);
        }

        private static uint ParseUInt32(string s)
        {
            return uint.Parse(
                s,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture);
        }

        private static ulong ParseUInt64(string s)
        {
            return ulong.Parse(
                s,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture);
        }

        // ------------------------------------------------------------
        // Numeric normalization.
        //
        // Supports both:
        //   0.5
        //   0,5
        //
        // We deliberately do NOT use AllowThousands when parsing
        // floating-point values. Otherwise "0,5" with InvariantCulture
        // can be interpreted as 5.
        // ------------------------------------------------------------

        private static string NormalizeNumericLiteral(string value)
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));

            value = value.Trim();

            if (value.Length == 0)
            {
                throw new FormatException(
                    "Numeric literal is empty.");
            }

            bool hasComma = value.Contains(',');
            bool hasDot = value.Contains('.');

            // 0,5 -> 0.5
            if (hasComma && !hasDot)
            {
                return value.Replace(',', '.');
            }

            // 0.5
            if (!hasComma && hasDot)
            {
                return value;
            }

            // 5
            if (!hasComma && !hasDot)
            {
                return value;
            }

            // Both separators occur.
            // Treat the LAST separator as the decimal separator.
            int commaIndex = value.LastIndexOf(',');
            int dotIndex = value.LastIndexOf('.');

            if (commaIndex > dotIndex)
            {
                // 1.234,5 -> 1234.5
                value = value.Replace(".", "");
                value = value.Replace(',', '.');
                return value;
            }
            else
            {
                // 1,234.5 -> 1234.5
                value = value.Replace(",", "");
                return value;
            }
        }

        private static float ParseSingle(string s)
        {
            string normalized = NormalizeNumericLiteral(s);

            return float.Parse(
                normalized,
                NumberStyles.Float,
                CultureInfo.InvariantCulture);
        }

        private static double ParseDouble(string s)
        {
            string normalized = NormalizeNumericLiteral(s);

            return double.Parse(
                normalized,
                NumberStyles.Float,
                CultureInfo.InvariantCulture);
        }

        private sealed class DumpScalar
        {
            public int LineNumber;
            public string Type = "";
            public string FieldName = "";
            public string Value = "";
            public string Path = "";
        }

        private sealed class ScalarFieldEntry
        {
            public string Path = "";
            public string Type = "";
            public string FieldName = "";
            public AssetTypeValueField Field = null;
        }

        private sealed class DumpTargetMatch
        {
            public DumpScalar Dump = null;
            public ScalarFieldEntry Target = null;
        }

        private static string ParseDumpNodeName(string left, out string type)
        {
            left = left.Trim();

            if (string.IsNullOrEmpty(left))
            {
                type = "";
                return "";
            }

            // Array nodes are printed as: "Array Array (N items)".
            if (left.StartsWith("Array Array", StringComparison.Ordinal))
            {
                type = "Array";
                return "Array";
            }

            int split = left.LastIndexOf(' ');

            if (split <= 0 || split >= left.Length - 1)
            {
                type = "";
                return left;
            }

            type = left.Substring(0, split).Trim();
            return left.Substring(split + 1).Trim();
        }

        private static List<DumpScalar> ReadDumpScalars(
            string inputFile)
        {
            var result = new List<DumpScalar>();

            // One node name per indentation depth.
            var stack = new List<string>();
            var stackIsArray = new List<bool>();
            var arrayItemCounters = new List<int>();

            using (var reader = new StreamReader(inputFile, Encoding.UTF8, true))
            {
                int lineNumber = 0;

                while (true)
                {
                    string line = reader.ReadLine();
                    if (line == null)
                        break;

                    lineNumber++;

                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    int depth = LeadingSpaces(line);
                    if (depth >= line.Length || line[depth] == '[')
                        continue;

                    string payload = line.Substring(depth).TrimStart();
                    int firstSpace = payload.IndexOf(' ');
                    if (firstSpace <= 0)
                        continue;

                    payload = payload.Substring(firstSpace + 1).TrimStart();
                    if (payload.Length == 0)
                        continue;

                    int eq = payload.IndexOf('=');
                    string left = eq >= 0 ? payload.Substring(0, eq).Trim() : payload;
                    if (left.Length == 0)
                        continue;

                    string type;
                    string fieldName = ParseDumpNodeName(left, out type);
                    if (string.IsNullOrEmpty(fieldName))
                        continue;

                    while (stack.Count > depth)
                    {
                        stack.RemoveAt(stack.Count - 1);
                        stackIsArray.RemoveAt(stackIsArray.Count - 1);
                        arrayItemCounters.RemoveAt(arrayItemCounters.Count - 1);
                    }

                    while (stack.Count < depth)
                    {
                        stack.Add("<anonymous>");
                        stackIsArray.Add(false);
                        arrayItemCounters.Add(0);
                    }

                    bool parentIsArray = depth > 0 &&
                                         depth - 1 < stackIsArray.Count &&
                                         stackIsArray[depth - 1];

                    bool isScalar = eq >= 0;

                    // The depth-0 line is the dump root (for example
                    // "MonoBehaviour Base"). The AssetsTools.NET BaseField
                    // traversal below starts at its children, so the root
                    // itself must not become part of the scalar path.
                    if (!isScalar && depth == 0)
                    {
                        stack.Clear();
                        stackIsArray.Clear();
                        arrayItemCounters.Clear();
                        continue;
                    }

                    if (!isScalar)
                    {
                        string nodeName = fieldName;

                        // For an element inside an Array, preserve the item
                        // ordinal in the structural path. This prevents two
                        // otherwise identical fields in different array
                        // elements from collapsing to the same path.
                        if (parentIsArray)
                        {
                            int idx = arrayItemCounters[depth - 1]++;
                            nodeName += "[" + idx + "]";
                        }

                        if (stack.Count == depth)
                        {
                            stack.Add(nodeName);
                            stackIsArray.Add(
                                string.Equals(type, "Array", StringComparison.Ordinal));
                            arrayItemCounters.Add(0);
                        }
                        else
                        {
                            stack[depth] = nodeName;
                            stackIsArray[depth] =
                                string.Equals(type, "Array", StringComparison.Ordinal);
                            arrayItemCounters[depth] = 0;
                        }

                        continue;
                    }

                    string value = payload.Substring(eq + 1).Trim();

                    // m_Array.size describes container size, not a scalar
                    // field of the serialized object. Ignore it.
                    if (string.Equals(fieldName, "size", StringComparison.Ordinal))
                        continue;

                    var pathParts = new List<string>();
                    for (int i = 0; i < stack.Count; i++)
                    {
                        if (stack[i] == "<anonymous>")
                            continue;
                        pathParts.Add(stack[i]);
                    }

                    if (parentIsArray)
                    {
                        int idx = arrayItemCounters[depth - 1]++;
                        if (pathParts.Count > 0)
                            pathParts[pathParts.Count - 1] =
                                pathParts[pathParts.Count - 1] + "[" + idx + "]";
                    }

                    pathParts.Add(fieldName);

                    result.Add(new DumpScalar
                    {
                        LineNumber = lineNumber,
                        Type = type,
                        FieldName = fieldName,
                        Value = value,
                        Path = string.Join("/", pathParts)
                    });
                }
            }

            return result;
        }

        private static void CollectScalarFieldEntriesRecursive(
            AssetTypeValueField field,
            string parentPath,
            List<ScalarFieldEntry> result)
        {
            if (field == null || field.IsDummy)
                return;

            string fieldName = field.TemplateField?.Name ?? "<unnamed>";
            string currentPath = string.IsNullOrEmpty(parentPath)
                ? fieldName
                : parentPath + "/" + fieldName;

            if (field.Value != null && field.Value.ValueType == AssetValueType.Array)
            {
                if (field.Children != null)
                {
                    for (int i = 0; i < field.Children.Count; i++)
                    {
                        CollectScalarFieldEntriesRecursive(
                            field.Children[i],
                            currentPath + "[" + i + "]",
                            result);
                    }
                }

                return;
            }

            if (field.Children != null && field.Children.Count > 0)
            {
                foreach (var child in field.Children)
                {
                    CollectScalarFieldEntriesRecursive(
                        child,
                        currentPath,
                        result);
                }

                return;
            }

            if (field.Value == null)
                return;

            if (field.Value.ValueType == AssetValueType.ByteArray ||
                field.Value.ValueType == AssetValueType.ManagedReferencesRegistry)
            {
                return;
            }

            result.Add(new ScalarFieldEntry
            {
                Path = currentPath,
                Type = RuntimeTypeToDumpType(field.Value.ValueType),
                FieldName = fieldName,
                Field = field
            });
        }

        private static List<ScalarFieldEntry> CollectScalarFieldEntries(
            AssetTypeValueField baseField)
        {
            var result = new List<ScalarFieldEntry>();

            if (baseField?.Children != null)
            {
                foreach (var child in baseField.Children)
                {
                    CollectScalarFieldEntriesRecursive(child, "", result);
                }
            }

            return result;
        }

        private static List<DumpTargetMatch> BuildDumpTargetMatches(
            string inputFile,
            AssetTypeValueField baseField,
            bool requireExactTargetCount)
        {
            var dumpScalars = ReadDumpScalars(inputFile);
            var targetEntries = CollectScalarFieldEntries(baseField);

            DebugStr(
                $"[TXT] Structural mapping: dump scalar count={dumpScalars.Count}; " +
                $"target scalar count={targetEntries.Count}");

            var targetByPath = new Dictionary<string, ScalarFieldEntry>(StringComparer.Ordinal);
            foreach (var target in targetEntries)
            {
                if (!targetByPath.TryAdd(target.Path, target))
                {
                    throw new InvalidDataException(
                        $"Duplicate target scalar path '{target.Path}'.");
                }
            }

            var matches = new List<DumpTargetMatch>(dumpScalars.Count);
            var usedPaths = new HashSet<string>(StringComparer.Ordinal);

            foreach (var dump in dumpScalars)
            {
                if (!targetByPath.TryGetValue(dump.Path, out var target))
                {
                    throw new InvalidDataException(
                        $"FULL structural mapping failed at dump line {dump.LineNumber}: " +
                        $"'{dump.Type} {dump.FieldName}' path='{dump.Path}' has no matching target field.");
                }

                if (!string.Equals(dump.Type, target.Type, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        $"FULL type mismatch at dump line {dump.LineNumber}: " +
                        $"path='{dump.Path}', dump='{dump.Type}', target='{target.Type}'.");
                }

                if (!usedPaths.Add(dump.Path))
                {
                    throw new InvalidDataException(
                        $"Duplicate dump scalar path '{dump.Path}' at line {dump.LineNumber}.");
                }

                matches.Add(new DumpTargetMatch
                {
                    Dump = dump,
                    Target = target
                });
            }

            if (requireExactTargetCount && dumpScalars.Count != targetEntries.Count)
            {
                throw new InvalidDataException(
                    $"FULL checked mapping count mismatch: dump={dumpScalars.Count}, target={targetEntries.Count}.");
            }

            if (!requireExactTargetCount)
            {
                int extras = targetEntries.Count - usedPaths.Count;
                if (extras > 0)
                {
                    DebugStr(
                        $"[TXT] FULL structural mapping: {extras} target scalar(s) are not present in the dump; they will remain unchanged.");
                }
            }

            return matches;
        }

        private static string RuntimeTypeToDumpType(
            AssetValueType t)
        {
            switch (t)
            {
                case AssetValueType.Bool:
                    return "bool";

                case AssetValueType.UInt8:
                    return "UInt8";

                case AssetValueType.Int8:
                    return "SInt8";

                case AssetValueType.UInt16:
                    return "UInt16";

                case AssetValueType.Int16:
                    return "SInt16";

                case AssetValueType.UInt32:
                    return "unsigned int";

                case AssetValueType.Int32:
                    return "int";

                case AssetValueType.UInt64:
                    return "UInt64";

                case AssetValueType.Int64:
                    return "SInt64";

                case AssetValueType.Float:
                    return "float";

                case AssetValueType.Double:
                    return "double";

                case AssetValueType.String:
                    return "string";

                default:
                    return t.ToString();
            }
        }

        private static void ApplyDumpValue(
            AssetsTools.NET.AssetTypeValueField field,
            DumpScalar dump)
        {
            if (field == null || field.Value == null)
            {
                throw new InvalidOperationException(
                    $"Target field '{dump.FieldName}' has no scalar value.");
            }

            switch (field.Value.ValueType)
            {
                case AssetValueType.Bool:
                    field.AsBool = bool.Parse(dump.Value);
                    break;

                case AssetValueType.UInt8:
                    field.AsUInt =
                        byte.Parse(
                            dump.Value,
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture);
                    break;

                case AssetValueType.Int8:
                    field.AsInt =
                        sbyte.Parse(
                            dump.Value,
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture);
                    break;

                case AssetValueType.UInt16:
                    field.AsUInt =
                        ushort.Parse(
                            dump.Value,
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture);
                    break;

                case AssetValueType.Int16:
                    field.AsInt =
                        short.Parse(
                            dump.Value,
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture);
                    break;

                case AssetValueType.UInt32:
                    field.AsUInt =
                        uint.Parse(
                            dump.Value,
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture);
                    break;

                case AssetValueType.Int32:
                    field.AsInt =
                        int.Parse(
                            dump.Value,
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture);
                    break;

                case AssetValueType.UInt64:
                    field.AsULong =
                        ulong.Parse(
                            dump.Value,
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture);
                    break;

                case AssetValueType.Int64:
                    field.AsLong =
                        long.Parse(
                            dump.Value,
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture);
                    break;

                case AssetValueType.Float:
                    field.AsFloat =
                        ParseSingle(dump.Value);
                    break;

                case AssetValueType.Double:
                    field.AsDouble =
                        ParseDouble(dump.Value);
                    break;

                case AssetValueType.String:
                    field.AsString =
                        ParseDumpString(dump.Value);
                    break;

                default:
                    throw new NotSupportedException(
                        $"Unsupported runtime scalar type " +
                        $"'{field.Value.ValueType}' " +
                        $"for dump line {dump.LineNumber} " +
                        $"({dump.Type} {dump.FieldName}).");
            }
        }

        private static byte[] ApplyTextDumpToBaseField(
            string inputFile,
            AssetsTools.NET.AssetTypeValueField baseField)
        {
            DebugStr(
                "[TXT] Applying FULL checked dump with structural preflight.");

            var matches = BuildDumpTargetMatches(
                inputFile,
                baseField,
                true);

            DebugStr(
                $"[TXT] FULL checked structural preflight passed for {matches.Count} fields. Applying values now.");

            foreach (var match in matches)
            {
                try
                {
                    ApplyDumpValue(match.Target.Field, match.Dump);
                }
                catch (Exception ex)
                {
                    throw new InvalidDataException(
                        $"Unable to apply checked FULL field path='{match.Dump.Path}', line={match.Dump.LineNumber}.",
                        ex);
                }
            }

            byte[] data = baseField.WriteToByteArray();

            DebugStr(
                $"[TXT] BaseField reserialized: {data.Length} bytes SHA256={Sha256Hex(data)}");

            return data;
        }

        private static bool AreFloatsEqual(
            float expected,
            float actual)
        {
            if (float.IsNaN(expected) &&
                float.IsNaN(actual))
            {
                return true;
            }

            if (float.IsPositiveInfinity(expected) &&
                float.IsPositiveInfinity(actual))
            {
                return true;
            }

            if (float.IsNegativeInfinity(expected) &&
                float.IsNegativeInfinity(actual))
            {
                return true;
            }

            if (float.IsNaN(expected) ||
                float.IsNaN(actual))
            {
                return false;
            }

            float tolerance =
                Math.Max(
                    1e-6f,
                    Math.Abs(expected) * 1e-6f);

            return Math.Abs(expected - actual) <= tolerance;
        }

        private static bool AreDoublesEqual(
            double expected,
            double actual)
        {
            if (double.IsNaN(expected) &&
                double.IsNaN(actual))
            {
                return true;
            }

            if (double.IsPositiveInfinity(expected) &&
                double.IsPositiveInfinity(actual))
            {
                return true;
            }

            if (double.IsNegativeInfinity(expected) &&
                double.IsNegativeInfinity(actual))
            {
                return true;
            }

            if (double.IsNaN(expected) ||
                double.IsNaN(actual))
            {
                return false;
            }

            double tolerance =
                Math.Max(
                    1e-12,
                    Math.Abs(expected) * 1e-12);

            return Math.Abs(expected - actual) <= tolerance;
        }

        private static void ValidateDumpAgainstBaseField(
            string inputFile,
            AssetsTools.NET.AssetTypeValueField baseField)
        {
            var matches = BuildDumpTargetMatches(
                inputFile,
                baseField,
                true);

            DebugStr(
                $"[CHECK] FINAL dump structural mapping passed: {matches.Count} fields.");

            foreach (var match in matches)
            {
                var dump = match.Dump;
                var target = match.Target.Field;
                string targetName = match.Target.FieldName;

                if (target.Value.ValueType == AssetValueType.String)
                {
                    string expectedString = ParseDumpString(dump.Value);
                    string actualString = target.AsString ?? "";
                    if (!string.Equals(expectedString, actualString, StringComparison.Ordinal))
                    {
                        throw new InvalidDataException(
                            $"FINAL CHECK: string mismatch at '{dump.Path}': expectedLength={expectedString.Length}, actualLength={actualString.Length}.");
                    }
                    continue;
                }

                if (target.Value.ValueType == AssetValueType.Float)
                {
                    float expectedFloat = ParseSingle(dump.Value);
                    if (!AreFloatsEqual(expectedFloat, target.AsFloat))
                    {
                        throw new InvalidDataException(
                            $"FINAL CHECK: float mismatch at '{dump.Path}': dump='{dump.Value}' actual='{target.AsFloat.ToString("R", CultureInfo.InvariantCulture)}'.");
                    }
                    continue;
                }

                if (target.Value.ValueType == AssetValueType.Double)
                {
                    double expectedDouble = ParseDouble(dump.Value);
                    if (!AreDoublesEqual(expectedDouble, target.AsDouble))
                    {
                        throw new InvalidDataException(
                            $"FINAL CHECK: double mismatch at '{dump.Path}': dump='{dump.Value}' actual='{target.AsDouble.ToString("R", CultureInfo.InvariantCulture)}'.");
                    }
                    continue;
                }

                string actualValue = ReadFieldAsDumpValue(target);

                if (target.Value.ValueType == AssetValueType.UInt8 ||
                    target.Value.ValueType == AssetValueType.Int8 ||
                    target.Value.ValueType == AssetValueType.UInt16 ||
                    target.Value.ValueType == AssetValueType.Int16 ||
                    target.Value.ValueType == AssetValueType.UInt32 ||
                    target.Value.ValueType == AssetValueType.Int32 ||
                    target.Value.ValueType == AssetValueType.UInt64 ||
                    target.Value.ValueType == AssetValueType.Int64)
                {
                    string normalizedDump = NormalizeNumericLiteral(dump.Value);
                    string normalizedActual = NormalizeNumericLiteral(actualValue);

                    if (!string.Equals(normalizedDump, normalizedActual, StringComparison.Ordinal))
                    {
                        throw new InvalidDataException(
                            $"FINAL CHECK: numeric mismatch at '{dump.Path}': dump='{dump.Value}' actual='{actualValue}'.");
                    }
                }
                else if (!string.Equals(actualValue, dump.Value, StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        $"FINAL CHECK: value mismatch at '{dump.Path}': dump='{dump.Value}' actual='{actualValue}'.");
                }
            }
        }

        private static string ReadFieldAsDumpValue(
            AssetsTools.NET.AssetTypeValueField field)
        {
            switch (field.Value.ValueType)
            {
                case AssetValueType.Bool:
                    return field.AsBool
                        ? "true"
                        : "false";

                case AssetValueType.UInt8:
                    return field.AsUInt.ToString(
                        CultureInfo.InvariantCulture);

                case AssetValueType.Int8:
                    return field.AsInt.ToString(
                        CultureInfo.InvariantCulture);

                case AssetValueType.UInt16:
                    return field.AsUInt.ToString(
                        CultureInfo.InvariantCulture);

                case AssetValueType.Int16:
                    return field.AsInt.ToString(
                        CultureInfo.InvariantCulture);

                case AssetValueType.UInt32:
                    return field.AsUInt.ToString(
                        CultureInfo.InvariantCulture);

                case AssetValueType.Int32:
                    return field.AsInt.ToString(
                        CultureInfo.InvariantCulture);

                case AssetValueType.UInt64:
                    return field.AsULong.ToString(
                        CultureInfo.InvariantCulture);

                case AssetValueType.Int64:
                    return field.AsLong.ToString(
                        CultureInfo.InvariantCulture);

                case AssetValueType.Float:
                    return field.AsFloat.ToString(
                        "R",
                        CultureInfo.InvariantCulture);

                case AssetValueType.Double:
                    return field.AsDouble.ToString(
                        "R",
                        CultureInfo.InvariantCulture);

                case AssetValueType.String:
                    return field.AsString;

                default:
                    throw new NotSupportedException(
                        "Unsupported field type in final validation: " +
                        field.Value.ValueType);
            }
        }
    }
}