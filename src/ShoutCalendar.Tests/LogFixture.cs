using System.Buffers.Binary;
using System.Text;

namespace ShoutCalendar.Tests;

internal static class LogFixture
{
    public static byte[] File(uint begin, params byte[][] entries)
    {
        var bodyLength = entries.Sum(entry => entry.Length);
        var file = new byte[8 + (entries.Length * 4) + bodyLength];
        BinaryPrimitives.WriteUInt32LittleEndian(file, begin);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(4), begin + (uint)entries.Length);
        var relativeEnd = 0;
        for (var i = 0; i < entries.Length; i++)
        {
            relativeEnd += entries[i].Length;
            BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(8 + (i * 4)), (uint)relativeEnd);
        }

        var offset = 8 + (entries.Length * 4);
        foreach (var entry in entries)
        {
            entry.CopyTo(file.AsSpan(offset));
            offset += entry.Length;
        }

        return file;
    }

    public static byte[] Entry(uint timestamp, byte filter, byte channel, string sender, byte[] message)
    {
        return Entry(timestamp, filter, channel, Encoding.UTF8.GetBytes(sender), message);
    }

    public static byte[] Entry(uint timestamp, byte filter, byte channel, byte[] senderBytes, byte[] message)
    {
        var entry = new byte[9 + senderBytes.Length + 1 + message.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(entry, timestamp);
        entry[4] = filter;
        entry[5] = channel;
        entry[6] = 0x00;
        entry[7] = 0x00;
        entry[8] = 0x1F;
        senderBytes.CopyTo(entry.AsSpan(9));
        entry[9 + senderBytes.Length] = 0x1F;
        message.CopyTo(entry.AsSpan(10 + senderBytes.Length));
        return entry;
    }

    public static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);
}
