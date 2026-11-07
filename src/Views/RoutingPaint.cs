// MIXLINE Scene Manager — Views/RoutingPaint.cs
// 路由可视化的公共绘制：主界面与详情窗共用，保证"拖拽预览 / 曲线高亮 / 芯片框"完全一致。
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Asr
{
    static class RoutingPaint
    {
        // 贝塞尔控制点比例（与连线绘制保持同一曲率）
        const float C1 = 0.42f, C2 = 0.58f;

        /// <summary>采样一条"水平入、水平出"的贝塞尔曲线（用于命中测试与高亮描边）。</summary>
        public static PointF[] Bezier(Point start, Point end, int segments)
        {
            if (segments < 2) segments = 2;
            float x0 = start.X, y0 = start.Y, x3 = end.X, y3 = end.Y;
            float c1x = x0 + (x3 - x0) * C1, c1y = y0;
            float c2x = x0 + (x3 - x0) * C2, c2y = y3;
            var pts = new PointF[segments + 1];
            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments, u = 1 - t;
                pts[i] = new PointF(
                    u * u * u * x0 + 3 * u * u * t * c1x + 3 * u * t * t * c2x + t * t * t * x3,
                    u * u * u * y0 + 3 * u * u * t * c1y + 3 * u * t * t * c2y + t * t * t * y3);
            }
            return pts;
        }

        public static void Polyline(Graphics g, PointF[] pts, Color color, float width)
        {
            if (pts == null || pts.Length < 2) return;
            using (var pen = new Pen(color, width)) g.DrawLines(pen, pts);
        }

        /// <summary>
        /// 拖拽中的连线预览：贝塞尔虚线 + 两端圆点。
        /// ok=true 绿色（落在合法目标上），false 红色（松手将取消）。
        /// </summary>
        public static void DrawDragCurve(Graphics g, Point start, Point end, bool ok)
        {
            Color col = ok ? C.Success : C.Danger;
            var pts = Bezier(start, end, 24);
            using (var pen = new Pen(col, 1.8f))
            {
                pen.DashStyle = DashStyle.Dash;
                g.DrawLines(pen, pts);
            }
            using (var b = new SolidBrush(col))
            {
                g.FillEllipse(b, start.X - 3, start.Y - 3, 6, 6);
                g.FillEllipse(b, end.X - 3.5f, end.Y - 3.5f, 7, 7);
            }
        }

        /// <summary>
        /// 曲线悬停高亮：先画一圈半透明白色"光晕"，再把原色细线画回去。
        /// 这样是描边效果（outline），而不是把线加粗/填白。
        /// </summary>
        public static void DrawCurveHighlight(Graphics g, PointF[] pts, Color core)
        {
            // 半透明细白圈 + 原色芯线：看起来是"描边"，不会变成一根白棒
            Polyline(g, pts, Color.FromArgb(120, 255, 255, 255), 4.0f);
            Polyline(g, pts, core, 1.8f);
        }

        /// <summary>统一的芯片外观：层级底色 + 圆角描边 + 输入/输出色条。</summary>
        public static void DrawChipFrame(Graphics g, Rectangle r, bool hot, bool isInput, Color accent)
        {
            using (var b = new SolidBrush(hot ? C.Overlay(10) : C.Sunken))
            using (var path = Rd.Path(r, Rad.Chip)) g.FillPath(b, path);
            using (var pen = new Pen(hot ? C.Accent : C.BorderSubtle, hot ? 1.4f : 1f))
            using (var path = Rd.Path(r, Rad.Chip)) g.DrawPath(pen, path);


        }

        // ---------------- 图标与芯片（主界面与详情窗共用） ----------------

        /// <summary>按设备类型画矢量图标（16px 基准，随 size 缩放）。</summary>
        public static void DrawGlyph(Graphics g, int x, int y, int size, string kind, Color col)
        {
            float u = size / 16f;
            using (var pen = new Pen(col, Math.Max(1.2f, 1.5f * u)))
            using (var br = new SolidBrush(col))
            {
                if (kind == "mic")
                {
                    g.DrawEllipse(pen, x + 5.5f * u, y + 2f * u, 5f * u, 8f * u);
                    g.DrawArc(pen, x + 3f * u, y + 6f * u, 10f * u, 9f * u, 200, 140);
                    g.DrawLine(pen, x + 8f * u, y + 13f * u, x + 8f * u, y + 15f * u);
                    g.DrawLine(pen, x + 5f * u, y + 15f * u, x + 11f * u, y + 15f * u);
                }
                else if (kind == "speaker")
                {
                    g.FillPolygon(br, new PointF[] {
                        new PointF(x + 2f*u, y + 5.5f*u), new PointF(x + 5f*u, y + 5.5f*u),
                        new PointF(x + 9f*u, y + 2f*u),  new PointF(x + 9f*u, y + 14f*u),
                        new PointF(x + 5f*u, y + 10.5f*u), new PointF(x + 2f*u, y + 10.5f*u) });
                    g.DrawArc(pen, x + 8f*u, y + 4f*u, 6f*u, 8f*u, -55, 110);
                }
                else if (kind == "stream")
                {
                    g.DrawRectangle(pen, x + 1.5f*u, y + 2f*u, 10f*u, 12f*u);
                    g.FillPolygon(br, new PointF[] {
                        new PointF(x + 5.5f*u, y + 5f*u), new PointF(x + 5.5f*u, y + 11f*u), new PointF(x + 10f*u, y + 8f*u) });
                }
                else
                {
                    g.DrawRectangle(pen, x + 1.5f*u, y + 2.5f*u, 13f*u, 11f*u);
                    g.DrawLine(pen, x + 1.5f*u, y + 6f*u, x + 14.5f*u, y + 6f*u);
                    g.FillEllipse(br, x + 3f*u, y + 3.5f*u, 1.6f*u, 1.6f*u);
                }
            }
        }

        /// <summary>
        /// 统一的芯片：方框 + 输入左条/输出右条 + 图标（应用图标或矢量）+ 单行标签。
        /// 图标与标签作为整体垂直居中，标签放不下时逐级缩字号、再省略。
        /// </summary>
        public static void DrawChip(Graphics g, Rectangle r, bool isInput, string kind, string key,
                                    bool hot, Color accent, string label, int iconSize)
        {
            DrawChipFrame(g, r, hot, isInput, accent);

            int isz = Math.Max(12, Math.Min(iconSize, Math.Min(r.Width - 16, r.Height - 26)));
            Font fs = T.Micro;
            string line = label ?? "";
            int maxW = r.Width - 12;
            if (g.MeasureString(line, fs).Width > maxW)
            {
                fs = T.MicroSmall;
                if (g.MeasureString(line, fs).Width > maxW) fs = T.MicroTiny;
                if (g.MeasureString(line, fs).Width > maxW)
                {
                    string t = line;
                    while (t.Length > 1 && g.MeasureString(t, fs).Width > maxW) t = t.Substring(0, t.Length - 1);
                    line = t.Length < label.Length ? t + "…" : t;
                }
            }
            int labelH = 12, gap = 4;
            int contentH = isz + gap + labelH;
            int top = r.Y + Math.Max(3, (r.Height - contentH) / 2);
            int cx = r.X + (r.Width - isz) / 2;

            var img = Icons.Get(key);
            if (img != null)
            {
                var old = g.InterpolationMode;
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                int gi = Math.Max(10, (int)Math.Round(isz * IconScale.AppIcon));
                g.DrawImage(img, new Rectangle(cx + (isz - gi) / 2, top + (isz - gi) / 2, gi, gi));
                g.InterpolationMode = old;
            }
            else DrawGlyph(g, cx, top, isz, kind, isInput ? accent : C.TextSecondary);

            var flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine
                      | TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter;
            TextRenderer.DrawText(g, line, fs, new Rectangle(r.X + 4, top + isz + gap, r.Width - 8, Math.Max(14, labelH + 4)),
                hot ? Color.White : (isInput ? C.TextPrimary : C.TextSecondary), flags);
        }

        /// <summary>
        /// 详情窗用的宽芯片：左侧图标、右侧完整名称（最多两行，按词边界折行）。
        /// 详情页空间大，优先把名字显示全。
        /// </summary>

        /// <summary>两行折行（优先空格/·/- 边界）；放不下则第二行置空并省略。</summary>
        public static string[] WrapTwo(Graphics g, Font fs, string text, int maxW)
        {
            if (string.IsNullOrEmpty(text)) return new[] { "", "" };
            if (g.MeasureString(text, fs).Width <= maxW) return new[] { text, "" };
            int best = -1;
            for (int i = 1; i < text.Length; i++)
            {
                if (text[i - 1] != ' ' && text[i - 1] != '·' && text[i - 1] != '-') continue;
                string a = text.Substring(0, i).Trim(), b = text.Substring(i).Trim();
                if (b.Length == 0) break;
                if (g.MeasureString(a, fs).Width <= maxW && g.MeasureString(b, fs).Width <= maxW) best = i;   // 取尽量靠后的断点，第一行更满
            }
            if (best < 0)
            {
                string t = text;
                while (t.Length > 1 && g.MeasureString(t, fs).Width > maxW) t = t.Substring(0, t.Length - 1);
                return new[] { t + "…", "" };
            }
            return new[] { text.Substring(0, best).Trim(), text.Substring(best).Trim() };
        }

        /// <summary>拖拽时的高亮目标：在芯片外再描一圈强调色。</summary>
        public static void DrawDropTarget(Graphics g, Rectangle r)
        {
            var outer = new Rectangle(r.X - 2, r.Y - 2, r.Width + 4, r.Height + 4);
            using (var pen = new Pen(C.Success, 2f))
            using (var pa = Rd.Path(outer, Rad.Chip + 2)) g.DrawPath(pen, pa);
        }
    }
}
