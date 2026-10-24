// 自带 PNG 编解码：不解码到 GDI+，避免任何色彩管理 / 位深转换破坏边框 LSB
namespace CxQuickDecode.Core;

internal static class PngCodec
{
    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            t[n] = c;
        }
        return t;
    }

    private static uint Crc32(byte[] data, int off, int len)
    {
        uint c = 0xFFFFFFFFu;
        for (int i = 0; i < len; i++) c = CrcTable[(c ^ data[off + i]) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }

    private static uint Adler32(byte[] data)
    {
        uint a = 1, b = 0;
        foreach (byte v in data)
        {
            a = (a + v) % 65521;
            b = (b + a) % 65521;
        }
        return (b << 16) | a;
    }

    public static bool LooksLikePng(byte[] b) =>
        b.Length > 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47;

    public static Rgba Decode(byte[] file)
    {
        if (!LooksLikePng(file)) throw new InvalidDataException("不是 PNG 文件");
        int pos = 8;
        int w = 0, h = 0, bitDepth = 8, colorType = 0, interlace = 0;
        var idat = new MemoryStream();
        byte[]? palette = null;
        byte[]? trns = null;

        while (pos + 8 <= file.Length)
        {
            int len = (file[pos] << 24) | (file[pos + 1] << 16) | (file[pos + 2] << 8) | file[pos + 3];
            string type = System.Text.Encoding.ASCII.GetString(file, pos + 4, 4);
            int dataOff = pos + 8;
            if (len < 0 || dataOff + len > file.Length) throw new InvalidDataException("PNG 块长度损坏: " + type);

            switch (type)
            {
                case "IHDR":
                    w = (file[dataOff] << 24) | (file[dataOff + 1] << 16) | (file[dataOff + 2] << 8) | file[dataOff + 3];
                    h = (file[dataOff + 4] << 24) | (file[dataOff + 5] << 16) | (file[dataOff + 6] << 8) | file[dataOff + 7];
                    bitDepth = file[dataOff + 8];
                    colorType = file[dataOff + 9];
                    interlace = file[dataOff + 12];
                    break;
                case "PLTE":
                    palette = new byte[len];
                    Buffer.BlockCopy(file, dataOff, palette, 0, len);
                    break;
                case "tRNS":
                    trns = new byte[len];
                    Buffer.BlockCopy(file, dataOff, trns, 0, len);
                    break;
                case "IDAT":
                    idat.Write(file, dataOff, len);
                    break;
                case "IEND":
                    pos = file.Length;
                    break;
            }
            pos = dataOff + len + 4;
        }
        if (w <= 0 || h <= 0) throw new InvalidDataException("PNG 缺少 IHDR");
        if (w * (long)h > 64_000_000) throw new InvalidDataException("PNG 过大: " + w + "x" + h);
        if (interlace != 0) throw new InvalidDataException("暂不支持隔行 PNG（请用普通 PNG）");
        if (bitDepth != 8 && bitDepth != 16) throw new InvalidDataException("暂不支持 " + bitDepth + " bit PNG");

        int channels = colorType switch
        {
            0 => 1,     // gray
            2 => 3,     // rgb
            3 => 1,     // palette
            4 => 2,     // gray+alpha
            6 => 4,     // rgba
            _ => throw new InvalidDataException("不支持的 PNG colorType=" + colorType)
        };
        int bpp = Math.Max(1, channels * bitDepth / 8);
        int stride = (w * channels * bitDepth + 7) / 8;

        byte[] raw = ZlibInflate(idat.ToArray());
        int expected = (stride + 1) * h;
        if (raw.Length < expected) throw new InvalidDataException("PNG 数据不完整（可能被截断）");

        // 反过滤
        var px = new byte[stride * h];
        Buffer.BlockCopy(raw, 0, px, 0, 0);
        for (int y = 0; y < h; y++)
        {
            int ft = raw[y * (stride + 1)];
            int srcOff = y * (stride + 1) + 1;
            int dstOff = y * stride;
            int prevOff = (y - 1) * stride;
            for (int x = 0; x < stride; x++)
            {
                int a = x >= bpp ? px[dstOff + x - bpp] : 0;
                int b = y > 0 ? px[prevOff + x] : 0;
                int c = (x >= bpp && y > 0) ? px[prevOff + x - bpp] : 0;
                int v = raw[srcOff + x];
                int outv = ft switch
                {
                    0 => v,
                    1 => v + a,
                    2 => v + b,
                    3 => v + ((a + b) >> 1),
                    4 => v + Paeth(a, b, c),
                    _ => throw new InvalidDataException("PNG 未知过滤类型 " + ft)
                };
                px[dstOff + x] = (byte)outv;
            }
        }

        var img = new Rgba(w, h);
        var d = img.Data;
        bool is16 = bitDepth == 16;

        for (int y = 0; y < h; y++)
        {
            int rowOff = y * stride;
            for (int x = 0; x < w; x++)
            {
                int di = (y * w + x) * 4;
                switch (colorType)
                {
                    case 0:
                    {
                        int g = is16 ? px[rowOff + x * 2] : px[rowOff + x];
                        d[di] = d[di + 1] = d[di + 2] = (byte)g;
                        d[di + 3] = 255;
                        break;
                    }
                    case 4:
                    {
                        int g = is16 ? px[rowOff + x * 4] : px[rowOff + x * 2];
                        int a = is16 ? px[rowOff + x * 4 + 2] : px[rowOff + x * 2 + 1];
                        d[di] = d[di + 1] = d[di + 2] = (byte)g;
                        d[di + 3] = (byte)a;
                        break;
                    }
                    case 2:
                    {
                        int o = rowOff + x * (is16 ? 6 : 3);
                        d[di] = px[o];
                        d[di + 1] = px[is16 ? o + 2 : o + 1];
                        d[di + 2] = px[is16 ? o + 4 : o + 2];
                        d[di + 3] = 255;
                        break;
                    }
                    case 6:
                    {
                        int o = rowOff + x * (is16 ? 8 : 4);
                        d[di] = px[o];
                        d[di + 1] = px[is16 ? o + 2 : o + 1];
                        d[di + 2] = px[is16 ? o + 4 : o + 2];
                        d[di + 3] = px[is16 ? o + 6 : o + 3];
                        break;
                    }
                    case 3:
                    {
                        int idx = px[rowOff + x];
                        if (palette == null) throw new InvalidDataException("调色板 PNG 缺 PLTE");
                        int po = idx * 3;
                        d[di] = po + 2 < palette.Length ? palette[po] : (byte)0;
                        d[di + 1] = po + 2 < palette.Length ? palette[po + 1] : (byte)0;
                        d[di + 2] = po + 2 < palette.Length ? palette[po + 2] : (byte)0;
                        d[di + 3] = trns != null && idx < trns.Length ? trns[idx] : (byte)255;
                        break;
                    }
                }
            }
        }
        return img;
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        if (pa <= pb && pa <= pc) return a;
        if (pb <= pc) return b;
        return c;
    }

    private static byte[] ZlibInflate(byte[] zlib)
    {
        if (zlib.Length < 6) throw new InvalidDataException("PNG 压缩流过短");
        using var input = new MemoryStream(zlib, 2, zlib.Length - 6);   // 跳过 2B zlib 头 + 4B adler32
        using var ds = new System.IO.Compression.DeflateStream(input, System.IO.Compression.CompressionMode.Decompress);
        using var outMs = new MemoryStream();
        ds.CopyTo(outMs);
        return outMs.ToArray();
    }

    /// <summary>编码为 RGBA8 PNG（filter=None，无损；LSB 原样保留）。</summary>
    public static byte[] Encode(Rgba img)
    {
        int w = img.W, h = img.H;
        int stride = w * 4;
        var raw = new byte[(stride + 1) * h];
        for (int y = 0; y < h; y++)
        {
            raw[y * (stride + 1)] = 0;   // filter None
            Buffer.BlockCopy(img.Data, y * stride, raw, y * (stride + 1) + 1, stride);
        }
        byte[] comp;
        using (var ms = new MemoryStream())
        {
            ms.WriteByte(0x78);
            ms.WriteByte(0x9C);      // zlib: deflate, default compression
            using (var ds = new System.IO.Compression.DeflateStream(ms, System.IO.Compression.CompressionLevel.Optimal, true))
                ds.Write(raw, 0, raw.Length);
            uint ad = Adler32(raw);
            ms.WriteByte((byte)(ad >> 24)); ms.WriteByte((byte)(ad >> 16));
            ms.WriteByte((byte)(ad >> 8)); ms.WriteByte((byte)ad);
            comp = ms.ToArray();
        }

        using var outMs = new MemoryStream();
        outMs.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

        var ihdr = new byte[13];
        WriteBe(ihdr, 0, (uint)w);
        WriteBe(ihdr, 4, (uint)h);
        ihdr[8] = 8;    // bit depth
        ihdr[9] = 6;    // RGBA
        ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;
        WriteChunk(outMs, "IHDR", ihdr);
        WriteChunk(outMs, "IDAT", comp);
        WriteChunk(outMs, "IEND", Array.Empty<byte>());
        return outMs.ToArray();
    }

    private static void WriteBe(byte[] buf, int off, uint v)
    {
        buf[off] = (byte)(v >> 24); buf[off + 1] = (byte)(v >> 16);
        buf[off + 2] = (byte)(v >> 8); buf[off + 3] = (byte)v;
    }

    private static void WriteChunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4];
        WriteBe(len, 0, (uint)data.Length);
        s.Write(len, 0, 4);
        var t = System.Text.Encoding.ASCII.GetBytes(type);
        s.Write(t, 0, 4);
        s.Write(data, 0, data.Length);
        var crcBuf = new byte[4 + data.Length];
        Buffer.BlockCopy(t, 0, crcBuf, 0, 4);
        Buffer.BlockCopy(data, 0, crcBuf, 4, data.Length);
        uint crc = Crc32(crcBuf, 0, crcBuf.Length);
        var cb = new byte[4];
        WriteBe(cb, 0, crc);
        s.Write(cb, 0, 4);
    }
}
