namespace NewUAFGJ;

internal static class UabeaTextAssetDump
{
    public static byte[] DecodeIfPresent(byte[] input)
    {
        if (!UabeaDumpText.HasRoot(input, "TextAsset Base"u8))
            return input;

        using var reader = UabeaDumpText.Open(input);
        reader.ReadLine();

        string? script = null;
        while (reader.ReadLine() is { } line)
        {
            ReadOnlySpan<char> field = line.AsSpan().Trim();
            if (field.IsEmpty)
                continue;

            if (!field.StartsWith("0 string ", StringComparison.Ordinal) &&
                !field.StartsWith("1 string ", StringComparison.Ordinal))
            {
                throw new InvalidDataException("Expected a string field in the UABEA TextAsset dump.");
            }

            field = field["0 string ".Length..];
            int equals = field.IndexOf('=');
            if (equals < 0)
                throw new InvalidDataException("Expected a field assignment in the UABEA TextAsset dump.");

            ReadOnlySpan<char> name = field[..equals].TrimEnd();
            ReadOnlySpan<char> value = field[(equals + 1)..].Trim();
            if (!UabeaDumpText.IsQuoted(value))
                throw new InvalidDataException("Expected a quoted string in the UABEA TextAsset dump.");

            if (name.SequenceEqual("m_Script"))
            {
                if (script is not null)
                    throw new InvalidDataException("The UABEA TextAsset dump contains multiple m_Script fields.");

                // UABEA quotes the value but leaves embedded quotes verbatim.
                script = UabeaDumpText.Unescape(value[1..^1]);
            }
        }

        if (script is null)
            throw new InvalidDataException("The UABEA TextAsset dump does not contain an m_Script field.");

        return UabeaDumpText.Utf8.GetBytes(script);
    }
}
