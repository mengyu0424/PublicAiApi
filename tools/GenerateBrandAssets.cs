using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;
using ApiStudio;

internal static class GenerateBrandAssets
{
    [STAThread]
    private static void Main(string[] args)
    {
        Directory.CreateDirectory(args[0]);
        int[] sizes = { 16, 24, 32, 48, 64, 128, 256 };
        byte[][] images = new byte[sizes.Length][];
        for (int i = 0; i < sizes.Length; i++)
        {
            var visual = new DrawingVisual();
            using (var drawing = visual.RenderOpen())
                BrandArtwork.Draw(drawing, new Rect(0, 0, sizes[i] - 1, sizes[i] - 1));
            var bitmap = new RenderTargetBitmap(sizes[i], sizes[i], 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var buffer = new MemoryStream())
            {
                encoder.Save(buffer); images[i] = buffer.ToArray();
                if (sizes[i] == 256) File.WriteAllBytes(Path.Combine(args[0], "ApiStudio.png"), images[i]);
            }
        }
        using (var output = new BinaryWriter(File.Create(Path.Combine(args[0], "ApiStudio.ico"))))
        {
            output.Write((ushort)0); output.Write((ushort)1); output.Write((ushort)sizes.Length);
            int offset = 6 + sizes.Length * 16;
            for (int i = 0; i < sizes.Length; i++)
            {
                output.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); output.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
                output.Write((byte)0); output.Write((byte)0); output.Write((ushort)1); output.Write((ushort)32);
                output.Write(images[i].Length); output.Write(offset); offset += images[i].Length;
            }
            foreach (var image in images) output.Write(image);
        }
        Console.WriteLine("Created PNG and 7-size ICO assets.");
    }
}
