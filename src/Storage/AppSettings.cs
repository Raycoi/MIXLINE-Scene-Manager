// MIXLINE Scene Manager — Storage/AppSettings.cs
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
    // ============================ 设置 ============================
    static class Settings
    {
        public static string ConfigDir;
        public static string IniPath;

        static bool CanWrite(string d)
        {
            try
            {
                System.IO.Directory.CreateDirectory(d);
                string probe = System.IO.Path.Combine(d, "_w.tmp");
                System.IO.File.WriteAllText(probe, "x");
                System.IO.File.Delete(probe);
                return true;
            }
            catch { return false; }
        }

        public const string FileName = "MSM-config.json";
        public const string IniName = "MSM-settings.ini";

        public static string DefaultDir(string exeDir) { return System.IO.Path.Combine(exeDir, "MSM-data"); }
        public static string SubDir(string exeDir) { return System.IO.Path.Combine(exeDir, "MSM-data"); }

        public static string AppDataDir()
        {
            try
            {
                string a = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return System.IO.Path.Combine(a, "MIXLINE Scene Manager", "MSM-data");
            }
            catch { return null; }
        }

        public static void Load(string exeDir)
        {
            IniPath = System.IO.Path.Combine(exeDir, IniName);
            string dir = null;
            try
            {
                if (System.IO.File.Exists(IniPath))
                {
                    foreach (var line in System.IO.File.ReadAllLines(IniPath))
                    {
                        int i = line.IndexOf('=');
                        if (i <= 0) continue;
                        if (line.Substring(0, i).Trim() == "configDir") dir = line.Substring(i + 1).Trim();
                    }
                }
            }
            catch (Exception exIgnored1) { Log.Warn("AppSettings.cs: 忽略可恢复异常", exIgnored1); }

            // 主 ini 可能位于旧安装目录（只读）；此时优先使用 %APPDATA% 里的兜底 ini（迁移后总是更新它）
            string appIni = AppIniPath();
            try
            {
                if (!string.IsNullOrEmpty(appIni) && System.IO.File.Exists(appIni))
                {
                    bool appNewer = true;
                    try { appNewer = !System.IO.File.Exists(IniPath) || System.IO.File.GetLastWriteTimeUtc(appIni) >= System.IO.File.GetLastWriteTimeUtc(IniPath); }
                    catch (Exception exIgnored) { Log.Warn("AppSettings.cs: 比较 ini 时间失败", exIgnored); }
                    if (appNewer)
                    {
                        foreach (var line in System.IO.File.ReadAllLines(appIni))
                        {
                            int i = line.IndexOf('=');
                            if (i <= 0) continue;
                            if (line.Substring(0, i).Trim() == "configDir") { string appDir = line.Substring(i + 1).Trim(); if (!string.IsNullOrEmpty(appDir) && System.IO.Directory.Exists(appDir)) dir = appDir; }
                        }
                    }
                }
            }
            catch (Exception exIgnored) { Log.Warn("AppSettings.cs: 读取兜底 ini 失败", exIgnored); }

            if (!string.IsNullOrEmpty(dir) && CanWrite(dir)) { ConfigDir = dir; return; }

            // 默认：exe 同目录（与 MIXLINE 同级也安全，文件名带 MSM 前缀）
            string d1 = DefaultDir(exeDir);
            if (CanWrite(d1)) { ConfigDir = d1; if (!string.IsNullOrEmpty(dir)) Save(); return; }

            // 其次：exe 同级 MSM-config 文件夹
            string d2 = SubDir(exeDir);
            if (CanWrite(d2)) { ConfigDir = d2; return; }

            // 最后：%APPDATA%MIXLINE Scene Manager
            string d3 = AppDataDir();
            if (!string.IsNullOrEmpty(d3) && CanWrite(d3)) { ConfigDir = d3; return; }

            ConfigDir = d1;
        }

        static string AppIniPath()
        {
            try
            {
                string a = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                if (string.IsNullOrEmpty(a)) return null;
                string d = System.IO.Path.Combine(a, "MIXLINE Scene Manager");
                System.IO.Directory.CreateDirectory(d);
                return System.IO.Path.Combine(d, IniName);
            }
            catch { return null; }
        }

        public static void Save()
        {
            string body = "configDir=" + ConfigDir + "\r\n";
            bool primaryOk = false;
            try
            {
                if (IniPath != null) { System.IO.File.WriteAllText(IniPath, body, new UTF8Encoding(false)); primaryOk = true; }
            }
            catch (Exception exIgnored2) { Log.Warn("AppSettings.cs: 主 ini 不可写", exIgnored2); }
            // 只有主 ini 写不进去（旧安装/只读）时才写 AppData 兜底；
            // 便携版和测试使用自己的 ini，不去污染用户目录。
            if (primaryOk) return;
            try
            {
                string appIni = AppIniPath();
                if (!string.IsNullOrEmpty(appIni)) System.IO.File.WriteAllText(appIni, body, new UTF8Encoding(false));
            }
            catch (Exception exIgnored3) { Log.Warn("AppSettings.cs: 兜底 ini 不可写", exIgnored3); }
        }
    }

}
