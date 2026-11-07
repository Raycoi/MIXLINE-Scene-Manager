// MIXLINE Scene Manager — Models/GraphLookup.cs
// 音频图（Snap）上的统一查找：设备/输出/连线的"按业务键找节点"逻辑集中在这里。
// 之前 Panel、SceneDetail、Store 各自写一遍 foreach 扫描，命名与判空方式不一致。
using System;
using System.Collections.Generic;

namespace Asr
{
    static class GraphLookup
    {
        /// <summary>业务键：kind + ":" + key（如 "mic:389dc444-..."）。</summary>
        public static string Key(string kind, string key) { return kind + ":" + key; }

        public static Node FindInput(Snap g, string kind, string key)
        {
            if (g == null) return null;
            for (int i = 0; i < g.ins.Count; i++)
                if (g.ins[i].kind == kind && g.ins[i].key == key) return g.ins[i];
            return null;
        }

        public static Node FindOutput(Snap g, string kind, string key)
        {
            if (g == null) return null;
            for (int i = 0; i < g.outs.Count; i++)
                if (g.outs[i].kind == kind && g.outs[i].key == key) return g.outs[i];
            return null;
        }

        /// <summary>按 id 建索引，供"已存在的连线"快速比对。</summary>
        public static Dictionary<string, Node> IndexByIds(List<Node> nodes)
        {
            var map = new Dictionary<string, Node>();
            if (nodes == null) return map;
            foreach (var n in nodes) map[n.id] = n;
            return map;
        }

        /// <summary>连线键：inId + ">" + outId。</summary>
        public static string LinkKey(string inId, string outId) { return inId + ">" + outId; }

        public static Dictionary<string, GLink> IndexLinks(List<GLink> links)
        {
            var map = new Dictionary<string, GLink>();
            if (links == null) return map;
            foreach (var l in links) map[LinkKey(l.inId, l.outId)] = l;
            return map;
        }

        public static GLink FindLink(Snap g, string inId, string outId)
        {
            if (g == null) return null;
            foreach (var l in g.links) if (l.inId == inId && l.outId == outId) return l;
            return null;
        }

        /// <summary>该输入的全部连线（用于"断开某输入的所有连接"）。</summary>
        public static List<GLink> LinksFrom(Snap g, string inId)
        {
            var list = new List<GLink>();
            if (g == null) return list;
            foreach (var l in g.links) if (l.inId == inId) list.Add(l);
            return list;
        }
    }
}
