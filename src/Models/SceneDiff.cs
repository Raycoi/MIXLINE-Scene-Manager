// MIXLINE Scene Manager — Models/SceneDiff.cs
// 纯函数：场景期望路由 vs 实时路由的比较。无引擎、无文件、无 UI，可直接单元测试。
using System;
using System.Collections.Generic;

namespace Asr
{
    static class SceneDiff
    {
        /// <summary>把 "<c>kind:key&gt;kind:key</c>" 解析成 SceneLink（解析失败返回 null）。</summary>
        public static SceneLink ParseBizKey(string bizKey)
        {
            if (string.IsNullOrEmpty(bizKey)) return null;
            int gt = bizKey.IndexOf('>');
            if (gt <= 0) return null;
            string a = bizKey.Substring(0, gt), b = bizKey.Substring(gt + 1);
            int ca = a.IndexOf(':'), cb = b.IndexOf(':');
            if (ca <= 0 || cb <= 0) return null;
            return new SceneLink
            {
                sk = a.Substring(0, ca), sv = a.Substring(ca + 1),
                dk = b.Substring(0, cb), dv = b.Substring(cb + 1),
                level = 1, muted = false
            };
        }

        /// <summary>场景期望的连线键集合（形如 "mic:key>stream:key"）。</summary>
        public static HashSet<string> DesiredKeys(Scene scene)
        {
            var set = new HashSet<string>();
            if (scene == null || scene.links == null) return set;
            foreach (var l in scene.links) set.Add(Key(l));
            return set;
        }

        /// <summary>连线业务键：kind:key>kind:key。</summary>
        public static string Key(SceneLink l)
        {
            return l.sk + ":" + l.sv + ">" + l.dk + ":" + l.dv;
        }

        /// <summary>实时连线键集合：由 Snap 生成（同一对节点只保留一条）。</summary>
        public static HashSet<string> LiveKeys(Snap live)
        {
            var set = new HashSet<string>();
            if (live == null) return set;
            var inMap = GraphLookup.IndexByIds(live.ins);
            var outMap = GraphLookup.IndexByIds(live.outs);
            foreach (var link in live.links)
            {
                Node from, to;
                if (!inMap.TryGetValue(link.inId, out from)) continue;
                if (!outMap.TryGetValue(link.outId, out to)) continue;
                set.Add(from.kind + ":" + from.key + ">" + to.kind + ":" + to.key);
            }
            return set;
        }

        /// <summary>路由是否与场景完全一致（空场景永不匹配）。</summary>
        public static bool IsSameRouting(Scene scene, HashSet<string> liveKeys)
        {
            if (scene == null || liveKeys == null || liveKeys.Count == 0) return false;
            var want = DesiredKeys(scene);
            if (want.Count != liveKeys.Count) return false;
            foreach (var k in want) if (!liveKeys.Contains(k)) return false;
            return true;
        }

        /// <summary>期望有、实时没有的连线键。</summary>
        public static List<string> Missing(Scene scene, HashSet<string> liveKeys)
        {
            var list = new List<string>();
            foreach (var k in DesiredKeys(scene)) if (liveKeys == null || !liveKeys.Contains(k)) list.Add(k);
            return list;
        }

        /// <summary>实时有、场景里没有的连线键（additive 场景不关心）。</summary>
        public static List<string> Extra(Scene scene, HashSet<string> liveKeys)
        {
            var list = new List<string>();
            if (liveKeys == null) return list;
            var want = DesiredKeys(scene);
            foreach (var k in liveKeys) if (!want.Contains(k)) list.Add(k);
            return list;
        }

        /// <summary>在列表里找与 scene 连线集合完全相同的另一个场景（排除自身）；没有返回 null。</summary>
        public static Scene FindDuplicate(IList<Scene> scenes, Scene scene)
        {
            if (scenes == null || scene == null) return null;
            var mine = DesiredKeys(scene);
            foreach (var other in scenes)
            {
                if (other == null || ReferenceEquals(other, scene)) continue;
                if (DesiredKeys(other).SetEquals(mine)) return other;
            }
            return null;
        }
        /// <summary>一组场景里第一个匹配者（用于"仅高亮一个"的规则）。</summary>
        public static Scene FirstMatch(List<Scene> scenes, HashSet<string> liveKeys)
        {
            if (scenes == null) return null;
            foreach (var sc in scenes) if (IsSameRouting(sc, liveKeys)) return sc;
            return null;
        }
    }
}
