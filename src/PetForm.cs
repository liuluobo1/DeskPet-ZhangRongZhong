// PetForm.cs —— 桌宠主窗口：分层透明窗、爬行/磕头状态机、键鼠空闲计时、托盘、任务栏、右键动作菜单
using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DeskPet
{
    internal enum PetMode
    {
        Crawl,      // 空闲：像狗一样爬
        Kowtow,     // 磕头（连续用满 5 分钟 / 手动）
        Paused      // 原地休息
    }

    internal sealed class PetForm : Form
    {
        // ---- 可调参数 ----
        private const int FiveMinutes = 5 * 60 * 1000;   // 连续使用阈值
        private const int IdleReset = 30 * 1000;         // 空闲超过 30 秒 = 断开"连续使用"
        private const int KowtowMs = 4600;               // 一次磕头持续时长
        private const int TickMs = 100;                  // 定时器间隔
        private const int BubbleZone = 96;               // 画布顶部留给气泡的空间
        private const int HitAlpha = 24;                 // alpha 阈值：低于它鼠标穿透
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunName = "DeskPet";
        private const int WM_SYSCOMMAND = 0x0112;
        private const int SC_MINIMIZE = 0xF020;

        private readonly int _thresholdMs;
        private readonly int _idleResetMs;

        private SpriteSet _sprites;
        private Canvas _canvas;
        private Timer _timer;
        private NotifyIcon _tray;
        private ContextMenuStrip _menu;
        private Icon _icon;
        private ToolStripMenuItem _miShow;
        private ToolStripMenuItem _miPause;

        private PetMode _mode = PetMode.Crawl;
        private int _x;
        private int _y;
        private int _dir = 1;                 // 1 = 朝右（原图方向），-1 = 朝左
        private float _speed = 60f;           // px/s
        private int _nextTurn;
        private float _walkPhase;
        private int _lastTick;
        private int _activeMs;                // 连续使用累计毫秒
        private int _kowtowStart;
        private int _repeatKowtow = 1;        // 连磕次数（磕三个 → 3）
        private bool _dragging;
        private Point _dragOffset;

        public PetForm(int thresholdMs)
        {
            _thresholdMs = thresholdMs <= 0 ? FiveMinutes : thresholdMs;
            _idleResetMs = thresholdMs <= 0 ? IdleReset : int.MaxValue;   // 调试模式忽略“空闲”判定

            _sprites = SpriteSet.Load();
            int w = _sprites.Width;
            int h = _sprites.Height + BubbleZone;
            _canvas = new Canvas(w, h, BubbleZone);

            _icon = LoadAppIcon();

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = true;             // 任务栏显示
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            MinimizeBox = true;
            MaximizeBox = false;
            Text = Program.PetName;
            if (_icon != null) Icon = _icon;
            SetClientSizeCore(w, h);

            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            // 不同角色用不同起点，避免两个宠物叠在一起
            int seed = Application.ExecutablePath.GetHashCode() & 0x7fffffff;
            Random rnd = new Random(seed);
            _x = wa.Left + 24 + rnd.Next(Math.Max(1, wa.Width - w - 48));
            _y = wa.Bottom - h - 6;

            BuildMenu();
            BuildTray();

            _lastTick = Environment.TickCount;
            _nextTurn = _lastTick + 2500;

            _timer = new Timer();
            _timer.Interval = TickMs;
            _timer.Tick += new EventHandler(OnTick);
            _timer.Start();
        }

        private static Icon LoadAppIcon()
        {
            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                using (Stream s = asm.GetManifestResourceStream("DeskPet.icon.ico"))
                {
                    if (s != null) return new Icon(s);
                }
                return Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch { return null; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_LAYERED;   // 逐像素透明
                return cp;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Render();
            Log.Write("shown at " + _x + "," + _y + " size " + _canvas.Width + "x" + _canvas.Height);
        }

        // ---------------- 托盘 ----------------
        private void BuildTray()
        {
            _tray = new NotifyIcon();
            _tray.Icon = _icon;
            _tray.Text = Program.PetName + "：右键有菜单";
            _tray.ContextMenuStrip = _menu;
            _tray.DoubleClick += delegate { SetPetVisible(!Visible); };
            _tray.Visible = true;
        }

        // ---------------- 菜单 ----------------
        private void BuildMenu()
        {
            _menu = new ContextMenuStrip();
            _menu.ShowImageMargin = false;

            ToolStripMenuItem act = new ToolStripMenuItem("动作");
            ToolStripMenuItem miCrawl = new ToolStripMenuItem("爬来爬去（默认）");
            miCrawl.Click += delegate { _mode = PetMode.Crawl; _nextTurn = Environment.TickCount + 1200; UpdatePauseText(); };
            ToolStripMenuItem miKow = new ToolStripMenuItem("磕一个");
            miKow.Click += delegate { StartKowtow(1); };
            ToolStripMenuItem miKow3 = new ToolStripMenuItem("连磕三个");
            miKow3.Click += delegate { StartKowtow(3); };
            ToolStripMenuItem miRest = new ToolStripMenuItem("原地休息");
            miRest.Click += delegate { if (_mode != PetMode.Kowtow) { _mode = PetMode.Paused; UpdatePauseText(); } };
            ToolStripMenuItem miTurn = new ToolStripMenuItem("掉个头");
            miTurn.Click += delegate { _dir = -_dir; };
            act.DropDownItems.Add(miCrawl);
            act.DropDownItems.Add(miKow);
            act.DropDownItems.Add(miKow3);
            act.DropDownItems.Add(miRest);
            act.DropDownItems.Add(new ToolStripSeparator());
            act.DropDownItems.Add(miTurn);

            _miPause = new ToolStripMenuItem("暂停/继续");
            _miPause.Click += delegate
            {
                if (_mode == PetMode.Kowtow) return;
                _mode = (_mode == PetMode.Paused) ? PetMode.Crawl : PetMode.Paused;
                UpdatePauseText();
            };

            _miShow = new ToolStripMenuItem("收起" + Program.PetName);
            _miShow.Click += delegate { SetPetVisible(!Visible); };

            ToolStripMenuItem miTop = new ToolStripMenuItem("始终置顶");
            miTop.CheckOnClick = true;
            miTop.Checked = true;
            miTop.Click += delegate { TopMost = miTop.Checked; };

            ToolStripMenuItem miAuto = new ToolStripMenuItem("开机自启");
            miAuto.CheckOnClick = true;
            miAuto.Checked = IsAutoStart();
            miAuto.Click += delegate { SetAutoStart(miAuto.Checked); };

            ToolStripMenuItem miExit = new ToolStripMenuItem("退出" + Program.PetName);
            miExit.Click += delegate { ExitApp(); };

            _menu.Items.Add(act);
            _menu.Items.Add(_miPause);
            _menu.Items.Add(_miShow);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(miTop);
            _menu.Items.Add(miAuto);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(miExit);

            // 托盘 / 桌宠 右键共用这一套菜单（Form.ContextMenuStrip 不设，避免重复弹出）
        }

        private void UpdatePauseText()
        {
            if (_miPause.Text != null)
                _miPause.Text = (_mode == PetMode.Paused) ? "继续爬行" : "暂停爬行";
            if (_miShow != null)
                _miShow.Text = Visible ? ("收起" + Program.PetName) : ("叫出" + Program.PetName);
        }

        private void SetPetVisible(bool show)
        {
            if (show)
            {
                Show();
                TopMost = true;
                Render();
            }
            else
            {
                Hide();
            }
            UpdatePauseText();
        }

        private void ExitApp()
        {
            Log.Write("exit by user");
            if (_timer != null) _timer.Stop();
            if (_tray != null) { _tray.Visible = false; _tray.Dispose(); _tray = null; }
            Close();
        }

        private static bool IsAutoStart()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, false))
                {
                    if (k == null) return false;
                    return k.GetValue(RunName) != null;
                }
            }
            catch { return false; }
        }

        private static void SetAutoStart(bool on)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (k == null) return;
                    if (on) k.SetValue(RunName, "\"" + Application.ExecutablePath + "\"");
                    else k.DeleteValue(RunName, false);
                }
            }
            catch (Exception ex) { Log.Write("autostart failed: " + ex.Message); }
        }

        // ---------------- 计时与状态机 ----------------
        private void OnTick(object sender, EventArgs e)
        {
            int now = Environment.TickCount;
            int dt = now - _lastTick;
            if (dt < 0) dt = 0;
            if (dt > 500) dt = 500;
            _lastTick = now;

            uint idle = Native.IdleMilliseconds();
            if (idle < _idleResetMs) _activeMs += dt;
            else _activeMs = 0;

            if (_mode == PetMode.Crawl && _activeMs >= _thresholdMs) StartKowtow(1);

            if (_mode == PetMode.Kowtow && now - _kowtowStart >= KowtowMs)
            {
                if (_repeatKowtow > 1)
                {
                    _repeatKowtow--;
                    _kowtowStart = now;              // 接着磕下一个
                }
                else
                {
                    _repeatKowtow = 1;
                    _mode = PetMode.Crawl;
                    _activeMs = 0;
                    _nextTurn = now + 2000;
                    UpdatePauseText();
                }
            }

            if (_mode == PetMode.Crawl && !_dragging && Visible) MoveCrawl(dt, now);

            if (Visible) Render();
        }

        private void MoveCrawl(int dt, int now)
        {
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            if (now >= _nextTurn)
            {
                _nextTurn = now + 2500 + new Random().Next(3500);
                if (new Random().Next(100) < 20) _speed = 0f;          // 偶尔停一下
                else _speed = 45f + new Random().Next(50);
                if (new Random().Next(100) < 25) _dir = -_dir;
            }
            _x += (int)(_dir * _speed * dt / 1000f);

            int minX = wa.Left - 26;
            int maxX = wa.Right - _canvas.Width + 26;
            if (_x < minX) { _x = minX; _dir = 1; }
            if (_x > maxX) { _x = maxX; _dir = -1; }

            _walkPhase += dt * 8f / 1000f;
            if (_walkPhase > 1000f) _walkPhase -= 1000f;
        }

        private void StartKowtow(int times)
        {
            _repeatKowtow = times < 1 ? 1 : times;
            _mode = PetMode.Kowtow;
            _kowtowStart = Environment.TickCount;
            _dragging = false;
            UpdatePauseText();
            Log.Write("kowtow start x" + _repeatKowtow);
        }

        // ---------------- 绘制 ----------------
        private void Render()
        {
            if (!Visible) return;
            bool toLeft = _dir < 0;
            Bitmap frame;
            int bubbleAlpha = 0;

            if (_mode == PetMode.Kowtow)
            {
                int elapsed = Environment.TickCount - _kowtowStart;
                int[] seq = new int[] { 0, 1, 2, 1 };
                int idx = (elapsed / 180) % seq.Length;
                frame = toLeft ? _sprites.KowtowLeft[seq[idx]] : _sprites.Kowtow[seq[idx]];

                if (elapsed < 260) bubbleAlpha = elapsed * 255 / 260;
                else if (elapsed > 3600) bubbleAlpha = Math.Max(0, (KowtowMs - elapsed) * 255 / 1000);
                else bubbleAlpha = 255;
            }
            else
            {
                int idx = ((int)_walkPhase) % _sprites.Crawl.Count;
                if (_mode == PetMode.Paused) idx = 1;
                frame = toLeft ? _sprites.CrawlLeft[idx] : _sprites.Crawl[idx];
            }

            byte[] premul = _canvas.Compose(frame, bubbleAlpha, toLeft);
            Native.BlitLayered(Handle, premul, _canvas.Width, _canvas.Height, _x, _y);
        }

        // ---------------- 窗口消息 ----------------
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_NCHITTEST)
            {
                int lp = m.LParam.ToInt32();
                int sx = (short)(lp & 0xFFFF);
                int sy = (short)((lp >> 16) & 0xFFFF);
                Point p = PointToClient(new Point(sx, sy));
                if (!_canvas.HitTest(p.X, p.Y, HitAlpha))
                {
                    m.Result = (IntPtr)Native.HTTRANSPARENT;    // 透明区域让鼠标穿过去
                    return;
                }
            }
            // 任务栏点“最小化”= 收起桌宠（分层窗口最小化容易出问题，直接隐藏更稳）
            if (m.Msg == WM_SYSCOMMAND && ((int)m.WParam & 0xFFF0) == SC_MINIMIZE)
            {
                SetPetVisible(false);
                return;
            }
            base.WndProc(ref m);
        }

        // ---------------- 鼠标 ----------------
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left && _canvas.HitTest(e.X, e.Y, HitAlpha))
            {
                _dragging = true;
                _dragOffset = new Point(Cursor.Position.X - _x, Cursor.Position.Y - _y);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragging)
            {
                _x = Cursor.Position.X - _dragOffset.X;
                _y = Cursor.Position.Y - _dragOffset.Y;
                Render();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left && _dragging)
            {
                _dragging = false;
                _lastTick = Environment.TickCount;
            }
            if (e.Button == MouseButtons.Right && _canvas.HitTest(e.X, e.Y, HitAlpha))
            {
                _menu.Show(Cursor.Position);
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_timer != null) _timer.Stop();
            if (_tray != null) { _tray.Visible = false; _tray.Dispose(); _tray = null; }
            base.OnFormClosing(e);
        }
    }
}
