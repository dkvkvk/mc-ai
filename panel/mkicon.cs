// 生成 panel/icon.ico：16×16 像素风草方块（与面板 .brand-block 同一套配色）
// 用法：csc /nologo /target:exe /out:mkicon.exe mkicon.cs 然后 mkicon.exe
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

static class MkIcon
{
    // 调色板（取自 styles.css 的 CSS 变量）
    static readonly Color Line = ColorTranslator.FromHtml("#0e0e0e");
    static readonly Color Grass = ColorTranslator.FromHtml("#5c9a3a");
    static readonly Color GrassHi = ColorTranslator.FromHtml("#6fae45");
    static readonly Color GrassDk = ColorTranslator.FromHtml("#3f6b26");
    static readonly Color Dirt = ColorTranslator.FromHtml("#7a5230");
    static readonly Color DirtDk = ColorTranslator.FromHtml("#5c3d22");
    static readonly Color DirtHi = ColorTranslator.FromHtml("#8f6440");

    // 16×16 像素设计：. 黑边 g 草 G 亮草 d 暗草 b 土 B 暗土 l 亮土
    static readonly string[] Art =
    {
        "################",
        "#ggGggggggggggG#",
        "#ggggggGggggggg#",
        "#gGgggggggdgggg#",
        "#gggdgggggggggg#",
        "#gddddddddddddg#",
        "#dddddddddddddd#",
        "#bbbbbbllbbbbbb#",
        "#bBbbbbbbbbbBbb#",
        "#bbbbllbbbbbbbb#",
        "#bbbbbbbbbBbbbb#",
        "#bBbbbbbbbbbbbb#",
        "#bbbbbbbllbbBbb#",
        "#bbBbbbbbbbbbbb#",
        "#bbbbbbbbBbbbbb#",
        "################",
    };

    static Color Pick(char c)
    {
        switch (c)
        {
            case '.': return Color.Transparent;
            case '#': return Line;
            case 'g': return Grass;
            case 'G': return GrassHi;
            case 'd': return GrassDk;
            case 'b': return Dirt;
            case 'B': return DirtDk;
            case 'l': return DirtHi;
            default: return Color.Transparent;
        }
    }

    static Bitmap Render(int size)
    {
        // 先画 16×16 逻辑图，再用最近邻放大——保住像素风的硬边
        var small = new Bitmap(16, 16, PixelFormat.Format32bppArgb);
        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
                small.SetPixel(x, y, Pick(Art[y][x]));

        var big = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(big))
        {
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.SmoothingMode = SmoothingMode.None;
            g.DrawImage(small, new Rectangle(0, 0, size, size));
        }
        small.Dispose();
        return big;
    }

    static byte[] Png(Bitmap bmp)
    {
        using (var ms = new MemoryStream())
        {
            bmp.Save(ms, ImageFormat.Png);
            return ms.ToArray();
        }
    }

    static void Main(string[] args)
    {
        string outPath = args.Length > 0 ? args[0] : "icon.ico";
        int[] sizes = { 16, 24, 32, 48, 64, 128, 256 };
        var blobs = new List<byte[]>();
        foreach (int s in sizes)
        {
            using (var bmp = Render(s)) blobs.Add(Png(bmp));
        }

        using (var fs = new FileStream(outPath, FileMode.Create, FileAccess.Write))
        using (var w = new BinaryWriter(fs))
        {
            w.Write((ushort)0);              // reserved
            w.Write((ushort)1);              // type = icon
            w.Write((ushort)sizes.Length);   // count
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                int s = sizes[i];
                w.Write((byte)(s >= 256 ? 0 : s));   // width（256 记 0）
                w.Write((byte)(s >= 256 ? 0 : s));   // height
                w.Write((byte)0);                    // palette
                w.Write((byte)0);                    // reserved
                w.Write((ushort)1);                  // planes
                w.Write((ushort)32);                 // bpp
                w.Write((uint)blobs[i].Length);
                w.Write((uint)offset);
                offset += blobs[i].Length;
            }
            foreach (var b in blobs) w.Write(b);
        }
        Console.WriteLine("written " + Path.GetFullPath(outPath) + "  " + new FileInfo(outPath).Length + " bytes");
    }
}
