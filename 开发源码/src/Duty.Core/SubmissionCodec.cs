using System.Buffers.Binary;
using System.IO.Hashing;
using System.Text;

namespace Duty.Core;

public static class SubmissionCodec
{
    public const string Prefix = "PB1";
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);

    // PB1 covers the protocol version. Payload: name length, flags, 5 packed bytes, UTF-8 name, CRC32.
    public static string Encode(Person person)
    {
        var name = Utf8.GetBytes(person.Name);
        var payload = new byte[7 + name.Length + 4];
        payload[0] = checked((byte)name.Length);
        payload[1] = (byte)((person.Target == 4 ? 1 : 0) | (person.Special ? 2 : 0));
        for (var s = 0; s < 20; s++)
            payload[2 + s / 4] |= (byte)(person.Preferences[s] << (6 - 2 * (s % 4)));
        name.CopyTo(payload, 7);
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(payload.Length - 4), Checksum(payload.AsSpan(0, payload.Length - 4)));
        var body = EncodeBase32(payload);
        return Prefix + "-" + string.Join("-", Enumerable.Range(0, (body.Length + 7) / 8)
            .Select(i => body.Substring(i * 8, Math.Min(8, body.Length - i * 8))));
    }

    public static Person Decode(string input)
    {
        if (input.Length > 512) throw new FormatException("填报码过长。");
        var compact = new string(input.Where(c => c != '-' && c is not (' ' or '\t' or '\r' or '\n')).ToArray()).ToUpperInvariant();
        if (!compact.StartsWith(Prefix, StringComparison.Ordinal)) throw new FormatException("无法识别填报码版本，应以PB1开头。");
        var body = compact[Prefix.Length..];
        var bytes = DecodeBase32(body);
        if (bytes.Length < 12) throw new FormatException("填报码不完整，数据长度不足。");
        if (bytes.Length != 7 + bytes[0] + 4) throw new FormatException("填报码长度不匹配，可能有漏字或多字。");
        var actual = Checksum(bytes.AsSpan(0, bytes.Length - 4));
        if (BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(bytes.Length - 4)) != actual)
            throw new FormatException("校验码不匹配，填报码可能损坏，请重新复制。");
        if ((bytes[1] & ~3) != 0) throw new FormatException("填报码包含未知选项。");
        var preferences = new byte[20];
        for (var s = 0; s < 20; s++)
        {
            preferences[s] = (byte)((bytes[2 + s / 4] >> (6 - 2 * (s % 4))) & 3);
            if (preferences[s] == 3) throw new FormatException("填报码包含非法的时间状态。");
        }
        if (((bytes[1] & 2) != 0) != (preferences[7] > 0))
            throw new FormatException("特殊班与周二第四节不一致。");
        try
        {
            var name = Utf8.GetString(bytes, 7, bytes[0]);
            var person = new Person(name, (bytes[1] & 1) == 0 ? 3 : 4, preferences);
            if (person.Name != name) throw new FormatException("姓名格式不规范。");
            return person;
        }
        catch (Exception e) when (e is ArgumentException)
        { throw new FormatException("姓名或时间数据无效：" + e.Message); }
    }

    private static uint Checksum(ReadOnlySpan<byte> payload)
    {
        var data = new byte[3 + payload.Length];
        Encoding.ASCII.GetBytes(Prefix).CopyTo(data, 0);
        payload.CopyTo(data.AsSpan(3));
        return Crc32.HashToUInt32(data);
    }

    internal static string EncodeBase32(ReadOnlySpan<byte> bytes)
    {
        var output = new StringBuilder();
        var buffer = 0;
        var bits = 0;
        foreach (var value in bytes)
        {
            buffer = (buffer << 8) | value;
            bits += 8;
            while (bits >= 5)
            {
                bits -= 5;
                output.Append(Alphabet[(buffer >> bits) & 31]);
            }
            buffer &= (1 << bits) - 1;
        }
        if (bits > 0) output.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        return output.ToString();
    }

    private static byte[] DecodeBase32(string text)
    {
        if (text.Length == 0) throw new FormatException("填报码正文为空。");
        var bytes = new List<byte>();
        var buffer = 0;
        var bits = 0;
        foreach (var c in text)
        {
            var value = Alphabet.IndexOf(c);
            if (value < 0) throw new FormatException("填报码只能包含规定的英文字母、数字及分组横线。");
            buffer = (buffer << 5) | value;
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                bytes.Add((byte)(buffer >> bits));
                buffer &= (1 << bits) - 1;
            }
        }
        var result = bytes.ToArray();
        if (buffer != 0 || EncodeBase32(result) != text)
            throw new FormatException("填报码尾部不完整或格式不规范。");
        return result;
    }
}
