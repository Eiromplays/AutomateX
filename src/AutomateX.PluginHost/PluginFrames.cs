using System.Buffers.Binary;
using System.Text;
using System.Text.Json.Nodes;

namespace AutomateX.PluginHost;

// The wire framing for host↔plugin: a 4-byte big-endian length prefix, then UTF-8 JSON. Same codec
// both directions so the engine and the child agree by construction.
public static class PluginFrames
{
    public static void Write(Stream stream, JsonNode message)
    {
        var payload = Encoding.UTF8.GetBytes(message.ToJsonString());
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, payload.Length);
        stream.Write(length);
        stream.Write(payload);
        stream.Flush();
    }

    public static bool TryRead(Stream stream, out JsonObject message)
    {
        message = null!;
        var lengthBytes = new byte[4];
        if (!ReadExactly(stream, lengthBytes))
        {
            return false;
        }

        var length = BinaryPrimitives.ReadInt32BigEndian(lengthBytes);
        var payload = new byte[length];
        if (!ReadExactly(stream, payload))
        {
            return false;
        }

        message = JsonNode.Parse(payload) as JsonObject
            ?? throw new InvalidOperationException("Plugin frame was not a JSON object.");
        return true;
    }

    private static bool ReadExactly(Stream stream, byte[] buffer)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = stream.Read(buffer, offset, buffer.Length - offset);
            if (read <= 0)
            {
                return false; // clean EOF (pipe closed)
            }

            offset += read;
        }

        return true;
    }
}
