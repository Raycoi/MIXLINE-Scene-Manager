// MIXLINE Scene Manager — Helpers/AppNames.cs
// 应用友好名解析（不维护映射表、不写入配置）：
//   1) 快捷方式名（桌面 / 公共桌面 / 开始菜单 / 公共开始菜单）——中文名通常在这里
//   2) exe 版本信息 FileDescription / ProductName（EarTrumpet 等工具用的就是这一招）
//   3) exe 文件名
//   4) 引擎给的窗口标题
// 结果按 exe 路径缓存在内存里，不落盘，配置不会膨胀。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Asr
{
    static class AppNames
    {
        static readonly Dictionary<string, string> cache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static Dictionary<string, string> shortcutMap;          // 目标 exe 路径 → 快捷方式名

        /// <summary>解析应用的友好名；解析不到返回 null（调用方回退到 exe 名 / 窗口标题）。</summary>
        public static string Friendly(string exePath)
        {
            if (string.IsNullOrEmpty(exePath)) return null;
            lock (cache)
            {
                string v;
                if (cache.TryGetValue(exePath, out v)) return v;
            }
            string name = null;
            try
            {
                string full;
                try { full = Path.GetFullPath(exePath); } catch { full = exePath; }

                // 1) 快捷方式名
                var map = Shortcuts();
                if (map.Count > 0)
                {
                    string hit;
                    if (map.TryGetValue(full, out hit) && !string.IsNullOrEmpty(hit)) name = hit;
                }

                // 2) exe 版本信息（本地化描述通常就是中文名）
                if (string.IsNullOrEmpty(name))
                {
                    var vi = FileVersionInfo.GetVersionInfo(full);
                    string desc = (vi.FileDescription ?? "").Trim();
                    string prod = (vi.ProductName ?? "").Trim();
                    string file = Path.GetFileNameWithoutExtension(full);
                    if (IsGoodName(desc, file)) name = desc;
                    else if (IsGoodName(prod, file)) name = prod;
                }
            }
            catch (Exception ex) { Log.Debug("解析应用名失败: " + exePath, ex); }

            lock (cache) { cache[exePath] = name; }
            return name;
        }

        /// <summary>文件描述像"cloudmusic.exe"这种就丢弃，只保留真正可读的名字。</summary>
        static bool IsGoodName(string candidate, string exeBaseName)
        {
            if (string.IsNullOrEmpty(candidate)) return false;
            if (candidate.Equals(exeBaseName, StringComparison.OrdinalIgnoreCase)) return false;
            if (candidate.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return false;
            return candidate.Length >= 2;
        }

        /// <summary>扫描桌面与开始菜单里的 .lnk，建立"目标 exe → 快捷方式名"的索引（只做一次）。</summary>
        static Dictionary<string, string> Shortcuts()
        {
            if (shortcutMap != null) return shortcutMap;
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var folders = new List<string>();
                TryAdd(folders, Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
                TryAdd(folders, Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory));
                TryAdd(folders, Environment.GetFolderPath(Environment.SpecialFolder.Programs));
                TryAdd(folders, Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms));

                object shell = null;
                try { shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")); }
                catch (Exception ex) { Log.Debug("WScript.Shell 不可用，跳过快捷方式解析", ex); }

                foreach (var dir in folders)
                {
                    if (!Directory.Exists(dir)) continue;
                    IEnumerable<string> files;
                    try { files = Directory.GetFiles(dir, "*.lnk", SearchOption.AllDirectories); }
                    catch (Exception ex) { Log.Debug("枚举快捷方式失败: " + dir, ex); continue; }
                    foreach (var f in files)
                    {
                        string target = null;
                        if (shell != null)
                        {
                            try
                            {
                                object lnk = shell.GetType().InvokeMember("CreateShortcut",
                                    System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { f });
                                if (lnk != null)
                                    target = "" + lnk.GetType().InvokeMember("TargetPath",
                                        System.Reflection.BindingFlags.GetProperty, null, lnk, null);
                            }
                            catch (Exception ex) { Log.Debug("读取快捷方式失败: " + f, ex); }
                        }
                        if (string.IsNullOrEmpty(target)) continue;
                        try { target = Path.GetFullPath(target); } catch { }
                        string friendly = Path.GetFileNameWithoutExtension(f);
                        if (string.IsNullOrEmpty(friendly)) continue;
                        if (!map.ContainsKey(target)) map[target] = friendly;   // 同名目标取第一个快捷方式
                    }
                }
            }
            catch (Exception ex) { Log.Warn("建立快捷方式索引失败", ex); }
            shortcutMap = map;
            return map;
        }

        static void TryAdd(List<string> list, string dir)
        {
            if (!string.IsNullOrEmpty(dir) && !list.Contains(dir)) list.Add(dir);
        }

        /// <summary>清空缓存（刷新图标/名称时调用）。</summary>
        public static void Reset()
        {
            lock (cache) { cache.Clear(); }
            shortcutMap = null;
        }
    }
}
