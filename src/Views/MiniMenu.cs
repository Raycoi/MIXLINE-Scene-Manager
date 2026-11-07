// MIXLINE Scene Manager — Views/MiniMenu.cs
// 自绘弹出菜单（单层，行内确认/子菜单能力已移除：所有两步操作都改用行内/底部栏按钮）。
// .NET Framework 4.8 / csc(C# 5)
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Asr
{
    class MiniMenu : Form
    {
        public class Item
        {
            public string Text; public string Tip; public Action Fn; public bool Danger; public bool Sep;
            public Item(string t, Action f) { Text = t; Fn = f; }
            public Item(string t, Action f, bool danger) { Text = t; Fn = f; Danger = danger; }
            public Item(string t, string tip, Action f) { Text = t; Tip = tip; Fn = f; }
            public Item(string t, string tip, Action f, bool danger) { Text = t; Tip = tip; Fn = f; Danger = danger; }
        }

        readonly List<Item> items;
        int hover = -1;
        static int PadV { get { return Dpi.S(6); } }
        static int SepH { get { return Dpi.S(9); } }
        int W = 306, H;

        static readonly Font FMain = new Font("Microsoft YaHei UI", 9.5f);
        static readonly Font FTip = new Font("Microsoft YaHei UI", 8f);
        static int TitleH { get { return Dpi.S(22); } }
        static int TipLineH { get { return Dpi.S(14); } }
        static int ItemPadV { get { return Dpi.S(8); } }

        /// <summary>与绘制使用同一字体/同一宽度测量提示文字换行数，避免高度算小导致条目互相重叠。</summary>
        int TipLines(string tip)
        {
            if (string.IsNullOrEmpty(tip)) return 0;
            var sz = TextRenderer.MeasureText(tip, FTip, new Size(W - 32, 0),
                TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            int lineH = Math.Max(1, TextRenderer.MeasureText("中", FTip).Height);
            int n = (int)Math.Ceiling((double)sz.Height / lineH);
            return n < 1 ? 1 : n;
        }
        int HItem(Item it)
        {
            if (it.Sep) return SepH;
            int lines = TipLines(it.Tip);
            return ItemPadV * 2 + TitleH + lines * TipLineH;
        }

        public MiniMenu(List<Item> list)
        {
            items = list;
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual; BackColor = C.Bg2; TopMost = true;
            DoubleBuffered = true; Font = new Font("Microsoft YaHei UI", 9.5f);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            ComputeWidth();
            Height = ComputeHeight();
            ClientSize = new Size(W, H);
            using (var gp = Rd.Path(new Rectangle(0, 0, W, H), 10)) Region = new Region(gp);
            Deactivate += (a, b) => Close();
            KeyDown += (a, b) => { if (b.KeyCode == Keys.Escape) Close(); };
            MouseMove += OnMove2; MouseClick += OnClick2;
        }

        int ComputeHeight()
        {
            H = PadV * 2;
            foreach (var it in items) H += HItem(it);
            return H;
        }

        /// <summary>按最长条目测量宽度（190–340）。</summary>
        void ComputeWidth()
        {
            int w = Dpi.S(190);
            foreach (var it in items)
            {
                if (it.Sep) continue;
                int tw = TextRenderer.MeasureText(it.Text, T.Hint).Width + Dpi.S(40);
                if (tw > w) w = tw;
                if (!string.IsNullOrEmpty(it.Tip))
                {
                    int t2 = Math.Min(Dpi.S(300), TextRenderer.MeasureText(it.Tip, FTip).Width + Dpi.S(40));
                    if (t2 > w) w = t2;
                }
            }
            W = Math.Max(Dpi.S(190), Math.Min(Dpi.S(340), w));
        }

        public void ShowAt(Func<Point> where)
        {
            var pt = where();
            var wa = Screen.FromPoint(pt).WorkingArea;
            int x = Math.Min(pt.X, wa.Right - W - 4);
            int y = Math.Min(pt.Y, wa.Bottom - H - 4);
            Location = new Point(Math.Max(wa.Left, x), Math.Max(wa.Top, y));
            Show(); Activate();
        }

        int ItemTop(int i)
        {
            int y = PadV;
            for (int k = 0; k < i; k++) y += HItem(items[k]);
            return y;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(C.Bg2)) g.FillRectangle(b, ClientRectangle);
            using (var pen = new Pen(C.Line)) using (var gp = Rd.Path(new Rectangle(0, 0, W - 1, H - 1), 10))
                g.DrawPath(pen, gp);

            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i]; int y = ItemTop(i); int hh = HItem(it);
                if (it.Sep)
                {
                    using (var pen = new Pen(C.Line)) g.DrawLine(pen, Dpi.S(10), y + SepH / 2, W - Dpi.S(10), y + SepH / 2);
                    continue;
                }
                if (i == hover)
                {
                    using (var b = new SolidBrush(it.Danger ? C.Fade(C.Danger, 26) : C.SelectedBg))
                    using (var gp = Rd.Path(new Rectangle(5, y + 1, W - 10, hh - 4), 7)) g.FillPath(b, gp);
                }
                if (it.Danger)
                {
                    using (var b = new SolidBrush(C.Danger))
                        g.FillRectangle(b, 7, y + 7, 2, hh - 16);
                }
                bool hasTip = !string.IsNullOrEmpty(it.Tip);
                float ty = hasTip ? y + ItemPadV : y + (hh - TitleH) / 2f;
                using (var b = new SolidBrush(it.Danger ? C.Err : C.Text))
                {
                    var sf = new StringFormat { FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter };
                    g.DrawString(it.Text, FMain, b, new RectangleF(Dpi.S(16), ty, W - Dpi.S(40), TitleH), sf);
                }
                if (hasTip)
                {
                    TextRenderer.DrawText(g, it.Tip, FTip,
                        new Rectangle(Dpi.S(16), y + ItemPadV + TitleH, W - Dpi.S(32), hh - ItemPadV * 2 - TitleH),
                        C.Mut, TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                }
            }
        }

        int HitItem(Point pt)
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].Sep) continue;
                int y = ItemTop(i); int hh = HItem(items[i]);
                if (pt.Y >= y + 1 && pt.Y <= y + hh - 3) return i;
            }
            return -1;
        }

        void OnMove2(object sender, MouseEventArgs e)
        {
            int h = HitItem(e.Location);
            if (h != hover) { hover = h; Invalidate(); }
            Cursor = h >= 0 ? Cursors.Hand : Cursors.Default;
        }

        void OnClick2(object sender, MouseEventArgs e)
        {
            int h = HitItem(e.Location);
            if (h < 0) return;
            var it = items[h];
            Close();
            if (it.Fn != null) { try { it.Fn(); } catch (Exception ex) { Log.Debug("MiniMenu.cs: 菜单项执行失败", ex); } }
        }
    }

    // 托盘菜单：深色渲染器
    class DarkRenderer : ToolStripProfessionalRenderer
    {
        public DarkRenderer() : base(new DarkColors()) { }
    }

    class DarkColors : ProfessionalColorTable
    {
        public override Color MenuItemSelected { get { return Color.FromArgb(31, 35, 43); } }
        public override Color MenuItemBorder { get { return Color.FromArgb(79, 140, 255); } }
        public override Color ToolStripDropDownBackground { get { return Color.FromArgb(25, 28, 34); } }
        public override Color ImageMarginGradientBegin { get { return Color.FromArgb(25, 28, 34); } }
        public override Color ImageMarginGradientMiddle { get { return Color.FromArgb(25, 28, 34); } }
        public override Color ImageMarginGradientEnd { get { return Color.FromArgb(25, 28, 34); } }
        public override Color SeparatorDark { get { return Color.FromArgb(42, 46, 56); } }
        public override Color SeparatorLight { get { return Color.FromArgb(42, 46, 56); } }
    }
}
