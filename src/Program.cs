// MIXLINE Scene Manager — Program.cs
// 由单文件 App.cs 机械拆分而成：类型名、命名空间、逻辑均未改动。
// .NET Framework 4.8 / csc(C# 5)
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using System.Web.Script.Serialization;

namespace Asr
{
    // ============================ 入口 ============================
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            uiMode = UiMode.FromArgs(args);

            // 1) 仅调试版提供：查看菜单样式（编译时加 /define:MSM_DEBUG）
#if MSM_DEBUG
            if (uiMode.ShowMenuDemo) { RunMenuDemo(); return; }
#endif

            // 2) 关闭已运行的实例
            if (uiMode.ExitRunning) { SignalRunningInstance("AsrSwitcher.Exit"); return; }

            // 3) CLI 子命令（切换场景 / 自检）——先于单实例检查，
            //    这样应用已运行时仍能用 --switch 切换场景
            if (CommandLine.TryRunSwitch(args)) return;
#if MSM_DEBUG
            if (CommandLine.TryRunDump(args)) return;
#endif
            if (uiMode.SelfTest) { CommandLine.RunSelfTest(); return; }
#if MSM_DEBUG
            if (Array.IndexOf(args, "--settings") >= 0)                    // 调试专用：直接打开设置窗
            {
                var st = new Store(System.IO.Path.GetDirectoryName(Application.ExecutablePath));
                new SettingsForm(Settings.ConfigDir, st.CardSize, st.LogEnabled, st.LogMinLevel).ShowDialog();
                return;
            }
#endif

            // 4) 单实例保护：第二个进程只负责把已有窗口调到前台
            if (!EnsureSingleInstance()) return;

            // 5) 最外层兜底：未处理异常写日志并提示，绝不静默退出
            AppDomain.CurrentDomain.UnhandledException += delegate (object sender, UnhandledExceptionEventArgs e)
            {
                var ex = e.ExceptionObject as Exception;
                Log.Error("未处理的异常（顶层）", ex);
                if (!e.IsTerminating) return;
                try { MessageBox.Show((ex == null ? "未知错误" : ex.Message) + "\n\n详情见 MSM-data\\logs，程序即将退出。", "MIXLINE Scene Manager"); }
                catch (Exception ex2) { Log.Debug("顶层异常提示失败", ex2); }
            };
            try
            {
                RunUi();
            }
            catch (Exception ex)
            {
                Log.Error("启动失败", ex);
                try { MessageBox.Show("启动失败：" + ex.Message + "\n\n详情见 MSM-data\\logs。", "MIXLINE Scene Manager"); }
                catch (Exception ex3) { Log.Debug("启动失败提示失败", ex3); }
            }
        }

        /// <summary>正常启动：创建 Store + 面板，并注册"显示/退出"两个跨进程信号。</summary>
        static void RunUi()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += delegate (object sender, System.Threading.ThreadExceptionEventArgs e)
            {
                Log.Error("未捕获的界面异常", e.Exception);
                MessageBox.Show(e.Exception.Message, "MIXLINE Scene Manager");
            };

            string exeDir = System.IO.Path.GetDirectoryName(Application.ExecutablePath);
            var store = new Store(exeDir);
            Log.Enabled = store.LogEnabled;                 // 配置里的日志开关
            Log.MinLevel = (Log.Level)Math.Max(0, Math.Min(3, store.LogMinLevel));
            Log.Info("启动界面 · 配置目录=" + Settings.ConfigDir + " · 场景数=" + store.Scenes.Count + " · 行高=" + store.CardSize + "px · 日志=" + (store.LogEnabled ? "开" : "关") + " 等级=" + store.LogMinLevel);
            var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "AsrSwitcher.Show");
            var exitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "AsrSwitcher.Exit");
            var panel = new Panel(store, false);

            ThreadPool.RegisterWaitForSingleObject(showEvent, delegate
            {
                Log.Guard("前台化已有窗口", delegate
                {
                    panel.BeginInvoke(new Action(delegate { panel.ShowWindowFront(); }));
                });
            }, null, Timeout.Infinite, false);

            ThreadPool.RegisterWaitForSingleObject(exitEvent, delegate
            {
                Log.Guard("响应退出信号", delegate
                {
                    try { panel.BeginInvoke(new Action(delegate { panel.SavePos(); Application.Exit(); })); }
                    catch { Application.Exit(); }
                });
            }, null, Timeout.Infinite, false);

            panel.ShowWindowFront();
            Application.Run(panel);
        }

#if MSM_DEBUG
        /// <summary>调试专用：人工检查右键菜单外观（--menu-demo）。</summary>
        static void RunMenuDemo()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var list = new List<MiniMenu.Item>();
            list.Add(new MiniMenu.Item("打开 MIXLINE", delegate { }));
            list.Add(new MiniMenu.Item("", null, false) { Sep = true });
            list.Add(new MiniMenu.Item("用本程序替换 MIXLINE 界面",
                "点 MIXLINE 托盘菜单里的「打开 MIXLINE」时，弹出的就是本场景切换器",
                delegate { }));
            list.Add(new MiniMenu.Item("还原 MIXLINE 原版界面",
                "撤销上一项，点「打开 MIXLINE」重新打开原生界面",
                delegate { }));
            list.Add(new MiniMenu.Item("", null, false) { Sep = true });
            list.Add(new MiniMenu.Item("退出", delegate { }, true));
            var menu = new MiniMenu(list);
            var host = new Form();
            host.FormBorderStyle = FormBorderStyle.None; host.ShowInTaskbar = false;
            host.StartPosition = FormStartPosition.Manual; host.Location = new Point(320, 320);
            host.Size = new Size(1, 1); host.BackColor = C.Bg;
            host.Show();
            menu.ShowAt(delegate { return new Point(360, 360); });
            Application.Run();
        }
#endif

        static bool EnsureSingleInstance()
        {
            bool created;
            mutex = new Mutex(true, "Global\\MixlineSceneManager", out created);
            if (created) return true;
            SignalRunningInstance("AsrSwitcher.Show");
            return false;
        }

        static void SignalRunningInstance(string eventName)
        {
            Log.Guard("发送跨进程信号 " + eventName, delegate
            {
                EventWaitHandle.OpenExisting(eventName).Set();
            });
        }

        static Mutex mutex;
        static UiMode uiMode;
    }

    /// <summary>命令行开关解析结果（集中处理，避免 Main 里到处判字符串）。</summary>
    class UiMode
    {
        public bool ExitRunning, Pinned, Silent, SelfTest, ShowMenuDemo;

        public static UiMode FromArgs(string[] args)
        {
            var m = new UiMode();
            if (args == null) return m;
            foreach (var a in args)
            {
                if (a == "--exit") m.ExitRunning = true;
                else if (a == "--pin") m.Pinned = true;
                else if (a == "--silent") m.Silent = true;
                else if (a == "--selftest") m.SelfTest = true;
                else if (a == "--menu-demo") m.ShowMenuDemo = true;
            }
            return m;
        }
    }

    /// <summary>一次性 CLI 子命令：切换场景 / 协议转储 / 自检。返回 true 表示已处理完，不再启动界面。</summary>
    static class CommandLine
    {
        public static bool TryRunSwitch(string[] args)
        {
            string want = ValueAfter(args, "--switch");
            if (want == null) return false;

            string result = "NOT FOUND: " + want;
            try
            {
                var store = new Store(System.IO.Path.GetDirectoryName(Application.ExecutablePath));
                Scene target = null;
                foreach (var sc in store.Scenes) if (sc.name == want) target = sc;
                if (target != null) result = new SceneService(store).Switch(target);
            }
            catch (Exception ex)
            {
                result = "ERR " + ex.Message;
                Log.Error("命令行切换场景失败: " + want, ex);
            }
            Log.Info("命令行切换: " + want + " → " + result);
            WriteResultFile("switchresult.txt", result);
            return true;
        }

#if MSM_DEBUG
        public static bool TryRunDump(string[] args)
        {
            if (HasFlag(args, "--dumpapps")) { DumpApplications(); return true; }
            if (HasFlag(args, "--dumprpc")) { DumpDevices(); return true; }
            return false;
        }
#endif

        /// <summary>自检：把引擎连接、graph、场景匹配情况写入 selftest.txt（不发任何修改请求）。</summary>
        public static void RunSelfTest()
        {
            // 自检可能在 Store 初始化之前运行：先把配置目录确定下来
            Settings.Load(System.IO.Path.GetDirectoryName(Application.ExecutablePath));
            var sb = new StringBuilder();
            try
            {
                var g = Engine.GetGraphFull();
                sb.AppendLine("port=" + Engine.Port() + " tokenOK=" + (Engine.Token() != null));
                sb.AppendLine("ins=" + g.ins.Count + " outs=" + g.outs.Count + " links=" + g.links.Count);
                foreach (var n in g.ins) sb.AppendLine("  IN  " + n.kind + " " + n.key);
                foreach (var n in g.outs) sb.AppendLine("  OUT " + n.kind + " " + n.key);
                foreach (var l in g.links) sb.AppendLine("  LNK " + l.inId.Substring(0, 4) + "->" + l.outId.Substring(0, 4) + " lvl=" + l.level);
                var store = new Store(System.IO.Path.GetDirectoryName(Application.ExecutablePath));
                new SceneService(store).MarkMatch();
                foreach (var sc in store.Scenes) sb.AppendLine("  SCENE " + sc.name + " links=" + sc.links.Count + " match=" + sc.match);
            }
            catch (Exception ex)
            {
                sb.AppendLine("ERR " + ex.GetType().Name + ": " + ex.Message);
                Log.Error("自检失败", ex);
            }
            Log.Info("自检完成");
            WriteResultFile("selftest.txt", sb.ToString());
        }

#if MSM_DEBUG
        static void DumpApplications()
        {
            Log.Guard("转储应用列表", delegate
            {
                var raw = Engine.DumpRpc("/protocol.audio.ApplicationService/GetApplications", new byte[0]);
                WriteResultFile("apps-dump.txt", "len=" + raw.Length + "\n" + HexDump(raw, 4000));
                Dictionary<string, string> exeMap;
                var names = Engine.GetApplicationNames(out exeMap);
                var sb = new StringBuilder();
                foreach (var kv in names)
                {
                    sb.Append(kv.Key).Append(" = ").Append(kv.Value);
                    string exe;
                    if (exeMap.TryGetValue(kv.Key, out exe)) sb.Append("  [").Append(exe).Append("]");
                    sb.Append('\n');
                }
                WriteResultFile("apps-list.txt", "count=" + names.Count + "\n" + sb);
            });
        }

        static void DumpDevices()
        {
            Log.Guard("转储设备列表", delegate
            {
                var raw = Engine.DumpRpc("/protocol.audio.DeviceService/GetDevices", new byte[0]);
                WriteResultFile("rpc-dump.txt", "len=" + raw.Length + "\n" + HexDump(raw, raw.Length));
            });
        }
#endif

        static string HexDump(byte[] data, int limit)
        {
            var sb = new StringBuilder();
            int n = Math.Min(data.Length, limit);
            for (int i = 0; i < n; i++)
            {
                sb.Append(data[i].ToString("x2"));
                if ((i & 15) == 15) sb.Append('\n');
            }
            return sb.ToString();
        }

        static void WriteResultFile(string fileName, string content)
        {
            // CLI 子命令可能早于 Store 初始化：ConfigDir 为空时回落到 exe 目录，避免 Path.Combine(null) 崩溃
            string dir = Settings.ConfigDir;
            if (string.IsNullOrEmpty(dir))
                dir = Settings.DefaultDir(System.IO.Path.GetDirectoryName(Application.ExecutablePath));
            try { if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir); }
            catch (Exception ex) { Log.Warn("创建结果目录失败: " + dir, ex); }
            string path = System.IO.Path.Combine(dir, fileName);
            Log.Guard("写结果文件 " + fileName, delegate
            {
                System.IO.File.WriteAllText(path, content, new UTF8Encoding(false));
            });
        }

        static bool HasFlag(string[] args, string flag)
        {
            if (args == null) return false;
            foreach (var a in args) if (a == flag) return true;
            return false;
        }

        static string ValueAfter(string[] args, string flag)
        {
            if (args == null) return null;
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == flag) return args[i + 1];
            return null;
        }
    }

}
