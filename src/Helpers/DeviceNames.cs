// MIXLINE Scene Manager — Helpers/DeviceNames.cs
// 设备/应用的显示名与短名规则集中在此。
// 之前 Panel 与 SceneDetail 各有一份 NameOf / ShortName，规则迟早会漂移。
using System;
using System.Collections.Generic;

namespace Asr
{
    static class DeviceNames
    {
        public static string KindLabel(string kind)
        {
            if (kind == "app") return "应用";
            if (kind == "mic") return "麦克风";
            if (kind == "speaker") return "扬声器";
            if (kind == "stream") return "Stream";
            return kind;
        }

        /// <summary>悬停/菜单用的完整名称（引擎给的原始描述）。</summary>
        public static string Display(Dictionary<string, string> names, string kind, string key)
        {
            string v;
            if (names != null && names.TryGetValue(key, out v) && !string.IsNullOrEmpty(v)) return v;
            return KindLabel(kind);
        }


        public static string Short(Dictionary<string, string> names, string kind, string key)
        {
            if (kind == "stream")
            {
                string sv;
                if (names != null && names.TryGetValue(key, out sv) && !string.IsNullOrEmpty(sv) &&
                    sv.ToLowerInvariant().Contains("record")) return "MIXLINE Record";
                return "MIXLINE Stream";
            }
            string v;
            if (names != null && names.TryGetValue(key, out v) && !string.IsNullOrEmpty(v))
            {
                if (kind != "app")
                {
                    int dot = v.LastIndexOf('\u00B7');   // "厂商型号 · 扬声器"
                    if (dot >= 0)
                    {
                        string tail = v.Substring(dot + 1).Trim();
                        if (tail.Length > 0) return tail;
                    }
                }
                return v.Length <= 10 ? v : v.Substring(0, 9) + "…";
            }
            return KindLabel(kind);
        }
        /// <summary>
        /// 应用名：引擎给的其实是"窗口标题"（例如网易云会显示成歌名），不稳定。
        /// 因此应用优先用 exe 文件名（cloudmusic.exe → Cloudmusic），设备仍用引擎描述。
        /// </summary>
        public static string Short(Dictionary<string, string> names, Dictionary<string, string> exePaths, string kind, string key)
        {
            if (kind == "app" && exePaths != null)
            {
                string exe;
                if (exePaths.TryGetValue(key, out exe) && !string.IsNullOrEmpty(exe))
                {
                    // 友好名：快捷方式名 / exe 版本信息；都没有才用文件名
                    string friendly = AppNames.Friendly(exe);
                    string n = friendly;
                    if (string.IsNullOrEmpty(n))
                    {
                        try { n = System.IO.Path.GetFileNameWithoutExtension(exe); }
                        catch (Exception ex) { Log.Debug("取应用文件名失败", ex); }
                    }
                    if (!string.IsNullOrEmpty(n))
                        return n.Length <= 14 ? n : n.Substring(0, 13) + "…";
                }
            }
            return Short(names, kind, key);
        }
    }
}
