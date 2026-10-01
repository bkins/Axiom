using System.Buffers.Binary;

namespace Axiom.Infrastructure;

internal static class ExportImageDimensions
{
    public static (int Width, int Height) Read(byte[] bytes, string mediaType)
    {
        var data = bytes.AsSpan();
        var dimensions = (Width: 0, Height: 0);
        if (mediaType == "image/png" && data.Length >= 24 && data[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            dimensions = (BinaryPrimitives.ReadInt32BigEndian(data[16..20]), BinaryPrimitives.ReadInt32BigEndian(data[20..24]));
        else if (mediaType == "image/gif" && data.Length >= 10 && (data[..6].SequenceEqual("GIF87a"u8) || data[..6].SequenceEqual("GIF89a"u8)))
            dimensions = (BinaryPrimitives.ReadUInt16LittleEndian(data[6..8]), BinaryPrimitives.ReadUInt16LittleEndian(data[8..10]));
        else if (mediaType == "image/bmp" && data.Length >= 26 && data[..2].SequenceEqual("BM"u8))
            dimensions = (BinaryPrimitives.ReadInt32LittleEndian(data[18..22]), Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(data[22..26])));
        else if (mediaType == "image/jpeg" && data.Length > 2 && data[0] == 0xff && data[1] == 0xd8)
        {
            var offset = 2;
            while (offset + 9 < data.Length)
            {
                if (data[offset] != 0xff) break;
                var marker = data[offset + 1];
                var length = BinaryPrimitives.ReadUInt16BigEndian(data[(offset + 2)..(offset + 4)]);
                if (marker is 0xc0 or 0xc1 or 0xc2) { dimensions = (BinaryPrimitives.ReadUInt16BigEndian(data[(offset + 7)..(offset + 9)]), BinaryPrimitives.ReadUInt16BigEndian(data[(offset + 5)..(offset + 7)])); break; }
                if (length < 2) break;
                offset += length + 2;
            }
        }
        if (dimensions.Width <= 0 || dimensions.Height <= 0 || dimensions.Width > 50000 || dimensions.Height > 50000) throw new IOException("Image header or dimensions are invalid/unsupported.");
        return dimensions;
    }
}
