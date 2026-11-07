// MIXLINE Scene Manager — Views/FlatControls.cs
// 自绘基础控件：按钮与滑块。全部状态（默认/悬停/按下/禁用）在一处实现，避免系统控件外观。
using System;
using System.Drawing;
using System.Windows.Forms;

namespace Asr
{
    /// <summary>扁平按钮：Primary（强调色）/ Secondary（描边）/ Danger。</summary>
    class FlatButton : Control
    {
        public enum Kind { Primary, Secondary, Danger }

        public Kind Style = Kind.Secondary;
        bool hot, down;

        public FlatButton(string text, Kind style)
        {
            Text = text;
            Style = style;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Font = T.Body;
            Cursor = Cursors.Hand;
            Height = 32;
        }

        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { down = true; Invalidate(); } base.OnMouseDown(e); }
        DateTime lastClickAt;
        protected override void OnClick(EventArgs e)
        {
            // 部分情况下鼠标消息会让 Click 触发两次（打开文件夹会开两个窗口），这里做一次去抖
            if ((DateTime.UtcNow - lastClickAt).TotalMilliseconds < 150) return;
            lastClickAt = DateTime.UtcNow;
            base.OnClick(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && down)
            {
                down = false; Invalidate();
                if (ClientRectangle.Contains(e.Location)) OnClick(EventArgs.Empty);
            }
            base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            Color fg, bg = Color.Transparent;
            bool outlined = false;

            if (Style == Kind.Primary) { fg = Color.White; bg = down ? C.Fade(C.Accent, 200) : (hot ? C.Fade(C.Accent, 230) : C.Accent); }
            else if (Style == Kind.Danger) { fg = C.Danger; if (hot || down) bg = C.Fade(C.Danger, down ? 40 : 24); }
            else { fg = hot || down ? C.TextPrimary : C.TextSecondary; outlined = true; if (hot || down) bg = C.Overlay(down ? 16 : 10); }

            if (bg != Color.Transparent)
                using (var b = new SolidBrush(bg))
                using (var pa = Rd.Path(r, Rad.Button)) g.FillPath(b, pa);
            if (outlined)
                using (var pen = new Pen(hot ? C.BorderStrong : C.BorderSubtle))
                using (var pa = Rd.Path(r, Rad.Button)) g.DrawPath(pen, pa);

            TextRenderer.DrawText(g, Text, Font, ClientRectangle, Enabled ? fg : C.TextTertiary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>扁平滑块：12–64 像素范围，拖拽/方向键调节。</summary>
    class FlatSlider : Control
    {
        public int Minimum = 12, Maximum = 64;
        int val = 36;
        bool drag;
        public event EventHandler ValueChanged;

        public int Value
        {
            get { return val; }
            set
            {
                int v = Math.Max(Minimum, Math.Min(Maximum, value));
                if (v == val) return;
                val = v;
                Invalidate();
                if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            }
        }

        public FlatSlider()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = 28;
            Cursor = Cursors.Hand;
        }

        Rectangle TrackRect { get { return new Rectangle(10, Height / 2 - 2, Width - 20, 4); } }

        int KnobX
        {
            get
            {
                var tr = TrackRect;
                int span = Math.Max(1, Maximum - Minimum);
                return tr.X + (int)Math.Round((val - Minimum) / (float)span * tr.Width);
            }
        }

        void SetFromX(int x)
        {
            var tr = TrackRect;
            double k = (x - tr.X) / (double)Math.Max(1, tr.Width);
            Value = Minimum + (int)Math.Round(k * (Maximum - Minimum));
        }

        protected override void OnMouseDown(MouseEventArgs e) { drag = true; SetFromX(e.X); base.OnMouseDown(e); }
        protected override void OnMouseMove(MouseEventArgs e) { if (drag) SetFromX(e.X); base.OnMouseMove(e); }
        protected override void OnMouseUp(MouseEventArgs e) { drag = false; base.OnMouseUp(e); }
        protected override void OnMouseWheel(MouseEventArgs e) { Value = val + (e.Delta > 0 ? 2 : -2); base.OnMouseWheel(e); }

        protected override bool IsInputKey(Keys keyData)
        {
            if (keyData == Keys.Left || keyData == Keys.Right) return true;
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Left) { Value = val - 1; e.Handled = true; }
            else if (e.KeyCode == Keys.Right) { Value = val + 1; e.Handled = true; }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var tr = TrackRect;

            using (var b = new SolidBrush(C.BorderSubtle))
            using (var pa = Rd.Path(tr, 2)) g.FillPath(b, pa);

            var filled = new Rectangle(tr.X, tr.Y, Math.Max(2, KnobX - tr.X), tr.Height);
            using (var b = new SolidBrush(C.Accent))
            using (var pa = Rd.Path(filled, 2)) g.FillPath(b, pa);

            int kx = KnobX;
            using (var b = new SolidBrush(C.TextPrimary))
                g.FillEllipse(b, kx - 6, Height / 2 - 6, 12, 12);
            using (var b = new SolidBrush(C.Accent))
                g.FillEllipse(b, kx - 4, Height / 2 - 4, 8, 8);
        }
    }
}
