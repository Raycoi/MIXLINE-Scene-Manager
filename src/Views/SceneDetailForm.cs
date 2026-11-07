// MIXLINE Scene Manager — Views/SceneDetailForm.cs
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
    class SceneDetail : Form
    {
        class Chip { public Rectangle Rect; public bool IsInput; public string Kind; public string Key; public bool New; }
        readonly Store store; readonly Scene sc;
        readonly List<Chip> chips = new List<Chip>();
        readonly List<PointF[]> curves = new List<PointF[]>();
        readonly List<Color> curveCols = new List<Color>();
        Chip pressIn; Point pressPt; bool dragging; Point dragPt; Chip hover;
        int hoverCurveIdx = -1;
        string hoverFullName;                      // 悬停设备时在底部显示完整名称
        static int BarH { get { return Dpi.S(44); } }
        int scrollY, maxScroll;
        static readonly Font FBar = new Font("Microsoft YaHei UI", 9.5f);
        static readonly Font FBarBold = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold);
        static readonly Font FChipTitle = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold);
        bool hoverSave, hoverDupOk, hoverDupCancel;
        // 跟随 MIXLINE：打开时只记基线，之后 MIXLINE 真有变化才把"新增"合并进草稿
        readonly HashSet<string> newKeys = new HashSet<string>();     // kind|key
        readonly HashSet<string> newLinks = new HashSet<string>();    // bizKey
        readonly HashSet<string> removedLinks = new HashSet<string>();// MIXLINE 里已删除、待确认同步的连线
        readonly HashSet<string> removedKeys = new HashSet<string>(); // 对应设备 kind|key
        Rectangle syncDelRect, dismissRect; bool hoverSyncDel, hoverDismiss;
        HashSet<string> liveSnap;
        string followNote; int followAdded;
        System.Windows.Forms.Timer liveTimer;
        bool dirty;                                   // 有未保存的修改
        Scene dupScene;                               // 保存时发现与之重复的场景（非空=待合并）
        bool dupSecond;                               // 合并的二次确认
        bool dupArmed; Point dupGate;                 // 防连点：必须移动鼠标后才能点「合并 / 确定覆盖」
        Point lastDown;                               // 最近一次按下位置（用来判断"有没有移动过"）
        Rectangle saveRect, dupOkRect, dupCancelRect; // 底部按钮命中区
        TextBox aliasBox; Chip aliasTarget;              // 行内重命名设备（别名）
        public bool Changed;

        public SceneDetail(SceneService service, Scene scene)
        {
            store = service.Data; sc = scene;
            Text = scene.name + " — 路由详情";
            FormBorderStyle = FormBorderStyle.Sizable; StartPosition = FormStartPosition.CenterParent;
            BackColor = C.Bg; ClientSize = new Size(Dpi.S(900), Dpi.S(620)); MinimumSize = new Size(Dpi.S(640), Dpi.S(420));
            DoubleBuffered = true;
            AutoScaleMode = AutoScaleMode.None;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Paint += delegate (object s, PaintEventArgs pe) { Draw(pe.Graphics); };
            aliasBox = new TextBox
            {
                BorderStyle = BorderStyle.None,
                BackColor = C.Sunken,
                ForeColor = C.TextPrimary,
                Font = T.MicroSmall,
                Visible = false,
                AutoSize = false
            };
            aliasBox.KeyDown += delegate (object a, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; BeginInvoke(new Action(CommitAlias)); }
                else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; CancelAlias(); }
            };
            // 点到输入框外面时默认保存（比"点外面=丢弃"更符合直觉）；点在「取消」上则取消
            aliasBox.Leave += delegate
            {
                if (aliasTarget == null) return;
                Point cp = PointToClient(Cursor.Position);
                if (AliasCancelRect(aliasTarget.Rect).Contains(cp)) { CancelAlias(); return; }
                CommitAlias();
            };
            Controls.Add(aliasBox);

            liveTimer = new System.Windows.Forms.Timer { Interval = 1200 };
            liveTimer.Tick += delegate { LiveSyncTick(); };
            liveTimer.Start();

            statusTimer = new System.Windows.Forms.Timer { Interval = 250 };
            statusTimer.Tick += delegate
            {
                if (status == null) { statusTimer.Stop(); return; }
                if ((DateTime.UtcNow - statusAt).TotalSeconds < 3) return;
                status = null; statusTimer.Stop(); Invalidate();
            };
            statusTimer.Start();

            MouseWheel += delegate (object a, MouseEventArgs e)
            {
                if (maxScroll <= 0) return;
                scrollY = Math.Max(0, Math.Min(maxScroll, scrollY - e.Delta / 4));
                Invalidate();
            };
        }

        void BuildLayout(out List<string> inK, out List<string> inKind, out List<string> outK, out List<string> outKind,
                    out int chipW, out int chipH, out int step, out int startY)
        {
            inK = new List<string>(); inKind = new List<string>();
            outK = new List<string>(); outKind = new List<string>();
            foreach (var l in sc.links)
            {
                string a = l.sk + "|" + l.sv, d = l.dk + "|" + l.dv;
                if (!inK.Contains(a)) { inK.Add(a); inKind.Add(l.sk); }
                if (!outK.Contains(d)) { outK.Add(d); outKind.Add(l.dk); }
            }
            int n = Math.Max(inK.Count, outK.Count);
            int top = Dpi.S(48);
            int availH = ClientSize.Height - top - BarH - Dpi.S(10);
            int colW = (ClientSize.Width - Dpi.S(56)) / 2;
            chipW = Math.Min(Math.Max(Dpi.S(180), colW), Dpi.S(340));   // 大芯片：图标 + 主名称 + 全名称
            chipH = Dpi.S(92);
            step = n <= 1 ? 0 : chipH + Dpi.S(14);
            int totalH = n <= 1 ? chipH : (n - 1) * step + chipH;
            if (totalH <= availH)
            {
                // 轻微偏上：避免窗口大而节点少时，标题和画布之间空出一大块
                startY = top + (int)((availH - totalH) * 0.18);
                maxScroll = 0;
            }
            else { startY = top - scrollY; maxScroll = totalH - availH; }
        }

        void Draw(Graphics g)
        {
            chips.Clear(); curves.Clear(); curveCols.Clear();
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var b = new SolidBrush(C.Bg)) g.FillRectangle(b, ClientRectangle);
                List<string> inK, inKind, outK, outKind; int chipW, chipH, step, startY;
                BuildLayout(out inK, out inKind, out outK, out outKind, out chipW, out chipH, out step, out startY);
                int lx = Dpi.S(28), rx2 = ClientSize.Width - Dpi.S(28) - chipW;
                int x0 = lx + chipW + Dpi.S(6), x3 = rx2 - Dpi.S(6);

                // 顶部：场景名 · 路数 · 当前生效（紧凑一行，不留大块空白）
                int hx = Dpi.S(24), hy = Dpi.S(12);
                float nameW;
                using (var b = new SolidBrush(C.TextPrimary))
                {
                    g.DrawString(sc.name, T.CardTitle, b, hx, hy);
                    nameW = g.MeasureString(sc.name, T.CardTitle).Width;
                }
                float hcountX = hx + nameW + Dpi.S(12);
                using (var b = new SolidBrush(C.TextTertiary))
                    g.DrawString(sc.links.Count + " 路", T.Caption, b, hcountX, hy + Dpi.S(6));
                hcountX += g.MeasureString(sc.links.Count + " 路", T.Caption).Width + Dpi.S(14);
                if (sc.match)
                {
                    string tag = "当前生效";
                    var tsz = g.MeasureString(tag, T.Micro);
                    var pr = new Rectangle((int)hcountX, hy + Dpi.S(3), (int)tsz.Width + Dpi.S(18), Dpi.S(21));
                    using (var b = new SolidBrush(C.Fade(C.Success, 30)))
                    using (var pa = Rd.Path(pr, Dpi.S(10))) g.FillPath(b, pa);
                    using (var b = new SolidBrush(C.Success))
                    {
                        var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                        g.DrawString(tag, T.Micro, b, new RectangleF(pr.X, pr.Y, pr.Width, pr.Height), sf);
                    }
                }

                // 连线
                var contentClip = new Rectangle(0, Dpi.S(44), ClientSize.Width, Math.Max(Dpi.S(10), ClientSize.Height - Dpi.S(44) - BarH));
                g.SetClip(contentClip);
                foreach (var l in sc.links)
                {
                    int ai = inK.IndexOf(l.sk + "|" + l.sv), bi = outK.IndexOf(l.dk + "|" + l.dv);
                    if (ai < 0 || bi < 0) continue;
                    int ay = startY + ai * step + chipH / 2;
                    int by = startY + bi * step + chipH / 2;
                    string bk = BizKey(l);
                    bool isNewLink = newLinks.Contains(bk);
                    bool isRemovedLink = removedLinks.Contains(bk);
                    bool related = hover != null && (hover.IsInput
                        ? (l.sk == hover.Kind && l.sv == hover.Key)
                        : (l.dk == hover.Kind && l.dv == hover.Key));
                    Color raw = isRemovedLink ? C.Danger : (isNewLink ? C.Success : CurveColors.For(ai));
                    Color col;
                    float w;
                    if (isRemovedLink || isNewLink) { col = raw; w = 2.0f; }
                    else if (hover != null)
                    {
                        col = related ? C.Accent : Color.FromArgb(48, raw);
                        w = related ? 2.4f : 1.4f;
                    }
                    else { col = Mix(raw, C.Bg, 0.28f); w = 1.6f; }

                    using (var pen = new Pen(col, w))
                    using (var bb = new SolidBrush(col))
                    {
                        if (isRemovedLink || isNewLink) { pen.DashStyle = DashStyle.Dash; pen.DashPattern = new float[] { 3.2f, 2.2f }; }
                        float cx1 = x0 + (x3 - x0) * 0.45f, cx2 = x0 + (x3 - x0) * 0.55f;
                        g.DrawBezier(pen, x0, ay, cx1, ay, cx2, by, x3, by);
                        int dr = related ? 3 : 2;
                        g.FillEllipse(bb, x0 - dr, ay - dr, dr * 2, dr * 2);
                        g.FillEllipse(bb, x3 - dr, by - dr, dr * 2, dr * 2);
                    }
                    curves.Add(RoutingPaint.Bezier(new Point(x0, ay), new Point(x3, by), 8));
                    curveCols.Add(col);
                }

                for (int k = 0; k < inK.Count; k++)
                    DrawChip(g, new Rectangle(lx, startY + k * step, chipW, chipH), true, inKind[k], KeyOf(inK[k]),
                        hover != null && hover.IsInput && hover.Key == KeyOf(inK[k]));
                for (int k = 0; k < outK.Count; k++)
                    DrawChip(g, new Rectangle(rx2, startY + k * step, chipW, chipH), false, outKind[k], KeyOf(outK[k]),
                        hover != null && !hover.IsInput && hover.Key == KeyOf(outK[k]));

                // 拖动预览：与主界面同一套反馈（贝塞尔虚线 + 双端圆点 + 合法目标描边）
                if (dragging && pressIn != null)
                {
                    var a = new Point(pressIn.Rect.Right - 2, pressIn.Rect.Top + pressIn.Rect.Height / 2);
                    bool ok = hover != null && !hover.IsInput;
                    RoutingPaint.DrawDragCurve(g, a, dragPt, ok);
                    if (ok) RoutingPaint.DrawDropTarget(g, hover.Rect);
                }
                // 曲线悬停高亮（与主界面一致）
                if (hoverCurveIdx >= 0 && hoverCurveIdx < curves.Count)
                    RoutingPaint.DrawCurveHighlight(g, curves[hoverCurveIdx], curveCols[hoverCurveIdx]);

                g.ResetClip();
                if (aliasTarget != null) DrawAliasEdit(g, aliasTarget.Rect);
                DrawScrollBar(g);
                DrawBottomBar(g);
            }
            }

        bool discardArmed;
        string status; DateTime statusAt = DateTime.MinValue;   // 底部状态提示（替代一闪而过的弹窗）
        System.Windows.Forms.Timer statusTimer;

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            Dpi.Set((int)Math.Round(e.DeviceDpiNew / 96.0 * 100));
            MinimumSize = new Size(Dpi.S(640), Dpi.S(420));
            ClientSize = new Size(Dpi.S(900), Dpi.S(620));
            scrollY = 0;
            Invalidate();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (liveTimer != null) liveTimer.Stop();
            if (statusTimer != null) statusTimer.Stop();
            base.OnFormClosed(e);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (dirty && !discardArmed)
            {
                discardArmed = true;
                e.Cancel = true;
                Invalidate();
                return;
            }
            base.OnFormClosing(e);
        }
        string KeyOf(string kv) { int i = kv.IndexOf('|'); return i < 0 ? kv : kv.Substring(i + 1); }

        string FullNameOf(Chip c)
        {
            string full = DeviceNames.Display(store.Names, c.Kind, c.Key);
            string alias = store.AliasOf(c.Key);
            return string.IsNullOrEmpty(alias) ? full : alias + "  ·  " + full;
        }

        // ==================== 保存 / 重复合并（行内，不弹窗） ====================
        void DrawBottomBar(Graphics g)
        {
            int y = ClientSize.Height - BarH;
            using (var b = new SolidBrush(C.Bg)) g.FillRectangle(b, 0, y, ClientSize.Width, BarH);
            using (var pen = new Pen(C.BorderSubtle)) g.DrawLine(pen, 0, y, ClientSize.Width, y);

            int bh = Dpi.S(30), by = y + (BarH - bh) / 2;
            saveRect = dupOkRect = dupCancelRect = Rectangle.Empty;

            if (dupScene != null)
            {
                string msg = dupSecond
                    ? "确定用「" + sc.name + "」覆盖「" + dupScene.name + "」？"
                    : "此场景与「" + dupScene.name + "」完全相同";
                using (var b = new SolidBrush(dupSecond ? C.Warning : C.Danger))
                    g.DrawString(msg, FBar, b, Dpi.S(18), by + Dpi.S(8));

                string okText = dupSecond ? "确定覆盖" : "合并";
                var szOk = g.MeasureString(okText, FBarBold);
                var szNo = g.MeasureString("取消", FBarBold);
                dupCancelRect = new Rectangle(ClientSize.Width - Dpi.S(18) - ((int)szNo.Width + Dpi.S(32)), by, (int)szNo.Width + Dpi.S(32), bh);
                dupOkRect = new Rectangle(dupCancelRect.X - Dpi.S(10) - ((int)szOk.Width + Dpi.S(32)), by, (int)szOk.Width + Dpi.S(32), bh);
                DrawBarButton(g, dupOkRect, okText, true, C.Danger, hoverDupOk);
                DrawBarButton(g, dupCancelRect, "取消", false, C.TextPrimary, hoverDupCancel);
                return;
            }

            if (removedLinks.Count > 0)
            {
                using (var b = new SolidBrush(C.Danger))
                    g.DrawString("MIXLINE 少了 " + removedLinks.Count + " 条连线（红虚线）", FBar, b, 18, by + 8);

                string syncText = "同步删除";
                var szSync = g.MeasureString(syncText, FBarBold);
                var szIgn = g.MeasureString("忽略", FBarBold);
                saveRect = new Rectangle(ClientSize.Width - Dpi.S(18) - ((int)g.MeasureString("保存此场景", FBarBold).Width + Dpi.S(36)), by,
                                         (int)g.MeasureString("保存此场景", FBarBold).Width + Dpi.S(36), bh);
                int rx = saveRect.X - Dpi.S(12);
                syncDelRect = new Rectangle(rx - ((int)szSync.Width + Dpi.S(32)), by, (int)szSync.Width + Dpi.S(32), bh);
                rx = syncDelRect.X - Dpi.S(8);
                dismissRect = new Rectangle(rx - ((int)szIgn.Width + Dpi.S(28)), by, (int)szIgn.Width + Dpi.S(28), bh);

                DrawBarButton(g, syncDelRect, syncText, true, C.Danger, hoverSyncDel);
                DrawBarButton(g, dismissRect, "忽略", false, C.BorderStrong, hoverDismiss, C.TextPrimary);
                DrawBarButton(g, saveRect, "保存此场景", true, C.Accent, hoverSave);
                return;
            }

            string hint = dirty ? "有未保存的修改：编辑后点右侧「保存此场景」"
                                : "拖动图标到右侧 = 添加连线；右键图标/连线 = 移除";
            if (status != null) hint = status;
            else if (!string.IsNullOrEmpty(hoverFullName)) hint = hoverFullName + "    ·    点右下角铅笔可改名";
            using (var b = new SolidBrush(followNote != null && status == null ? C.Success : C.TextSecondary))
            {
                var sf = new StringFormat { FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter };
                g.DrawString(hint, FBar, b, new RectangleF(Dpi.S(18), by + (bh - FBar.Height) / 2, ClientSize.Width - Dpi.S(18) - Dpi.S(210), bh), sf);
            }

            string saveText = "保存此场景";
            var szs = g.MeasureString(saveText, FBarBold);
            saveRect = new Rectangle(ClientSize.Width - Dpi.S(18) - ((int)szs.Width + Dpi.S(36)), by, (int)szs.Width + Dpi.S(36), bh);
            DrawBarButton(g, saveRect, saveText, true, C.Accent, hoverSave);
        }

        void DrawBarButton(Graphics g, Rectangle r, string text, bool filled, Color color, bool hot)
        {
            DrawBarButton(g, r, text, filled, color, hot, Color.White);
        }

        void DrawBarButton(Graphics g, Rectangle r, string text, bool filled, Color color, bool hot, Color textColor)
        {
            if (filled)
            {
                using (var b = new SolidBrush(hot ? ControlPaint.Light(color, 0.18f) : color))
                using (var pa = Rd.Path(r, Rad.Button)) g.FillPath(b, pa);
            }
            else
            {
                using (var b = new SolidBrush(hot ? C.CardHover : C.Card))
                using (var pa = Rd.Path(r, Rad.Button)) g.FillPath(b, pa);
                using (var pen = new Pen(hot ? C.Accent : color, 1.4f))
                using (var pa = Rd.Path(r, Rad.Button)) g.DrawPath(pen, pa);
            }
            using (var b = new SolidBrush(textColor))
            {
                var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString(text, FBarBold, b, new RectangleF(r.X, r.Y, r.Width, r.Height), sf);
            }
        }

        Scene FindDuplicate() { return SceneDiff.FindDuplicate(store.Scenes, sc); }

        string BizKey(SceneLink l) { return l.sk + ":" + l.sv + ">" + l.dk + ":" + l.dv; }

        bool DraftHas(string bizKey)
        {
            foreach (var l in sc.links) if (BizKey(l) == bizKey) return true;
            return false;
        }

        /// <summary>草稿里是否还有使用该设备（kind|key）的连线。</summary>
        bool DraftHasDev(string kindKey)
        {
            foreach (var l in sc.links)
                if (l.sk + "|" + l.sv == kindKey || l.dk + "|" + l.dv == kindKey) return true;
            return false;
        }

        static SceneLink ParseBizKey(string bizKey) { return SceneDiff.ParseBizKey(bizKey); }

        /// <summary>
        /// 跟随 MIXLINE：打开时只取基线；之后只有 MIXLINE 里真的发生了改动，才把"新增"的连线合并进草稿。
        /// 这样打开详情页本身不会把当前实时路由强塞进场景（避免误覆盖）。
        /// </summary>
        void LiveSyncTick()
        {
            if (IsDisposed || !Visible) return;
            if (dupScene != null) return;                      // 合并确认中不打扰
            try
            {
                if (!Engine.Running) return;
                var keys = SceneDiff.LiveKeys(Engine.GetGraphFull());
                if (keys == null || keys.Count == 0) return;
                if (liveSnap == null) { liveSnap = keys; return; }   // 首次：只记基线，不合并
                if (keys.SetEquals(liveSnap)) return;                // 没有变化

                // 只跟随"打开详情页之后"MIXLINE 里新出现的连线：
                // 与打开时的基线比较，而不是拿实时路由和场景草稿比较
                var before = liveSnap;
                int added = 0;
                foreach (var k in keys)
                {
                    if (before.Contains(k)) continue;   // 打开前就存在的连线，不自动塞进场景
                    if (DraftHas(k)) continue;
                    var sl = ParseBizKey(k);
                    if (sl == null) continue;
                    sc.links.Add(sl);
                    newLinks.Add(k);
                    newKeys.Add(sl.sk + "|" + sl.sv);
                    newKeys.Add(sl.dk + "|" + sl.dv);
                    added++;
                }
                // 检测"MIXLINE 里少了"的连线：不自动删，先标红虚线并给出同步按钮
                foreach (var k in before)
                {
                    if (keys.Contains(k)) continue;
                    if (!DraftHas(k)) continue;
                    var sl = ParseBizKey(k);
                    if (sl == null) continue;
                    removedLinks.Add(k);
                    removedKeys.Add(sl.sk + "|" + sl.sv);
                    removedKeys.Add(sl.dk + "|" + sl.dv);
                }

                liveSnap = keys;
                if (removedLinks.Count > 0 && added == 0)
                {
                    Log.Info("详情页：检测到 MIXLINE 少了 " + removedLinks.Count + " 条连线（场景 " + sc.name + "），等待确认同步");
                    Invalidate();
                }
                if (added > 0)
                {
                    followAdded += added;
                    dirty = true;
                    followNote = "已跟随 MIXLINE 新增 " + added + " 条连线（绿色标出）；点右侧保存即用当前路由覆盖本场景";
                    Log.Info("详情页：跟随 MIXLINE 新增 " + added + " 条连线（场景 " + sc.name + "）");
                    Invalidate();
                }
            }
            catch (Exception ex) { Log.Debug("详情页跟随 MIXLINE 失败（可忽略）", ex); }
        }

        /// <summary>按 MIXLINE 的当前状态，把检测到的删除同步进草稿（只删那些线，不动其它编辑）。</summary>
        void ApplyRemovals()
        {
            int n = 0;
            for (int i = sc.links.Count - 1; i >= 0; i--)
            {
                if (removedLinks.Contains(BizKey(sc.links[i]))) { sc.links.RemoveAt(i); n++; }
            }
            removedLinks.Clear(); removedKeys.Clear();
            // 被删掉的连线若还挂着"新增"标记，一并清掉，避免绿色残影
            newLinks.RemoveWhere(delegate (string k) { return !DraftHas(k); });
            newKeys.RemoveWhere(delegate (string k) { return !DraftHasDev(k); });
            if (n > 0) { dirty = true; followNote = null; }
            else { Notify("没有可同步的删除"); return; }
            Log.Info("详情页：按 MIXLINE 同步删除 " + n + " 条连线（场景 " + sc.name + "）");
            Notify("已同步删除 " + n + " 条连线（未保存）");
        }

        void SaveScene()
        {
            Log.Info("详情页：点击保存场景「" + sc.name + "」，连线=" + sc.links.Count + "，dirty=" + dirty);
            if (removedLinks.Count > 0)
            {
                Notify("有 MIXLINE 删除待同步：请先点「同步删除」或「忽略」");
                return;
            }
            if (!dirty)
            {
                Notify("没有未保存的修改");
                return;
            }
            if (sc.links.Count == 0) { Notify("至少保留一条连线才能保存"); return; }
            Scene dup = FindDuplicate();
            if (dup != null) { dupScene = dup; dupSecond = false; dupArmed = false; dupGate = lastDown; Invalidate(); return; }
            CommitSave();
        }

        void CommitSave()
        {
            try
            {
                store.Save();
                dirty = false;
                Changed = true;
                dupScene = null; dupSecond = false;
                Text = sc.name + " — 路由详情";
                int followed = followAdded;
                followAdded = 0; followNote = null;
                newKeys.Clear(); newLinks.Clear();
                removedLinks.Clear(); removedKeys.Clear();
                Invalidate();
                Log.Info("详情页：已保存场景「" + sc.name + "」，连线=" + sc.links.Count + (followed > 0 ? "（跟随新增 " + followed + " 条）" : ""));
                Notify(followed > 0
                    ? "已用当前 MIXLINE 路由覆盖「" + sc.name + "」（新增 " + followed + " 条）"
                    : "已保存「" + sc.name + "」（" + sc.links.Count + " 条连线）");
            }
            catch (Exception ex) { Notify("保存失败：" + ex.Message); }
        }

        void MergeConfirm()
        {
            if (dupScene == null) return;
            if (!dupArmed)
            {
                Notify("请先把鼠标移开再点，避免连点误操作");
                return;
            }
            if (!dupSecond)
            {
                dupSecond = true;
                dupArmed = false; dupGate = lastDown;   // 进入二次确认前重新上锁
                Invalidate();
                return;
            }

            try
            {
                string dupName = dupScene.name;
                store.Scenes.Remove(dupScene);
                store.Save();
                dirty = false;
                Changed = true;
                dupScene = null; dupSecond = false; dupArmed = false;
                Invalidate();
                Notify("已用「" + sc.name + "」覆盖「" + dupName + "」");
            }
            catch (Exception ex) { Notify("合并失败：" + ex.Message); }
        }

        void DrawChip(Graphics g, Rectangle r, bool isInput, string kind, string key, bool hot)
        {
            bool isNew = newKeys.Contains(kind + "|" + key);
            bool isRemoved = removedKeys.Contains(kind + "|" + key);
            bool editing = aliasTarget != null && aliasTarget.Kind == kind && aliasTarget.Key == key;
            chips.Add(new Chip { Rect = r, IsInput = isInput, Kind = kind, Key = key, New = isNew });

            string sysNode = kind + ":" + key;
            var effOut = SystemIO.EffectiveOut(sc);
            var effIn = SystemIO.EffectiveIn(sc);
            bool explicitOut = kind == "speaker" && sc.sysOutNode == sysNode;
            bool explicitIn = (kind == "stream" || kind == "mic") && sc.sysInNode == sysNode;
            bool selOut = kind == "speaker" && (explicitOut || (effOut != null && effOut.Node == sysNode));
            bool selIn = (kind == "stream" || kind == "mic") && (explicitIn || (effIn != null && effIn.Node == sysNode));
            bool autoSel = (selOut && effOut != null && effOut.Node == sysNode && !explicitOut)
                        || (selIn && effIn != null && effIn.Node == sysNode && !explicitIn);

            string alias = store != null ? store.AliasOf(key) : null;
            string full = null;
            if (store != null)
            {
                if (kind == "app") full = DeviceNames.Short(store.Names, store.ExePaths, kind, key);
                else full = DeviceNames.Display(store.Names, kind, key);
            }
            if (string.IsNullOrEmpty(full))
                full = (kind == "app" ? "应用" : (kind == "mic" ? "麦克风" : (kind == "speaker" ? "扬声器" : "MIXLINE Stream")));
            string title = alias;
            if (string.IsNullOrEmpty(title))
            {
                if (kind == "app") title = full;
                else if (kind == "stream") title = DeviceNames.Short(store.Names, store.ExePaths, kind, key);
                else
                {
                    int dot = full.IndexOf('\u00B7');
                    title = dot > 0 ? full.Substring(0, dot).Trim() : full;
                }
            }

            if (editing)
            {
                RoutingPaint.DrawChipFrame(g, r, false, isInput, isInput ? C.Accent : C.TextSecondary);
                DrawIconBox(g, r, kind, key, isInput);
                return;
            }

            bool selAny = selOut || selIn;
            bool baseHot = hot && !selAny;
            RoutingPaint.DrawChipFrame(g, r, baseHot, isInput, isInput ? C.Accent : C.TextSecondary);
            DrawIconBox(g, r, kind, key, isInput);


            int tx = r.X + Dpi.S(14) + Dpi.S(64) + Dpi.S(12);
            int titleW = r.Right - tx - Dpi.S(72);
            var sf = new StringFormat { FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter };
            bool hasSub = !string.IsNullOrEmpty(full) && full != title;
            int titleY = hasSub ? r.Y + Dpi.S(13) : r.Y + (r.Height - Dpi.S(24)) / 2;
            using (var b = new SolidBrush(C.TextPrimary))
                g.DrawString(title, FChipTitle, b, new RectangleF(tx, titleY, Math.Max(Dpi.S(40), titleW), Dpi.S(24)), sf);
            if (hasSub)
            {
                int sw = Math.Max(Dpi.S(40), r.Right - tx - Dpi.S(44));
                var ls = RoutingPaint.WrapTwo(g, T.Micro, full, sw);
                using (var b = new SolidBrush(C.TextSecondary))
                {
                    g.DrawString(ls[0], T.Micro, b, new RectangleF(tx, r.Y + Dpi.S(43), sw, Dpi.S(16)), sf);
                    if (!string.IsNullOrEmpty(ls[1]))
                        g.DrawString(ls[1], T.Micro, b, new RectangleF(tx, r.Y + Dpi.S(58), sw, Dpi.S(16)), sf);
                }
            }

            if (selAny)
            {
                Color scCol = selIn ? SystemIO.InColor : SystemIO.OutColor;
                string btxt = autoSel ? (selIn ? "自动输入" : "自动输出") : (selIn ? "输入" : "输出");
                int btw = autoSel ? Dpi.S(54) : Dpi.S(34);
                var btag = new Rectangle(r.Right - Dpi.S(12) - btw, r.Y + Dpi.S(9), btw, Dpi.S(17));
                using (var b = new SolidBrush(C.Fade(scCol, 34)))
                using (var pa = Rd.Path(btag, Dpi.S(8))) g.FillPath(b, pa);
                using (var b = new SolidBrush(scCol))
                {
                    var cf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    g.DrawString(btxt, T.MicroTiny, b, new RectangleF(btag.X, btag.Y, btag.Width, btag.Height), cf);
                }
            }

            // 编辑按钮：常驻但低干扰（悬停才提亮）
            DrawPencil(g, PencilRect(r), hot);

            if (selAny)
            {
                Color scCol = selIn ? SystemIO.InColor : SystemIO.OutColor;
                using (var pen = new Pen(scCol, hot ? 2.4f : 1.9f))
                using (var pa = Rd.Path(new Rectangle(r.X - 1, r.Y - 1, r.Width + 2, r.Height + 2), Rad.Chip + 1))
                    g.DrawPath(pen, pa);
            }
            if (isNew || isRemoved)
            {
                Color tagColor = isRemoved ? C.Danger : C.Success;
                using (var pen = new Pen(tagColor, 1.6f))
                {
                    pen.DashStyle = DashStyle.Dash;
                    pen.DashPattern = new float[] { 3.2f, 2.2f };
                    using (var pa = Rd.Path(new Rectangle(r.X - 4, r.Y - 4, r.Width + 8, r.Height + 8), Rad.Chip + 3))
                        g.DrawPath(pen, pa);
                }
                var tag = new Rectangle(r.X + Dpi.S(10), r.Y - Dpi.S(7), Dpi.S(30), Dpi.S(16));
                using (var b = new SolidBrush(tagColor))
                using (var pa = Rd.Path(tag, Dpi.S(8))) g.FillPath(b, pa);
                using (var b = new SolidBrush(C.Base))
                {
                    var cf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    g.DrawString(isRemoved ? "删" : "新", T.MicroTiny, b, new RectangleF(tag.X, tag.Y, tag.Width, tag.Height), cf);
                }
            }
        }

        void DrawIconBox(Graphics g, Rectangle r, string kind, string key, bool isInput)
        {
            int ibs = Dpi.S(64);
            var ib = new Rectangle(r.X + Dpi.S(14), r.Y + (r.Height - ibs) / 2, ibs, ibs);
            using (var b = new SolidBrush(C.Sunken))
            using (var pa = Rd.Path(ib, Dpi.S(12))) g.FillPath(b, pa);
            using (var pen = new Pen(C.BorderSubtle, 1f))
            using (var pa = Rd.Path(ib, Dpi.S(12))) g.DrawPath(pen, pa);
            var img = Icons.Get(key);
            if (img != null)
            {
                var old = g.InterpolationMode;
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                int gi = Math.Max(Dpi.S(16), (int)Math.Round(ibs * IconScale.AppIcon));
                gi = Math.Min(gi, ibs - Dpi.S(10));
                g.DrawImage(img, new Rectangle(ib.X + (ibs - gi) / 2, ib.Y + (ibs - gi) / 2, gi, gi));
                g.InterpolationMode = old;
            }
            else DrawGlyph(g, ib.X + Dpi.S(12), ib.Y + Dpi.S(12), ibs - Dpi.S(24), kind, isInput ? C.Accent : C.TextSecondary);
        }

        Rectangle PencilRect(Rectangle chip)
        {
            int w = Dpi.S(28), h = Dpi.S(24);
            return new Rectangle(chip.Right - Dpi.S(12) - w, chip.Bottom - Dpi.S(11) - h, w, h);
        }
        Rectangle AliasOkRect(Rectangle chip)
        {
            int w = Dpi.S(64), h = Dpi.S(28);
            return new Rectangle(chip.Right - Dpi.S(16) - w - Dpi.S(8) - w, chip.Bottom - Dpi.S(12) - h, w, h);
        }
        Rectangle AliasCancelRect(Rectangle chip)
        {
            int w = Dpi.S(64), h = Dpi.S(28);
            return new Rectangle(chip.Right - Dpi.S(16) - w, chip.Bottom - Dpi.S(12) - h, w, h);
        }

        /// <summary>编辑态：芯片高亮 + 输入框 + 保存/取消（与主界面行内编辑同一套交互）。</summary>
        void DrawAliasEdit(Graphics g, Rectangle chip)
        {
            // 只加一圈强调边框，不铺底色：芯片图标保持可见
            using (var pen = new Pen(C.Accent, 1.6f)) using (var pa = Rd.Path(chip, Rad.Chip)) g.DrawPath(pen, pa);
            DrawAliasButton(g, AliasOkRect(chip), "保存", true);
            DrawAliasButton(g, AliasCancelRect(chip), "取消", false);
        }

        void DrawAliasButton(Graphics g, Rectangle r, string text, bool primary)
        {
            if (primary)
                using (var b = new SolidBrush(C.Accent)) using (var pa = Rd.Path(r, Rad.Button)) g.FillPath(b, pa);
            else
                using (var pen = new Pen(C.BorderStrong)) using (var pa = Rd.Path(r, Rad.Button)) g.DrawPath(pen, pa);
            using (var b = new SolidBrush(primary ? Color.White : C.TextSecondary))
            {
                var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString(text, T.MicroSmall, b, new RectangleF(r.X, r.Y, r.Width, r.Height), sf);
            }
        }

        /// <summary>内容超出时在右侧画一条细滚动条（只作指示，滚轮仍可用）。</summary>
        void DrawScrollBar(Graphics g)
        {
            if (maxScroll <= 0) return;
            int top = Dpi.S(48), bottom = ClientSize.Height - BarH - Dpi.S(8);
            int hgt = Math.Max(Dpi.S(20), bottom - top);
            int trackW = Dpi.S(3), x = ClientSize.Width - Dpi.S(9);
            using (var b = new SolidBrush(C.Overlay(10)))
            using (var pa = Rd.Path(new Rectangle(x, top, trackW, hgt), 2)) g.FillPath(b, pa);
            float ratio = (float)hgt / (hgt + maxScroll);
            int thumbH = Math.Max(Dpi.S(24), (int)(hgt * ratio));
            int thumbY = top + (int)((hgt - thumbH) * ((float)scrollY / Math.Max(1, maxScroll)));
            using (var b = new SolidBrush(C.Fade(C.Accent, 130)))
            using (var pa = Rd.Path(new Rectangle(x, thumbY, trackW, thumbH), 2)) g.FillPath(b, pa);
        }

        void DrawPencil(Graphics g, Rectangle r, bool hot)
        {
            using (var b = new SolidBrush(hot ? C.Overlay(80) : C.CardHover))
            using (var pa = Rd.Path(r, Rad.Button)) g.FillPath(b, pa);
            using (var pen = new Pen(hot ? C.Accent : C.BorderSubtle, 1f))
            using (var pa2 = Rd.Path(r, Rad.Button)) g.DrawPath(pen, pa2);
            Color col = hot ? C.TextPrimary : C.TextSecondary;
            try
            {
                using (var b = new SolidBrush(col))
                {
                    var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    g.DrawString("\uE70F", new Font("Segoe MDL2 Assets", 8.5f), b,
                        new RectangleF(r.X, r.Y, r.Width, r.Height), sf);
                }
            }
            catch (Exception ex)
            {
                Log.Debug("详情页铅笔图标字体不可用", ex);
                using (var pen = new Pen(col, 1.3f)) g.DrawLine(pen, r.X + 8, r.Y + 15, r.X + 16, r.Y + 7);
            }
        }

        static Color Mix(Color a, Color b, float t)
        {
            t = Math.Max(0f, Math.Min(1f, t));
            return Color.FromArgb(
                (int)(a.A + (b.A - a.A) * t),
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        void BeginAlias(Chip c)
        {
            if (c == null) return;
            aliasTarget = c;
            string cur = store != null ? store.AliasOf(c.Key) : null;
            if (string.IsNullOrEmpty(cur) && store != null)
            {
                // 编辑框里的初始文本 = 芯片当前显示的名字（设备用原名称）
                cur = (c.Kind == "app" || c.Kind == "stream")
                    ? DeviceNames.Short(store.Names, store.ExePaths, c.Kind, c.Key)
                    : DeviceNames.Display(store.Names, c.Kind, c.Key);
            }
            aliasBox.Font = T.Body;
            aliasBox.Text = cur ?? "";
            int ix = c.Rect.X + Dpi.S(88);                            // 图标右侧，输入框在右上
            int iw = Math.Max(Dpi.S(120), c.Rect.Width - Dpi.S(88) - Dpi.S(16));
            int ih = Dpi.S(26);
            aliasBox.Bounds = new Rectangle(ix, c.Rect.Y + Dpi.S(16), iw, ih);
            aliasBox.Visible = true;
            aliasBox.BringToFront();
            aliasBox.Focus();
            aliasBox.SelectAll();
        }

        void CommitAlias()
        {
            if (aliasTarget == null) { CancelAlias(); return; }
            string v = (aliasBox.Text ?? "").Trim();
            try
            {
                store.SetAlias(aliasTarget.Key, v);      // 空字符串 = 取消别名
                Changed = true;
                Log.Info("设备改名：" + aliasTarget.Key + " → 「" + v + "」");
                if (store.LastSaveError != null) Notify("改名未保存：" + store.LastSaveError);
                else if (store.LastSaveNote != null) Notify("已改名；" + store.LastSaveNote);
                else Notify("已更新设备名称");
            }
            catch (Exception ex) { Log.Warn("保存设备别名失败", ex); }
            aliasTarget = null;
            aliasBox.Visible = false;
            Invalidate();
        }

        void CancelAlias()
        {
            aliasTarget = null;
            aliasBox.Visible = false;
            Invalidate();
        }

        void DrawGlyph(Graphics g, int x, int y, int sz, string kind, Color col)
        {
            using (var pen = new Pen(col, 1.6f))
            using (var br = new SolidBrush(col))
            {
                if (kind == "mic")
                {
                    g.DrawEllipse(pen, x + sz * 0.36f, y + sz * 0.12f, sz * 0.28f, sz * 0.5f);
                    g.DrawArc(pen, x + sz * 0.2f, y + sz * 0.36f, sz * 0.6f, sz * 0.5f, 200, 140);
                }
                else if (kind == "stream")
                {
                    g.DrawRectangle(pen, x + sz * 0.12f, y + sz * 0.12f, sz * 0.62f, sz * 0.76f);
                    g.FillPolygon(br, new PointF[] { new PointF(x + sz * 0.36f, y + sz * 0.3f), new PointF(x + sz * 0.36f, y + sz * 0.7f), new PointF(x + sz * 0.66f, y + sz * 0.5f) });
                }
                else
                {
                    g.FillPolygon(br, new PointF[] { new PointF(x + sz * 0.12f, y + sz * 0.34f), new PointF(x + sz * 0.32f, y + sz * 0.34f), new PointF(x + sz * 0.56f, y + sz * 0.14f), new PointF(x + sz * 0.56f, y + sz * 0.86f), new PointF(x + sz * 0.32f, y + sz * 0.66f), new PointF(x + sz * 0.12f, y + sz * 0.66f) });
                    g.DrawArc(pen, x + sz * 0.5f, y + sz * 0.28f, sz * 0.4f, sz * 0.44f, -55, 110);
                }
            }
        }

        Chip ChipAt(Point p) { for (int k = chips.Count - 1; k >= 0; k--) if (chips[k].Rect.Contains(p)) return chips[k]; return null; }
        int CurveAt(Point p)
        {
            for (int c = 0; c < curves.Count; c++)
            {
                var pts = curves[c];
                for (int k = 0; k + 1 < pts.Length; k++)
                {
                    float vx = pts[k + 1].X - pts[k].X, vy = pts[k + 1].Y - pts[k].Y;
                    float wx = p.X - pts[k].X, wy = p.Y - pts[k].Y;
                    float l2 = vx * vx + vy * vy;
                    float t = l2 <= 0.001f ? 0 : (wx * vx + wy * vy) / l2;
                    if (t < 0) t = 0; if (t > 1) t = 1;
                    float dx = pts[k].X + t * vx - p.X, dy = pts[k].Y + t * vy - p.Y;
                    if (Math.Sqrt(dx * dx + dy * dy) < 5) return c;
                }
            }
            return -1;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (hover != null || hoverCurveIdx >= 0 || hoverFullName != null)
            {
                hover = null; hoverCurveIdx = -1; hoverFullName = null;
                Invalidate();
            }
            base.OnMouseLeave(e);
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (dupScene != null && !dupArmed && Math.Abs(e.X - dupGate.X) + Math.Abs(e.Y - dupGate.Y) > Dpi.S(8))
                dupArmed = true;
            bool hd1 = !syncDelRect.IsEmpty && syncDelRect.Contains(e.Location);
            bool hd2 = !dismissRect.IsEmpty && dismissRect.Contains(e.Location);
            if (hd1 != hoverSyncDel || hd2 != hoverDismiss) { hoverSyncDel = hd1; hoverDismiss = hd2; Invalidate(); }
            Cursor = (hd1 || hd2) ? Cursors.Hand : Cursors.Default;

            bool hs = !saveRect.IsEmpty && saveRect.Contains(e.Location);
            bool ho = !dupOkRect.IsEmpty && dupOkRect.Contains(e.Location);
            bool hc = !dupCancelRect.IsEmpty && dupCancelRect.Contains(e.Location);
            if (hs != hoverSave || ho != hoverDupOk || hc != hoverDupCancel)
            {
                hoverSave = hs; hoverDupOk = ho; hoverDupCancel = hc; Invalidate();
            }
            Cursor = (hs || ho || hc) ? Cursors.Hand : Cursors.Default;

            var c = ChipAt(e.Location);
            if (pressIn != null && !dragging && (Math.Abs(e.X - pressPt.X) + Math.Abs(e.Y - pressPt.Y)) > 6)
                dragging = true;                       // 超过阈值才进入拖拽，避免点击被当成拖拽
            if (dragging) { dragPt = e.Location; hover = c; Invalidate(); return; }
            int ci = CurveAt(e.Location);
            {
                hover = c; hoverCurveIdx = ci;
                hoverFullName = c == null ? null : FullNameOf(c);
                Invalidate();
            }
            Cursor = (c != null || ci >= 0) ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            lastDown = e.Location;
            bool hd1 = !syncDelRect.IsEmpty && syncDelRect.Contains(e.Location);
            bool hd2 = !dismissRect.IsEmpty && dismissRect.Contains(e.Location);
            if (hd1 != hoverSyncDel || hd2 != hoverDismiss) { hoverSyncDel = hd1; hoverDismiss = hd2; Invalidate(); }
            Cursor = (hd1 || hd2) ? Cursors.Hand : Cursors.Default;

            bool hs = !saveRect.IsEmpty && saveRect.Contains(e.Location);
            bool ho = !dupOkRect.IsEmpty && dupOkRect.Contains(e.Location);
            bool hc = !dupCancelRect.IsEmpty && dupCancelRect.Contains(e.Location);
            if (hs != hoverSave || ho != hoverDupOk || hc != hoverDupCancel)
            {
                hoverSave = hs; hoverDupOk = ho; hoverDupCancel = hc; Invalidate();
            }
            Cursor = (hs || ho || hc) ? Cursors.Hand : Cursors.Default;

            var c = ChipAt(e.Location);
            if (e.Button == MouseButtons.Left && dupScene != null)
            {
                if (dupOkRect.Contains(e.Location)) { MergeConfirm(); return; }
                if (dupCancelRect.Contains(e.Location)) { dupScene = null; dupSecond = false; dupArmed = false; Invalidate(); return; }
                return;                       // 合并确认期间不处理其它点击
            }
            if (e.Button == MouseButtons.Left && removedLinks.Count > 0)
            {
                if (syncDelRect.Contains(e.Location)) { ApplyRemovals(); return; }
                if (dismissRect.Contains(e.Location))
                {
                    removedLinks.Clear(); removedKeys.Clear();
                    status = "已忽略 MIXLINE 的删除"; statusAt = DateTime.UtcNow;
                    if (statusTimer != null) statusTimer.Start();
                    Invalidate();
                    return;
                }
            }
            if (e.Button == MouseButtons.Left && !saveRect.IsEmpty && saveRect.Contains(e.Location))
            {
                SaveScene();
                return;
            }
            if (aliasTarget != null)
            {
                var ar = aliasTarget.Rect;
                if (e.Button == MouseButtons.Left && AliasOkRect(ar).Contains(e.Location)) { CommitAlias(); return; }
                if (e.Button == MouseButtons.Left && AliasCancelRect(ar).Contains(e.Location)) { CancelAlias(); return; }
                CancelAlias();          // 点别处 → 取消编辑，且不触发底层连线/拖拽
                return;
            }
            if (e.Button == MouseButtons.Left && hover != null && PencilRect(hover.Rect).Contains(e.Location))
            {
                BeginAlias(hover);
                return;
            }
            if (e.Button == MouseButtons.Left && c != null && c.IsInput)
            {
                pressIn = c; pressPt = e.Location; dragPt = e.Location; dragging = false;
                return;
            }
            if (e.Button == MouseButtons.Right)
            {
                if (c != null) { MenuChip(c); return; }
                int ci = CurveAt(e.Location);
                if (ci >= 0) { MenuCurve(ci); return; }
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (pressIn != null)
            {
                var src = pressIn;
                var dst = ChipAt(e.Location);
                bool moved = Math.Abs(e.X - pressPt.X) + Math.Abs(e.Y - pressPt.Y) > 8;
                pressIn = null; dragging = false;
                if (moved)
                {
                    if (dst != null && !dst.IsInput) Connect(src, dst);
                    else Notify("已取消（没落在右侧图标上）");
                }
                else MenuChip(src);
                Invalidate();
            }
            base.OnMouseUp(e);
        }

        /// <summary>底部状态栏提示（3 秒后自动消失，不弹窗）。</summary>
        void Notify(string t)
        {
            status = t;
            statusAt = DateTime.UtcNow;
            if (statusTimer != null) statusTimer.Start();
            Invalidate();
        }

        /// <summary>在本场景（草稿）里添加一条连线；不触碰 MIXLINE 实时路由。</summary>
        void Connect(Chip a, Chip b)
        {
            foreach (var l in sc.links)
                if (l.sk == a.Kind && l.sv == a.Key && l.dk == b.Kind && l.dv == b.Key) { Notify("本场景里已存在这条连线"); return; }
            sc.links.Add(new SceneLink { sk = a.Kind, sv = a.Key, dk = b.Kind, dv = b.Key, level = 1, muted = false });
            dirty = true;
            Invalidate();
        }

        /// <summary>在本场景（草稿）里移除一条连线；不触碰 MIXLINE 实时路由。</summary>
        void Disconnect(Chip a, Chip b)
        {
            int n = sc.links.RemoveAll(delegate (SceneLink l)
            {
                return l.sk == a.Kind && l.sv == a.Key && l.dk == b.Kind && l.dv == b.Key;
            });
            if (n == 0) { Notify("本场景里没有这条连线"); return; }
            dirty = true;
            Invalidate();
        }

        void MenuChip(Chip c)
        {
            var items = new List<MiniMenu.Item>();
            if (c.IsInput)
            {
                foreach (var o in chips) if (!o.IsInput)
                    items.Add(new MiniMenu.Item("连接到 " + NameOf(o.Kind, o.Key), delegate { Connect(c, o); }));
                if (items.Count == 0) items.Add(new MiniMenu.Item("（没有输出）", delegate { }));
            }
            else
            {
                foreach (var a in chips) if (a.IsInput)
                    items.Add(new MiniMenu.Item("断开 " + NameOf(a.Kind, a.Key) + " → 这里", delegate { Disconnect(a, c); }, true));
                if (items.Count == 0) items.Add(new MiniMenu.Item("（没有连线）", delegate { }));
            }
            items.Add(new MiniMenu.Item("", null, false) { Sep = true });
            items.Add(new MiniMenu.Item("重命名此设备…", delegate { BeginAlias(c); }));

            bool isOut = c.Kind == "speaker";
            bool isIn = c.Kind == "stream" || c.Kind == "mic";
            if (isOut || isIn)
            {
                string node = c.Kind + ":" + c.Key;
                var effOut = SystemIO.EffectiveOut(sc);
                var effIn = SystemIO.EffectiveIn(sc);
                bool selected = isOut
                    ? (sc.sysOutNode == node || (effOut != null && effOut.Node == node))
                    : (sc.sysInNode == node || (effIn != null && effIn.Node == node));
                items.Add(new MiniMenu.Item("", null, false) { Sep = true });
                if (selected)
                    items.Add(new MiniMenu.Item(isOut ? "取消系统默认输出" : "取消系统默认输入", delegate
                    {
                        SystemIO.ClearSelected(sc, !isOut);
                        dirty = true;
                        Notify("已取消系统默认" + (isOut ? "输出" : "输入"));
                        Invalidate();
                    }));
                else
                    items.Add(new MiniMenu.Item(isOut ? "设为系统默认输出" : "设为系统默认输入", delegate
                    {
                        string matched, err;
                        if (SystemIO.SetSelected(sc, store, c.Kind, c.Key, !isOut, out matched, out err))
                        {
                            dirty = true;
                            Log.Info("已设为系统默认" + (isOut ? "输出" : "输入") + "：" + matched + "（场景 " + sc.name + "）");
                            Notify("已设为系统默认" + (isOut ? "输出" : "输入") + "：" + matched);
                        }
                        else Notify("设置失败：" + err);
                        Invalidate();
                    }));
            }
            var mm = new MiniMenu(items);
            mm.ShowAt(delegate { return Cursor.Position; });
        }

        void MenuCurve(int idx)
        {
            var items = new List<MiniMenu.Item>();
            if (idx >= 0 && idx < sc.links.Count)
            {
                var l = sc.links[idx];
                items.Add(new MiniMenu.Item("从本场景删除这条连线", delegate
                {
                    if (idx < 0 || idx >= sc.links.Count) return;
                    sc.links.RemoveAt(idx);
                    dirty = true;
                    Invalidate();
                }, true));
            }
            var mm = new MiniMenu(items);
            mm.ShowAt(delegate { return Cursor.Position; });
        }

        string NameOf(string kind, string key)
        {
            string v;
            if (store.Names.TryGetValue(key, out v) && !string.IsNullOrEmpty(v)) return v;
            return kind == "app" ? "应用" : (kind == "mic" ? "麦克风" : (kind == "speaker" ? "扬声器" : "Stream"));
        }
    }

}
