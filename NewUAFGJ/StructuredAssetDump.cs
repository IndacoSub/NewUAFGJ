using System.Globalization;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace NewUAFGJ;

internal static class StructuredAssetDump
{
    private static readonly NumberFormatInfo DecimalComma = new() { NumberDecimalSeparator = "," };

    private enum FontLayout
    {
        None,
        Native,
        TextMeshPro,
        LegacyTextMeshPro
    }

    public static byte[] Import(AssetsFile file, AssetFileInfo asset, byte[] dump, ReplacementTypeTree resolver,
        string? typeTreeSource)
    {
        AssetClassID assetClass = (AssetClassID)asset.TypeId;
        AssetTypeValueField root;
        if (assetClass == AssetClassID.MonoBehaviour && !ReplacementTypeTree.HasEmbedded(file, asset) &&
            typeTreeSource is not null && resolver.OriginalFontDump(typeTreeSource) is { } original)
        {
            root = ReadVerifiedFont(file, asset, original);
        }
        else
        {
            var template = new AssetTypeTemplateField();
            template.FromTypeTree(resolver.Resolve(file, asset, typeTreeSource));
            root = SerializedObject.ReadFields(file, asset, template);
        }

        bool bigEndian = file.Header.Endianness;

        FontLayout fontLayout = GetFontLayout(assetClass, root);
        string[] identityPaths = assetClass switch
        {
            AssetClassID.Sprite => ["m_Name", "m_RD.texture", "m_RD.alphaTexture", "m_SpriteAtlas"],
            AssetClassID.RectTransform => ["m_GameObject", "m_Father"],
            AssetClassID.GameObject => ["m_Name", "m_Component"],
            _ => ["m_GameObject", "m_Script", "m_Name"]
        };
        IEnumerable<AssetTypeValueField> identityFields = identityPaths.Select(path => root[path]).Where(field => !field.IsDummy);
        if (fontLayout != FontLayout.None || assetClass == AssetClassID.Material)
            identityFields = identityFields.Concat(PointerFields(root)).Distinct();

        var identity = identityFields
            .Select(field => (Field: field, Bytes: field.WriteToByteArray(bigEndian)))
            .ToArray();

        ApplyDump(root, dump, assetClass, fontLayout, allowResize: assetClass == AssetClassID.Sprite);

        foreach (var (field, before) in identity)
        {
            if (!field.WriteToByteArray(bigEndian).AsSpan().SequenceEqual(before))
                throw new InvalidDataException($"The dump changes the {assetClass}'s {field.FieldName}.");
        }

        return root.WriteToByteArray(bigEndian);
    }

    private static AssetTypeValueField ReadVerifiedFont(AssetsFile file, AssetFileInfo asset, byte[] original)
    {
        const AssetClassID assetClass = AssetClassID.MonoBehaviour;
        using StringReader text = UabeaDumpText.Open(original);
        var reader = new DumpReader(text, assetClass, FontLayout.None);
        var root = ValueBuilder.DefaultValueFieldFromTemplate(OriginalFontDump.ReadTemplate(reader));
        ApplyDump(root, original, assetClass, FontLayout.None, allowResize: true);
        if (GetFontLayout(assetClass, root) is not (FontLayout.TextMeshPro or FontLayout.LegacyTextMeshPro))
            throw new InvalidDataException("An original dump source requires a TextMeshPro font.");

        byte[] originalBytes = SerializedObject.ReadBytes(file, asset);
        if (!root.WriteToByteArray(file.Header.Endianness).AsSpan().SequenceEqual(originalBytes))
            throw new InvalidDataException("The original dump does not match the selected object's serialized bytes.");

        return root;
    }

    private static void ApplyDump(AssetTypeValueField root, byte[] dump, AssetClassID assetClass, FontLayout fontLayout,
        bool allowResize)
    {
        using StringReader text = UabeaDumpText.Open(dump);
        var reader = new DumpReader(text, assetClass, fontLayout);
        ApplyField(reader, root, 0, allowResize);
        if (reader.ReadLine() is not null)
            throw reader.Error($"Unexpected content after the {assetClass}.");
    }

    private static FontLayout GetFontLayout(AssetClassID assetClass, AssetTypeValueField root)
    {
        if (assetClass == AssetClassID.Font)
            return FontLayout.Native;
        if (assetClass != AssetClassID.MonoBehaviour)
            return FontLayout.None;

        var glyphs = root["m_GlyphTable"];
        var characters = root["m_CharacterTable"];
        var atlases = root["m_AtlasTextures.Array"];
        if (!glyphs.IsDummy && glyphs.TypeName == "Glyph" &&
            !characters.IsDummy && characters.TypeName == "TMP_Character" &&
            !atlases.IsDummy && atlases.TemplateField.IsArray && atlases.TemplateField.Children.Count == 2 &&
            IsPointer(atlases.TemplateField.Children[1]))
        {
            return FontLayout.TextMeshPro;
        }

        var face = root["m_fontInfo"];
        var legacyGlyphs = root["m_glyphInfoList"];
        var atlas = root["atlas"];
        if (!face.IsDummy && face.TypeName == "FaceInfo" &&
            !legacyGlyphs.IsDummy && legacyGlyphs.TypeName == "TMP_Glyph" &&
            !atlas.IsDummy && IsPointer(atlas.TemplateField))
        {
            return FontLayout.LegacyTextMeshPro;
        }

        return FontLayout.None;
    }

    private static bool IsPointer(AssetTypeTemplateField template) => template.Type.StartsWith("PPtr<", StringComparison.Ordinal);

    private static bool HasPointer(AssetTypeTemplateField template) =>
        IsPointer(template) || template.Children.Any(HasPointer);

    private static IEnumerable<AssetTypeValueField> PointerFields(AssetTypeValueField field)
    {
        if (IsPointer(field.TemplateField))
        {
            yield return field;
            yield break;
        }

        foreach (AssetTypeValueField child in field.Children)
        foreach (AssetTypeValueField pointer in PointerFields(child))
            yield return pointer;
    }

    private static void ApplyField(DumpReader reader, AssetTypeValueField field, int depth, bool allowResize, string? name = null)
    {
        AssetTypeTemplateField template = field.TemplateField;
        allowResize |= depth == 1 && reader.IsResizableFontField(template) && !HasPointer(template);
        string label = FieldLabel(template, depth, name: name);
        if (template.IsArray)
        {
            ApplyArray(reader, field, depth, label, allowResize);
            return;
        }

        if (template.ValueType == AssetValueType.None)
        {
            if (field.Children is [var array] && array.TemplateField is { IsArray: true, Type: "Array", Name: "Array" } &&
                reader.NextIsArray(name ?? template.Name, depth))
            {
                string arrayName = name ?? template.Name;
                ApplyArray(reader, array, depth, FieldLabel(array.TemplateField, depth, name: arrayName), allowResize, arrayName);
                return;
            }

            reader.Expect(label, AlternateStructLabel(template, depth, name));
            foreach (AssetTypeValueField child in field.Children)
                ApplyField(reader, child, depth + 1, allowResize);

            return;
        }

        ReadOnlySpan<char> value = reader.ReadValue(label);
        try
        {
            ApplyValue(field, value);
        }
        catch (Exception error) when (error is FormatException or OverflowException or InvalidDataException)
        {
            throw reader.Error($"Invalid value for {template.Type} {template.Name}.", error);
        }
    }

    private static void ApplyArray(DumpReader reader, AssetTypeValueField field, int depth, string label, bool allowResize,
        string? flattenedName = null)
    {
        AssetTypeTemplateField template = field.TemplateField;
        bool isBytes = template.ValueType == AssetValueType.ByteArray;
        if (template.ValueType != AssetValueType.Array && (!allowResize || !isBytes))
            throw reader.Error($"Unsupported array type {template.Type}.");

        // UABEA exports can vary in their alignment marker. Serialization follows the TypeTree.
        int count = field.Children.Count;
        string? alternateLabel = template.Type == "Array"
            ? FieldLabel(template, depth, alignment: template.IsAligned ? 0 : 1, name: flattenedName)
            : null;
        if (allowResize)
            count = reader.ReadArrayCount(label, alternateLabel);
        else
        {
            string suffix = $" ({count.ToString(CultureInfo.InvariantCulture)} items)";
            reader.Expect(label + suffix, alternateLabel is null ? null : alternateLabel + suffix);
        }

        reader.Expect($"{FieldLabel(template.Children[0], depth + 1)} = {count.ToString(CultureInfo.InvariantCulture)}");
        AssetTypeTemplateField element = template.Children[1];
        if (count > 0 && element.Type.Length == 0)
            throw reader.Error("The original dump has no elements to describe this array.");

        var bytes = isBytes ? new List<byte>() : null;
        var children = allowResize && !isBytes ? new List<AssetTypeValueField>() : null;
        string indent = new string(' ', depth + 1);
        for (int i = 0; i < count; i++)
        {
            reader.ExpectArrayIndex($"{indent}[{i.ToString(CultureInfo.InvariantCulture)}]");
            if (bytes is not null)
            {
                ReadOnlySpan<char> value = reader.ReadValue(FieldLabel(element, depth + 2, name: flattenedName));
                if (!byte.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out byte next))
                    throw reader.Error("Invalid byte array value.");

                bytes.Add(next);
                continue;
            }

            var child = children is null ? field.Children[i] : ValueBuilder.DefaultValueFieldFromTemplate(element);
            ApplyField(reader, child, depth + 2, allowResize, flattenedName);
            children?.Add(child);
        }

        if (bytes is not null)
            field.AsByteArray = bytes.ToArray();
        else if (children is not null)
        {
            field.Children = children;
            field.AsArray = new AssetTypeArrayInfo(count);
        }
    }

    private static void ApplyValue(AssetTypeValueField field, ReadOnlySpan<char> value)
    {
        var culture = CultureInfo.InvariantCulture;
        field.Value.AsObject = field.Value.ValueType switch
        {
            AssetValueType.String when UabeaDumpText.IsQuoted(value) => UabeaDumpText.Unescape(value[1..^1]),
            AssetValueType.Bool => bool.Parse(value),
            AssetValueType.Int8 => sbyte.Parse(value, culture),
            AssetValueType.UInt8 => byte.Parse(value, culture),
            AssetValueType.Int16 => short.Parse(value, culture),
            AssetValueType.UInt16 => ushort.Parse(value, culture),
            AssetValueType.Int32 => int.Parse(value, culture),
            AssetValueType.UInt32 => uint.Parse(value, culture),
            AssetValueType.Int64 => long.Parse(value, culture),
            AssetValueType.UInt64 => ulong.Parse(value, culture),
            AssetValueType.Float => float.Parse(value, NumberStyles.Float, DecimalFormat(value)),
            AssetValueType.Double => double.Parse(value, NumberStyles.Float, DecimalFormat(value)),
            _ => throw new InvalidDataException("Expected a supported primitive value.")
        };
    }

    // UABEA exports floating-point values using the exporter's locale.
    private static IFormatProvider DecimalFormat(ReadOnlySpan<char> value) =>
        value.Contains(',') ? DecimalComma : CultureInfo.InvariantCulture;

    private static string? AlternateStructLabel(AssetTypeTemplateField template, int depth, string? name)
    {
        string? alternate = template.Type switch
        {
            "Color" or "ColorRGBA" when HasScalarChildren(template, AssetValueType.Float, "r", "g", "b", "a")
                => template.Type == "Color" ? "ColorRGBA" : "Color",
            "Color32" or "ColorRGBA" when HasScalarChildren(template, AssetValueType.UInt32, "rgba")
                => template.Type == "Color32" ? "ColorRGBA" : "Color32",
            "Vector4" or "Vector4f" when HasScalarChildren(template, AssetValueType.Float, "x", "y", "z", "w")
                => template.Type == "Vector4" ? "Vector4f" : "Vector4",
            _ => null
        };
        return alternate is null ? null : FieldLabel(template, depth, typeName: alternate, name: name);
    }

    private static bool HasScalarChildren(AssetTypeTemplateField template, AssetValueType valueType, params string[] names) =>
        template.Children.Count == names.Length &&
        template.Children.Zip(names, (child, name) =>
            child.Name == name && child.ValueType == valueType && !child.IsArray && child.Children.Count == 0).All(match => match);

    private static string FieldLabel(AssetTypeTemplateField template, int depth, string? typeName = null, int? alignment = null,
        string? name = null)
    {
        int aligned = alignment ?? (template.IsAligned || template.ValueType == AssetValueType.String ? 1 : 0);
        return $"{new string(' ', depth)}{aligned} {typeName ?? template.Type} {name ?? template.Name}";
    }

    private static class OriginalFontDump
    {
        public static AssetTypeTemplateField ReadTemplate(DumpReader reader, int depth = 0)
        {
            string indent = new(' ', depth);
            string? line = reader.PeekLine();
            if (line is null || line.Length < depth + 3 || depth > 32 ||
                !line.StartsWith(indent, StringComparison.Ordinal) || line[depth] is not ('0' or '1') ||
                line[depth + 1] != ' ')
            {
                throw reader.Error("Expected a typed field in the original dump.");
            }

            bool aligned = line[depth] == '1';
            string arrayLabel = indent + line[depth] + " Array Array";
            if (line.StartsWith(arrayLabel + " (", StringComparison.Ordinal))
                return ReadArrayTemplate(reader, depth, arrayLabel, aligned);

            reader.ReadLine();
            string declaration = line[(depth + 2)..];
            int assignment = declaration.IndexOf(" = ", StringComparison.Ordinal);
            string label = assignment >= 0 ? declaration[..assignment] : declaration;
            int separator = label.LastIndexOf(' ');
            if (separator <= 0 || separator == label.Length - 1)
                throw reader.Error("Expected a type and field name in the original dump.");

            string type = label[..separator];
            string name = label[(separator + 1)..];
            AssetValueType valueType = AssetTypeValueField.GetValueTypeByTypeName(type);
            if ((assignment >= 0) != (valueType != AssetValueType.None))
                throw reader.Error($"Unexpected value or field structure for {type} {name}.");

            var field = Template(type, name, valueType, aligned);
            string childIndent = indent + " ";
            if (valueType == AssetValueType.None)
            {
                while (reader.PeekLine()?.StartsWith(childIndent, StringComparison.Ordinal) == true)
                    field.Children.Add(ReadTemplate(reader, depth + 1));
            }
            return field;
        }

        private static AssetTypeTemplateField ReadArrayTemplate(DumpReader reader, int depth, string label, bool aligned)
        {
            int count = reader.ReadArrayCount(label, null);
            string indent = new(' ', depth + 1);
            reader.Expect($"{indent}0 int size = {count.ToString(CultureInfo.InvariantCulture)}");
            var element = Template("", "data", AssetValueType.None, false);
            if (count > 0)
            {
                reader.ExpectArrayIndex(indent + "[0]");
                element = ReadTemplate(reader, depth + 2);
                if (element.Name != "data")
                    throw reader.Error("Expected a data element in the original array.");

                // The first item describes the layout. The import pass checks every item.
                while (reader.PeekLine()?.StartsWith(indent, StringComparison.Ordinal) == true)
                    reader.ReadLine();
            }

            var array = Template("Array", "Array", element.ValueType == AssetValueType.UInt8
                ? AssetValueType.ByteArray : AssetValueType.Array, aligned);
            array.IsArray = true;
            array.Children = [Template("int", "size", AssetValueType.Int32, false), element];
            return array;
        }

        private static AssetTypeTemplateField Template(string type, string name, AssetValueType valueType, bool aligned) =>
            new() { Type = type, Name = name, ValueType = valueType, HasValue = valueType != AssetValueType.None,
                IsAligned = aligned, Children = [] };
    }

    private sealed class DumpReader(StringReader reader, AssetClassID assetClass, FontLayout fontLayout)
    {
        private int lineNumber;
        private string? pendingLine;

        public bool IsResizableFontField(AssetTypeTemplateField field) => fontLayout switch
        {
            FontLayout.TextMeshPro => (field.Name, field.Type) is
                ("m_GlyphTable", "Glyph") or ("m_CharacterTable", "TMP_Character") or
                ("m_UsedGlyphRects", "GlyphRect") or ("m_FreeGlyphRects", "GlyphRect") ||
                field is
                {
                    Name: "m_FontFeatureTable",
                    Type: "TMP_FontFeatureTable",
                    Children:
                    [
                        {
                            Name: "m_GlyphPairAdjustmentRecords",
                            Type: "TMP_GlyphPairAdjustmentRecord",
                            Children: [{ IsArray: true, Type: "Array", Name: "Array" }]
                        }
                    ]
                },
            FontLayout.LegacyTextMeshPro => (field.Name, field.Type) is
                ("m_glyphInfoList", "TMP_Glyph") or ("m_kerningInfo", "KerningTable"),
            FontLayout.Native => (field.Name, field.Type) is
                ("m_CharacterRects", "vector") or ("m_KerningValues", "map") or
                ("m_FontData", "vector") or ("m_FontNames", "vector"),
            _ => false
        };

        public string? ReadLine()
        {
            if (pendingLine is { } pending)
            {
                pendingLine = null;
                return pending;
            }

            while (reader.ReadLine() is { } line)
            {
                lineNumber++;
                if (!string.IsNullOrWhiteSpace(line))
                    return line.TrimEnd();
            }

            return null;
        }

        public void Expect(string label, string? alternate = null)
        {
            string? line = ReadLine();
            if (!string.Equals(line, label, StringComparison.Ordinal) &&
                (alternate is null || !string.Equals(line, alternate, StringComparison.Ordinal)))
                throw Error($"Expected {label.TrimStart()}.");
        }

        public string? PeekLine() => pendingLine ??= ReadLine();

        public bool NextIsArray(string name, int depth)
        {
            string indent = new(' ', depth);
            return PeekLine() is { } line &&
                (line.StartsWith($"{indent}0 Array {name} (", StringComparison.Ordinal) ||
                 line.StartsWith($"{indent}1 Array {name} (", StringComparison.Ordinal));
        }

        public void ExpectArrayIndex(string label)
        {
            if (assetClass != AssetClassID.Font)
            {
                Expect(label);
                return;
            }

            // Some Font dumps list the typed array elements directly after the size.
            string? line = ReadLine();
            if (line is not null && !line.TrimStart().StartsWith("[", StringComparison.Ordinal))
                pendingLine = line;
            else if (!string.Equals(line, label, StringComparison.Ordinal))
                throw Error($"Expected {label.TrimStart()}.");
        }

        public int ReadArrayCount(string label, string? alternateLabel)
        {
            const string suffix = " items)";
            string prefix = label + " (";
            string? line = ReadLine();
            if (alternateLabel is not null && line is not null)
            {
                string alternatePrefix = alternateLabel + " (";
                if (line.StartsWith(alternatePrefix, StringComparison.Ordinal))
                    prefix = alternatePrefix;
            }

            if (line is null || !line.StartsWith(prefix, StringComparison.Ordinal) ||
                !line.EndsWith(suffix, StringComparison.Ordinal) ||
                !int.TryParse(line.AsSpan(prefix.Length, line.Length - prefix.Length - suffix.Length),
                    NumberStyles.None, CultureInfo.InvariantCulture, out int count))
            {
                throw Error($"Expected an array item count for {label.TrimStart()}.");
            }

            return count;
        }

        public ReadOnlySpan<char> ReadValue(string label)
        {
            string? line = ReadLine();
            if (line is null || !line.StartsWith(label + " = ", StringComparison.Ordinal))
                throw Error($"Expected a value for {label.TrimStart()}.");

            return line.AsSpan(label.Length + 3).Trim();
        }

        public InvalidDataException Error(string message, Exception? inner = null) =>
            new($"UABEA {assetClass} dump line {lineNumber}: {message}", inner);
    }
}
