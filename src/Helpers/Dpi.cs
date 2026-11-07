// MIXLINE Scene Manager — Helpers/Dpi.cs
// 统一的 DPI 缩放：布局用逻辑像素（96dpi 设计值），运行时按当前 DPI 放大。
// 字体用 point 值，GDI/GDI+ 会随系统 DPI 自动放大，所以只缩"尺寸/间距"。
using System;
using System.Drawing;
using System.Windows.Forms;

namespace Asr
{
    static class Dpi
    {
        static int percent;      // 100 = 96dpi

        /// <summary>当前缩放百分比（100/125/150/175/200…）。</summary>
        public static int Percent
        {
            get
            {
                if (percent > 0) return percent;
                int env;
                string s = Environment.GetEnvironmentVariable("MSM_DPI");
                if (!string.IsNullOrEmpty(s) && int.TryParse(s, out env) && env >= 72 && env <= 400) percent = env;
                else
                {
                    try
                    {
                        using (var g = Graphics.FromHwnd(IntPtr.Zero))
                            percent = (int)Math.Round(g.DpiX / 96.0 * 100.0);
                    }
                    catch (Exception ex) { Log.Debug("读取系统 DPI 失败，按 100% 处理", ex); }
                    if (percent <= 0) percent = 100;
                }
                return percent;
            }
        }

        public static void Set(int dpiPercent)
        {
            if (dpiPercent >= 72 && dpiPercent <= 400) percent = dpiPercent;
        }

        /// <summary>逻辑像素 → 物理像素。</summary>
        public static int S(int v) { return (int)Math.Round(v * Percent / 100.0); }

        public static float SF(float v) { return v * Percent / 100f; }

        /// <summary>窗口按 DPI 换算后的推荐客户区尺寸。</summary>
        public static Size ScaledSize(int w, int h) { return new Size(S(w), S(h)); }

        /// <summary>让窗体采用当前 DPI 的尺寸（仅启动时调用一次）。</summary>
        public static void ApplyTo(Form f, int logicalW, int logicalH, int minW, int minH)
        {
            f.ClientSize = ScaledSize(logicalW, logicalH);
            f.MinimumSize = ScaledSize(minW, minH);
        }
    }
}
