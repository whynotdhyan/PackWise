using System.Windows.Media;
using System.Windows.Media.Imaging;
using Northstar.Design.Branding;

namespace Northstar.IconTool;

/// <summary>
/// Renders Northstar Labs brand marks into multi-resolution .ico files (PNG frames) and 512 px PNGs.
/// Usage: dotnet run --project eng/IconTool -- [output folder]
/// </summary>
internal static class Program
{
    private static readonly int[] Sizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256];

    [STAThread]
    private static int Main(string[] args)
    {
        var output = args.Length > 0 ? args[0] : Path.Combine("src", "PackWise", "Assets");
        Directory.CreateDirectory(output);

        Export(MonogramLogo.PackWise, "PackWise", output);
        Export(MonogramLogo.Northstar, "NorthstarLabs", output);
        Console.WriteLine($"Wrote brand assets to {Path.GetFullPath(output)}");
        return 0;
    }

    private static void Export(MonogramLogo logo, string name, string output)
    {
        var artwork = logo.CreateDrawing();
        WriteIcon(Path.Combine(output, $"{name}.ico"), Sizes.Select(size => (size, Render(artwork, size))).ToList());
        File.WriteAllBytes(Path.Combine(output, $"{name}-512.png"), Render(artwork, 512));
    }

    private static byte[] Render(Drawing artwork, int size)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            var scale = size / MonogramLogo.Size;
            context.PushTransform(new ScaleTransform(scale, scale));
            context.DrawDrawing(artwork);
            context.Pop();
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static void WriteIcon(string path, List<(int Size, byte[] Png)> frames)
    {
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)frames.Count);

        var offset = 6 + (16 * frames.Count);
        foreach (var (size, png) in frames)
        {
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write(png.Length);
            writer.Write(offset);
            offset += png.Length;
        }

        foreach (var (_, png) in frames)
        {
            writer.Write(png);
        }
    }
}
