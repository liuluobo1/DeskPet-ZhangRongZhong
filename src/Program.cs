// Program.cs —— 入口、日志、自检
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace DeskPet
{
    internal static class Log
    {
        public static bool Enabled;
        public static readonly string FilePath =
            Path.Combine(Path.GetTempPath(), "DeskPet.log");

        public static void Write(string text)
        {
            if (!Enabled) return;
            try
            {
                File.AppendAllText(FilePath, DateTime.Now.ToString("HH:mm:ss.fff") + "  " + text + "\r\n");
            }
            catch { }
        }
    }

    internal static class Program
    {
        /// <summary>宠物显示名（编译时由 petname.txt 资源决定，如“桌宠”/“姜林”）。</summary>
        public static string PetName = "桌宠";

        [STAThread]
        private static void Main(string[] args)
        {
            bool debug = false, selftest = false, dev = false;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--debug") debug = true;
                else if (args[i] == "--selftest") selftest = true;
                else if (args[i] == "--dev") dev = true;
            }
            Log.Enabled = debug;
            Log.Write("boot os=" + Environment.OSVersion.VersionString + " clr=" + Environment.Version
                      + " pid=" + System.Diagnostics.Process.GetCurrentProcess().Id);

            PetName = ReadPetName();
            string self = Path.GetFileNameWithoutExtension(Application.ExecutablePath);
            string mutexName = "DeskPet_" + (string.IsNullOrEmpty(self) ? "default" : self);

            try
            {
                Native.SetProcessDPIAware();
                if (selftest) { SelfTest.Run(); return; }

                bool created;
                using (Mutex mutex = new Mutex(true, mutexName, out created))
                {
                    if (!created)
                    {
                        Log.Write("already running, exit");
                        return;
                    }
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    // --dev：把「连续使用 5 分钟」缩到 15 秒，方便调试磕头动作
                    Application.Run(new PetForm(dev ? 15000 : 0));
                }
            }
            catch (Exception ex)
            {
                Log.Write("FATAL " + ex);
                try
                {
                    MessageBox.Show(PetName + "启动失败：\r\n" + ex.Message + "\r\n\r\n日志：" + Log.FilePath,
                        "DeskPet", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                catch { }
            }
        }

        private static string ReadPetName()
        {
            try
            {
                using (Stream s = System.Reflection.Assembly.GetExecutingAssembly()
                           .GetManifestResourceStream("DeskPet.petname.txt"))
                {
                    if (s == null) return "桌宠";
                    using (StreamReader r = new StreamReader(s, System.Text.Encoding.UTF8))
                    {
                        string t = r.ReadToEnd().Trim();
                        return string.IsNullOrEmpty(t) ? "桌宠" : t;
                    }
                }
            }
            catch { return "桌宠"; }
        }
    }

    /// <summary>--selftest：不显示窗口，只合成几帧存成 PNG，用于验证素材与绘制链路。</summary>
    internal static class SelfTest
    {
        private const int BubbleZone = 96;

        public static void Run()
        {
            SpriteSet sprites = SpriteSet.Load();
            int w = sprites.Width;
            int h = sprites.Height + BubbleZone;
            Canvas canvas = new Canvas(w, h, BubbleZone);
            string dir = Path.GetTempPath();
            Log.Write("selftest canvas " + w + "x" + h + " crawl=" + sprites.Crawl.Count + " kowtow=" + sprites.Kowtow.Count);

            SaveOnChecker(canvas.Compose(sprites.Crawl[0], 0, false), w, h, Path.Combine(dir, "deskpet_selftest_crawl.png"));
            SaveOnChecker(canvas.Compose(sprites.CrawlLeft[2], 0, true), w, h, Path.Combine(dir, "deskpet_selftest_crawl_left.png"));
            SaveOnChecker(canvas.Compose(sprites.Kowtow[0], 255, false), w, h, Path.Combine(dir, "deskpet_selftest_kowtow.png"));
            Log.Write("selftest done");
        }

        private static void SaveOnChecker(byte[] premulBgra, int w, int h, string path)
        {
            using (Bitmap bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            {
                BitmapData bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                try
                {
                    byte[] row = new byte[bd.Stride];
                    int tile = 16;
                    for (int y = 0; y < h; y++)
                    {
                        for (int x = 0; x < w; x++)
                        {
                            int i = x * 4;
                            byte a = premulBgra[y * bd.Stride + i + 3];
                            byte bg = (byte)(((x / tile + y / tile) % 2 == 0) ? 236 : 208);
                            // 预乘像素叠加到背景：dst = src + bg*(1-a)
                            int inv = 255 - a;
                            int o = i;
                            row[o] = (byte)(premulBgra[y * bd.Stride + i] + bg * inv / 255);
                            row[o + 1] = (byte)(premulBgra[y * bd.Stride + i + 1] + bg * inv / 255);
                            row[o + 2] = (byte)(premulBgra[y * bd.Stride + i + 2] + bg * inv / 255);
                            row[o + 3] = 255;
                        }
                        Marshal.Copy(row, 0, new IntPtr(bd.Scan0.ToInt64() + (long)y * bd.Stride), row.Length);
                    }
                }
                finally { bmp.UnlockBits(bd); }
                bmp.Save(path, ImageFormat.Png);
            }
            Log.Write("selftest -> " + path);
        }
    }
}
