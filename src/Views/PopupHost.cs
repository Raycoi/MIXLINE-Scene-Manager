// MIXLINE Scene Manager — Views/PopupHost.cs
// 弹窗宿主：让"点击弹窗以外的任何地方（包括本应用自己的窗口）"都取消交互。
// 用线程级消息过滤器实现，不依赖窗口失去激活（ShowDialog 会把父窗口禁用，收不到点击）。
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Asr
{
    static class PopupHost
    {
        [DllImport("user32.dll", EntryPoint = "SetForegroundWindow")]
        static extern bool BringForeground(IntPtr hWnd);

        static int openCount;
        static int suspendClickAway;
        /// <summary>临时放开点击穿透（弹窗内再弹系统对话框，如选择文件夹时使用）。</summary>
        public static void SuspendClickAway() { suspendClickAway++; }
        public static void ResumeClickAway() { if (suspendClickAway > 0) suspendClickAway--; }
        static DateTime closedAt = DateTime.MinValue;
        /// <summary>最近一次弹窗关闭的时间（主界面据此忽略关闭瞬间的残留点击）。</summary>
        public static bool JustClosed { get { return (DateTime.UtcNow - closedAt).TotalMilliseconds < 300; } }
        /// <summary>是否有弹窗正在显示（主界面据此屏蔽点击，避免"点穿"）。</summary>
        public static bool IsOpen { get { return openCount > 0; } }
        /// <summary>以"轻模态"方式显示弹窗，返回 DialogResult。</summary>
        public static DialogResult Run(Form dlg)
        {
            var filter = new ClickAway(dlg);
            Application.AddMessageFilter(filter);
            openCount++;
            try
            {
                CenterOnOwner(dlg);
                dlg.Show();
                dlg.BringToFront();
                dlg.Activate();
                BringForeground(dlg.Handle);
                var start = DateTime.UtcNow;
                while (!dlg.IsDisposed && dlg.Visible)
                {
                    Application.DoEvents();
                    Thread.Sleep(15);
                    if ((DateTime.UtcNow - start).TotalSeconds > 120)   // 兜底，避免死循环
                    {
                        dlg.DialogResult = DialogResult.Cancel;
                        dlg.Close();
                    }
                }
                return dlg.DialogResult;
            }
            finally
            {
                openCount--;
                closedAt = DateTime.UtcNow;
                Application.RemoveMessageFilter(filter);
                if (!dlg.IsDisposed) dlg.Dispose();
            }
        }

        /// <summary>弹窗居中到主窗口（没有主窗口时居中到当前屏幕），并保证完整落在工作区内。</summary>
        static void CenterOnOwner(Form dlg)
        {
            Form owner = dlg.Owner;
            if (owner == null || owner.IsDisposed || !owner.Visible) owner = Form.ActiveForm;
            if (owner == null || owner == dlg || owner.IsDisposed || !owner.Visible)
            {
                try
                {
                    foreach (Form f in Application.OpenForms)
                    {
                        if (f != dlg && f.Visible && !f.IsDisposed) { owner = f; break; }
                    }
                }
                catch (Exception exIgnored) { Log.Warn("PopupHost.cs: 查找主窗口失败", exIgnored); }
            }

            Rectangle area;
            int x, y;
            if (owner != null && owner != dlg)
            {
                var r = owner.RectangleToScreen(owner.ClientRectangle);
                area = Screen.FromRectangle(r).WorkingArea;
                x = r.Left + (r.Width - dlg.Width) / 2;
                y = r.Top + (r.Height - dlg.Height) / 2;
            }
            else
            {
                area = Screen.FromPoint(Cursor.Position).WorkingArea;
                x = area.Left + (area.Width - dlg.Width) / 2;
                y = area.Top + (area.Height - dlg.Height) / 2;
            }
            x = Math.Max(area.Left + 8, Math.Min(x, area.Right - dlg.Width - 8));
            y = Math.Max(area.Top + 8, Math.Min(y, area.Bottom - dlg.Height - 8));
            dlg.StartPosition = FormStartPosition.Manual;
            dlg.Location = new Point(x, y);
        }

        class ClickAway : IMessageFilter
        {
            readonly Form f;
            readonly DateTime shownAt = DateTime.UtcNow;
            public ClickAway(Form form) { f = form; }

            const int WM_LBUTTONDOWN = 0x0201, WM_RBUTTONDOWN = 0x0204, WM_MBUTTONDOWN = 0x0207, WM_NCLBUTTONDOWN = 0x00A1;

            public bool PreFilterMessage(ref Message m)
            {
                if (m.Msg != WM_LBUTTONDOWN && m.Msg != WM_RBUTTONDOWN && m.Msg != WM_MBUTTONDOWN && m.Msg != WM_NCLBUTTONDOWN)
                    return false;
                if (suspendClickAway > 0) return false;
                if (f.IsDisposed || !f.Visible) return false;

                // 1) 点在弹窗内部（含子控件）：永远放行，保证按钮第一次点击就生效
                if (m.HWnd == f.Handle || IsChild(f.Handle, m.HWnd)) return false;

                // 2) 点在弹窗外部：刚弹出的极短时间内只吞掉、不关闭
                //    （避免"触发弹窗的那次点击"的尾巴把它立刻关掉）
                if ((DateTime.UtcNow - shownAt).TotalMilliseconds < 180) return true;

                // 3) 正常的外部点击：关闭并取消
                f.DialogResult = DialogResult.Cancel;
                f.Close();
                return true;
            }

            [DllImport("user32.dll")]
            static extern bool IsChild(IntPtr parent, IntPtr child);

            [DllImport("user32.dll")]
            static extern bool SetForegroundWindow(IntPtr hWnd);
        }
    }
}
