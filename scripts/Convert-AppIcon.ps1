param(
    [Parameter(Mandatory = $true)][string]$SourcePath,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../src/Radio.Windows/Assets')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
# Use the supplied PNG's own colors and alpha. Never infer transparency from
# brightness or remove black pixels: black may be part of the artwork.
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
            double scale = Math.Min((double)size / source.Width, (double)size / source.Height);
            int width = Math.Max(1, (int)Math.Round(source.Width * scale));
            int height = Math.Max(1, (int)Math.Round(source.Height * scale));
            graphics.DrawImage(source, new Rectangle((size - width) / 2, (size - height) / 2, width, height));
        }
        return result;
    }

    public static void Convert(string sourcePath, string outputDirectory) {
        Directory.CreateDirectory(outputDirectory);
        using (var original = new Bitmap(sourcePath)) {
            if (original.RawFormat.Guid != ImageFormat.Png.Guid || !Image.IsAlphaPixelFormat(original.PixelFormat))
                throw new InvalidDataException("Supply a PNG with its own alpha channel; no automatic background removal is performed.");
            bool transparent = false, visible = false;
            for (int y = 0; y < original.Height; y++) {
                for (int x = 0; x < original.Width; x++) {
                    int alpha = original.GetPixel(x, y).A;
                    transparent |= alpha == 0; visible |= alpha > 0;
                }
            }
            if (!transparent || !visible)
                throw new InvalidDataException("Source must contain both transparent background and visible artwork.");
            // Preserve the PNG byte-for-byte. Only ICO frames are resampled.
            string pngPath = Path.Combine(outputDirectory, "RadioMini.png");
            if (!String.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(pngPath), StringComparison.OrdinalIgnoreCase))
                File.Copy(sourcePath, pngPath, true);

            int[] sizes = { 16, 20, 24, 32, 48, 64, 128, 256 };
            var frames = new byte[sizes.Length][];
            for (int i = 0; i < sizes.Length; i++) {
                using (var bitmap = Resize(original, sizes[i]))
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
            Console.WriteLine("Preserved original RGBA PNG {0}x{1}; resized only eight ICO frames.", original.Width, original.Height);
        }
    }
}
'@
[RadioIconConverter]::Convert((Resolve-Path -LiteralPath $SourcePath).Path, [IO.Path]::GetFullPath($OutputDirectory))
