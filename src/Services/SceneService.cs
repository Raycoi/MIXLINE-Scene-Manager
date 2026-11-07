// MIXLINE Scene Manager — Services/SceneService.cs
// 场景业务层：快照 / 差异比较 / 切换执行 / 路由编辑的唯一入口。
//
// 依赖方向（只允许单向）：
//   Ui  ──►  Services.SceneService  ──►  Protocol.MixlineEngine   （读写 MIXLINE 实时路由）
//                                  ──►  Data.SceneStore          （读写持久化配置）
// SceneService 不接触任何 UI 类型；Engine / Store 也不知道 SceneService 的存在。
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace Asr
{
    class SceneService
    {
        readonly Store store;

        /// <summary>切换/编辑串行化：避免后台刷新与用户操作同时改 MIXLINE 路由。</summary>
        readonly object operationGate = new object();

        public SceneService(Store store) { this.store = store; }

        public Store Data { get { return store; } }

        // ---------------- 读回校验（替代固定 Thread.Sleep） ----------------
        const int PollStartMs = 40, PollMaxMs = 400;

        /// <summary>
        /// 轮询直到条件成立或超时（指数退避 40→80→160→320→400ms）。
        /// 用于替代"睡固定的 120ms 赌引擎已经落地"的写法。
        /// </summary>
        public bool WaitUntil(Func<bool> ok, int timeoutMs, string what)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int delay = PollStartMs;
            while (true)
            {
                try { if (ok()) return true; }
                catch (Exception ex) { Log.Debug("轮询条件异常：" + what, ex); }
                if (sw.ElapsedMilliseconds >= timeoutMs) break;
                Thread.Sleep(delay);
                delay = Math.Min(delay * 2, PollMaxMs);
            }
            Log.Warn(what + "：未在 " + timeoutMs + "ms 内达成，继续后续步骤");
            return false;
        }

        /// <summary>实时路由里是否已包含这条业务键连线。</summary>
        public bool LiveHasLink(string bizKey)
        {
            try { return SceneDiff.LiveKeys(ReadLiveGraph()).Contains(bizKey); }
            catch (Exception ex) { Log.Debug("读图失败（判定连线）", ex); return false; }
        }

        /// <summary>实时路由里是否还有该输入源的任何连线。</summary>
        public bool LiveHasAnyFrom(string inKind, string inKey)
        {
            try
            {
                var g = ReadLiveGraph();
                var src = GraphLookup.FindInput(g, inKind, inKey);
                if (src == null) return false;
                return GraphLookup.LinksFrom(g, src.id).Count > 0;
            }
            catch (Exception ex) { Log.Debug("读图失败（判定输入）", ex); return false; }
        }

        /// <summary>实时静音状态是否与期望一致（只比较 want 里出现的键）。</summary>
        bool LiveMutesMatch(Dictionary<string, bool> want)
        {
            try
            {
                var g = ReadLiveGraph();
                var inM = GraphLookup.IndexByIds(g.ins);
                var outM = GraphLookup.IndexByIds(g.outs);
                foreach (var l in g.links)
                {
                    Node a, b;
                    if (!inM.TryGetValue(l.inId, out a) || !outM.TryGetValue(l.outId, out b)) continue;
                    string k = a.kind + ":" + a.key + ">" + b.kind + ":" + b.key;
                    bool w;
                    if (want.TryGetValue(k, out w) && w != l.muted) return false;
                }
                return true;
            }
            catch (Exception ex) { Log.Debug("读图失败（判定静音）", ex); return false; }
        }
        public List<Scene> Scenes { get { return store.Scenes; } }
        public Dictionary<string, string> Names { get { return store.Names; } }

        // ======================== 读取实时状态 ========================

        /// <summary>读取 MIXLINE 当前完整音频图（每次调用新建一次 RPC）。</summary>
        public Snap ReadLiveGraph() { return Engine.GetGraphFull(); }


        public Scene Snapshot(string name)
        {
            var g = Engine.GetGraphFull();
            var sc = new Scene { id = "sc_" + DateTime.Now.Ticks.ToString("x"), name = name, createdAt = DateTime.UtcNow.ToString("o") };
            var inMap = new Dictionary<string, Node>(); foreach (var n in g.ins) inMap[n.id] = n;
            var outMap = new Dictionary<string, Node>(); foreach (var n in g.outs) outMap[n.id] = n;
            foreach (var l in g.links)
            {
                Node a, b;
                if (!inMap.TryGetValue(l.inId, out a) || !outMap.TryGetValue(l.outId, out b)) continue;
                sc.links.Add(new SceneLink { sk = a.kind, sv = a.key, dk = b.kind, dv = b.key, level = l.level, muted = l.muted });
            }
            return sc;
        }

        public void SyncSceneFromLive(string id)
        {
            var target = store.FindScene(id);
            if (target == null) return;
            try
            {
                var g = Engine.GetGraphFull();
                var inMap = new Dictionary<string, Node>(); foreach (var n in g.ins) inMap[n.id] = n;
                var outMap = new Dictionary<string, Node>(); foreach (var n in g.outs) outMap[n.id] = n;
                var list = new List<SceneLink>();
                foreach (var l in g.links)
                {
                    Node a, b;
                    if (!inMap.TryGetValue(l.inId, out a) || !outMap.TryGetValue(l.outId, out b)) continue;
                    list.Add(new SceneLink { sk = a.kind, sv = a.key, dk = b.kind, dv = b.key, level = l.level, muted = l.muted });
                }
                target.links = list;
                target.createdAt = DateTime.UtcNow.ToString("o");
                store.Save();
            }
            catch (Exception exIgnored1) { Log.Warn("SceneService.cs: 忽略可恢复异常", exIgnored1); }
        }

        public void MarkMatch()
        {
            Snap g;
            try
            {
                g = Engine.GetGraphFull();
            }
            catch (Exception ex)
            {
                // MIXLINE 未启动 / 引擎端口失效：不要抛异常（会闪退），当作"当前无路由"
                Log.Warn("读取 MIXLINE 路由失败（可能未启动）：" + ex.Message, ex);
                foreach (var sc in store.Scenes) sc.match = false;
                return;
            }
            bool muteChanged;
            var live = SnapshotLiveState(g, out muteChanged);
            bool nameChanged = RefreshNamesAndIcons(g);
            if (muteChanged || nameChanged) store.Save();
            UpdateMatchFlags(live);
        }

        /// <summary>读取实时连线键集合，并把静音状态同步进 store.Mutes（返回是否需要落盘）。</summary>
        HashSet<string> SnapshotLiveState(Snap g, out bool muteChanged)
        {
            var inMap = GraphLookup.IndexByIds(g.ins);
            var outMap = GraphLookup.IndexByIds(g.outs);
            var live = new HashSet<string>();
            muteChanged = false;
            foreach (var link in g.links)
            {
                Node from = null, to = null;
                if (!inMap.TryGetValue(link.inId, out from) || !outMap.TryGetValue(link.outId, out to)) continue;
                string key = from.kind + ":" + from.key + ">" + to.kind + ":" + to.key;
                live.Add(key);
                bool known;
                if (store.Mutes.TryGetValue(key, out known) ? known != link.muted : true)
                {
                    store.Mutes[key] = link.muted;
                    muteChanged = true;
                }
            }
            return live;
        }
        /// <summary>补齐设备名/应用名与图标缓存；返回是否发生变更（变更则需落盘）。</summary>
        bool RefreshNamesAndIcons(Snap g)
        {
            bool nameChanged = false;
            foreach (var n in g.ins) if (!store.Names.ContainsKey(n.key)) nameChanged = true;
            foreach (var n in g.outs) if (!store.Names.ContainsKey(n.key)) nameChanged = true;
            if (nameChanged)
            {
                Log.Guard("刷新设备/应用名称", delegate
                {
                    var dev = Engine.GetDeviceNames();
                    foreach (var kv in dev) if (SetName(kv.Key, kv.Value)) nameChanged = true;
                    Dictionary<string, string> appExe;
                    var apps = Engine.GetApplicationNames(out appExe);
                    foreach (var kv in apps) if (SetName(kv.Key, kv.Value)) nameChanged = true;
                    foreach (var kv in appExe)
                    {
                        string oldExe;
                        if (!store.ExePaths.TryGetValue(kv.Key, out oldExe) || oldExe != kv.Value) { store.ExePaths[kv.Key] = kv.Value; nameChanged = true; }
                        Icons.Ensure(kv.Key, kv.Value);
                    }
                });
            }


            // 只要还有 app 节点缺 exe 路径/图标，就补抓一次（与名字是否变化无关）
            bool needApps = false;
            foreach (var n in g.ins) if (n.kind == "app" && !store.ExePaths.ContainsKey(n.key)) needApps = true;
            foreach (var n in g.outs) if (n.kind == "app" && !store.ExePaths.ContainsKey(n.key)) needApps = true;
            if (!needApps)
            {
                foreach (var kv in store.ExePaths)
                {
                    if (!System.IO.File.Exists(System.IO.Path.Combine(Icons.Dir, kv.Key + ".png"))) { needApps = true; break; }
                }
            }
            if (needApps)
            {
                Log.Guard("补抓应用图标", delegate
                {
                    Dictionary<string, string> exeMap;
                    var apps = Engine.GetApplicationNames(out exeMap);
                    foreach (var kv in apps) if (SetName(kv.Key, kv.Value)) nameChanged = true;
                    foreach (var kv in exeMap)
                    {
                        string oldExe2;
                        if (!store.ExePaths.TryGetValue(kv.Key, out oldExe2) || oldExe2 != kv.Value) nameChanged = true;
                        store.ExePaths[kv.Key] = kv.Value;
                        Icons.Ensure(kv.Key, kv.Value);
                    }
                    nameChanged = true;
                });
            }
            return nameChanged;
        }

        /// <summary>写入名称；返回是否发生变化（用于决定是否落盘）。</summary>
        bool SetName(string key, string value)
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(value)) return false;
            string old;
            if (store.Names.TryGetValue(key, out old) && old == value) return false;
            store.Names[key] = value;
            return true;
        }

        /// <summary>
        /// 重新从引擎拉取设备名/应用名（应用的“名称”其实是窗口标题，会随播放内容变化），
        /// 覆盖旧值并落盘。切换场景后调用，保证界面副标题与 MIXLINE 当前状态一致。
        /// </summary>
        public void RefreshNamesNow()
        {
            Log.Guard("重新拉取设备/应用名称", delegate
            {
                bool changed = false;
                var dev = Engine.GetDeviceNames();
                foreach (var kv in dev) if (SetName(kv.Key, kv.Value)) changed = true;
                Dictionary<string, string> exeMap;
                var apps = Engine.GetApplicationNames(out exeMap);
                foreach (var kv in apps) if (SetName(kv.Key, kv.Value)) changed = true;
                foreach (var kv in exeMap)
                {
                    string old;
                    if (!store.ExePaths.TryGetValue(kv.Key, out old) || old != kv.Value) { store.ExePaths[kv.Key] = kv.Value; changed = true; }
                }
                if (changed) store.Save();
            });
        }

        /// <summary>把每个场景与实时连线集合比对，标记 match；只有第一个匹配者高亮。</summary>
        void UpdateMatchFlags(HashSet<string> live)
        {
            // 匹配规则只保留一份实现（SceneDiff），避免各处比较逻辑漂移
            foreach (var sc in store.Scenes) sc.match = SceneDiff.IsSameRouting(sc, live);
            bool first = true;
            foreach (var sc in store.Scenes)
            {
                if (!sc.match) continue;
                if (first) { first = false; continue; }
                sc.match = false;
            }
        }

        // ======================== 路由编辑（界面唯一写入口） ========================

        /// <summary>把某个输入接到某个输出；成功返回 true，失败写日志并返回 false。</summary>
        public bool Connect(string inKind, string inKey, string outKind, string outKey)
        {
            lock (operationGate)
            {
                try
                {
                    var g = ReadLiveGraph();
                    Node src = GraphLookup.FindInput(g, inKind, inKey);
                    Node dst = GraphLookup.FindOutput(g, outKind, outKey);
                    if (src == null) { Log.Warn("连接失败：找不到输入 " + inKind + ":" + inKey); return false; }
                    if (dst == null) { Log.Warn("连接失败：找不到输出 " + outKind + ":" + outKey); return false; }
                    Engine.AddLink(src.id, dst.id);
                    WaitUntil(delegate
                    {
                        return LiveHasLink(inKind + ":" + inKey + ">" + outKind + ":" + outKey);
                    }, 1200, "连线生效");
                    Log.Info("已连接 " + inKind + ":" + inKey + " → " + outKind + ":" + outKey);
                    return true;
                }
                catch (Exception ex)
                {
                    Log.Error("连接失败 " + inKey + " → " + outKey, ex);
                    return false;
                }
            }
        }

        /// <summary>断开一条具体连线；返回实际删除的条数。</summary>
        public int Disconnect(string inKind, string inKey, string outKind, string outKey)
        {
            lock (operationGate)
            {
                try
                {
                    var g = ReadLiveGraph();
                    Node src = GraphLookup.FindInput(g, inKind, inKey);
                    Node dst = GraphLookup.FindOutput(g, outKind, outKey);
                    if (src == null || dst == null) { Log.Warn("断开失败：找不到连线 " + inKey + " → " + outKey); return 0; }
                    int removed = 0;
                    foreach (var l in GraphLookup.LinksFrom(g, src.id))
                        if (l.outId == dst.id) { Engine.RemoveLink(l.id); removed++; }
                    WaitUntil(delegate
                    {
                        return !LiveHasLink(inKind + ":" + inKey + ">" + outKind + ":" + outKey);
                    }, 1200, "断开生效");
                    Log.Info("已断开 " + inKind + ":" + inKey + " → " + outKind + ":" + outKey + "（" + removed + " 条）");
                    return removed;
                }
                catch (Exception ex)
                {
                    Log.Error("断开失败 " + inKey + " → " + outKey, ex);
                    return 0;
                }
            }
        }

        /// <summary>断开某个输入源的全部连线；返回实际删除的条数。</summary>
        public int DisconnectAllFrom(string inKind, string inKey)
        {
            lock (operationGate)
            {
                try
                {
                    var g = ReadLiveGraph();
                    Node src = GraphLookup.FindInput(g, inKind, inKey);
                    if (src == null) { Log.Warn("断开全部失败：找不到输入 " + inKind + ":" + inKey); return 0; }
                    int removed = 0;
                    foreach (var l in GraphLookup.LinksFrom(g, src.id)) { Engine.RemoveLink(l.id); removed++; }
                    WaitUntil(delegate
                    {
                        return !LiveHasAnyFrom(inKind, inKey);
                    }, 1200, "断开全部生效");
                    Log.Info("已断开 " + inKind + ":" + inKey + " 的全部 " + removed + " 条连线");
                    return removed;
                }
                catch (Exception ex)
                {
                    Log.Error("断开全部失败 " + inKey, ex);
                    return 0;
                }
            }
        }

        /// <summary>最近一次切换的可读原因（失败时给界面用）。</summary>
        public string LastSwitchReason = "";

        /// <summary>最近一次切换时"系统输入/输出"设置失败的原因（成功为空）。</summary>
        public string LastSystemWarning = "";

        /// <summary>把连线业务键翻译成 "输入 → 输出"（用别名/短名）。</summary>
        string ReadableLink(string key)
        {
            try
            {
                int gt = key.IndexOf('>');
                if (gt <= 0) return key;
                string src = key.Substring(0, gt), dst = key.Substring(gt + 1);
                return ReadableNode(src) + " → " + ReadableNode(dst);
            }
            catch { return key; }
        }

        string ReadableNode(string kindKey)
        {
            int c = kindKey.IndexOf(':');
            string kind = c < 0 ? kindKey : kindKey.Substring(0, c);
            string key = c < 0 ? "" : kindKey.Substring(c + 1);
            string a = store.AliasOf(key);
            if (!string.IsNullOrEmpty(a)) return a;
            return DeviceNames.Short(store.Names, store.ExePaths, kind, key);
        }

        /// <summary>切换后把实时路由与场景期望对比，仅用于日志/诊断，不改变切换结果。</summary>
        public bool VerifySwitch(Scene sc)
        {
            try
            {
                var g = ReadLiveGraph();
                var live = new HashSet<string>();
                var inMap = GraphLookup.IndexByIds(g.ins);
                var outMap = GraphLookup.IndexByIds(g.outs);
                foreach (var l in g.links)
                {
                    Node a, b;
                    if (!inMap.TryGetValue(l.inId, out a) || !outMap.TryGetValue(l.outId, out b)) continue;
                    live.Add(a.kind + ":" + a.key + ">" + b.kind + ":" + b.key);
                }
                var missing = SceneDiff.Missing(sc, live);
                var extra = sc.additive ? new List<string>() : SceneDiff.Extra(sc, live);
                foreach (var k in missing) Log.Warn("切换校验：缺少连线 " + k + "（场景 " + sc.name + "）");

                // 生成人话原因：优先说"缺哪条"，其次"多了哪条"
                var sb = new StringBuilder();
                int shown = 0;
                if (missing.Count > 0)
                {
                    sb.Append("缺少 ").Append(ReadableLink(missing[0]));
                    shown = 1;
                    while (shown < missing.Count && shown < 2) { sb.Append("、").Append(ReadableLink(missing[shown])); shown++; }
                    if (missing.Count > shown) sb.Append(" 等 ").Append(missing.Count).Append(" 条");
                }
                if (extra.Count > 0)
                {
                    if (sb.Length > 0) sb.Append("；");
                    sb.Append("多出 ").Append(ReadableLink(extra[0]));
                    if (extra.Count > 1) sb.Append(" 等 ").Append(extra.Count).Append(" 条");
                }
                if (sb.Length == 0 && missing.Count == 0 && extra.Count == 0) sb.Append("路由与场景一致");
                LastSwitchReason = sb.ToString();
                return missing.Count == 0 && extra.Count == 0;
            }
            catch (Exception ex) { Log.Error("切换校验失败", ex); return false; }
        }

        public string Switch(Scene sc)
        {
            lock (operationGate) { return SwitchCore(sc); }
        }

        /// <summary>切换实现：补节点 → 加连线 → 删多余 → 恢复音量 → 恢复静音 → 校验。</summary>
        string SwitchCore(Scene sc)
        {
            var g = Engine.GetGraphFull();
            var inByKey = new Dictionary<string, Node>(); foreach (var n in g.ins) inByKey[n.kind + ":" + n.key] = n;
            var outByKey = new Dictionary<string, Node>(); foreach (var n in g.outs) outByKey[n.kind + ":" + n.key] = n;

            var desired = new List<GLink>();
            foreach (var l in sc.links)
            {
                Node a = inByKey.ContainsKey(l.sk + ":" + l.sv) ? inByKey[l.sk + ":" + l.sv] : null;
                if (a == null)
                {
                    a = l.sk == "mic" ? Engine.AddMic(l.sv) : (l.sk == "app" ? Engine.AddApp(l.sv) : null);
                    if (a == null) continue;
                    inByKey[a.kind + ":" + a.key] = a;
                }
                Node b = outByKey.ContainsKey(l.dk + ":" + l.dv) ? outByKey[l.dk + ":" + l.dv] : null;
                if (b == null)
                {
                    b = l.dk == "speaker" ? Engine.AddSpeaker(l.dv) : (l.dk == "stream" ? Engine.AddStream(l.dv) : null);
                    if (b == null) continue;
                    outByKey[b.kind + ":" + b.key] = b;
                }
                desired.Add(new GLink { inId = a.id, outId = b.id, level = l.level, muted = l.muted });
            }

            var cur = new Dictionary<string, GLink>();
            foreach (var l in g.links) cur[l.inId + ">" + l.outId] = l;

            // 切换前先抓一份"此刻真实静音状态"，作为切换后恢复的唯一依据
            var liveMutes = new Dictionary<string, bool>();
            {
                var inM = new Dictionary<string, Node>(); foreach (var n in g.ins) inM[n.id] = n;
                var outM = new Dictionary<string, Node>(); foreach (var n in g.outs) outM[n.id] = n;
                foreach (var l in g.links)
                {
                    Node a, b;
                    if (!inM.TryGetValue(l.inId, out a) || !outM.TryGetValue(l.outId, out b)) continue;
                    string kk = a.kind + ":" + a.key + ">" + b.kind + ":" + b.key;
                    liveMutes[kk] = l.muted;
                    store.Mutes[kk] = l.muted;
                }
            }

            int added = 0, removed = 0;
            foreach (var d in desired)
                if (!cur.ContainsKey(d.inId + ">" + d.outId)) { Engine.AddLink(d.inId, d.outId); added++; }

            var want2 = new HashSet<string>();
            foreach (var d in desired) want2.Add(d.inId + ">" + d.outId);
            if (added > 0)
            {
                var wantBiz = SceneDiff.DesiredKeys(sc);
                WaitUntil(delegate
                {
                    try
                    {
                        var live = SceneDiff.LiveKeys(Engine.GetGraphFull());
                        foreach (var k in wantBiz) if (!live.Contains(k)) return false;
                        return true;
                    }
                    catch (Exception ex) { Log.Debug("等待加连线时读图失败", ex); return false; }
                }, 1500, "加连线生效");
            }
            if (!sc.additive)   // 场景标记"保留现有连线"时不删除任何多余连线
            {
                foreach (var l in g.links)
                    if (!want2.Contains(l.inId + ">" + l.outId)) { Engine.RemoveLink(l.id); removed++; }
            }

            if (removed > 0)
            {
                WaitUntil(delegate
                {
                    try { return SceneDiff.Extra(sc, SceneDiff.LiveKeys(Engine.GetGraphFull())).Count == 0; }
                    catch (Exception ex) { Log.Debug("等待删连线时读图失败", ex); return false; }
                }, 1500, "删除多余连线生效");
            }

            foreach (var d in desired)
            {
                GLink ex;
                if (cur.TryGetValue(d.inId + ">" + d.outId, out ex))
                    if (Math.Abs(ex.level - d.level) > 0.001f)
                        Engine.SetLinkVolume(ex.id, d.level, ex.muted);
            }

            try
            {
                var g2 = Engine.GetGraphFull();
                var in2 = new Dictionary<string, Node>(); foreach (var n2 in g2.ins) in2[n2.id] = n2;
                var out2 = new Dictionary<string, Node>(); foreach (var n2 in g2.outs) out2[n2.id] = n2;
                bool dirty = false;
                foreach (var l2 in g2.links)
                {
                    Node a2, b2;
                    if (!in2.TryGetValue(l2.inId, out a2) || !out2.TryGetValue(l2.outId, out b2)) continue;
                    string k2 = a2.kind + ":" + a2.key + ">" + b2.kind + ":" + b2.key;
                    bool wantMute;
                    bool known = liveMutes.TryGetValue(k2, out wantMute);
                    if (!known) known = store.Mutes.TryGetValue(k2, out wantMute);
                    if (known && wantMute != l2.muted)
                    {
                        Engine.SetLinkVolume(l2.id, l2.level, wantMute);
                        dirty = true;
                    }
                }
                if (dirty)
                    WaitUntil(delegate { return LiveMutesMatch(liveMutes); }, 900, "恢复静音生效");
            }
            catch (Exception exIgnored2) { Log.Warn("SceneService.cs: 忽略可恢复异常", exIgnored2); }

            if (!VerifySwitch(sc)) Log.Warn("切换完成但校验发现差异：" + sc.name);

            // 路由完成后再改 Windows 默认设备（失败不影响切换结果）
            LastSystemWarning = SystemIO.ApplyOnSwitch(sc, store);
            if (!string.IsNullOrEmpty(LastSystemWarning)) Log.Warn("切换后系统设备设置：" + LastSystemWarning);

            return "＋" + added + "  −" + removed;
        }
    }
}

