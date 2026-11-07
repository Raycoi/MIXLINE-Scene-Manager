// MIXLINE Scene Manager — Views/SettingsForm.cs
// 设置窗口：全部自绘（无系统 TrackBar/Button/Label），只用系统文件夹选择器。
using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Asr
{
    class SettingsForm : Form
    {
        static readonly string[] LevelNames = { "调试", "信息", "警告", "错误" };
        static readonly string[] LevelDesc =
        {
            "记录调试、信息、警告、错误",
            "记录信息、警告、错误",
            "记录警告、错误",
            "只记录错误"
        };

        string dir;
        readonly FlatSlider slider = new FlatSlider();
        readonly FlatButton open, change, reset, ok, cancel;
        Rectangle pathBox;
        string openHint;

        bool logOn;
        int logLevel;
        Rectangle toggleRect, levelRect;
        int levelHover = -1;

        public string ChosenDir { get { return dir; } }
        public int ChosenCardSize { get { return slider.Value; } }
        public bool ChosenLog { get { return logOn; } }
        public int ChosenLevel { get { return logLevel; } }

        public SettingsForm(string current, int curCard, bool curLog, int curLevel)
        {
            dir = current;
            logOn = curLog;
            logLevel = Math.Max(0, Math.Min(3, curLevel));

            Text = "设置";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = C.Base;
            ClientSize = new Size(Dpi.S(492), Dpi.S(348));
            Font = T.Body;
            DoubleBuffered = true;
            AutoScaleMode = AutoScaleMode.None;

            slider.Minimum = 56; slider.Maximum = 110; slider.Value = Math.Max(56, Math.Min(110, curCard));
            slider.Bounds = new Rectangle(Dpi.S(18), Dpi.S(100), Dpi.S(300), Dpi.S(28));
            slider.ValueChanged += delegate { Invalidate(); };
            Controls.Add(slider);

            open = new FlatButton("打开文件夹", FlatButton.Kind.Secondary) { Bounds = new Rectangle(Dpi.S(274), Dpi.S(40), Dpi.S(96), Dpi.S(30)) };
            change = new FlatButton("更改目录…", FlatButton.Kind.Secondary) { Bounds = new Rectangle(Dpi.S(378), Dpi.S(40), Dpi.S(96), Dpi.S(30)) };
            reset = new FlatButton("恢复默认", FlatButton.Kind.Secondary) { Bounds = new Rectangle(Dpi.S(18), Dpi.S(302), Dpi.S(96), Dpi.S(32)) };
            ok = new FlatButton("确定", FlatButton.Kind.Primary) { Bounds = new Rectangle(Dpi.S(300), Dpi.S(302), Dpi.S(84), Dpi.S(32)) };
            cancel = new FlatButton("取消", FlatButton.Kind.Secondary) { Bounds = new Rectangle(Dpi.S(392), Dpi.S(302), Dpi.S(84), Dpi.S(32)) };
            Controls.Add(open); Controls.Add(change); Controls.Add(reset); Controls.Add(ok); Controls.Add(cancel);

            pathBox = new Rectangle(Dpi.S(18), Dpi.S(40), Dpi.S(248), Dpi.S(30));
            toggleRect = new Rectangle(Dpi.S(392), Dpi.S(162), Dpi.S(48), Dpi.S(26));
            levelRect = new Rectangle(Dpi.S(18), Dpi.S(224), Dpi.S(330), Dpi.S(34));

            open.Click += delegate
            {
                openHint = null;
                if (!System.IO.Directory.Exists(dir))
                {
                    openHint = "目录不存在：" + dir;
                    Invalidate();
                    return;
                }
                // 用 Windows 目录下 explorer.exe 的绝对路径 + 固定工作目录：
                // 避免从游戏/旧安装目录解析到错误的 explorer.exe（0xc0000142），也避免 ShellExecute 打开目录被拒绝访问
                string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                string explorer = System.IO.Path.Combine(winDir, "explorer.exe");
                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo(explorer, "\"" + dir + "\"");
                    psi.UseShellExecute = false;
                    psi.WorkingDirectory = winDir;
                    System.Diagnostics.Process.Start(psi);
                    return;
                }
                catch (Exception ex)
                {
                    Log.Warn("SettingsForm: explorer 打开目录失败", ex);
                    openHint = "打开文件夹失败：" + ex.Message;
                }
                // 兜底 1：ShellExecute 打开目录本身（非提权场景通常可用）
                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo(dir);
                    psi.UseShellExecute = true;
                    psi.Verb = "open";
                    System.Diagnostics.Process.Start(psi);
                    openHint = null;
                    Invalidate();
                    return;
                }
                catch (Exception ex)
                {
                    Log.Warn("SettingsForm: ShellExecute 打开目录失败", ex);
                }
                // 兜底 2：Shell.Application COM（提权进程里比直接 CreateProcess explorer 更可靠）
                try
                {
                    var shellType = Type.GetTypeFromProgID("Shell.Application");
                    if (shellType != null)
                    {
                        object shell = Activator.CreateInstance(shellType);
                        shellType.InvokeMember("Open", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { dir });
                        openHint = null;
                        Invalidate();
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn("SettingsForm: Shell.Application 打开目录失败", ex);
                }
                try { Clipboard.SetText(dir); openHint = "打开文件夹失败；路径已复制，可粘贴到资源管理器打开"; }
                catch { openHint = "打开文件夹失败"; }
                Invalidate();
            };
            change.Click += delegate
            {
                using (var f = new FolderBrowserDialog())
                {
                    f.Description = "选择 MSM-config.json 保存目录";
                    f.SelectedPath = dir;
                    PopupHost.SuspendClickAway();          // 选择器是独立原生窗口，不能被设置窗的"点外取消"吞掉
                    try
                    {
                        if (f.ShowDialog(this) == DialogResult.OK)
                        {
                            dir = f.SelectedPath;
                            openHint = null;
                            Invalidate();
                        }
                    }
                    finally { PopupHost.ResumeClickAway(); }
                }
            };
            reset.Click += delegate
            {
                dir = Settings.DefaultDir(Path.GetDirectoryName(Application.ExecutablePath));
                slider.Value = 88;
                logOn = false;
                logLevel = 1;
                Invalidate();
            };
            ok.Click += delegate { DialogResult = DialogResult.OK; Close(); };
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            cancel.KeyDown += delegate (object a, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); } };
            AcceptButton = null;
            CancelButton = null;
        }

        protected override bool ProcessDialogKey(Keys keyData)
        {
            if (keyData == Keys.Enter) { DialogResult = DialogResult.OK; Close(); return true; }
            if (keyData == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); return true; }
            return base.ProcessDialogKey(keyData);
        }

        Rectangle SegmentRect(int i)
        {
            int n = LevelNames.Length;
            int pad = Dpi.S(3);
            int w = (levelRect.Width - pad * 2) / n;
            return new Rectangle(levelRect.X + pad + i * w, levelRect.Y + pad, w, levelRect.Height - pad * 2);
        }

        void SelectLevel(Point p)
        {
            if (!logOn) return;
            for (int i = 0; i < LevelNames.Length; i++)
            {
                if (SegmentRect(i).Contains(p))
                {
                    if (i != logLevel) { logLevel = i; Invalidate(); }
                    return;
                }
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (toggleRect.Contains(e.Location))
            {
                logOn = !logOn;
                Invalidate();
                return;
            }
            if (logOn && levelRect.Contains(e.Location))
            {
                SelectLevel(e.Location);
                return;
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int hv = -1;
            if (logOn && levelRect.Contains(e.Location))
                for (int i = 0; i < LevelNames.Length; i++) if (SegmentRect(i).Contains(e.Location)) { hv = i; break; }
            if (hv != levelHover) { levelHover = hv; Invalidate(); }
            Cursor = (toggleRect.Contains(e.Location) || hv >= 0) ? Cursors.Hand : Cursors.Default;
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (levelHover >= 0) { levelHover = -1; Invalidate(); }
            base.OnMouseLeave(e);
        }

        void DrawToggle(Graphics g)
        {
            int r = toggleRect.Height / 2;
            using (var b = new SolidBrush(logOn ? C.Accent : C.Sunken))
            using (var pa = Rd.Path(toggleRect, r)) g.FillPath(b, pa);
            using (var pen = new Pen(logOn ? C.Accent : C.BorderStrong))
            using (var pa = Rd.Path(toggleRect, r)) g.DrawPath(pen, pa);
            int kr = r - Dpi.S(3);
            int kx = logOn ? toggleRect.Right - r : toggleRect.X + r;
            using (var b = new SolidBrush(logOn ? Color.White : C.TextSecondary))
                g.FillEllipse(b, kx - kr, toggleRect.Y + r - kr, kr * 2, kr * 2);
            using (var b = new SolidBrush(C.TextTertiary))
                g.DrawString(logOn ? "开" : "关", T.MicroTiny, b,
                    new RectangleF(toggleRect.X - Dpi.S(26), toggleRect.Y + Dpi.S(6), Dpi.S(22), Dpi.S(16)));
        }

        /// <summary>日志等级：分段按钮组（容器 + 每档一个胶囊按钮，选中高亮）。</summary>
        void DrawLevelSegments(Graphics g)
        {
            int r = levelRect.Height / 2;
            using (var b = new SolidBrush(C.Sunken))
            using (var pa = Rd.Path(levelRect, r)) g.FillPath(b, pa);
            using (var pen = new Pen(C.BorderSubtle))
            using (var pa = Rd.Path(levelRect, r)) g.DrawPath(pen, pa);

            for (int i = 0; i < LevelNames.Length; i++)
            {
                var sr = SegmentRect(i);
                bool cur = i == logLevel;
                bool hot = logOn && i == levelHover;
                if (cur && logOn)
                {
                    using (var b = new SolidBrush(C.Accent))
                    using (var pa = Rd.Path(sr, Dpi.S(12))) g.FillPath(b, pa);
                }
                else if (hot)
                {
                    using (var b = new SolidBrush(C.SelectedBg))
                    using (var pa = Rd.Path(sr, Dpi.S(12))) g.FillPath(b, pa);
                }
                Color col = cur ? (logOn ? Color.White : C.TextSecondary)
                                : (!logOn ? C.Fade(C.TextTertiary, 140) : (hot ? C.TextPrimary : C.TextSecondary));
                using (var b = new SolidBrush(col))
                {
                    var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    g.DrawString(LevelNames[i], T.Micro, b, new RectangleF(sr.X, sr.Y, sr.Width, sr.Height), sf);
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(C.Base)) g.FillRectangle(b, ClientRectangle);

            // 第 1 组：配置目录
            using (var b = new SolidBrush(C.TextSecondary)) g.DrawString("配置文件目录（MSM-config.json）", T.Micro, b, Dpi.S(18), Dpi.S(16));
            using (var b = new SolidBrush(C.Sunken))
            using (var pa = Rd.Path(pathBox, Rad.Chip)) g.FillPath(b, pa);
            using (var pen = new Pen(C.BorderSubtle))
            using (var pa = Rd.Path(pathBox, Rad.Chip)) g.DrawPath(pen, pa);
            TextRenderer.DrawText(g, dir, T.Micro, new Rectangle(pathBox.X + Dpi.S(10), pathBox.Y, pathBox.Width - Dpi.S(20), pathBox.Height),
                C.TextPrimary, TextFormatFlags.VerticalCenter | TextFormatFlags.PathEllipsis | TextFormatFlags.SingleLine);

            // 第 2 组：行高
            using (var b = new SolidBrush(C.TextSecondary)) g.DrawString("列表行高（每行高度，56–110）", T.Micro, b, Dpi.S(18), Dpi.S(80));
            using (var b = new SolidBrush(C.TextPrimary))
                g.DrawString(slider.Value + " px", T.Caption, b, Dpi.S(330), Dpi.S(104));
            using (var b = new SolidBrush(C.TextTertiary)) g.DrawString("56", T.Micro, b, Dpi.S(20), Dpi.S(128));
            using (var b = new SolidBrush(C.TextTertiary)) g.DrawString("110", T.Micro, b, Dpi.S(296), Dpi.S(128));

            using (var pen = new Pen(C.BorderSubtle)) g.DrawLine(pen, Dpi.S(18), Dpi.S(150), ClientSize.Width - Dpi.S(18), Dpi.S(150));

            // 第 3 组：日志开关 + 等级
            using (var b = new SolidBrush(C.TextSecondary))
                g.DrawString("记录日志（关 = 完全不写，节省空间）", T.Micro, b, Dpi.S(18), Dpi.S(166));
            DrawToggle(g);

            using (var b = new SolidBrush(logOn ? C.TextSecondary : C.TextTertiary))
                g.DrawString("日志等级：" + LevelNames[logLevel] + "（" + LevelDesc[logLevel] + "）", T.Micro, b, Dpi.S(18), Dpi.S(206));
            DrawLevelSegments(g);

            if (!string.IsNullOrEmpty(openHint))
            {
                using (var b = new SolidBrush(C.Danger))
                {
                    var sf = new StringFormat { FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter };
                    g.DrawString(openHint, T.Micro, b, new RectangleF(Dpi.S(18), Dpi.S(262), ClientSize.Width - Dpi.S(36), Dpi.S(20)), sf);
                }
            }

            using (var pen = new Pen(C.BorderSubtle)) g.DrawLine(pen, Dpi.S(18), Dpi.S(286), ClientSize.Width - Dpi.S(18), Dpi.S(286));
        }
    }
}
