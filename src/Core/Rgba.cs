namespace CxQuickDecode.Core;

/// <summary>RGBA8 图像缓冲（stride = W*4），像素与文件逐字节对应，不做任何色彩管理。</summary>
internal sealed class Rgba
{
    public readonly int W;
    public readonly int H;
    public byte[] Data;          // length = W*H*4, 顺序 R,G,B,A

    public Rgba(int w, int h)
    {
        if (w <= 0 || h <= 0) throw new ArgumentException("图像尺寸非法: " + w + "x" + h);
        W = w;
        H = h;
        Data = new byte[(long)w * h * 4];
    }

    public Rgba(int w, int h, byte[] data)
    {
        if (w <= 0 || h <= 0) throw new ArgumentException("图像尺寸非法: " + w + "x" + h);
        if (data.Length < (long)w * h * 4) throw new ArgumentException("像素缓冲过短");
        W = w;
        H = h;
        Data = data;
    }

    public int Idx(int x, int y) => (y * W + x) * 4;

    public Rgba Clone()
    {
        var c = new Rgba(W, H);
        Buffer.BlockCopy(Data, 0, c.Data, 0, Data.Length);
        return c;
    }

    /// <summary>裁掉四周各 pad 像素。</summary>
    public Rgba Crop(int pad)
    {
        if (pad <= 0) return Clone();
        int w = W - pad * 2, h = H - pad * 2;
        if (w <= 0 || h <= 0) return Clone();
        var outImg = new Rgba(w, h);
        for (int y = 0; y < h; y++)
            Buffer.BlockCopy(Data, ((y + pad) * W + pad) * 4, outImg.Data, y * w * 4, w * 4);
        return outImg;
    }

    public Rgba CropRect(int x, int y, int w, int h)
    {
        x = Math.Clamp(x, 0, W - 1);
        y = Math.Clamp(y, 0, H - 1);
        w = Math.Clamp(w, 1, W - x);
        h = Math.Clamp(h, 1, H - y);
        var outImg = new Rgba(w, h);
        for (int r = 0; r < h; r++)
            Buffer.BlockCopy(Data, ((y + r) * W + x) * 4, outImg.Data, r * w * 4, w * 4);
        return outImg;
    }

    /// <summary>最近邻缩放（只用于预览显示，不参与解码）。</summary>
    public Rgba Scale(int nw, int nh)
    {
        if (nw == W && nh == H) return Clone();
        var outImg = new Rgba(nw, nh);
        for (int y = 0; y < nh; y++)
        {
            int sy = (int)((long)y * H / nh);
            for (int x = 0; x < nw; x++)
            {
                int sx = (int)((long)x * W / nw);
                int si = (sy * W + sx) * 4, di = (y * nw + x) * 4;
                outImg.Data[di] = Data[si];
                outImg.Data[di + 1] = Data[si + 1];
                outImg.Data[di + 2] = Data[si + 2];
                outImg.Data[di + 3] = Data[si + 3];
            }
        }
        return outImg;
    }
}
