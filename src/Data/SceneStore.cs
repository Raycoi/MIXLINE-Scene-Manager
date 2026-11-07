// MIXLINE Scene Manager — Data/SceneStore.cs
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
    class Store
    {
        public string ConfigPath;
        readonly object gate = new object();
        readonly JavaScriptSerializer js = new JavaScriptSerializer();
        public List<Scene> Scenes = new List<Scene>();
        public Dictionary<string, bool> Mutes = new Dictionary<string, bool>();
        public Dictionary<string, string> Names = new Dictionary<string, string>();
        public Dictionary<string, string> ExePaths = new Dictionary<string, string>();
        /// <summary>用户给设备/应用起的名字（键为设备 key）。为空时回退到引擎名称。</summary>
        public Dictionary<string, string> Aliases = new Dictionary<string, string>();
        public string UiBackup = "";
        public bool HasWindow; public int WinX, WinY, WinW, WinH;
        public int IconSize = 26;          // 旧字段：仅为兼容旧配置保留，不再参与布局
        public int CardSize = 88;          // 列表行高（用户可调；两行系统输入/输出需要 ~88 才舒展）
        public bool LogEnabled = false;    // 日志总开关：默认关闭，避免长期占用空间
        public int LogMinLevel = 1;        // 最低记录等级：0=调试 1=信息 2=警告 3=错误

        public string LastSaveError;
        /// <summary>保存位置被自动迁移时给界面看的说明；正常保存为 null。</summary>
        public string LastSaveNote;
        readonly string exeDir;

        static string PickDir(string exeDir)
        {
            var cands = new List<string>();
            cands.Add(System.IO.Path.Combine(exeDir, "data"));
            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                if (!string.IsNullOrEmpty(appData))
                    cands.Add(System.IO.Path.Combine(appData, "MIXLINE Scene Manager"));
            }
            catch (Exception exIgnored1) { Log.Warn("SceneStore.cs: 忽略可恢复异常", exIgnored1); }
            cands.Add(exeDir);
            foreach (var d in cands)
            {
                try
                {
                    System.IO.Directory.CreateDirectory(d);
                    string probe = System.IO.Path.Combine(d, "_w.tmp");
                    System.IO.File.WriteAllText(probe, "x");
                    System.IO.File.Delete(probe);
                    return d;
                }
                catch (Exception exIgnored2) { Log.Warn("SceneStore.cs: 忽略可恢复异常", exIgnored2); }
            }
            return cands[0];
        }

        public Store(string exeDir)
        {
            this.exeDir = exeDir;
            Settings.Load(exeDir);
            // 旧版把配置放在 exe 同级，这里迁移到 MSM-data 里
            try
            {
                string legacy = System.IO.Path.Combine(exeDir, Settings.FileName);
                string now = System.IO.Path.Combine(Settings.ConfigDir, Settings.FileName);
                if (!System.IO.File.Exists(now) && System.IO.File.Exists(legacy))
                    System.IO.File.Move(legacy, now);
            }
            catch (Exception exIgnored3) { Log.Warn("SceneStore.cs: 忽略可恢复异常", exIgnored3); }
            Icons.Init(Settings.ConfigDir);
            ConfigPath = System.IO.Path.Combine(Settings.ConfigDir, Settings.FileName);
            Log.Init(Settings.ConfigDir);
            // 旧位置迁移：data\config.json 或同目录 config.json
            try
            {
                if (!System.IO.File.Exists(ConfigPath))
                {
                    string[] candidates = new string[]
                    {
                        System.IO.Path.Combine(exeDir, "data", "config.json"),
                        System.IO.Path.Combine(Settings.ConfigDir, "config.json"),
                        System.IO.Path.Combine(Settings.ConfigDir, "data", "config.json"),
                    };
                    foreach (var c in candidates)
                    {
                        if (!System.IO.File.Exists(c)) continue;
                        System.IO.File.Copy(c, ConfigPath, true);
                        break;
                    }
                }
            }
            catch (Exception exIgnored4) { Log.Warn("SceneStore.cs: 忽略可恢复异常", exIgnored4); }
            Load();
            Save();
        }

        public void Relocate(string dir)
        {
            System.IO.Directory.CreateDirectory(dir);
            ConfigPath = System.IO.Path.Combine(dir, Settings.FileName);
            Settings.ConfigDir = dir;
            Settings.Save();
            Load();
            Save();
        }

        string Dir() { return System.IO.Path.GetDirectoryName(ConfigPath); }
        string Q(string x)
        {
            if (x == null) x = "";
            var b = new StringBuilder("\"");
            foreach (var c in x)
            {
                if (c == '"' || c == '\\') b.Append('\\').Append(c);
                else if (c == '\n') b.Append("\\n");
                else if (c == '\r') b.Append("\\r");
                else if (c == '\t') b.Append("\\t");
                else if (c < ' ') b.Append("\\u").Append(((int)c).ToString("x4"));
                else b.Append(c);
            }
            return b.Append('"').ToString();
        }

        // ---------------- 读取 ----------------
        public void Load()
        {
            lock (gate)
            {
            Scenes.Clear(); Mutes.Clear(); Names.Clear(); ExePaths.Clear(); Aliases.Clear(); UiBackup = "";
            try
            {
                if (System.IO.File.Exists(ConfigPath))
                {
                    Parse(System.IO.File.ReadAllText(ConfigPath).TrimStart('\uFEFF'));
                    return;
                }
            }
            catch (Exception exIgnored5) { Log.Warn("SceneStore.cs: 忽略可恢复异常", exIgnored5); }
            MigrateOld();
            }
        }

        static string S(Dictionary<string, object> d, string k) { object v; return d != null && d.TryGetValue(k, out v) && v != null ? "" + v : ""; }
        static double D(Dictionary<string, object> d, string k) { object v; if (d != null && d.TryGetValue(k, out v) && v != null) { try { return Convert.ToDouble(v); } catch (Exception exIgnored6) { Log.Warn("SceneStore.cs: 忽略可恢复异常", exIgnored6); } } return 0; }
        static bool Bo(Dictionary<string, object> d, string k) { object v; return d != null && d.TryGetValue(k, out v) && v is bool && (bool)v; }

        void Parse(string txt)
        {
            var root = (Dictionary<string, object>)js.DeserializeObject(txt);
            if (root == null) return;
            object o;

            if (root.TryGetValue("window", out o) && o is Dictionary<string, object>)
            {
                var w = (Dictionary<string, object>)o;
                WinX = (int)D(w, "x"); WinY = (int)D(w, "y");
                WinW = (int)D(w, "w"); WinH = (int)D(w, "h");
                HasWindow = WinW > 0 && WinH > 0;
            }
            if (root.TryGetValue("uiBackup", out o) && o != null) UiBackup = "" + o;
            if (root.TryGetValue("iconSize", out o) && o != null) { int iv = 0; int.TryParse("" + o, out iv); if (iv >= 12 && iv <= 64) IconSize = iv; }
            if (root.TryGetValue("cardSize", out o) && o != null) { int cv = 0; int.TryParse("" + o, out cv); if (cv >= 56 && cv <= 120) CardSize = cv; }   // 旧配置里是卡片边长(>120)，自动回落到默认行高
            if (root.TryGetValue("logEnabled", out o) && o != null) LogEnabled = (o is bool) ? (bool)o : ("" + o == "1" || "" + o == "true");
            if (root.TryGetValue("logLevel", out o) && o != null) { int lv = 1; int.TryParse("" + o, out lv); if (lv >= 0 && lv <= 3) LogMinLevel = lv; }
            if (root.TryGetValue("aliases", out o) && o is Dictionary<string, object>)
                foreach (var kv in (Dictionary<string, object>)o)
                    Aliases[kv.Key] = "" + kv.Value;
            if (root.TryGetValue("exePaths", out o) && o is Dictionary<string, object>)
                foreach (var kv in (Dictionary<string, object>)o)
                    ExePaths[kv.Key] = "" + kv.Value;
            if (root.TryGetValue("names", out o) && o is Dictionary<string, object>)
                foreach (var kv in (Dictionary<string, object>)o)
                    Names[kv.Key] = "" + kv.Value;
            if (root.TryGetValue("mutes", out o) && o is Dictionary<string, object>)
                foreach (var kv in (Dictionary<string, object>)o)
                    Mutes[kv.Key] = kv.Value is bool ? (bool)kv.Value : ("" + kv.Value == "1");

            if (root.TryGetValue("scenes", out o) && o is object[])
            {
                foreach (var so in (object[])o)
                {
                    var d = (Dictionary<string, object>)so;
                    var sc = new Scene { id = S(d, "id"), name = S(d, "name"), createdAt = S(d, "createdAt"), additive = Bo(d, "additive") };
                    sc.sysInNode = S(d, "sysInNode"); sc.sysInId = S(d, "sysInId"); sc.sysInName = S(d, "sysInName");
                    sc.sysOutNode = S(d, "sysOutNode"); sc.sysOutId = S(d, "sysOutId"); sc.sysOutName = S(d, "sysOutName");
                    ReadLinks(d, "links", sc.links);
                    ReadLinks(d, "outputs", sc.outs);
                    Scenes.Add(sc);
                }
            }
        }

        static void ReadLinks(Dictionary<string, object> d, string key, List<SceneLink> into)
        {
            object arr;
            if (d == null || !d.TryGetValue(key, out arr) || arr == null) return;
            foreach (var o in (object[])arr)
            {
                var x = (Dictionary<string, object>)o;
                var sl = new SceneLink();
                object v;
                if (x.TryGetValue("sk", out v) && v != null && ("" + v).Length > 0)
                {
                    sl.sk = S(x, "sk"); sl.sv = S(x, "sv"); sl.dk = S(x, "dk"); sl.dv = S(x, "dv");
                }
                else
                {
                    object src, dst;
                    if (x.TryGetValue("src", out src) && src != null)
                    { var s2 = (Dictionary<string, object>)src; sl.sk = S(s2, "kind"); sl.sv = S(s2, "key"); }
                    if (x.TryGetValue("dst", out dst) && dst != null)
                    { var d2 = (Dictionary<string, object>)dst; sl.dk = S(d2, "kind"); sl.dv = S(d2, "key"); }
                }
                sl.level = (float)D(x, "level");
                if (sl.level == 0f) sl.level = (float)D(x, "volume");
                if (sl.level == 0f) sl.level = 1f;
                sl.muted = Bo(x, "muted");
                into.Add(sl);
            }
        }

        // 旧版散文件迁移（scenes.json / mutes.txt / ui.json / ui-backup.txt）
        void MigrateOld()
        {
            bool any = false;
            try
            {
                string dd = Dir();
                string oldScenes = System.IO.Path.Combine(dd, "scenes.json");
                if (System.IO.File.Exists(oldScenes))
                {
                    Parse(System.IO.File.ReadAllText(oldScenes).TrimStart('\uFEFF'));
                    any = true;
                }
                string oldMutes = System.IO.Path.Combine(dd, "mutes.txt");
                if (System.IO.File.Exists(oldMutes))
                {
                    foreach (var line in System.IO.File.ReadAllLines(oldMutes))
                    {
                        int i = line.LastIndexOf('=');
                        if (i <= 0) continue;
                        Mutes[line.Substring(0, i)] = line.Substring(i + 1) == "1";
                    }
                    any = true;
                }
                string oldUi = System.IO.Path.Combine(dd, "ui.json");
                if (System.IO.File.Exists(oldUi))
                {
                    var t = System.IO.File.ReadAllText(oldUi).Split(',');
                    if (t.Length >= 2) { WinX = int.Parse(t[0]); WinY = int.Parse(t[1]); }
                    WinW = t.Length > 2 ? int.Parse(t[2]) : 564;
                    WinH = t.Length > 3 ? int.Parse(t[3]) : 326;
                    HasWindow = true;
                    any = true;
                }
                string oldBak = System.IO.Path.Combine(dd, "ui-backup.txt");
                if (System.IO.File.Exists(oldBak)) { UiBackup = System.IO.File.ReadAllText(oldBak).Trim(); any = true; }

                if (any)
                {
                    Save();
                    try { System.IO.File.Delete(oldScenes); } catch (Exception exIgnored7) { Log.Warn("SceneStore.cs: 忽略可恢复异常", exIgnored7); }
                    try { System.IO.File.Delete(oldMutes); } catch (Exception exIgnored8) { Log.Warn("SceneStore.cs: 忽略可恢复异常", exIgnored8); }
                    try { System.IO.File.Delete(oldUi); } catch (Exception exIgnored9) { Log.Warn("SceneStore.cs: 忽略可恢复异常", exIgnored9); }
                    try { System.IO.File.Delete(oldBak); } catch (Exception exIgnored10) { Log.Warn("SceneStore.cs: 忽略可恢复异常", exIgnored10); }
                }
            }
            catch (Exception exIgnored11) { Log.Warn("SceneStore.cs: 忽略可恢复异常", exIgnored11); }
        }

        // ---------------- 写入（嵌套结构） ----------------
        public void Save()
        {
            lock (gate)
            {
            try
            {
                var sb = new StringBuilder();
                sb.Append("{\n  \"version\": 1,\n");
                sb.Append("  \"window\": { \"x\": ").Append(WinX).Append(", \"y\": ").Append(WinY)
                  .Append(", \"w\": ").Append(WinW).Append(", \"h\": ").Append(WinH).Append(" },\n");
                sb.Append("  \"uiBackup\": ").Append(Q(UiBackup)).Append(",\n");
                sb.Append("  \"iconSize\": ").Append(IconSize).Append(",\n");
            sb.Append("  \"cardSize\": ").Append(CardSize).Append(",\n");
            sb.Append("  \"logEnabled\": ").Append(LogEnabled ? "true" : "false").Append(",\n");
            sb.Append("  \"logLevel\": ").Append(LogMinLevel).Append(",\n");

                sb.Append("  \"aliases\": {");
                bool firstAlias = true;
                foreach (var kv in Aliases)
                {
                    if (!firstAlias) sb.Append(",");
                    firstAlias = false;
                    sb.Append("\n    ").Append(Q(kv.Key)).Append(": ").Append(Q(kv.Value));
                }
                sb.Append(firstAlias ? "}" : "\n  }").Append(",\n");

                sb.Append("  \"exePaths\": {");
                bool firstE = true;
                foreach (var kv in ExePaths)
                {
                    if (!firstE) sb.Append(",");
                    firstE = false;
                    sb.Append("\n    ").Append(Q(kv.Key)).Append(": ").Append(Q(kv.Value));
                }
                sb.Append(firstE ? "}" : "\n  }").Append(",\n");

                sb.Append("  \"names\": {");
                bool firstN = true;
                foreach (var kv in Names)
                {
                    if (!firstN) sb.Append(",");
                    firstN = false;
                    sb.Append("\n    ").Append(Q(kv.Key)).Append(": ").Append(Q(kv.Value));
                }
                sb.Append(firstN ? "}" : "\n  }").Append(",\n");

                sb.Append("  \"mutes\": {");
                bool firstM = true;
                foreach (var kv in Mutes)
                {
                    if (!firstM) sb.Append(",");
                    firstM = false;
                    sb.Append("\n    ").Append(Q(kv.Key)).Append(": ").Append(kv.Value ? "true" : "false");
                }
                sb.Append(firstM ? "}" : "\n  }").Append(",\n");

                sb.Append("  \"scenes\": [");
                for (int i = 0; i < Scenes.Count; i++)
                {
                    var sc = Scenes[i];
                    if (i > 0) sb.Append(",");
                    sb.Append("\n    {\n");
                    sb.Append("      \"id\": ").Append(Q(sc.id)).Append(",\n");
                    sb.Append("      \"name\": ").Append(Q(sc.name)).Append(",\n");
                    sb.Append("      \"createdAt\": ").Append(Q(sc.createdAt)).Append(",\n");
                    sb.Append("      \"additive\": ").Append(sc.additive ? "true" : "false").Append(",\n");
                    sb.Append("      \"sysInNode\": ").Append(Q(sc.sysInNode)).Append(",\n");
                    sb.Append("      \"sysInId\": ").Append(Q(sc.sysInId)).Append(",\n");
                    sb.Append("      \"sysInName\": ").Append(Q(sc.sysInName)).Append(",\n");
                    sb.Append("      \"sysOutNode\": ").Append(Q(sc.sysOutNode)).Append(",\n");
                    sb.Append("      \"sysOutId\": ").Append(Q(sc.sysOutId)).Append(",\n");
                    sb.Append("      \"sysOutName\": ").Append(Q(sc.sysOutName)).Append(",\n");
                    sb.Append("      \"links\": ").Append(LinksJson(sc.links, "      ")).Append(",\n");
                    sb.Append("      \"outputs\": ").Append(LinksJson(sc.outs, "      ")).Append("\n");
                    sb.Append("    }");
                }
                sb.Append(Scenes.Count > 0 ? "\n  ]\n}\n" : "]\n}\n");

                string dir = System.IO.Path.GetDirectoryName(ConfigPath);
                if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
                // 覆盖前先备份上一版，防止误删/写坏后无法回滚
                try
                {
                    if (System.IO.File.Exists(ConfigPath))
                        System.IO.File.Copy(ConfigPath, ConfigPath + ".bak", true);
                }
                catch (Exception exIgnored12) { Log.Warn("SceneStore.cs: 忽略可恢复异常", exIgnored12); }

                string json = sb.ToString();
                LastSaveNote = null;
                try
                {
                    ClearReadOnly(ConfigPath);
                    ClearReadOnly(ConfigPath + ".bak");
                    System.IO.File.WriteAllText(ConfigPath, json, new UTF8Encoding(false));
                    LastSaveError = null;
                }
                catch (Exception exFirst)
                {
                    // 旧安装目录/旧配置文件可能是只读的：自动改存到当前用户可写目录，避免改名等操作静默丢失
                    if (TryMigrateConfig(json, exFirst))
                    {
                        LastSaveError = null;
                    }
                    else
                    {
                        LastSaveError = FriendlyError(exFirst);
                        Log.Error("保存配置失败(" + ConfigPath + ")", exFirst);
                    }
                }
            }
            catch (Exception ex) { LastSaveError = FriendlyError(ex); Log.Error("保存配置失败(" + ConfigPath + ")", ex); }
            }
        }

        static void ClearReadOnly(string path)
        {
            try
            {
                if (!System.IO.File.Exists(path)) return;
                var attrs = System.IO.File.GetAttributes(path);
                if ((attrs & System.IO.FileAttributes.ReadOnly) != 0)
                    System.IO.File.SetAttributes(path, attrs & ~System.IO.FileAttributes.ReadOnly);
            }
            catch (Exception exIgnored) { Log.Warn("SceneStore.cs: 清理只读属性失败", exIgnored); }
        }

        static string FriendlyError(Exception ex)
        {
            if (ex is UnauthorizedAccessException) return "没有写入权限（旧安装目录或配置文件被保护）";
            if (ex is System.IO.DirectoryNotFoundException) return "配置目录不存在";
            if (ex is System.IO.IOException) return "文件被占用或只读";
            string m = ex.Message ?? "未知错误";
            return m.Length > 60 ? m.Substring(0, 60) + "…" : m;
        }

        /// <summary>配置目录/文件不可写时，把当前内容改存到用户可写目录，并记住新位置。</summary>
        bool TryMigrateConfig(string json, Exception first)
        {
            var cands = new List<string>();
            string app = Settings.AppDataDir();
            if (!string.IsNullOrEmpty(app)) cands.Add(app);
            try
            {
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (!string.IsNullOrEmpty(local)) cands.Add(System.IO.Path.Combine(local, "MIXLINE Scene Manager", "MSM-data"));
            }
            catch (Exception exIgnored) { Log.Warn("SceneStore.cs: 取 LocalAppData 失败", exIgnored); }
            if (!string.IsNullOrEmpty(exeDir)) cands.Add(System.IO.Path.Combine(exeDir, "data"));

            string curDir = System.IO.Path.GetDirectoryName(ConfigPath);
            foreach (var d in cands)
            {
                if (string.IsNullOrEmpty(d)) continue;
                if (string.Equals(d, curDir, StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    System.IO.Directory.CreateDirectory(d);
                    string probe = System.IO.Path.Combine(d, "_w.tmp");
                    System.IO.File.WriteAllText(probe, "x");
                    System.IO.File.Delete(probe);

                    string target = System.IO.Path.Combine(d, Settings.FileName);
                    System.IO.File.WriteAllText(target, json, new UTF8Encoding(false));
                    ConfigPath = target;
                    Settings.ConfigDir = d;
                    Settings.Save();
                    try { Log.Init(d); } catch (Exception exIgnored2) { Log.Warn("SceneStore.cs: 重新初始化日志失败", exIgnored2); }
                    try { Icons.Init(d); } catch (Exception exIgnored3) { Log.Warn("SceneStore.cs: 重新初始化图标缓存失败", exIgnored3); }
                    LastSaveNote = "配置目录不可写，已自动改存到 " + d;
                    Log.Warn("配置目录不可写，已迁移到：" + d + "（原错误：" + FriendlyError(first) + "）");
                    return true;
                }
                catch (Exception exIgnored4) { Log.Warn("SceneStore.cs: 候选配置目录不可写 " + d, exIgnored4); }
            }
            return false;
        }

        string LinksJson(List<SceneLink> list, string indent)
        {
            if (list.Count == 0) return "[]";
            var sb = new StringBuilder();
            sb.Append("[");
            for (int i = 0; i < list.Count; i++)
            {
                var l = list[i];
                if (i > 0) sb.Append(",");
                sb.Append("\n").Append(indent).Append("  { ");
                sb.Append("\"src\": { \"kind\": ").Append(Q(l.sk)).Append(", \"key\": ").Append(Q(l.sv)).Append(" }, ");
                sb.Append("\"dst\": { \"kind\": ").Append(Q(l.dk)).Append(", \"key\": ").Append(Q(l.dv)).Append(" }, ");
                sb.Append("\"level\": ").Append(l.level.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)).Append(", ");
                sb.Append("\"muted\": ").Append(l.muted ? "true" : "false");
                sb.Append(" }");
            }
            sb.Append("\n").Append(indent).Append("]");
            return sb.ToString();
        }

        public void SetWindow(Point loc, Size size)
        {
            WinX = loc.X; WinY = loc.Y; WinW = size.Width; WinH = size.Height; HasWindow = true;
        }

        // ---------------- 场景 ----------------
        /// <summary>设置设备别名；传入空字符串表示恢复默认名称。</summary>
        public void SetAlias(string key, string alias)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (string.IsNullOrEmpty(alias)) Aliases.Remove(key);
            else Aliases[key] = alias.Trim();
            Save();
        }

        /// <summary>取设备别名（没有则返回 null）。</summary>
        public string AliasOf(string key)
        {
            string v;
            if (!string.IsNullOrEmpty(key) && Aliases.TryGetValue(key, out v) && !string.IsNullOrEmpty(v)) return v;
            return null;
        }

        public Scene FindScene(string id)
        {
            foreach (var x in Scenes) if (x.id == id) return x;
            return null;
        }

        public static string Key(SceneLink l) { return l.sk + ":" + l.sv + ">" + l.dk + ":" + l.dv; }
    }
}

