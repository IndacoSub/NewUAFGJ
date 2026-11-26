using System.Text;

namespace NewUAFGJ;

internal static class UabeaDumpText
{
    public static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static bool HasRoot(ReadOnlySpan<byte> input, ReadOnlySpan<byte> root) => GetRoot(input).SequenceEqual(root);

    public static ReadOnlySpan<byte> GetRoot(ReadOnlySpan<byte> input)
    {
        input = WithoutBom(input);
        int headerEnd = input.IndexOfAny((byte)'\r', (byte)'\n');
        ReadOnlySpan<byte> header = headerEnd < 0 ? input : input[..headerEnd];
        return header.Length >= 2 &&
            header[0] is (byte)'0' or (byte)'1' &&
            header[1] == (byte)' ' ? header[2..] : [];
    }

    public static StringReader Open(byte[] input) => new(Utf8.GetString(WithoutBom(input)));

    public static bool IsQuoted(ReadOnlySpan<char> value) => value.Length >= 2 && value[0] == '"' && value[^1] == '"';

    private static ReadOnlySpan<byte> WithoutBom(ReadOnlySpan<byte> input) =>
        input.StartsWith("\uFEFF"u8) ? input[3..] : input;

    public static string Unescape(ReadOnlySpan<char> value)
    {
        var result = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            char character = value[i];
            if (character == '\\')
            {
                if (++i == value.Length)
                    throw new InvalidDataException("The UABEA dump string ends with an incomplete escape.");

                character = value[i] switch
                {
                    '\\' => '\\',
                    ']' => ']',
                    'n' => '\n',
                    'r' => '\r',
                    _ => throw new InvalidDataException("The UABEA dump string contains an unsupported escape.")
                };
            }

            result.Append(character);
        }
        return result.ToString();
    }
}
