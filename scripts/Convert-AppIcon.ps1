param(
    [Parameter(Mandatory = $true)][string]$SourcePath,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../src/Radio.Windows/Assets')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
# The supplied artwork is white on black. Convert luminosity to alpha, not a
# color-key flood fill: dark negative spaces inside the artwork are transparent
# too, and anti-aliased edges cannot carry a black matte into the resized icon.
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

public static class RadioIconConverter {
    private static Bitmap Resize(Bitmap source, int size) {
        var result = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(result)) {
            graphics.Clear(Color.Transparent);
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.DrawImage(source, new Rectangle(0, 0, size, size));
        }
        return result;
    }

    public static void Convert(string sourcePath, string outputDirectory) {
        Directory.CreateDirectory(outputDirectory);
        using (var original = new Bitmap(sourcePath))
        using (var mask = new Bitmap(original.Width, original.Height, PixelFormat.Format32bppArgb)) {
            int left = original.Width, top = original.Height, right = -1, bottom = -1;
            for (int y = 0; y < original.Height; y++) {
                for (int x = 0; x < original.Width; x++) {
                    var pixel = original.GetPixel(x, y);
                    int alpha = (int)Math.Round((pixel.R * .2126 + pixel.G * .7152 + pixel.B * .0722) * pixel.A / 255);
                    if (alpha <= 8) alpha = 0;
                    mask.SetPixel(x, y, Color.FromArgb(alpha, 255, 255, 255));
                    if (alpha >= 32) {
                        left = Math.Min(left, x); top = Math.Min(top, y);
                        right = Math.Max(right, x); bottom = Math.Max(bottom, y);
                    }
                }
            }
            if (right < left) throw new InvalidDataException("Source has no visible white artwork.");
            var content = Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
            int side = (int)Math.Ceiling(Math.Max(content.Width, content.Height) / .84);
            using (var square = new Bitmap(side, side, PixelFormat.Format32bppArgb)) {
                using (var graphics = Graphics.FromImage(square)) {
                    graphics.Clear(Color.Transparent);
                    graphics.CompositingMode = CompositingMode.SourceCopy;
                    graphics.DrawImage(mask, new Rectangle((side - content.Width) / 2, (side - content.Height) / 2,
                        content.Width, content.Height), content, GraphicsUnit.Pixel);
                }
                using (var png = Resize(square, 512))
                    png.Save(Path.Combine(outputDirectory, "RadioMini.png"), ImageFormat.Png);

                int[] sizes = { 16, 20, 24, 32, 48, 64, 128, 256 };
                var frames = new byte[sizes.Length][];
                for (int i = 0; i < sizes.Length; i++) {
                    using (var bitmap = Resize(square, sizes[i]))
                    using (var stream = new MemoryStream()) {
                        bitmap.Save(stream, ImageFormat.Png); frames[i] = stream.ToArray();
                    }
                }
                using (var writer = new BinaryWriter(File.Create(Path.Combine(outputDirectory, "RadioMini.ico")))) {
                    writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)sizes.Length);
                    int offset = 6 + sizes.Length * 16;
                    for (int i = 0; i < sizes.Length; i++) {
                        writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
                        writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
                        writer.Write((byte)0); writer.Write((byte)0);
                        writer.Write((ushort)1); writer.Write((ushort)32);
                        writer.Write(frames[i].Length); writer.Write(offset); offset += frames[i].Length;
                    }
                    foreach (var frame in frames) writer.Write(frame);
                }
            }
            Console.WriteLine("Converted {0}x{1}; content {2}; transparent PNG 512 and eight ICO sizes.",
                original.Width, original.Height, content);
        }
    }
}
'@
[RadioIconConverter]::Convert((Resolve-Path -LiteralPath $SourcePath).Path, [IO.Path]::GetFullPath($OutputDirectory))
