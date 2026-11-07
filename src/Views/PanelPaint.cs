// MIXLINE Scene Manager — Views/PanelPaint.cs
// 主界面"行"的纯绘制：只依赖 (Graphics, Rectangle, Scene, Store, RowPaintState)，
// 不接触 Panel 的交互状态，便于单独调整视觉、也避免 PanelForm 继续膨胀。
using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Asr
{
    /// <summary>一行的绘制状态（由 Panel 在绘制前填充，绘制方只读）。</summary>
    class RowPaintState
    {
        public bool Active;          // 当前生效
        public bool Selected;        // 多选选中
        public bool Hover;           // 鼠标悬停
        public bool Focus;           // 键盘焦点
        public bool Switching;       // 正在切换
        public bool Failed;          // 上次切换失败
        public bool DelAsk;          // 行内删除确认
        public bool RenameEdit;      // 行内重命名中
        public string FailReason;    // 失败原因（可空）
        public int HoverBtn = -1;    // 悬停的图标按钮 0..2
        public int PressedBtn = -1;  // 按下的图标按钮 0..2
        public float SpinAngle;      // 切换转圈角度
    }

    static class PanelPaint
    {
        public static int BtnW { get { return Dpi.S(34); } }
        public static int BtnH { get { return Dpi.S(26); } }
        public const int BtnN = 3;

        static readonly Font RowTitle = T.Body;
        static readonly Font RowSub = T.MicroSmall;
        static readonly Font FIcon = new Font("Segoe MDL2 Assets", 9.5f);

        // ---------- 几何（Panel 的命中测试也复用这里，避免绘制/命中漂移） ----------
        public static Rectangle BtnRect(Rectangle row, int i)
        {
            int gap = Dpi.S(6);
            int x = row.Right - Dpi.S(16) - BtnW * BtnN - gap * (BtnN - 1) + i * (BtnW + gap);
            return new Rectangle(x, row.Y + row.Height - Dpi.S(34), BtnW, BtnH);
        }
        public static Rectangle OkRect(Rectangle r) { int w = Dpi.S(56), h = Dpi.S(26); return new Rectangle(r.Right - Dpi.S(16) - w - Dpi.S(8) - w, r.Y + r.Height / 2 - h / 2, w, h); }
        public static Rectangle CancelRect(Rectangle r) { int w = Dpi.S(56), h = Dpi.S(26); return new Rectangle(r.Right - Dpi.S(16) - w, r.Y + r.Height / 2 - h / 2, w, h); }
        public static Rectangle ConfirmOkRect(Rectangle r) { int w = Dpi.S(56), h = Dpi.S(26); return new Rectangle(r.Right - Dpi.S(16) - w - Dpi.S(8) - w, r.Y + r.Height - Dpi.S(34), w, h); }
        public static Rectangle ConfirmCancelRect(Rectangle r) { int w = Dpi.S(56), h = Dpi.S(26); return new Rectangle(r.Right - Dpi.S(16) - w, r.Y + r.Height - Dpi.S(34), w, h); }

        // ---------- 基础控件 ----------
        public static void DrawInlineButton(Graphics g, Rectangle r, string text, bool primary)
        {
            DrawInlineButton(g, r, text, primary, false);
        }

        public static void DrawInlineButton(Graphics g, Rectangle r, string text, bool primary, bool danger)
        {
            if (primary)
                using (var b = new SolidBrush(danger ? C.Danger : C.Accent))
                using (var pa = Rd.Path(r, Rad.Button)) g.FillPath(b, pa);
            else
                using (var pen = new Pen(C.BorderStrong))
                using (var pa = Rd.Path(r, Rad.Button)) g.DrawPath(pen, pa);

            using (var b = new SolidBrush(primary ? Color.White : C.TextSecondary))
            {
                var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString(text, RowSub, b, new RectangleF(r.X, r.Y, r.Width, r.Height), sf);
            }
        }

        /// <summary>行尾图标：0=重命名(铅笔 E70F) 1=设置(齿轮 E713) 2=删除(垃圾桶 E74D)。</summary>
        public static void DrawRowBtnIcon(Graphics g, Rectangle r, int kind, Color col)
        {
            string glyph = kind == 0 ? "\uE70F" : (kind == 1 ? "\uE713" : "\uE74D");
            try
            {
                var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                using (var b = new SolidBrush(col))
                    g.DrawString(glyph, FIcon, b, new RectangleF(r.X, r.Y, r.Width, r.Height), sf);
            }
            catch (Exception ex)
            {
                // 极少数系统没有该字体时退回简单形状，避免空白
                Log.Debug("MDL2 图标字体不可用", ex);
                using (var pen = new Pen(col, 1.4f))
                {
                    int cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
                    if (kind == 0) g.DrawLine(pen, cx - 6, cy + 6, cx + 5, cy - 5);
                    else if (kind == 1) g.DrawEllipse(pen, cx - 5, cy - 5, 10, 10);
                    else { g.DrawLine(pen, cx - 6, cy - 5, cx + 6, cy - 5); g.DrawRectangle(pen, cx - 4, cy - 3, 8, 9); }
                }
            }
        }

        // ---------- 行 ----------
        public static void DrawSceneRow(Graphics g, Rectangle r, Scene sc, Store store, RowPaintState st)
        {
            bool on = st.Active;
            bool delAsk = st.DelAsk;

            Color bg = delAsk ? C.Fade(C.Danger, 34)
                     : (on ? C.ActiveBg : (st.Selected ? C.SelectedBg : (st.Hover ? C.CardHover : C.Card)));
            using (var b = new SolidBrush(bg))
            using (var pa = Rd.Path(r, Rad.Card)) g.FillPath(b, pa);

            Color border = delAsk ? C.Fade(C.Danger, 200)
                         : (on ? C.Fade(C.Accent, 90) : (st.Selected ? C.Fade(C.Accent, 170) : C.BorderSubtle));
            float bw = (st.Selected && !on) || delAsk ? 1.6f : 1f;
            using (var pen = new Pen(border, bw))
            using (var pa = Rd.Path(r, Rad.Card)) g.DrawPath(pen, pa);

            if (on || st.Selected)
                using (var b = new SolidBrush(C.Accent))
                    g.FillRectangle(b, r.X + 1, r.Y + Rad.Card, 3, Math.Max(4, r.Height - Rad.Card * 2));

            if (st.Focus)
                using (var pen = new Pen(C.Accent, 1.6f))
                using (var pa = Rd.Path(new Rectangle(r.X - 3, r.Y - 3, r.Width + 6, r.Height + 6), Rad.Card + 3))
                    g.DrawPath(pen, pa);

            // 重命名中：只画输入框区 + 保存/取消（不画旧标题/摘要，避免残留文字）
            if (st.RenameEdit)
            {
                DrawInlineButton(g, ConfirmOkRect(r), "保存", true, false);
                DrawInlineButton(g, ConfirmCancelRect(r), "取消", false, false);
                return;
            }

            // 场景名前的状态点：生效/选中 = 蓝色，其它 = 灰色
            int dotSize = Dpi.S(8);
            int titleX = r.X + Dpi.S(16);
            int titleTop = r.Y + Dpi.S(12);
            using (var b = new SolidBrush(on || st.Selected ? C.Accent : C.TextTertiary))
                g.FillEllipse(b, titleX, titleTop + (Dpi.S(22) - dotSize) / 2, dotSize, dotSize);
            int textX = titleX + dotSize + Dpi.S(9);      // 标题/副标题统一左对齐到这里

            using (var b = new SolidBrush(C.TextPrimary))
            {
                var sf = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
                g.DrawString(sc.name ?? "", RowTitle, b, new RectangleF(textX, titleTop, r.Right - textX - Dpi.S(130), Dpi.S(22)), sf);
            }

            if (delAsk)
            {
                using (var b = new SolidBrush(C.Danger))
                {
                    var sf = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
                    g.DrawString("你确定要删除此场景吗？", RowSub, b,
                        new RectangleF(textX, r.Y + r.Height - Dpi.S(30), r.Right - textX - Dpi.S(150), Dpi.S(18)), sf);
                }
                DrawInlineButton(g, ConfirmOkRect(r), "删除", true, true);
                DrawInlineButton(g, ConfirmCancelRect(r), "取消", false, false);
                return;
            }

            // 副标题：切换中 / 失败原因 / 设备摘要
            string sub; Color subCol = C.TextSecondary;
            if (st.Switching) { sub = "正在切换…"; subCol = C.Accent; }
            else if (st.Failed)
            {
                sub = "切换失败" + (string.IsNullOrEmpty(st.FailReason) ? "" : "：" + st.FailReason);
                subCol = C.Danger;
            }
            else
            {
                DrawSystemIOSummary(g, r, sc, store, textX);   // 系统输入 / 系统输出（两行）
                sub = null;
            }
            if (sub != null)
                using (var b = new SolidBrush(subCol))
                {
                    var sf = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
                    g.DrawString(sub, RowSub, b, new RectangleF(textX, r.Y + r.Height - Dpi.S(30), r.Right - textX - Dpi.S(130), Dpi.S(18)), sf);
                }

            // 右上角：状态胶囊 或 路数
            string badge = on ? "生效中" : (st.Failed ? "失败" : "");
            if (badge.Length == 0)
            {
                string cnt = sc.links.Count + " 路";
                var szc = g.MeasureString(cnt, T.Micro);
                using (var b = new SolidBrush(C.TextTertiary))
                    g.DrawString(cnt, T.Micro, b, r.Right - Dpi.S(16) - szc.Width, r.Y + Dpi.S(16));
            }
            else
            {
                Color bc = on ? C.Accent : C.Danger;
                var sz = g.MeasureString(badge, T.Micro);
                var pr = new Rectangle(r.Right - Dpi.S(16) - (int)sz.Width - Dpi.S(18), r.Y + Dpi.S(12), (int)sz.Width + Dpi.S(18), Dpi.S(20));
                using (var b = new SolidBrush(C.Fade(bc, 36)))
                using (var pa = Rd.Path(pr, Dpi.S(10))) g.FillPath(b, pa);
                using (var b = new SolidBrush(bc))
                {
                    var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    g.DrawString(badge, T.Micro, b, new RectangleF(pr.X, pr.Y, pr.Width, pr.Height), sf);
                }
            }

            // 行尾三个图标按钮
            for (int bi = 0; bi < BtnN; bi++)
            {
                var br = BtnRect(r, bi);
                bool hot = st.Hover && st.HoverBtn == bi;
                bool down = hot && st.PressedBtn == bi;
                using (var pen = new Pen(hot ? C.Accent : C.BorderStrong, down ? 1.6f : 1f))
                using (var pa = Rd.Path(br, Rad.Button)) g.DrawPath(pen, pa);
                if (hot)
                    using (var b = new SolidBrush(C.Overlay(down ? 26 : 14)))
                    using (var pa = Rd.Path(br, Rad.Button)) g.FillPath(b, pa);
                DrawRowBtnIcon(g, br, bi, hot ? C.TextPrimary : C.TextSecondary);
            }

            // 切换中：整行压暗 + 居中转圈
            if (st.Switching)
            {
                using (var b = new SolidBrush(C.Fade(C.Base, 120)))
                using (var pa = Rd.Path(r, Rad.Card)) g.FillPath(b, pa);
                int sz2 = Dpi.S(22); var sp = new Rectangle(r.X + r.Width / 2 - sz2 / 2, r.Y + r.Height / 2 - sz2 / 2, sz2, sz2);
                using (var pen = new Pen(C.Accent, 2.4f))
                {
                    pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round;
                    g.DrawArc(pen, sp, st.SpinAngle, 110f);
                }
            }
        }

        /// <summary>行副标题两行：第一行系统输入、第二行系统输出。
        /// "标签+冒号"整体绘制并按最大宽度对齐数值列，避免冒号/数值错位与多余空隙。</summary>
        static void DrawSystemIOSummary(Graphics g, Rectangle r, Scene sc, Store store, int x)
        {
            string inName = SystemIO.SummaryIn(sc, store);
            string outName = SystemIO.SummaryOut(sc, store);
            int maxRight = r.Right - Dpi.S(16) - (BtnW * BtnN + Dpi.S(6) * (BtnN - 1)) - Dpi.S(8);
            int avail = maxRight - x;
            int lineH = Dpi.S(17);
            int contentTop = r.Y + Dpi.S(34);
            int contentH = r.Height - Dpi.S(34) - Dpi.S(6);

            string inLbl = "系统输入：", outLbl = "系统输出：";
            if (avail < Dpi.S(150)) { inLbl = "输入："; outLbl = "输出："; }
            // 小一号字；值列紧贴冒号（全角冒号字形自带右侧留白，这里扣掉一点）
            float lw = Math.Max(g.MeasureString(inLbl, T.Micro).Width, g.MeasureString(outLbl, T.Micro).Width);
            int valueX = x + (int)Math.Ceiling(lw) - Dpi.S(2);
            int valueW = Math.Max(Dpi.S(30), maxRight - valueX);
            var sf = new StringFormat { FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter };

            int gap = (contentH - lineH * 2) / 2;
            if (gap < 0) gap = 0;
            int l1 = contentTop + gap;
            int l2 = l1 + lineH;
            bool twoLines = l2 + lineH <= r.Y + r.Height - Dpi.S(2);

            if (twoLines)
            {
                DrawSysRow(g, x, valueX, valueW, l1, lineH, inLbl, inName, sf);
                DrawSysRow(g, x, valueX, valueW, l2, lineH, outLbl, outName, sf);
            }
            else
            {
                string text = inLbl + inName + "    " + outLbl + outName;
                using (var b = new SolidBrush(C.TextSecondary))
                    g.DrawString(text, T.MicroTiny, b, new RectangleF(x, contentTop - Dpi.S(2), avail, Dpi.S(14)), sf);
            }
        }

        static void DrawSysRow(Graphics g, int x, int valueX, int valueW, int y, int lineH, string labelWithColon, string value, StringFormat sf)
        {
            using (var b = new SolidBrush(C.TextSecondary))
                g.DrawString(labelWithColon, T.Micro, b, x, y);
            using (var b = new SolidBrush(C.TextPrimary))
                g.DrawString(value, T.Micro, b, new RectangleF(valueX, y, valueW, lineH), sf);
        }
        public static void DrawSaveRow(Graphics g, Rectangle r, bool hot, bool editingNew)
        {
            if (editingNew)
            {
                using (var b = new SolidBrush(C.CardHover))
                using (var pa = Rd.Path(r, Rad.Card)) g.FillPath(b, pa);
                return;
            }
            using (var pen = new Pen(hot ? C.Accent : C.BorderSubtle, 1f))
            {
                pen.DashStyle = DashStyle.Dash;
                using (var pa = Rd.Path(r, Rad.Card)) g.DrawPath(pen, pa);
            }
            if (hot)
                using (var b = new SolidBrush(C.Overlay(8)))
                using (var pa = Rd.Path(r, Rad.Card)) g.FillPath(b, pa);

            string t = "＋  保存当前路由";
            var sz = g.MeasureString(t, RowTitle);
            using (var b = new SolidBrush(hot ? C.TextPrimary : C.TextSecondary))
                g.DrawString(t, RowTitle, b, r.X + (r.Width - sz.Width) / 2, r.Y + (r.Height - sz.Height) / 2);
        }

        // ---------- 头部 ----------
        public static void DrawHeader(Graphics g, Rectangle client, string sub, bool subAccent,
                                      bool engineUp, bool menuHot, bool menuDown)
        {
            using (var b = new SolidBrush(C.TextPrimary)) g.DrawString("场景", T.Title, b, Dpi.S(16), Dpi.S(12));
            using (var b = new SolidBrush(subAccent ? C.Accent : C.TextSecondary))
                g.DrawString(sub, T.Caption, b, Dpi.S(17), Dpi.S(38));

            // 右上角 ⋯
            var cr = new Rectangle(client.Width - Dpi.S(16) - Dpi.S(34), Dpi.S(12), Dpi.S(34), Dpi.S(34));
            if (menuHot || menuDown)
            {
                using (var b = new SolidBrush(C.Overlay(menuDown ? 30 : 16)))
                using (var pa = Rd.Path(cr, Rad.Button)) g.FillPath(b, pa);
            }
            if (menuDown)
                using (var pen = new Pen(C.Accent, 1.2f))
                using (var pa = Rd.Path(cr, Rad.Button)) g.DrawPath(pen, pa);
            using (var b = new SolidBrush(menuHot ? C.TextPrimary : C.TextSecondary))
                for (int k = 0; k < 3; k++) g.FillEllipse(b, cr.X + Dpi.S(10) + k * Dpi.S(5), cr.Y + Dpi.S(15), Dpi.S(3), Dpi.S(3));

            // 引擎状态胶囊
            string st = engineUp ? "MIXLINE 运行中" : "MIXLINE 未运行";
            Color sc = engineUp ? C.Success : C.Danger;
            var sz = g.MeasureString(st, T.Micro);
            int pw = (int)sz.Width + Dpi.S(Sp.XL + 10), ph = Dpi.S(24);
            var pill = new Rectangle(cr.X - Dpi.S(Sp.S) - pw - Dpi.S(Sp.S), Dpi.S(16), pw, ph);
            using (var b = new SolidBrush(C.Overlay(10)))
            using (var pa = Rd.Path(pill, ph / 2)) g.FillPath(b, pa);
            using (var b = new SolidBrush(C.Fade(sc, 60))) g.FillEllipse(b, pill.X + Dpi.S(9), pill.Y + ph / 2 - Dpi.S(5), Dpi.S(10), Dpi.S(10));
            using (var b = new SolidBrush(sc)) g.FillEllipse(b, pill.X + Dpi.S(11), pill.Y + ph / 2 - Dpi.S(3), Dpi.S(6), Dpi.S(6));
            using (var b = new SolidBrush(C.TextSecondary)) g.DrawString(st, T.Micro, b, pill.X + Dpi.S(Sp.XL), pill.Y + Dpi.S(5));
        }
    }
}
