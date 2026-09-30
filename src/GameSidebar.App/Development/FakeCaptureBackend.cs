using System.Buffers.Binary;
using System.IO.Compression;
using GameSidebar.Application.Capture;
using GameSidebar.Core.Sessions;

namespace GameSidebar.App.Development;

public sealed class FakeCaptureBackend : IGameCaptureBackend
{
    private int _sequence;
    public Task<CapturePayload> CaptureAsync(WindowIdentity identity, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        const int width = 640, height = 360;
        var raw = new byte[height * (1 + width * 4)];
        var tick = Interlocked.Increment(ref _sequence);
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var offset = y * (1 + width * 4) + 1 + x * 4;
                raw[offset] = (byte)((x + tick * 10) % 256);
                raw[offset + 1] = (byte)((y + tick * 5) % 256);
                raw[offset + 2] = 100;
                raw[offset + 3] = 255;
            }
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, true)) zlib.Write(raw);
        using var png = new MemoryStream();
        png.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4, 4), height);
        header[8] = 8; header[9] = 6;
        Chunk(png, "IHDR", header);
        Chunk(png, "IDAT", compressed.ToArray());
        Chunk(png, "IEND", []);
        return Task.FromResult(new CapturePayload(png.ToArray(), "Demo Fixture"));
    }
    private static void Chunk(Stream output, string type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        output.Write(length);
        var name = System.Text.Encoding.ASCII.GetBytes(type);
        output.Write(name); output.Write(data);
        uint crc = 0xffffffff;
        foreach (var value in name.Concat(data))
        {
            crc ^= value;
            for (var i = 0; i < 8; i++) crc = (crc >> 1) ^ (0xedb88320u & (uint)-(int)(crc & 1));
        }
        BinaryPrimitives.WriteUInt32BigEndian(length, ~crc);
        output.Write(length);
    }
    public Task ResetAsync() => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
