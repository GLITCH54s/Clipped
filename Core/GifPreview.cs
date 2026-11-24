using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Clipped.Core;

/// <summary>Holds an animated GIF (or a still image) and steps through its frames.</summary>
public sealed class GifPreview : IDisposable
{
    private MemoryStream? _stream;
    private FrameDimension? _dimension;

    public Image? Image { get; private set; }
    public int FrameCount { get; private set; } = 1;
    public int Frame { get; private set; }

    public bool IsAnimated => FrameCount > 1;

    public static GifPreview FromBytes(byte[] bytes)
    {
        var preview = new GifPreview();
        preview.Load(bytes);
        return preview;
    }

    public static GifPreview FromFile(string path) => FromBytes(File.ReadAllBytes(path));

    private void Load(byte[] bytes)
    {
        var stream = new MemoryStream(bytes, writable: false);
        var image = Image.FromStream(stream, useEmbeddedColorManagement: false);
        _stream = stream;
        Image = image;

        try
        {
            var dimension = new FrameDimension(image.FrameDimensionsList[0]);
            int count = image.GetFrameCount(dimension);
            if (count > 1)
            {
                _dimension = dimension;
                FrameCount = count;
            }
        }
        catch
        {
            _dimension = null;
            FrameCount = 1;
        }
    }

    /// <summary>Advances to the next frame; no-op for still images.</summary>
    public void Advance()
    {
        if (Image is null || _dimension is null || FrameCount <= 1) return;
        Frame = (Frame + 1) % FrameCount;
        try
        {
            Image.SelectActiveFrame(_dimension, Frame);
        }
        catch (Exception ex)
        {
            Log.Error("GIF frame step failed", ex);
        }
    }

    /// <summary>Draws the current frame, scaled to fit the box and centered.</summary>
    public void Draw(Graphics g, RectangleF box)
    {
        if (Image is null) return;

        float scale = Math.Min(box.Width / Image.Width, box.Height / Image.Height);
        if (scale > 1f) scale = 1f;
        float w = Image.Width * scale;
        float h = Image.Height * scale;
        var dest = new RectangleF(box.X + (box.Width - w) / 2f, box.Y + (box.Height - h) / 2f, w, h);

        var state = g.Save();
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.DrawImage(Image, dest);
        g.Restore(state);
    }

    public void Dispose()
    {
        Image?.Dispose();
        Image = null;
        _stream?.Dispose();
        _stream = null;
    }
}
