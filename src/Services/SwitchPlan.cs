// MIXLINE Scene Manager — Services/SwitchPlan.cs
// 纯函数：把"切到某场景"翻译成可执行的增删计划。不调用引擎，便于离线测试。
// 执行顺序由 SceneService.SwitchCore 决定，这里只负责"算什么"。
using System;
using System.Collections.Generic;

namespace Asr
{
    class SwitchPlan
    {
        /// <summary>切换后期望存在的连线（使用实时图中的节点 id）。</summary>
        public List<GLink> Desired = new List<GLink>();
        /// <summary>需要新增的连线。</summary>
        public List<GLink> ToAdd = new List<GLink>();
        /// <summary>需要删除的现有连线（additive 场景为空）。</summary>
        public List<GLink> ToRemove = new List<GLink>();
        /// <summary>音量需要调整的连线：现有连线 + 期望值。</summary>
        public List<GLink[]> VolumeChanges = new List<GLink[]>();
        /// <summary>场景引用了但实时图中既不存在、也无法自动补建的节点。</summary>
        public List<SceneLink> Unresolved = new List<SceneLink>();

        public bool Additive;

        /// <summary>
        /// 生成计划。
        /// resolveInput/resolveOutput：把场景节点解析成实时节点；返回 null 表示该条无法解析。
        /// </summary>
        public static SwitchPlan Build(Scene scene, Snap live,
            Func<SceneLink, Node> resolveInput, Func<SceneLink, Node> resolveOutput)
        {
            var plan = new SwitchPlan { Additive = scene != null && scene.additive };
            if (scene == null) return plan;

            var seen = new HashSet<string>();
            foreach (var l in scene.links)
            {
                Node from = resolveInput == null ? null : resolveInput(l);
                Node to = resolveOutput == null ? null : resolveOutput(l);
                if (from == null || to == null) { plan.Unresolved.Add(l); continue; }

                string key = from.id + ">" + to.id;
                if (!seen.Add(key)) continue;      // 场景里重复的同一对节点只算一次

                var desired = new GLink { inId = from.id, outId = to.id, level = l.level, muted = l.muted };
                plan.Desired.Add(desired);
            }

            var existing = GraphLookup.IndexLinks(live == null ? null : live.links);
            foreach (var d in plan.Desired)
            {
                string key = GraphLookup.LinkKey(d.inId, d.outId);
                GLink cur;
                if (!existing.TryGetValue(key, out cur)) plan.ToAdd.Add(d);
                else if (Math.Abs(cur.level - d.level) > 0.001f) plan.VolumeChanges.Add(new GLink[] { cur, d });
            }

            if (!plan.Additive && live != null && live.links != null)
            {
                var wanted = new HashSet<string>();
                foreach (var d in plan.Desired) wanted.Add(GraphLookup.LinkKey(d.inId, d.outId));
                foreach (var cur in live.links)
                    if (!wanted.Contains(GraphLookup.LinkKey(cur.inId, cur.outId))) plan.ToRemove.Add(cur);
            }
            return plan;
        }
    }
}
