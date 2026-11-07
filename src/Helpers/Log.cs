// MIXLINE Scene Manager — Helpers/Log.cs
// 统一日志：所有"原本被静默吞掉"的异常都从这里落盘，便于事后定位。
// 设计为永不抛异常（日志失败只写内存缓冲），避免把日志本身变成新的崩溃源。
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace Asr
{
    static class Log
    {
        public enum Level { Debug = 0, Info = 1, Warn = 2, Error = 3 }

        // 只保留最近若干条在内存里，供 UI「错误信息」按钮即时展示
        const int MemoryLimit = 200;
        static readonly object gate = new object();
        static readonly List<string> recent = new List<string>();
        static string dir;
        static int lastDay = -1;

        public static Level MinLevel = Level.Info;

        /// <summary>日志开关：false 时只写 Error（崩溃排查用），Info/Warn/Debug 一律丢弃。</summary>
        public static bool Enabled = false;   // 默认关闭：不写任何日志

        public static void Init(string configDir)
        {
            try { dir = Path.Combine(configDir, "logs"); Directory.CreateDirectory(dir); }
            catch { dir = null; }
        }

        public static bool HasError
        {
            get { lock (gate) { foreach (var l in recent) if (l.StartsWith("[E]")) return true; } return false; }
        }

        public static string Recent(int max)
        {
            lock (gate)
            {
                var sb = new StringBuilder();
                int from = Math.Max(0, recent.Count - Math.Max(1, max));
                for (int i = from; i < recent.Count; i++) sb.Append(recent[i]).Append('\n');
                return sb.ToString().TrimEnd('\n');
            }
        }

        public static void Debug(string message) { Write(Level.Debug, message, null); }
        public static void Info(string message) { Write(Level.Info, message, null); }
        public static void Warn(string message) { Write(Level.Warn, message, null); }
        public static void Warn(string context, Exception ex) { Write(Level.Warn, context, ex); }
        public static void Debug(string context, Exception ex) { Write(Level.Debug, context, ex); }
        public static void Error(string context, Exception ex) { Write(Level.Error, context, ex); }

        /// <summary>包住"失败可以接受但需要留痕"的代码块，替代空 catch。</summary>
        public static void Guard(string context, Action action)
        {
            try { action(); }
            catch (Exception ex) { Write(Level.Warn, context, ex); }
        }

        /// <summary>带返回值的 Guard：失败时返回 fallback。</summary>
        public static T Guard<T>(string context, Func<T> func, T fallback)
        {
            try { return func(); }
            catch (Exception ex) { Write(Level.Warn, context, ex); return fallback; }
        }

        static void Write(Level level, string message, Exception ex)
        {
            if (!Enabled) return;                            // 关了日志就完全不写（含错误）
            if (level < MinLevel && ex == null) return;
            string tag = level == Level.Error ? "[E]" : level == Level.Warn ? "[W]" : level == Level.Info ? "[I]" : "[D]";
            string line = DateTime.Now.ToString("HH:mm:ss.fff") + " " + tag + " " + message;
            if (ex != null) line += "  << " + ex.GetType().Name + ": " + ex.Message;
            try
            {
                lock (gate)
                {
                    recent.Add(line);
                    if (recent.Count > MemoryLimit) recent.RemoveRange(0, recent.Count - MemoryLimit);
                    if (dir == null) return;
                    int day = DateTime.Now.DayOfYear;
                    string path = Path.Combine(dir, DateTime.Now.ToString("yyyyMMdd") + ".log");
                    File.AppendAllText(path, line + Environment.NewLine, new UTF8Encoding(false));
                    lastDay = day;
                }
            }
            catch { /* 日志写入失败不影响主流程 */ }
        }
    }
}
