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
    private static Rectangle ArtworkBounds(Bitmap source) {
        int left=source.Width, top=source.Height, right=-1, bottom=-1;
        for (int y=0; y<source.Height; y++) for (int x=0; x<source.Width; x++)
            if (source.GetPixel(x,y).A >= 8) {
                left=Math.Min(left,x); top=Math.Min(top,y); right=Math.Max(right,x); bottom=Math.Max(bottom,y);
            }
        if (right < left) throw new InvalidDataException("Source has no sufficiently visible artwork.");
        // Ignore near-transparent outer padding only for ICO framing, not PNG
        // pixels. One extra source pixel protects the visible antialiased edge.
        return Rectangle.FromLTRB(Math.Max(0,left-1),Math.Max(0,top-1),
            Math.Min(source.Width,right+2),Math.Min(source.Height,bottom+2));
    }

    private static Bitmap Resize(Bitmap source, Rectangle content, int size) {
        var result = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(result)) {
            graphics.Clear(Color.Transparent);
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            // User-selected framing: no reserved margin. The trimmed artwork fills
            // the frame on its longer axis; the shorter axis keeps only the
            // unavoidable aspect-ratio remainder. Pixels outside the artwork are
            // transparent in the source and are not painted.
            float scale = Math.Min((float)size / content.Width, (float)size / content.Height);
            float width = content.Width * scale, height = content.Height * scale;
            graphics.DrawImage(source, new RectangleF((size-width)/2, (size-height)/2, width, height), content, GraphicsUnit.Pixel);
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

            var content = ArtworkBounds(original);
            int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
            var frames = new byte[sizes.Length][];
            for (int i = 0; i < sizes.Length; i++) {
                using (var bitmap = Resize(original, content, sizes[i]))
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
            Console.WriteLine("Preserved RGBA PNG {0}x{1}; artwork {2}; nine ICO frames fill the canvas without reserved margins.", original.Width, original.Height, content);
        }
    }
}
'@
[RadioIconConverter]::Convert((Resolve-Path -LiteralPath $SourcePath).Path, [IO.Path]::GetFullPath($OutputDirectory))
