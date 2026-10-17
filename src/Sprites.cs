// Sprites.cs —— 嵌入资源（PNG 帧）加载、镜像、合成画布、预乘转换、气泡绘制
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace DeskPet
{
    internal sealed class SpriteSet
    {
        public readonly List<Bitmap> Crawl = new List<Bitmap>();
        public readonly List<Bitmap> Kowtow = new List<Bitmap>();
        public readonly List<Bitmap> CrawlLeft = new List<Bitmap>();
        public readonly List<Bitmap> KowtowLeft = new List<Bitmap>();
        public int Width;
        public int Height;

        private static Bitmap Load(string name)
        {
            Assembly asm = Assembly.GetExecutingAssembly();
            using (Stream s = asm.GetManifestResourceStream(name))
            {
                if (s == null) throw new InvalidOperationException("缺少嵌入资源: " + name);
                Bitmap raw = new Bitmap(s);
                Bitmap bmp = new Bitmap(raw.Width, raw.Height, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.DrawImage(raw, 0, 0, raw.Width, raw.Height);
                }
                raw.Dispose();
                return bmp;
            }
        }

        private static Bitmap Mirrored(Bitmap src)
        {
            Bitmap b = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImage(src, new Rectangle(0, 0, src.Width, src.Height),
                    new Rectangle(src.Width, 0, -src.Width, src.Height), GraphicsUnit.Pixel);
            }
            return b;
        }

        public static SpriteSet Load()
        {
            SpriteSet s = new SpriteSet();
            for (int i = 0; i < 4; i++)
            {
                Bitmap b = Load("DeskPet.crawl_" + i + ".png");
                s.Crawl.Add(b);
                s.Width = Math.Max(s.Width, b.Width);
                s.Height = Math.Max(s.Height, b.Height);
            }
            for (int i = 0; i < 3; i++)
            {
                Bitmap b = Load("DeskPet.kowtow_" + i + ".png");
                s.Kowtow.Add(b);
                s.Width = Math.Max(s.Width, b.Width);
                s.Height = Math.Max(s.Height, b.Height);
            }
            foreach (Bitmap b in s.Crawl) s.CrawlLeft.Add(Mirrored(b));
            foreach (Bitmap b in s.Kowtow) s.KowtowLeft.Add(Mirrored(b));
            return s;
        }
    }

    /// <summary>把精灵 + 气泡合成到一张 ARGB 画布，并转成 UpdateLayeredWindow 需要的预乘 BGRA。</summary>
    internal sealed class Canvas
    {
        public readonly int Width;
        public readonly int Height;
        public readonly int BubbleZone;          // 画布顶部留给气泡的高度
        private readonly Bitmap _bmp;
        private readonly Graphics _g;
        private readonly Bitmap _bubble;
        private readonly ImageAttributes _attr;
        private readonly ColorMatrix _cm;
        private byte[] _premul;
        private byte[] _alpha;                   // 未预乘的 alpha，用于命中测试

        public Canvas(int w, int h, int bubbleZone)
        {
            Width = w;
            Height = h;
            BubbleZone = bubbleZone;
            _bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            _g = Graphics.FromImage(_bmp);
            _g.CompositingMode = CompositingMode.SourceOver;
            _g.SmoothingMode = SmoothingMode.AntiAlias;
            _g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            _bubble = MakeBubble("爹，我错了");
            _cm = new ColorMatrix();
            _attr = new ImageAttributes();
        }

        /// <summary>画气泡（白底圆角 + 尾巴 + 微软雅黑粗体）。</summary>
        private static Bitmap MakeBubble(string text)
        {
            Font f = null;
            string[] names = new string[] { "微软雅黑", "Microsoft YaHei", "黑体", "SimHei", "宋体" };
            for (int i = 0; i < names.Length; i++)
            {
                try
                {
                    f = new Font(names[i], 20f, FontStyle.Bold, GraphicsUnit.Pixel);
                    if (f != null) break;
                }
                catch { f = null; }
            }
            if (f == null) f = new Font(FontFamily.GenericSansSerif, 20f, FontStyle.Bold, GraphicsUnit.Pixel);

            SizeF ts;
            using (Bitmap tmp = new Bitmap(1, 1))
            using (Graphics tg = Graphics.FromImage(tmp))
            {
                tg.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                ts = tg.MeasureString(text, f);
            }
            int padX = 14, padY = 8, tail = 12, deco = 4;
            int w = (int)Math.Ceiling(ts.Width) + padX * 2 + deco * 2;
            int h = (int)Math.Ceiling(ts.Height) + padY * 2 + deco * 2;
            Bitmap b = new Bitmap(w, h + tail, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.Clear(Color.Transparent);
                int r = 10;
                Rectangle box = new Rectangle(deco, deco, w - deco * 2 - 1, h - deco * 2 - 1);
                using (GraphicsPath p = RoundRect(box, r))
                using (SolidBrush fill = new SolidBrush(Color.FromArgb(252, 255, 255, 255)))
                using (Pen pen = new Pen(Color.FromArgb(255, 38, 38, 48), 2f))
                {
                    g.FillPath(fill, p);
                    g.DrawPath(pen, p);
                }
                // 尾巴
                PointF[] tri = new PointF[]
                {
                    new PointF(w / 2f - 10, h - deco - 1),
                    new PointF(w / 2f + 10, h - deco - 1),
                    new PointF(w / 2f + 2, h + tail - 1)
                };
                using (SolidBrush fill = new SolidBrush(Color.FromArgb(252, 255, 255, 255)))
                using (Pen pen = new Pen(Color.FromArgb(255, 38, 38, 48), 2f))
                {
                    g.FillPolygon(fill, tri);
                    g.DrawLine(pen, tri[0], tri[2]);
                    g.DrawLine(pen, tri[2], tri[1]);
                }
                using (SolidBrush tb = new SolidBrush(Color.FromArgb(255, 18, 18, 24)))
                    g.DrawString(text, f, tb, deco + padX, deco + padY);
            }
            f.Dispose();
            return b;
        }

        private static GraphicsPath RoundRect(Rectangle r, int radius)
        {
            GraphicsPath p = new GraphicsPath();
            int d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        /// <summary>合成一帧并返回预乘 BGRA 字节；alpha 留作命中测试。</summary>
        public byte[] Compose(Bitmap sprite, int bubbleAlpha, bool bubbleOnLeft)
        {
            _g.Clear(Color.Transparent);
            int sy = Height - sprite.Height;
            int sx = (Width - sprite.Width) / 2;
            _g.DrawImageUnscaled(sprite, sx, sy);

            if (bubbleAlpha > 0)
            {
                _cm.Matrix00 = 1f; _cm.Matrix11 = 1f; _cm.Matrix22 = 1f;
                _cm.Matrix40 = 0f; _cm.Matrix41 = 0f; _cm.Matrix42 = 0f;
                _cm.Matrix33 = Math.Max(0f, Math.Min(1f, bubbleAlpha / 255f));
                _attr.SetColorMatrix(_cm, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
                int bx = bubbleOnLeft ? 4 : Width - _bubble.Width - 4;
                int by = Math.Max(2, sy - _bubble.Height + 10);   // 贴在精灵头顶上方
                _g.DrawImage(_bubble, new Rectangle(bx, by, _bubble.Width, _bubble.Height),
                    0, 0, _bubble.Width, _bubble.Height, GraphicsUnit.Pixel, _attr);
            }

            BitmapData bd = _bmp.LockBits(new Rectangle(0, 0, Width, Height), ImageLockMode.ReadOnly,
                PixelFormat.Format32bppArgb);
            try
            {
                int stride = bd.Stride;
                int bytes = stride * Height;
                if (_premul == null || _premul.Length != bytes) _premul = new byte[bytes];
                if (_alpha == null || _alpha.Length != Width * Height) _alpha = new byte[Width * Height];
                byte[] row = new byte[stride];
                for (int y = 0; y < Height; y++)
                {
                    Marshal.Copy(new IntPtr(bd.Scan0.ToInt64() + (long)y * stride), row, 0, stride);
                    int o = y * stride;
                    int arow = y * Width;
                    for (int x = 0; x < Width; x++)
                    {
                        int i = x * 4;
                        byte b = row[i];
                        byte g = row[i + 1];
                        byte r = row[i + 2];
                        byte a = row[i + 3];
                        _alpha[arow + x] = a;
                        int oi = o + i;
                        _premul[oi] = (byte)(b * a / 255);
                        _premul[oi + 1] = (byte)(g * a / 255);
                        _premul[oi + 2] = (byte)(r * a / 255);
                        _premul[oi + 3] = a;
                    }
                }
            }
            finally
            {
                _bmp.UnlockBits(bd);
            }
            return _premul;
        }

        public byte AlphaAt(int x, int y)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height || _alpha == null) return 0;
            return _alpha[y * Width + x];
        }

        public bool HitTest(int x, int y, int threshold)
        {
            return AlphaAt(x, y) >= threshold;
        }
    }
}
