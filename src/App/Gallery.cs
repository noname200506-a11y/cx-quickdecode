// 本次运行内的解码图库（内存）：只存 PNG 字节 + 缩略图，避免大图把内存吃光
using System.Drawing;
using CxQuickDecode.Core;

namespace CxQuickDecode.App;

internal sealed class GalleryItem
{
    public DateTime Time;
    public int W, H;
    public string Info = "";
    public byte[] Png = Array.Empty<byte>();
    public Bitmap? Thumb;
}

internal static class Gallery
{
    public const int MaxItems = 60;
    public static readonly List<GalleryItem> Items = new();

    public static event Action? Changed;

    public static void Add(Rgba img, string info)
    {
        try
        {
            var item = new GalleryItem
            {
                Time = DateTime.Now,
                W = img.W,
                H = img.H,
                Info = info,
                Png = PngCodec.Encode(img),
                Thumb = ImageIo.ToBitmap(ImageIo.Thumb(img, 128))
            };
            Items.Insert(0, item);           // 最新的在最前
            while (Items.Count > MaxItems)
            {
                var last = Items[^1];
                Items.RemoveAt(Items.Count - 1);
                last.Thumb?.Dispose();
            }
            Changed?.Invoke();
        }
        catch (Exception ex) { AppConfig.Log("gallery add failed: " + ex.Message); }
    }

    public static Rgba? Decode(GalleryItem item)
    {
        try { return PngCodec.Decode(item.Png); }
        catch { return null; }
    }

    public static void Remove(GalleryItem item)
    {
        Items.Remove(item);
        item.Thumb?.Dispose();
        Changed?.Invoke();
    }

    public static void Clear()
    {
        foreach (var it in Items) it.Thumb?.Dispose();
        Items.Clear();
        Changed?.Invoke();
    }
}
