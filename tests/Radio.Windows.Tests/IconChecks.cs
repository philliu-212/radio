using Radio.Windows.UI;
using System.Runtime.InteropServices;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Security.Cryptography;
using System.Diagnostics;

namespace Radio.Windows.Tests;

internal static class IconChecks
{
    public static void Run(string? executablePath = null)
    {
        using var pngStream = typeof(IconChecks).Assembly.GetManifestResourceStream("Radio.Windows.Tests.RadioMini.png")!;
        using var pngCopy = new MemoryStream(); pngStream.CopyTo(pngCopy);
        Check(Convert.ToHexString(SHA256.HashData(pngCopy.ToArray())) == "105A4D1A4384E6E8785256D64EBD0ED9992DC066391E197CAB28A3101D9C9162",
            "PNG is byte-for-byte the user-cropped radio_1100.png, with original colors and alpha");
        pngCopy.Position = 0;
        using var png = new Bitmap(pngCopy);
        Check(png.Size == new Size(1100, 1100), "Source PNG retains its cropped 1100px resolution and composition");
        CheckTransparency(png, "Source PNG");
        var artwork = ArtworkBounds(png);
        using var stream = typeof(MainForm).Assembly.GetManifestResourceStream(AppIcon.ResourceName)!;
        using var copy = new MemoryStream(); stream.CopyTo(copy);
        var bytes = copy.ToArray();
        using var reader = new BinaryReader(new MemoryStream(bytes));
        var expectedFrames = new Dictionary<int, byte[]>();
        Check(reader.ReadUInt16() == 0 && reader.ReadUInt16() == 1 && reader.ReadUInt16() == 9,
            "ICO contains nine Windows icon frames, including 40px for 125% scaling");
        foreach (var expected in new[] { 16, 20, 24, 32, 40, 48, 64, 128, 256 })
        {
            var width = reader.ReadByte(); var height = reader.ReadByte();
            Check((width == 0 ? 256 : width) == expected && (height == 0 ? 256 : height) == expected,
                $"ICO includes {expected}px frame");
            reader.ReadByte(); reader.ReadByte();
            Check(reader.ReadUInt16() == 1 && reader.ReadUInt16() == 32, $"ICO {expected}px declares 32-bit alpha");
            var length = reader.ReadInt32(); var offset = reader.ReadInt32();
            Check(offset >= 6 + 9 * 16 && length > 0 && offset <= bytes.Length - length,
                $"ICO {expected}px payload stays inside resource");
            using var frameStream = new MemoryStream(bytes, offset, length);
            using var bitmap = new Bitmap(frameStream);
            Check(bitmap.Width == expected && bitmap.Height == expected, $"ICO {expected}px PNG decodes correctly");
            CheckTransparency(bitmap, $"ICO {expected}px");
            CheckResizedSource(png, artwork, bitmap);
            var visibleBounds = ArtworkBounds(bitmap);
            Check(Math.Max(visibleBounds.Width, visibleBounds.Height) >= expected,
                $"ICO {expected}px artwork spans the full frame with no reserved margin");
            // .NET 8's multi-frame selector treats the 0-byte directory width
            // inconsistently for 256px. Verify the Windows PNG decoder directly.
            var payload = bytes.AsSpan(offset, length).ToArray();
            expectedFrames.Add(expected, payload);
            var handle = CreateIconFromResourceEx(payload, (uint)payload.Length, true, 0x00030000, expected, expected, 0);
            Check(handle != IntPtr.Zero, $"Windows decodes the {expected}px icon payload");
            try
            {
                using var native = Icon.FromHandle(handle);
                Check(native.Size == new Size(expected, expected), $"Native Windows icon is {expected}px");
            }
            finally { DestroyIcon(handle); }
        }
        using var owned = AppIcon.Load();
        using var ownedBitmap = owned.ToBitmap();
        CheckTransparency(ownedBitmap, "Detached runtime icon after resource stream closes");

        var exe = executablePath ?? Path.ChangeExtension(typeof(MainForm).Assembly.Location, ".exe");
        if (executablePath is null)
        {
            Check(Path.GetFileName(exe) == "RadioMini.exe" && typeof(MainForm).Assembly.GetName().Name == "RadioMini", "Apphost and managed assembly are named RadioMini");
            var metadata = FileVersionInfo.GetVersionInfo(exe);
            Check(metadata.FileDescription == "Radio Mini" && metadata.ProductName == "Radio Mini", "Native file description/product show Radio Mini without version");
        }
        Check(File.Exists(exe), "Built Windows apphost is available for EXE icon verification");
        ExecutableIconChecks.Verify(exe, expectedFrames);
        using var executableIcon = Icon.ExtractAssociatedIcon(exe)!;
        using var executableBitmap = executableIcon.ToBitmap();
        CheckTransparency(executableBitmap, "Compiled EXE icon");
    }

    private static void CheckTransparency(Bitmap bitmap, string name)
    {
        Check(bitmap.GetPixel(0, 0).A == 0 && bitmap.GetPixel(bitmap.Width - 1, 0).A == 0 &&
            bitmap.GetPixel(0, bitmap.Height - 1).A == 0 && bitmap.GetPixel(bitmap.Width - 1, bitmap.Height - 1).A == 0,
            $"{name}: all corners are truly transparent");
        var transparent = 0; var visible = 0; var antialiased = 0;
        for (var y = 0; y < bitmap.Height; y++)
            for (var x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.A == 0) transparent++;
                else { visible++; if (pixel.A < 255) antialiased++; }
            }
        // Tight framing deliberately changes the fraction occupied by artwork.
        // Exact RGBA comparison, not an arbitrary empty-area quota, checks that
        // source alpha was preserved.
        Check(transparent > 0 && visible > 0 && antialiased > 0,
            $"{name}: supplied transparency and antialiased artwork survive without forced opacity");
    }

    private static Rectangle ArtworkBounds(Bitmap source)
    {
        var left = source.Width; var top = source.Height; var right = -1; var bottom = -1;
        for (var y = 0; y < source.Height; y++)
            for (var x = 0; x < source.Width; x++)
                if (source.GetPixel(x, y).A >= 8)
                { left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x); bottom = Math.Max(bottom, y); }
        Check(right >= left, "Visible artwork bounds exist");
        return Rectangle.FromLTRB(Math.Max(0, left - 1), Math.Max(0, top - 1), Math.Min(source.Width, right + 2), Math.Min(source.Height, bottom + 2));
    }

    private static void CheckResizedSource(Bitmap source, Rectangle content, Bitmap actual)
    {
        using var expected = new Bitmap(actual.Width, actual.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(expected))
        {
            graphics.Clear(Color.Transparent); graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic; graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            var scale = Math.Min((float)expected.Width / content.Width, (float)expected.Height / content.Height);
            var width = content.Width * scale; var height = content.Height * scale;
            graphics.DrawImage(source, new RectangleF((expected.Width - width) / 2, (expected.Height - height) / 2, width, height), content, GraphicsUnit.Pixel);
        }
        for (var y = 0; y < actual.Height; y++)
            for (var x = 0; x < actual.Width; x++)
                if (expected.GetPixel(x, y).ToArgb() != actual.GetPixel(x, y).ToArgb())
                    throw new Exception($"ICO {actual.Width}px changes source RGBA beyond resizing at {x},{y}");
        Check(true, $"ICO {actual.Width}px matches edge-filled RGBA-preserving artwork resize");
    }

    private static void Check(bool valid, string name)
    {
        if (!valid) throw new Exception(name);
        Console.WriteLine($"PASS: {name}");
    }

    [DllImport("user32.dll")] private static extern IntPtr CreateIconFromResourceEx(byte[] bits, uint size, bool icon, uint version, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
}
