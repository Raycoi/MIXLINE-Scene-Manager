// MIXLINE Scene Manager — Helpers/SystemIO.cs
// 场景的"系统默认输入/输出"规则：显式选择优先；否则当场景里只有唯一候选时自动使用；多个候选=不指定。
// 纯逻辑（可单测），真正的 Windows 调用在 SystemAudio。
using System;
using System.Collections.Generic;

namespace Asr
{
    class SysDev
    {
        public string Kind;
        public string Key;
        public string Node { get { return Kind + ":" + Key; } }
        public bool IsInput;
    }

    static class SystemIO
    {
        public const string None = "none";   // 显式"不自动使用"哨兵
        public const string SysInColor = "#FF7AA8";
        public const string SysOutColor = "#5A9CFF";

        public static System.Drawing.Color InColor { get { return System.Drawing.Color.FromArgb(0xFF, 0x7A, 0xA8); } }
        public static System.Drawing.Color OutColor { get { return System.Drawing.Color.FromArgb(0x5A, 0x9C, 0xFF); } }

        /// <summary>Windows 匹配用的完整描述（store.Names 里形如 "厂商 · 设备类型"）。</summary>
        public static string FullLabel(Store store, string kind, string key)
        {
            string v = null;
            if (store != null && store.Names != null) store.Names.TryGetValue(key, out v);
            if (string.IsNullOrEmpty(v)) v = DeviceNames.Display(store != null ? store.Names : null, kind, key);
            return v;
        }

        /// <summary>界面显示用的短名（Stream 会区分 Stream / Record，避免两个都显示 "Stream"）。</summary>
        public static string ShortLabel(Store store, SysDev d)
        {
            if (d == null) return "无";
            // 用户自定义的设备名优先：改名后主界面摘要也要跟着变
            string alias = store != null ? store.AliasOf(d.Key) : null;
            if (!string.IsNullOrEmpty(alias)) return alias;
            if (d.Kind == "stream")
            {
                string full = FullLabel(store, d.Kind, d.Key);
                return (full != null && full.ToLowerInvariant().Contains("record")) ? "MIXLINE Record" : "MIXLINE Stream";
            }
            return DeviceNames.Short(store != null ? store.Names : null, d.Kind, d.Key);
        }

        static void AddUnique(List<SysDev> list, string kind, string key, bool input)
        {
            string node = kind + ":" + key;
            foreach (var x in list) if (x.Node == node) return;
            list.Add(new SysDev { Kind = kind, Key = key, IsInput = input });
        }

        /// <summary>场景里连出的扬声器（输出候选，按节点去重）。</summary>
        public static List<SysDev> Outputs(Scene sc)
        {
            var list = new List<SysDev>();
            if (sc == null) return list;
            foreach (var l in sc.links) if (l.dk == "speaker") AddUnique(list, l.dk, l.dv, false);
            return list;
        }

        /// <summary>场景里连出的虚拟输入（stream，按节点去重）。</summary>
        public static List<SysDev> Streams(Scene sc)
        {
            var list = new List<SysDev>();
            if (sc == null) return list;
            foreach (var l in sc.links) if (l.dk == "stream") AddUnique(list, l.dk, l.dv, true);
            return list;
        }

        public static bool SceneHas(Scene sc, string node)
        {
            if (sc == null || string.IsNullOrEmpty(node)) return false;
            foreach (var l in sc.links)
            {
                if (l.dk + ":" + l.dv == node) return true;
            }
            return false;
        }

        static SysDev FindNode(Scene sc, string node)
        {
            if (sc == null || string.IsNullOrEmpty(node)) return null;
            foreach (var l in sc.links)
            {
                // 输入设备（麦克风）在连线里是"源"，所以要同时看两端
                if (l.dk + ":" + l.dv == node) return new SysDev { Kind = l.dk, Key = l.dv, IsInput = l.dk != "speaker" };
                if (l.sk + ":" + l.sv == node) return new SysDev { Kind = l.sk, Key = l.sv, IsInput = l.sk != "speaker" };
            }
            return null;
        }

        /// <summary>有效输出：显式选择（且仍在场景里）→ 唯一扬声器 → 无。</summary>
        public static SysDev EffectiveOut(Scene sc)
        {
            if (sc != null && sc.sysOutNode == None) return null;
            var pick = FindNode(sc, sc != null ? sc.sysOutNode : null);
            if (pick != null && pick.Kind == "speaker") return pick;
            var outs = Outputs(sc);
            return outs.Count == 1 ? outs[0] : null;
        }

        /// <summary>有效输入：显式选择（stream/mic 且仍在场景里）→ 唯一 stream → 无。</summary>
        public static SysDev EffectiveIn(Scene sc)
        {
            if (sc != null && sc.sysInNode == None) return null;
            var pick = FindNode(sc, sc != null ? sc.sysInNode : null);
            if (pick != null && (pick.Kind == "stream" || pick.Kind == "mic")) return pick;
            var ins = Streams(sc);
            return ins.Count == 1 ? ins[0] : null;
        }

        /// <summary>主界面摘要：系统输出名（无则 "无"）。</summary>
        public static string SummaryOut(Scene sc, Store store) { return ShortLabel(store, EffectiveOut(sc)); }

        /// <summary>主界面摘要：系统输入名（无则 "无"）。</summary>
        public static string SummaryIn(Scene sc, Store store) { return ShortLabel(store, EffectiveIn(sc)); }

        /// <summary>右键设置：解析 Windows 端点并立即生效，成功后写入场景草稿。</summary>
        public static bool SetSelected(Scene sc, Store store, string kind, string key, bool input, out string matched, out string err)
        {
            matched = null; err = null;
            if (sc == null) { err = "场景为空"; return false; }
            string label = FullLabel(store, kind, key);
            var ep = SystemAudio.Resolve(kind, label, out err);
            if (ep == null) return false;
            if (!SystemAudio.SetDefault(ep.Id, out err)) return false;
            matched = ep.Name;
            if (input) { sc.sysInNode = kind + ":" + key; sc.sysInId = ep.Id; sc.sysInName = ep.Name; }
            else { sc.sysOutNode = kind + ":" + key; sc.sysOutId = ep.Id; sc.sysOutName = ep.Name; }
            return true;
        }

        public static void ClearSelected(Scene sc, bool input)
        {
            if (sc == null) return;
            if (input) { sc.sysInNode = None; sc.sysInId = ""; sc.sysInName = ""; }
            else { sc.sysOutNode = None; sc.sysOutId = ""; sc.sysOutName = ""; }
        }

        /// <summary>切场景时执行：先输出、再输入；返回失败提示（可空）。</summary>
        public static string ApplyOnSwitch(Scene sc, Store store)
        {
            if (sc == null) return null;
            string warn = null;
            var outDev = EffectiveOut(sc);
            if (outDev != null)
            {
                string matched, err;
                string label = FullLabel(store, outDev.Kind, outDev.Key);
                if (!SystemAudio.ApplyOutput(label, out matched, out err))
                    warn = "系统输出未切换：" + (err ?? "未知原因");
            }
            var inDev = EffectiveIn(sc);
            if (inDev != null)
            {
                string matched, err;
                string label = FullLabel(store, inDev.Kind, inDev.Key);
                if (!SystemAudio.ApplyInput(inDev.Kind, label, out matched, out err))
                    warn = (warn == null ? "" : warn + "；") + "系统输入未切换：" + (err ?? "未知原因");
            }
            return warn;
        }
    }
}
