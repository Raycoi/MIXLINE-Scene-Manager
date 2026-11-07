// MIXLINE Scene Manager — Services/SnapshotBuilder.cs
// 纯函数：实时音频图 → 场景快照。不做 IO，不调用引擎，便于测试。
using System;
using System.Collections.Generic;

namespace Asr
{
    static class SnapshotBuilder
    {
        /// <summary>把当前图拍成场景；孤儿连线（端点不在图里）会被跳过。</summary>
        public static Scene FromGraph(Snap graph, string name, string id, string createdAt)
        {
            var sc = new Scene { id = id, name = name, createdAt = createdAt };
            if (graph == null) return sc;

            var inMap = GraphLookup.IndexByIds(graph.ins);
            var outMap = GraphLookup.IndexByIds(graph.outs);
            foreach (var link in graph.links)
            {
                Node from, to;
                if (!inMap.TryGetValue(link.inId, out from)) continue;
                if (!outMap.TryGetValue(link.outId, out to)) continue;
                sc.links.Add(new SceneLink
                {
                    sk = from.kind,
                    sv = from.key,
                    dk = to.kind,
                    dv = to.key,
                    level = link.level,
                    muted = link.muted
                });
            }
            return sc;
        }
    }
}
