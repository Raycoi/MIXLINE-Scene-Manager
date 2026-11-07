// MIXLINE Scene Manager — Helpers/Theme.cs
// 设计系统：颜色 token、字体层级、间距与圆角规范。所有界面绘制只能从这里取值。
using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Asr
{
    /// <summary>颜色 token（Windows 11 Fluent 深色风格，降低饱和度、提高对比）。</summary>
    static class C
    {
        // 表面层级
        public static readonly Color Base = Color.FromArgb(0x10, 0x12, 0x16);
        public static readonly Color Card = Color.FromArgb(0x17, 0x1A, 0x20);
        public static readonly Color CardHover = Color.FromArgb(0x1C, 0x20, 0x28);
        public static readonly Color Elevated = Color.FromArgb(0x20, 0x24, 0x2D);
        public static readonly Color Sunken = Color.FromArgb(0x12, 0x15, 0x1A);

        // 状态底色
        public static readonly Color SelectedBg = Color.FromArgb(0x1B, 0x24, 0x32);
        public static readonly Color ActiveBg = Color.FromArgb(0x16, 0x24, 0x3A);

        // 描边
        public static readonly Color BorderSubtle = Color.FromArgb(0x24, 0x28, 0x32);
        public static readonly Color BorderStrong = Color.FromArgb(0x33, 0x3A, 0x47);

        // 文本
        public static readonly Color TextPrimary = Color.FromArgb(0xE9, 0xED, 0xF6);
        public static readonly Color TextSecondary = Color.FromArgb(0xA3, 0xAC, 0xBF);
        public static readonly Color TextTertiary = Color.FromArgb(0x6F, 0x78, 0x8B);

        // 语义色
        public static readonly Color Accent = Color.FromArgb(0x4C, 0x8D, 0xFF);
        public static readonly Color Success = Color.FromArgb(0x39, 0xC9, 0x8A);
        public static readonly Color Warning = Color.FromArgb(0xE8, 0xA3, 0x47);
        public static readonly Color Danger = Color.FromArgb(0xF0, 0x61, 0x6D);

        // ---- 兼容旧调用点的别名（语义不变，值升级） ----
        public static readonly Color Bg = Base;
        public static readonly Color Bg2 = Card;
        public static readonly Color BgOn = ActiveBg;
        public static readonly Color Line = BorderSubtle;
        public static readonly Color Text = TextPrimary;
        public static readonly Color Mut = TextSecondary;
        public static readonly Color Acc = Accent;
        public static readonly Color Ok = Success;
        public static readonly Color Hover = CardHover;
        public static readonly Color Err = Danger;

        /// <summary>白色半透明叠加（悬停/按下），避免为每个状态另立色值。</summary>
        public static Color Overlay(int alpha) { return Color.FromArgb(alpha, 255, 255, 255); }

        /// <summary>把颜色降到指定不透明度。</summary>
        public static Color Fade(Color c, int alpha) { return Color.FromArgb(alpha, c.R, c.G, c.B); }
    }

    /// <summary>字体层级：全项目只有 5 档字号，预创建复用（避免每帧 new Font）。</summary>
    static class T
    {
        public static readonly Font Title = new Font("Microsoft YaHei UI", 15f, FontStyle.Bold);
        public static readonly Font CardTitle = new Font("Microsoft YaHei UI", 12f, FontStyle.Bold);
        public static readonly Font Body = new Font("Microsoft YaHei UI", 11f, FontStyle.Regular);
        public static readonly Font Caption = new Font("Microsoft YaHei UI", 10f, FontStyle.Regular);
        public static readonly Font Micro = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular);
        public static readonly Font MicroBold = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold);
        public static readonly Font Hint = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Regular);
        public static readonly Font MicroSmall = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Regular);
        public static readonly Font MicroTiny = new Font("Microsoft YaHei UI", 8f, FontStyle.Regular);
        /// <summary>保存卡片中央的大号「＋」。</summary>
        public static readonly Font PlusBig = new Font("Segoe UI Light", 46f, FontStyle.Regular);
        public static readonly Font PlusBigHover = new Font("Segoe UI Light", 46f, FontStyle.Regular);
    }

    /// <summary>间距体系（4pt 基数）。</summary>
    static class Sp
    {
        public const int XS = 4, S = 8, M = 12, L = 16, XL = 24, XXL = 32;
    }

    /// <summary>圆角规范。</summary>
    /// <summary>应用图标相对图标框的内缩比例（与矢量图标视觉重量对齐）。</summary>
    static class IconScale { public const double AppIcon = 0.88; }

    static class Rad
    {
        public const int Chip = 8, Button = 6, Card = 12, Popup = 10;
    }

    static class Rd
    {
        public static GraphicsPath Path(Rectangle r, int rad)
        {
            var p = new GraphicsPath(); int d = rad * 2;
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            if (d <= 0) { p.AddRectangle(r); p.CloseAllFigures(); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseAllFigures(); return p;
        }
    }

    /// <summary>连线配色：与强调色同族、降饱和，避免比内容更抢眼。</summary>
    static class CurveColors
    {
        static readonly Color[] Palette =
        {
            Color.FromArgb(0x5A, 0x8D, 0xF0),
            Color.FromArgb(0x3F, 0xB9, 0x8A),
            Color.FromArgb(0xD9, 0x9A, 0x4E),
            Color.FromArgb(0xA9, 0x7F, 0xE0),
            Color.FromArgb(0xDB, 0x6E, 0x8C),
            Color.FromArgb(0x4F, 0xAF, 0xBE)
        };

        public static Color For(int index)
        {
            int i = index % Palette.Length;
            if (i < 0) i += Palette.Length;
            return Palette[i];
        }
    }
}
