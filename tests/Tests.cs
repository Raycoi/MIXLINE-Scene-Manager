// MIXLINE Scene Manager — tests/Tests.cs
// 离线单元测试：不连接 MIXLINE、不读写用户配置（除临时目录）。使用伪造的 Snap/Scene 数据。
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;

namespace Asr.Tests
{
    static class Tests
    {
        // ---------------- 伪造数据 ----------------
        static Node In(string kind, string key) { return new Node { id = "i_" + key, kind = kind, key = key }; }
        static Node Out(string kind, string key) { return new Node { id = "o_" + key, kind = kind, key = key }; }

        static Snap Graph(Node[] ins, Node[] outs, string[][] links)
        {
            var g = new Snap();
            g.ins.AddRange(ins);
            g.outs.AddRange(outs);
            if (links != null)
                foreach (var l in links)
                    g.links.Add(new GLink { id = "l_" + l[0] + "_" + l[1], inId = l[0], outId = l[1], level = 1f, muted = false });
            return g;
        }

        static SceneLink L(string sk, string sv, string dk, string dv)
        {
            return new SceneLink { sk = sk, sv = sv, dk = dk, dv = dv, level = 1f, muted = false };
        }

        static Scene SceneOf(params SceneLink[] links)
        {
            var sc = new Scene { id = "sc_test", name = "测试场景", createdAt = "2026-01-01T00:00:00Z" };
            sc.links.AddRange(links);
            return sc;
        }

        static string TempDir()
        {
            // 刻意建在工作目录下（bin/_test-tmp）：某些受限环境不允许写系统临时目录
            string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "_test-tmp");
            string d = Path.Combine(root, Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(d);
            return d;
        }

        // ================= 1. 键与集合 =================
        static void KeyAndSets()
        {
            T.Group("1. 场景键与集合（SceneDiff）");

            T.Case("空场景的期望键为空集", delegate
            {
                T.Eq(0, SceneDiff.DesiredKeys(SceneOf()).Count, "空场景不应有连线键");
            });

            T.Case("null 场景不抛异常且返回空集", delegate
            {
                T.Eq(0, SceneDiff.DesiredKeys(null).Count, "null 场景返回空集");
                T.False(SceneDiff.IsSameRouting(null, new HashSet<string>()), "null 场景永不算匹配");
            });

            T.Case("连线键格式为 kind:key>kind:key", delegate
            {
                T.Eq("mic:mic-A>stream:st-1", SceneDiff.Key(L("mic", "mic-A", "stream", "st-1")), "键格式");
            });

            T.Case("重复连线在期望集合中只算一个", delegate
            {
                var sc = SceneOf(L("mic", "mic-A", "stream", "st-1"), L("mic", "mic-A", "stream", "st-1"));
                T.Eq(1, SceneDiff.DesiredKeys(sc).Count, "重复连线应去重");
            });
        }

        // ================= 2. 场景比较 =================
        static void Comparison()
        {
            T.Group("2. 场景与实时路由比较");

            var g = Graph(
                new[] { In("mic", "mic-A"), In("app", "app-B") },
                new[] { Out("stream", "st-1"), Out("speaker", "spk-1") },
                new[] { new[] { "i_mic-A", "o_st-1" }, new[] { "i_app-B", "o_spk-1" } });
            var live = SceneDiff.LiveKeys(g);

            T.Case("实时键集合正确解析（含节点 kind/key）", delegate
            {
                T.Eq(2, live.Count, "两条实时连线");
                T.True(live.Contains("mic:mic-A>stream:st-1"), "缺少 mic→stream 键");
                T.True(live.Contains("app:app-B>speaker:spk-1"), "缺少 app→speaker 键");
            });

            T.Case("完全一致 → 匹配", delegate
            {
                var sc = SceneOf(L("mic", "mic-A", "stream", "st-1"), L("app", "app-B", "speaker", "spk-1"));
                T.True(SceneDiff.IsSameRouting(sc, live), "应匹配");
            });

            T.Case("少一条 → 不匹配，且能列出缺失", delegate
            {
                var sc = SceneOf(L("mic", "mic-A", "stream", "st-1"));
                T.False(SceneDiff.IsSameRouting(sc, live), "不应匹配");
                var missing = SceneDiff.Missing(sc, live);
                T.Eq(0, missing.Count, "该场景的连线在实时图中都存在");
            });

            T.Case("多一条实时连线 → 不匹配，且能列出多余", delegate
            {
                var sc = SceneOf(L("mic", "mic-A", "stream", "st-1"));
                var extra = SceneDiff.Extra(sc, live);
                T.Eq(1, extra.Count, "应有一条多余连线");
                T.Eq("app:app-B>speaker:spk-1", extra[0], "多余连线内容");
            });

            T.Case("实时为空 → 永不匹配（空场景不算生效）", delegate
            {
                var sc = SceneOf();
                T.False(SceneDiff.IsSameRouting(sc, new HashSet<string>()), "空实时不得匹配");
                T.False(SceneDiff.IsSameRouting(SceneOf(L("mic", "mic-A", "stream", "st-1")), new HashSet<string>()), "有期望但实时为空时不得匹配");
            });

            T.Case("FirstMatch 只返回第一个匹配场景", delegate
            {
                var a = SceneOf(L("mic", "mic-A", "stream", "st-1"), L("app", "app-B", "speaker", "spk-1"));
                var b = SceneOf(L("mic", "mic-A", "stream", "st-1"), L("app", "app-B", "speaker", "spk-1"));
                var list = new List<Scene> { a, b };
                T.True(ReferenceEquals(a, SceneDiff.FirstMatch(list, live)), "应返回第一个");
                T.Null(SceneDiff.FirstMatch(new List<Scene> { SceneOf() }, live), "无匹配返回 null");
            });

            T.Case("孤儿连线（端点不在图中）被忽略", delegate
            {
                var g2 = Graph(new[] { In("mic", "mic-A") }, new[] { Out("stream", "st-1") },
                    new[] { new[] { "i_mic-A", "o_ghost" }, new[] { "i_ghost", "o_st-1" } });
                T.Eq(0, SceneDiff.LiveKeys(g2).Count, "孤儿连线不应进入实时键集合");
            });
        }

        // ================= 3. 设备/连线查找 =================
        static void Lookup()
        {
            T.Group("3. 设备与连线查找（GraphLookup）");

            var ins = new[] { In("mic", "mic-A"), In("app", "app-B") };
            var outs = new[] { Out("stream", "st-1"), Out("speaker", "spk-1") };
            var g = Graph(ins, outs, new[] { new[] { "i_mic-A", "o_st-1" } });

            T.Case("按 kind+key 找到输入/输出", delegate
            {
                T.NotNull(GraphLookup.FindInput(g, "mic", "mic-A"), "应找到麦克风");
                T.NotNull(GraphLookup.FindOutput(g, "speaker", "spk-1"), "应找到扬声器");
                T.Null(GraphLookup.FindInput(g, "mic", "不存在"), "不存在的键返回 null");
                T.Null(GraphLookup.FindOutput(null, "stream", "st-1"), "null 图返回 null");
            });

            T.Case("按 id 建索引", delegate
            {
                var map = GraphLookup.IndexByIds(g.ins);
                T.Eq(2, map.Count, "两个输入");
                T.Eq("mic-A", map["i_mic-A"].key, "索引内容正确");
            });

            T.Case("连线索引与查找", delegate
            {
                var index = GraphLookup.IndexLinks(g.links);
                T.Eq(1, index.Count, "一条连线");
                T.NotNull(GraphLookup.FindLink(g, "i_mic-A", "o_st-1"), "应找到连线");
                T.Null(GraphLookup.FindLink(g, "i_app-B", "o_spk-1"), "不存在的连线返回 null");
            });

            T.Case("按输入取全部连线（一个输入多条）", delegate
            {
                var g3 = Graph(new[] { In("mic", "mic-A") }, outs,
                    new[] { new[] { "i_mic-A", "o_st-1" }, new[] { "i_mic-A", "o_spk-1" } });
                T.Eq(2, GraphLookup.LinksFrom(g3, "i_mic-A").Count, "麦克风应连两条");
                T.Eq(0, GraphLookup.LinksFrom(g3, "i_不存在").Count, "未知输入返回空表");
            });
        }

        // ================= 4. 显示名 =================
        static void Names()
        {
            T.Group("4. 设备/应用显示名（DeviceNames）");

            var names = new Dictionary<string, string>
            {
                { "dev-1", "HECATE G2 GAMING HEADSET · 扬声器" },
                { "dev-2", "HECATE G2 GAMING HEADSET · Microphone" },
                { "app-1", "Steam" },
                { "app-2", "一个相当长的应用名称用于测试截断" }
            };

            T.Case("完整名优先取引擎名称", delegate
            {
                T.Eq("HECATE G2 GAMING HEADSET · 扬声器", DeviceNames.Display(names, "speaker", "dev-1"), "完整名");
            });

            T.Case("缺名称时按类型兜底", delegate
            {
                T.Eq("麦克风", DeviceNames.Display(names, "mic", "缺失"), "麦克风兜底");
                T.Eq("应用", DeviceNames.Display(names, "app", "缺失"), "应用兜底");
                T.Eq("Stream", DeviceNames.Display(names, "stream", "缺失"), "Stream 兜底");
            });

            T.Case("设备短名取 · 之后的描述", delegate
            {
                T.Eq("扬声器", DeviceNames.Short(names, "speaker", "dev-1"), "扬声器短名");
                T.Eq("Microphone", DeviceNames.Short(names, "mic", "dev-2"), "麦克风短名");
            });

            T.Case("Stream 显示完整名称", delegate
            {
                T.Eq("MIXLINE Stream", DeviceNames.Short(names, "stream", "缺失"), "无记录时兜底 MIXLINE Stream");
                var sn = new System.Collections.Generic.Dictionary<string, string>
                {
                    { "rec", "MIXLINE Record · 麦克风" },
                    { "st", "MIXLINE Stream · 麦克风" }
                };
                T.Eq("MIXLINE Record", DeviceNames.Short(sn, "stream", "rec"), "Record 全名");
                T.Eq("MIXLINE Stream", DeviceNames.Short(sn, "stream", "st"), "Stream 全名");
            });

            T.Case("应用短名过长时截断并加省略号", delegate
            {
                string s = DeviceNames.Short(names, "app", "app-2");
                T.True(s.EndsWith("…"), "应带省略号");
                T.True(s.Length <= 10, "长度不超过 10");
            });

            T.Case("颜色索引可循环且支持负数", delegate
            {
                T.Eq(CurveColors.For(0).ToArgb(), CurveColors.For(6).ToArgb(), "第 6 个等于第 0 个");
                T.Eq(CurveColors.For(5).ToArgb(), CurveColors.For(-1).ToArgb(), "负数索引等价于末尾");
            });
        }

        // ================= 5. 快照 =================
        static void Snapshot()
        {
            T.Group("5. 图 → 场景快照（SnapshotBuilder）");

            T.Case("正常图生成对应连线", delegate
            {
                var g = Graph(new[] { In("mic", "mic-A") }, new[] { Out("stream", "st-1") },
                    new[] { new[] { "i_mic-A", "o_st-1" } });
                var sc = SnapshotBuilder.FromGraph(g, "测试", "id-1", "t");
                T.Eq(1, sc.links.Count, "一条连线");
                T.Eq("mic", sc.links[0].sk, "输入类型");
                T.Eq("mic-A", sc.links[0].sv, "输入 key");
                T.Eq("stream", sc.links[0].dk, "输出类型");
                T.Eq("st-1", sc.links[0].dv, "输出 key");
                T.Eq("测试", sc.name, "场景名");
                T.Eq("id-1", sc.id, "场景 id");
            });

            T.Case("空图 → 空场景（不抛异常）", delegate
            {
                var sc = SnapshotBuilder.FromGraph(new Snap(), "空", "id-2", "t");
                T.Eq(0, sc.links.Count, "空场景无连线");
            });

            T.Case("null 图 → 空场景", delegate
            {
                var sc = SnapshotBuilder.FromGraph(null, "空", "id-3", "t");
                T.NotNull(sc, "仍应返回场景对象");
                T.Eq(0, sc.links.Count, "无连线");
            });

            T.Case("孤儿连线被跳过", delegate
            {
                var g = Graph(new[] { In("mic", "mic-A") }, new[] { Out("stream", "st-1") },
                    new[] { new[] { "i_mic-A", "o_ghost" } });
                T.Eq(0, SnapshotBuilder.FromGraph(g, "x", "id", "t").links.Count, "孤儿连线不进场景");
            });

            T.Case("快照保留音量与静音", delegate
            {
                var g = new Snap();
                g.ins.Add(In("mic", "mic-A")); g.outs.Add(Out("stream", "st-1"));
                g.links.Add(new GLink { id = "l1", inId = "i_mic-A", outId = "o_st-1", level = 0.42f, muted = true });
                var sc = SnapshotBuilder.FromGraph(g, "x", "id", "t");
                T.Eq(0.42f, sc.links[0].level, "音量");
                T.True(sc.links[0].muted, "静音状态");
            });
        }

        // ================= 6. 切换计划 =================
        static void SwitchPlanning()
        {
            T.Group("6. 切换计划生成（SwitchPlan）");

            Func<SceneLink, Node> resolveIn = delegate (SceneLink l) { return new Node { id = "i_" + l.sv, kind = l.sk, key = l.sv }; };
            Func<SceneLink, Node> resolveOut = delegate (SceneLink l) { return new Node { id = "o_" + l.dv, kind = l.dk, key = l.dv }; };

            T.Case("实时已完全一致 → 不增不删", delegate
            {
                var g = Graph(new[] { In("mic", "mic-A") }, new[] { Out("stream", "st-1") }, new[] { new[] { "i_mic-A", "o_st-1" } });
                var sc = SceneOf(L("mic", "mic-A", "stream", "st-1"));
                var plan = SwitchPlan.Build(sc, g, resolveIn, resolveOut);
                T.Eq(0, plan.ToAdd.Count, "无需新增");
                T.Eq(0, plan.ToRemove.Count, "无需删除");
                T.Eq(0, plan.VolumeChanges.Count, "无需调音量");
                T.Eq(0, plan.Unresolved.Count, "无未解析");
            });

            T.Case("实时为空 → 全部新增", delegate
            {
                var sc = SceneOf(L("mic", "mic-A", "stream", "st-1"), L("app", "app-B", "speaker", "spk-1"));
                var plan = SwitchPlan.Build(sc, new Snap(), resolveIn, resolveOut);
                T.Eq(2, plan.ToAdd.Count, "两条都要新增");
                T.Eq(0, plan.ToRemove.Count, "无删除");
            });

            T.Case("实时有多余连线 → 计划删除", delegate
            {
                var g = Graph(new[] { In("mic", "mic-A") }, new[] { Out("stream", "st-1") },
                    new[] { new[] { "i_mic-A", "o_st-1" }, new[] { "i_mic-A", "o_ghost" } });
                var sc = SceneOf(L("mic", "mic-A", "stream", "st-1"));
                var plan = SwitchPlan.Build(sc, g, resolveIn, resolveOut);
                T.Eq(1, plan.ToRemove.Count, "应删除多余的一条");
                T.Eq(0, plan.ToAdd.Count, "已有连线不重复添加");
            });

            T.Case("additive 场景只增不删", delegate
            {
                var g = Graph(new[] { In("mic", "mic-A") }, new[] { Out("stream", "st-1") },
                    new[] { new[] { "i_mic-A", "o_ghost" } });
                var sc = SceneOf(L("mic", "mic-A", "stream", "st-1"));
                sc.additive = true;
                var plan = SwitchPlan.Build(sc, g, resolveIn, resolveOut);
                T.Eq(0, plan.ToRemove.Count, "additive 不删除");
                T.Eq(1, plan.ToAdd.Count, "仍会新增");
            });

            T.Case("节点解析失败 → 记入 Unresolved 且不中断", delegate
            {
                var sc = SceneOf(L("mic", "mic-missing", "stream", "st-1"), L("app", "app-B", "speaker", "spk-1"));
                var plan = SwitchPlan.Build(sc, new Snap(), null, resolveOut);
                T.Eq(2, plan.Unresolved.Count, "两条都无法解析");
                T.Eq(0, plan.Desired.Count, "不应产生期望连线");
            });

            T.Case("场景内重复的同一对节点只计划一次", delegate
            {
                var sc = SceneOf(L("mic", "mic-A", "stream", "st-1"), L("mic", "mic-A", "stream", "st-1"));
                var plan = SwitchPlan.Build(sc, new Snap(), resolveIn, resolveOut);
                T.Eq(1, plan.ToAdd.Count, "去重后只加一条");
            });

            T.Case("音量差异被识别", delegate
            {
                var g = Graph(new[] { In("mic", "mic-A") }, new[] { Out("stream", "st-1") },
                    new[] { new[] { "i_mic-A", "o_st-1" } });
                g.links[0].level = 0.5f;
                var sc = SceneOf(L("mic", "mic-A", "stream", "st-1"));
                sc.links[0].level = 0.9f;
                var plan = SwitchPlan.Build(sc, g, resolveIn, resolveOut);
                T.Eq(1, plan.VolumeChanges.Count, "应计划一次音量调整");
                T.Eq(0, plan.ToAdd.Count, "不应重复加线");
            });

            T.Case("空场景（非 additive）→ 删除全部现有连线", delegate
            {
                var g = Graph(new[] { In("mic", "mic-A") }, new[] { Out("stream", "st-1") },
                    new[] { new[] { "i_mic-A", "o_st-1" } });
                var plan = SwitchPlan.Build(SceneOf(), g, resolveIn, resolveOut);
                T.Eq(1, plan.ToRemove.Count, "空场景应清空连线");
            });
        }

        // ================= 7. 持久化 =================
        static void Persistence()
        {
            T.Group("7. 配置序列化/反序列化（Store）");

            T.Case("场景保存后重新加载内容一致", delegate
            {
                string dir = TempDir();
                try
                {
                    var st = new Store(dir);
                    var sc = SceneOf(L("mic", "mic-A", "stream", "st-1"), L("app", "app-B", "speaker", "spk-1"));
                    sc.name = "带中文的场景名";
                    sc.additive = true;
                    sc.links[1].muted = true;
                    sc.links[1].level = 0.7f;
                    st.Scenes.Add(sc);
                    st.Names["mic-A"] = "麦克风 A";
                    st.Mutes["mic:mic-A>stream:st-1"] = true;
                    st.IconSize = 41;
                    st.Save();
                    T.Null(st.LastSaveError, "保存不应报错");

                    var st2 = new Store(dir);
                    st2.Load();
                    T.Eq(1, st2.Scenes.Count, "场景数量");
                    T.Eq("带中文的场景名", st2.Scenes[0].name, "场景名（中文）");
                    T.True(st2.Scenes[0].additive, "additive 标记");
                    T.Eq(2, st2.Scenes[0].links.Count, "连线数量");
                    T.Eq("mic", st2.Scenes[0].links[0].sk, "输入类型");
                    T.True(st2.Scenes[0].links[1].muted, "静音状态");
                    T.Eq(0.7f, st2.Scenes[0].links[1].level, "音量");
                    T.Eq("麦克风 A", st2.Names["mic-A"], "名称映射");
                    T.True(st2.Mutes["mic:mic-A>stream:st-1"], "静音映射");
                    T.Eq(41, st2.IconSize, "图标大小");
                }
                finally { try { Directory.Delete(dir, true); } catch { } }
            });

            T.Case("空场景列表可保存并读回", delegate
            {
                string dir = TempDir();
                try
                {
                    var st = new Store(dir);
                    st.Save();
                    var st2 = new Store(dir);
                    st2.Load();
                    T.Eq(0, st2.Scenes.Count, "无场景");
                }
                finally { try { Directory.Delete(dir, true); } catch { } }
            });

            T.Case("连线 JSON 的两种历史写法都能解析", delegate
            {
                string dir = TempDir();
                try
                {
                    var st = new Store(dir);
                    st.Save();
                    string path = st.ConfigPath;   // 真实落盘位置（可能在 <dir>\MSM-data 下）
                    string legacy = "{\"version\":3,\"scenes\":[{\"id\":\"a\",\"name\":\"旧格式\",\"createdAt\":\"t\"," +
                        "\"links\":[{\"sk\":\"mic\",\"sv\":\"m1\",\"dk\":\"stream\",\"dv\":\"s1\"}," +
                        "{\"src\":{\"kind\":\"app\",\"key\":\"a1\"},\"dst\":{\"kind\":\"speaker\",\"key\":\"p1\"}}]}]," +
                        "\"names\":{},\"mutes\":{},\"exePaths\":{},\"uiBackup\":\"\",\"iconSize\":26}";
                    File.WriteAllText(path, legacy, new System.Text.UTF8Encoding(false));
                    var st2 = new Store(dir);
                    st2.Load();
                    T.Eq(1, st2.Scenes.Count, "一个场景");
                    T.Eq(2, st2.Scenes[0].links.Count, "两种写法各一条");
                    T.Eq("mic", st2.Scenes[0].links[0].sk, "旧写法 sk");
                    T.Eq("app", st2.Scenes[0].links[1].sk, "新写法 src.kind");
                }
                finally { try { Directory.Delete(dir, true); } catch { } }
            });

            T.Case("设备别名可保存、可清除，且不影响旧配置读取", delegate
            {
                string dir = TempDir();
                try
                {
                    var st = new Store(dir);
                    st.Aliases["mic-1"] = "麦克风";
                    st.Save();
                    var st2 = new Store(dir);
                    st2.Load();
                    T.Eq("麦克风", st2.AliasOf("mic-1"), "别名应读回");
                    T.Null(st2.AliasOf("不存在"), "无别名返回 null");

                    st2.SetAlias("mic-1", "");
                    var st3 = new Store(dir);
                    st3.Load();
                    T.Null(st3.AliasOf("mic-1"), "清空后应恢复默认");

                    // 旧配置（没有 aliases 字段）必须仍能正常加载
                    string p = st3.ConfigPath;
                    string old = "{\"version\":3,\"scenes\":[],\"names\":{},\"mutes\":{},\"exePaths\":{},\"uiBackup\":\"\",\"iconSize\":26}";
                    File.WriteAllText(p, old, new System.Text.UTF8Encoding(false));
                    var st4 = new Store(dir);
                    st4.Load();
                    T.Eq(0, st4.Scenes.Count, "旧配置可加载");
                    T.NotNull(st4.Aliases, "别名字典仍可用");
                }
                finally { try { Directory.Delete(dir, true); } catch (Exception exIgnored) { } }
            });

            T.Case("FindScene 按 id 查找", delegate
            {
                string dir = TempDir();
                try
                {
                    var st = new Store(dir);
                    var sc = SceneOf(); sc.id = "sc_x";
                    st.Scenes.Add(sc);
                    T.NotNull(st.FindScene("sc_x"), "应找到");
                    T.Null(st.FindScene("sc_不存在"), "找不到返回 null");
                }
                finally { try { Directory.Delete(dir, true); } catch { } }
            });

            T.Case("损坏的配置文件不应让 Load 崩溃", delegate
            {
                string dir = TempDir();
                try
                {
                    var st = new Store(dir);
                    st.Save();
                    File.WriteAllText(Path.Combine(dir, "MSM-config.json"), "{ this is not json ", new System.Text.UTF8Encoding(false));
                    var st2 = new Store(dir);
                    st2.Load();          // 期望：记录日志并保持可用状态
                    T.NotNull(st2.Scenes, "场景列表仍可用");
                    T.Eq(0, st2.Scenes.Count, "损坏文件不应产生场景");
                }
                finally { try { Directory.Delete(dir, true); } catch { } }
            });
        }

        // ================= 入口 =================
        // ---- 读回校验（WaitUntil）：成功立即返回，超时按退避结束 ----
        static void WaitUntilBackoff()
        {
            T.Group("读回校验");
            T.Case("成功立即返回 / 超时按退避结束", delegate
            {
                string dir = System.IO.Path.Combine(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.ExecutablePath), "_test-tmp"), "wait-" + Guid.NewGuid().ToString("N"));
                System.IO.Directory.CreateDirectory(dir);
                try
                {
                    var st = new Store(dir);
                    var svc = new SceneService(st);

                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    bool ok = svc.WaitUntil(delegate { return true; }, 200, "立即成功");
                    sw.Stop();
                    T.True(ok, "条件成立时返回 true");
                    T.True(sw.ElapsedMilliseconds < 150, "不应等待完整超时");

                    sw.Restart();
                    bool bad = svc.WaitUntil(delegate { return false; }, 300, "永不成立");
                    sw.Stop();
                    T.True(!bad, "条件不成立时返回 false");
                    T.True(sw.ElapsedMilliseconds >= 250, "应至少等满超时时间");
                    T.True(sw.ElapsedMilliseconds < 2000, "不应无限等待");
                }
                finally
                {
                    try { System.IO.Directory.Delete(dir, true); } catch (Exception ex) { Log.Debug("清理测试目录失败", ex); }
                }
            });
        }
        // ---- 重复场景检测（SceneDiff.FindDuplicate）：编辑后与谁重复 ----
        static void DuplicateScenes()
        {
            T.Group("重复场景检测");
            T.Case("相同连线集合 → 找到重复；不同 → 没有", delegate
            {
                var a = new Scene { name = "A" };
                a.links.Add(new SceneLink { sk = "mic", sv = "m", dk = "speaker", dv = "s" });
                var b = new Scene { name = "B" };
                b.links.Add(new SceneLink { sk = "mic", sv = "m", dk = "speaker", dv = "s" });
                var c = new Scene { name = "C" };
                c.links.Add(new SceneLink { sk = "mic", sv = "m", dk = "speaker", dv = "s" });
                c.links.Add(new SceneLink { sk = "mic", sv = "m", dk = "stream", dv = "t" });

                var list = new List<Scene> { a, c };
                T.True(SceneDiff.FindDuplicate(list, b) == a, "B 与 A 重复");
                T.True(SceneDiff.FindDuplicate(list, a) == null, "排除自身后 A 无重复");
                T.True(SceneDiff.FindDuplicate(list, c) == null, "C 无重复");
            });
        }
        // ---- 业务键解析（SceneDiff.ParseBizKey）：详情页跟随 MIXLINE 用 ----
        static void BizKeyParse()
        {
            T.Group("业务键解析");
            T.Case("kind:key>kind:key 往返", delegate
            {
                var l = SceneDiff.ParseBizKey("mic:m1>speaker:s1");
                T.NotNull(l, "应解析成功");
                T.Eq("mic", l.sk, "输入类型");
                T.Eq("m1", l.sv, "输入 key");
                T.Eq("speaker", l.dk, "输出类型");
                T.Eq("s1", l.dv, "输出 key");
                T.Eq("mic:m1>speaker:s1", SceneDiff.Key(l), "往返一致");

                T.Null(SceneDiff.ParseBizKey(""), "空串返回 null");
                T.Null(SceneDiff.ParseBizKey("bad"), "无 > 返回 null");
                T.Null(SceneDiff.ParseBizKey("mic:m1>bad"), "输出无 : 返回 null");
            });
        }
        // ---- 系统默认输入/输出规则（SystemIO）与设备名匹配（SystemAudio）----
        static void SystemDeviceRules()
        {
            T.Group("系统默认设备规则");
            T.Case("唯一候选/多个候选/显式选择", delegate
            {
                string dir = System.IO.Path.Combine(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.ExecutablePath), "_test-tmp"), "sysio-" + Guid.NewGuid().ToString("N"));
                System.IO.Directory.CreateDirectory(dir);
                try
                {
                    var store = new Store(dir);
                    store.Names["spkA"] = "NVIDIA High Definition Audio · G72 Max";
                    store.Names["spkB"] = "HECATE G2 GAMING HEADSET · 扬声器";
                    store.Names["stA"] = "MIXLINE Stream · 麦克风";
                    store.Names["stB"] = "MIXLINE Record · 麦克风";
                    store.Names["micA"] = "HECATE G2 GAMING HEADSET · Microphone";

                    var one = new Scene { name = "one" };
                    one.links.Add(new SceneLink { sk = "mic", sv = "micA", dk = "speaker", dv = "spkA" });
                    one.links.Add(new SceneLink { sk = "mic", sv = "micA", dk = "stream", dv = "stA" });
                    T.True(SystemIO.EffectiveOut(one) != null && SystemIO.EffectiveOut(one).Key == "spkA", "唯一扬声器 → 默认输出");
                    T.True(SystemIO.EffectiveIn(one) != null && SystemIO.EffectiveIn(one).Key == "stA", "唯一 stream → 默认输入");
                    T.True(SystemIO.SummaryOut(one, store) == "G72 Max", "输出摘要用短名");

                    var two = new Scene { name = "two" };
                    two.links.Add(new SceneLink { sk = "mic", sv = "micA", dk = "speaker", dv = "spkA" });
                    two.links.Add(new SceneLink { sk = "mic", sv = "micA", dk = "speaker", dv = "spkB" });
                    two.links.Add(new SceneLink { sk = "mic", sv = "micA", dk = "stream", dv = "stA" });
                    two.links.Add(new SceneLink { sk = "mic", sv = "micA", dk = "stream", dv = "stB" });
                    T.True(SystemIO.EffectiveOut(two) == null, "多个扬声器 → 系统输出无");
                    T.True(SystemIO.EffectiveIn(two) == null, "多个 stream → 系统输入无");
                    T.Eq("无", SystemIO.SummaryOut(two, store), "多扬声器摘要为无");

                    two.sysOutNode = "speaker:spkB";
                    two.sysInNode = "stream:stA";
                    T.True(SystemIO.EffectiveOut(two).Key == "spkB", "显式选择输出优先");
                    T.True(SystemIO.EffectiveIn(two).Key == "stA", "显式选择输入优先");

                    two.sysInNode = "mic:micA";
                    T.True(SystemIO.EffectiveIn(two) != null && SystemIO.EffectiveIn(two).Key == "micA", "显式选择物理麦克风作为输入");
                    two.sysOutNode = "speaker:gone";
                    T.True(SystemIO.EffectiveOut(two) == null, "显式节点已不在场景 → 回退到多候选=无");
                }
                finally { try { System.IO.Directory.Delete(dir, true); } catch (Exception ex) { Log.Debug("清理测试目录失败", ex); } }
            });
            T.Case("设备名匹配打分", delegate
            {
                T.True(SystemAudio.MatchScore("NVIDIA High Definition Audio · G72 Max", "G72 Max (NVIDIA High Definition Audio)") >= 0.99, "型号与厂商顺序不同也能全匹配");
                T.True(SystemAudio.MatchScore("MIXLINE Stream · 麦克风", "麦克风 (MIXLINE Stream)") >= 0.99, "中文/英文混排匹配");
                T.True(SystemAudio.MatchScore("MIXLINE Stream · 麦克风", "麦克风 (MIXLINE Record)") < 0.8, "Stream 不应匹配到 Record");
            });
        }
        public static int RunAll()
        {
            KeyAndSets();
            Comparison();
            Lookup();
            Names();
            Snapshot();
            SwitchPlanning();
            Persistence();
            WaitUntilBackoff();
            DuplicateScenes();
            BizKeyParse();
            SystemDeviceRules();
            return T.Report();
        }
    }
}
