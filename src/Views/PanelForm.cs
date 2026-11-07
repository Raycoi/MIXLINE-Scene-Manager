// MIXLINE Scene Manager — Views/PanelForm.cs
// 主页：竖向场景列表（紧凑行式）。路由细节在「编辑」打开的详情窗里查看/编辑。
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Asr
{
    class Panel : Form
    {
        readonly Store store;
        readonly SceneService scenes;
        readonly bool pinned;

        // 布局
        int Pad { get { return Dpi.S(Sp.L); } }
        int HeadH { get { return Dpi.S(56); } }
        int RowGap { get { return Dpi.S(8); } }
        int RowH { get { return Math.Max(Dpi.S(56), Math.Min(Dpi.S(110), Dpi.S(store.CardSize))); } }
        int Total { get { return store.Scenes.Count + 1; } }          // 末尾为“保存”行
        int ContentH { get { return Pad * 2 + HeadH + Total * (RowH + RowGap) - RowGap; } }
        int MaxScroll { get { return Math.Max(0, ContentH - ClientSize.Height); } }

        // 状态
        int scrollY, hover = -1, focusIdx = -1;
        bool downArmed, dragMoved, closeHover, dotPressed;
        int pressedRowBtn = -1;
        int pressIdx = -1, dragFrom = -1, dropIdx = -1;
        Point pressPt;
        int busyFlag;                                                   // 0 空闲 1 刷新中（Interlocked 单飞）
        string switchingId, failedId, failReason;
        float spinAngle;
        System.Windows.Forms.Timer spinTimer;
        string flash = "";
        bool engineUp;
        readonly HashSet<string> sel = new HashSet<string>();
        // 行内编辑（替代会跑到窗口外的弹窗）
        TextBox editBox; bool inlineEdit, inlineIsNew; int inlineIdx = -1;
        Rectangle delSelRect, cancelSelRect;      // 头部“删除 / 取消”多选操作
        bool confirmDel; int confirmDelIdx = -1;  // 行内删除确认（整行变红）
        bool confirmArmed; Point confirmGate;     // 防连点：必须先把鼠标移开再点「删除」
        bool centerOnStart;                       // 启动时需要在主屏居中（无有效旧位置）
        System.Windows.Forms.Timer posTimer; bool posDirty;


        public Panel(Store s, bool pinned)
        {
            store = s;
            this.pinned = pinned;
            scenes = new SceneService(s);

            Text = "MIXLINE Scene Manager";
            BackColor = C.Base;
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.Manual;
            MinimumSize = new Size(Dpi.S(380), Dpi.S(160));
            ClientSize = new Size(Dpi.S(460), Dpi.S(240));
            DoubleBuffered = true;
            AutoScaleMode = AutoScaleMode.None;
            KeyPreview = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            try { DwmSetWindowAttribute(Handle, 20, ref dark, Marshal.SizeOf(typeof(int))); }
            catch (Exception ex) { Log.Debug("标题栏深色失败", ex); }

            editBox = new TextBox
            {
                BorderStyle = BorderStyle.None,
                BackColor = C.Sunken,
                ForeColor = C.TextPrimary,
                Font = T.Body,
                Visible = false,
                AutoSize = false
            };
            editBox.KeyDown += delegate (object a, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; CommitInline(); }
                else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; CancelInline(); }
            };
            Controls.Add(editBox);

            KeyDown += OnKeyDown;
            MouseMove += OnMove; MouseLeave += OnLeave;
            MouseDown += OnDown; MouseUp += OnUp;
            MouseWheel += delegate (object a, MouseEventArgs e)
            {
                if (MaxScroll <= 0) return;
                scrollY = Math.Max(0, Math.Min(MaxScroll, scrollY - e.Delta / 3));
                Invalidate();
            };

            spinTimer = new System.Windows.Forms.Timer { Interval = 60 };
            spinTimer.Tick += delegate { spinAngle += 18f; if (spinAngle >= 360f) spinAngle -= 360f; Invalidate(); };

            var tick = new System.Windows.Forms.Timer { Interval = 2000 };
            tick.Tick += delegate { if (Visible && !PopupHost.IsOpen) KickRefresh(); };
            tick.Start();

            LoadPos();

            // 位置/大小改动后自动落盘（多屏拖动、启动器模式下被外部结束进程也不会丢）
            posTimer = new System.Windows.Forms.Timer { Interval = 600 };
            posTimer.Tick += delegate
            {
                posTimer.Stop();
                if (!posDirty) return;
                posDirty = false;
                SavePos();
            };
            LocationChanged += delegate { MarkPosDirty(); };
            SizeChanged += delegate { MarkPosDirty(); };
        }

        int dark = 1;
        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        // ==================== 布局 ====================
        Rectangle RowRect(int idx)
        {
            int y = Pad + HeadH + idx * (RowH + RowGap) - scrollY;
            return new Rectangle(Pad, y, ClientSize.Width - Pad * 2, RowH);
        }
        /// <summary>
        /// 行尾图标按钮的唯一命中入口：返回按钮序号（0 铅笔 1 齿轮 2 垃圾桶），row 为所在行。
        /// 行内编辑 / 删除确认期间一律返回 -1（不可点），避免“绘制隐藏但命中仍生效”的穿透。
        /// </summary>
        int HitRowButton(Point pt, out int row)
        {
            row = -1;
            if (inlineEdit || confirmDel) return -1;
            int idx = HitRow(pt);
            if (idx < 0 || idx >= store.Scenes.Count) return -1;
            var r = RowRect(idx);
            for (int bi = 0; bi < PanelPaint.BtnN; bi++)
            {
                if (PanelPaint.BtnRect(r, bi).Contains(pt)) { row = idx; return bi; }
            }
            return -1;
        }
        Rectangle MenuRect() { return new Rectangle(ClientSize.Width - Pad - Dpi.S(34), Dpi.S(12), Dpi.S(34), Dpi.S(34)); }

        int HitRow(Point pt)
        {
            for (int i = 0; i < Total; i++)
            {
                var r = RowRect(i);
                if (r.Contains(pt)) return i;
            }
            return -1;
        }

        int hoverBtn = -1, hoverRowBtn = -1, pressedBtn = -1;


        /// <summary>把 Panel 的交互状态映射成 PanelPaint 需要的只读状态。</summary>
        RowPaintState StateOf(int idx, Scene sc)
        {
            var st = new RowPaintState();
            st.Active = sc.match;
            st.Selected = sel.Contains(sc.id);
            st.Hover = idx == hover;
            st.Focus = idx == focusIdx;
            st.Switching = sc.id == switchingId;
            st.Failed = sc.id == failedId;
            st.DelAsk = confirmDel && idx == confirmDelIdx;
            st.RenameEdit = inlineEdit && !inlineIsNew && idx == inlineIdx;
            st.FailReason = failReason;
            st.HoverBtn = (hoverRowBtn == idx) ? hoverBtn : -1;
            st.PressedBtn = (pressedRowBtn == idx) ? pressedBtn : -1;
            st.SpinAngle = spinAngle;
            return st;
        }
        void FitHeight()
        {
            try
            {
                // 只"长高"来容纳内容，绝不把用户拉高的窗口缩回去（之前用户拉长后会被这里重置，看起来像自动缩小）
                int cap = Math.Min(Screen.FromControl(this).WorkingArea.Height - Dpi.S(60), Dpi.S(820));
                int want = Math.Min(ContentH, cap);
                if (ClientSize.Height < want) ClientSize = new Size(ClientSize.Width, want);
            }
            catch (Exception ex) { Log.Debug("FitHeight 失败", ex); }
        }

        // ==================== 绘制 ====================
        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(C.Base)) g.FillRectangle(b, ClientRectangle);
            g.SetClip(new Rectangle(0, 0, ClientSize.Width, Pad + HeadH - 4));

            string sub = flash.Length > 0 ? flash : (store.Scenes.Count + " 个场景 · 点一行切换，编辑看路由");
            if (sel.Count > 0) sub = "已选 " + sel.Count + " 项 · 点「删除」或按 Delete";
            PanelPaint.DrawHeader(g, ClientRectangle, sub, sel.Count > 0 || flash.Length > 0,
                engineUp, closeHover, dotPressed);

            // 多选操作条（头部下方，点击区域记录在字段里）
            delSelRect = Rectangle.Empty; cancelSelRect = Rectangle.Empty;
            if (sel.Count > 0)
            {
                var szDel = g.MeasureString("删除", T.Caption);
                var szCan = g.MeasureString("取消", T.Caption);
                int x2 = ClientSize.Width - Pad - (int)szDel.Width - (int)szCan.Width - Dpi.S(24);
                delSelRect = new Rectangle(x2, Dpi.S(34), (int)szDel.Width + Dpi.S(8), Dpi.S(20));
                cancelSelRect = new Rectangle(x2 + delSelRect.Width + Dpi.S(8), Dpi.S(34), (int)szCan.Width + Dpi.S(8), Dpi.S(20));
                using (var b = new SolidBrush(C.Danger))
                    g.DrawString("删除", T.Caption, b, delSelRect.X, delSelRect.Y + 1);
                using (var b = new SolidBrush(C.TextSecondary))
                    g.DrawString("取消", T.Caption, b, cancelSelRect.X, cancelSelRect.Y + 1);
            }


            g.ResetClip();
            if (scrollY > MaxScroll) scrollY = MaxScroll;
            g.SetClip(new Rectangle(0, Pad + HeadH - 4, ClientSize.Width, Math.Max(10, ClientSize.Height - (Pad + HeadH - 4))));

            for (int i = 0; i < Total; i++)
            {
                var r = RowRect(i);
                if (r.Bottom < Pad + HeadH || r.Y > ClientSize.Height) continue;
                if (i >= store.Scenes.Count) PanelPaint.DrawSaveRow(g, r, hover >= store.Scenes.Count, inlineEdit && inlineIsNew);
                else PanelPaint.DrawSceneRow(g, r, store.Scenes[i], store, StateOf(i, store.Scenes[i]));
            }

            // 行内编辑：把输入框放到目标行上，并画行内按钮
            if (inlineEdit)
            {
                int target = inlineIsNew ? store.Scenes.Count : inlineIdx;
                var er0 = RowRect(target);
                if (inlineIsNew)
                {
                    editBox.Bounds = new Rectangle(er0.X + Dpi.S(12), er0.Y + (er0.Height - Dpi.S(20)) / 2, er0.Width - Dpi.S(156), Dpi.S(20));
                    using (var pen = new Pen(C.Accent, 1.4f)) using (var pa = Rd.Path(er0, Rad.Card)) g.DrawPath(pen, pa);
                    PanelPaint.DrawInlineButton(g, PanelPaint.OkRect(er0), "保存", true);
                    PanelPaint.DrawInlineButton(g, PanelPaint.CancelRect(er0), "取消", false);
                }
                else
                {
                    editBox.Bounds = new Rectangle(er0.X + Dpi.S(16), er0.Y + (er0.Height - Dpi.S(20)) / 2, Math.Max(Dpi.S(80), er0.Width - Dpi.S(160)), Dpi.S(20));
                    using (var pen = new Pen(C.Accent, 1.4f)) using (var pa = Rd.Path(er0, Rad.Card)) g.DrawPath(pen, pa);
                }
                editBox.BringToFront();
            }

            if (dragFrom >= 0 && dragMoved && dropIdx >= 0 && dropIdx < store.Scenes.Count)
            {
                // 目标行：蓝色虚线框，表示“松手就与这一行交换位置”
                var tr = RowRect(dropIdx);
                using (var pen = new Pen(C.Accent, 1.8f))
                {
                    pen.DashStyle = DashStyle.Dash;
                    using (var pa = Rd.Path(new Rectangle(tr.X - 2, tr.Y - 2, tr.Width + 4, tr.Height + 4), Rad.Card + 2))
                        g.DrawPath(pen, pa);
                }
                // 被拖动的那一行：淡化显示
                var fr = RowRect(dragFrom);
                using (var b = new SolidBrush(C.Fade(C.Base, 170)))
                using (var pa = Rd.Path(fr, Rad.Card)) g.FillPath(b, pa);
            }
            g.ResetClip();
        }

        // ==================== 交互 ====================
        void OnMove(object sender, MouseEventArgs e)
        {
            int idx = HitRow(e.Location);
            bool ch = idx != hover;
            if (idx != hover) hover = idx;
            hover = idx;

            // 行内两个图标按钮的悬停/按下跟踪（按键位置变化也要重绘）
            int hr0; int hb = HitRowButton(e.Location, out hr0);
            int hr = hb >= 0 ? hr0 : -1;
            if (hb != hoverBtn || hr != hoverRowBtn) { hoverBtn = hb; hoverRowBtn = hr; ch = true; }

            bool cm = MenuRect().Contains(e.Location);
            if (cm != closeHover) { closeHover = cm; ch = true; }
            if (dragFrom >= 0 && downArmed)
            {
                if (Math.Abs(e.Y - pressPt.Y) > 6) dragMoved = true;
                if (dragMoved) dropIdx = DropIndex(e.Location);
                ch = true;
            }
            if (confirmDel && !confirmArmed && Math.Abs(e.X - confirmGate.X) + Math.Abs(e.Y - confirmGate.Y) > Dpi.S(8))
            {
                confirmArmed = true;
                ch = true;
            }
            if (ch) Invalidate();
        }

        void OnLeave(object sender, EventArgs e)
        {
            hover = -1; closeHover = false; hoverBtn = -1; hoverRowBtn = -1; pressedBtn = -1; dotPressed = false;
            Invalidate();
        }


        /// <summary>拖拽落点：光标所在的场景行索引（用于两两交换）。</summary>
        int DropIndex(Point pt)
        {
            int idx = (pt.Y - Pad - HeadH + scrollY) / (RowH + RowGap);
            return Math.Max(0, Math.Min(store.Scenes.Count - 1, idx));
        }

        void OnDown(object sender, MouseEventArgs e)
        {
            if (switchingId != null) return;
            if (PopupHost.IsOpen || PopupHost.JustClosed) return;
            // 记录按下的是哪个按钮（用于按下态反馈）；行内编辑/删除确认时禁用行尾图标
            pressedBtn = -1; pressedRowBtn = -1;
            if (MenuRect().Contains(e.Location)) dotPressed = true;
            int dr1; pressedBtn = HitRowButton(e.Location, out dr1);
            pressedRowBtn = pressedBtn >= 0 ? dr1 : -1;
            if (pressedBtn >= 0 || dotPressed) Invalidate();
            downArmed = true;
            pressPt = e.Location;
            pressIdx = HitRow(e.Location);
            dragFrom = pressIdx >= 0 && pressIdx < store.Scenes.Count ? pressIdx : -1;
            dragMoved = false; dropIdx = -1;
        }

        void OnUp(object sender, MouseEventArgs e)
        {
            if (!downArmed) return;
            downArmed = false;
            if (PopupHost.IsOpen || PopupHost.JustClosed) return;
            int idx = HitRow(e.Location);
            bool wasDrag = dragMoved;
            int from = dragFrom, to = dropIdx;
            dragFrom = -1; dragMoved = false; dropIdx = -1;

            if (e.Button == MouseButtons.Right)
            {
                if (idx >= 0 && idx < store.Scenes.Count) MenuForScene(idx);
                else if (MenuRect().Contains(e.Location)) ShowAppMenu();
                Invalidate();
                return;
            }
            if (e.Button != MouseButtons.Left) return;

            if (wasDrag && from >= 0 && to >= 0)
            {
                if (to != from && from < store.Scenes.Count && to < store.Scenes.Count)
                {
                    // 两两交换（与旧版行为一致）
                    var a = store.Scenes[from];
                    var b2 = store.Scenes[to];
                    store.Scenes[from] = b2;
                    store.Scenes[to] = a;
                    store.Save(); Reload();
                    Notify("已交换「" + a.name + "」与「" + b2.name + "」");
                }
                Invalidate();
                return;
            }

            if (MenuRect().Contains(e.Location)) { dotPressed = false; ShowAppMenu(); return; }
            if (confirmDel)
            {
                if (confirmDelIdx >= 0 && confirmDelIdx < store.Scenes.Count)
                {
                    var dr = RowRect(confirmDelIdx);
                    if (PanelPaint.ConfirmOkRect(dr).Contains(e.Location))
                    {
                        if (!confirmArmed)
                        {
                            Notify("请先把鼠标移到「删除」上再点，避免连点误删");
                            Invalidate();
                            return;
                        }
                        int di = confirmDelIdx;
                        confirmDel = false; confirmDelIdx = -1;
                        DeleteOne(di);
                        return;
                    }
                    if (PanelPaint.ConfirmCancelRect(dr).Contains(e.Location))
                    {
                        confirmDel = false; confirmDelIdx = -1;
                        Invalidate();
                        return;
                    }
                }
                confirmDel = false; confirmDelIdx = -1;
                Invalidate();
                return;
            }
            if (!inlineEdit && !confirmDel && pressedBtn >= 0 && pressedRowBtn >= 0 && pressedRowBtn < store.Scenes.Count)
            {
                int rowIdx = pressedRowBtn, bi = pressedBtn;
                var pr = RowRect(rowIdx);
                pressedBtn = -1; dotPressed = false; Invalidate();
                if (PanelPaint.BtnRect(pr, bi).Contains(e.Location))
                {
                    if (bi == 0)
                    {
                        // 铅笔：行内重命名
                        BeginInline(false, rowIdx);
                    }
                    else if (bi == 1)
                    {
                        using (var d = new SceneDetail(scenes, store.Scenes[rowIdx])) d.ShowDialog(this);
                        Reload();
                    }
                    else
                    {
                        // 垃圾桶：行内红色确认
                        confirmDel = true; confirmDelIdx = rowIdx;
                        confirmArmed = false; confirmGate = e.Location;
                        Invalidate();
                    }
                    return;
                }
            }
            pressedBtn = -1; dotPressed = false;
            if (sel.Count > 0)
            {
                if (delSelRect.Contains(e.Location)) { DeleteSelected(); return; }
                if (cancelSelRect.Contains(e.Location)) { sel.Clear(); Invalidate(); return; }
            }

            // 行内编辑：点“保存/取消”按钮生效，点其它地方取消
            if (inlineEdit)
            {
                var er0 = RowRect(inlineIsNew ? store.Scenes.Count : inlineIdx);
                // “保存当前路由”行画的是 OkRect/CancelRect（垂直居中），命中必须用同一套矩形；
                // 用 ConfirmOkRect 会导致点可见的「保存」无效，落到底下的 CancelInline。
                if (inlineIsNew)
                {
                    if (PanelPaint.OkRect(er0).Contains(e.Location)) { CommitInline(); return; }
                    if (PanelPaint.CancelRect(er0).Contains(e.Location)) { CancelInline(); return; }
                }
                CancelInline();
                Invalidate();
                return;
            }

            if (idx < 0) return;
            if (idx >= store.Scenes.Count) { SaveCurrent(); return; }


            if (sel.Count > 0) { sel.Clear(); Invalidate(); }
            if ((ModifierKeys & Keys.Control) == Keys.Control)
            {
                string id = store.Scenes[idx].id;
                if (!sel.Add(id)) sel.Remove(id);
                Invalidate();
                return;
            }
            Switch(store.Scenes[idx]);
        }

        void OnKeyDown(object sender, KeyEventArgs ke)
        {
            if (inlineEdit) return;                 // 编辑中，按键交给输入框
            if (ke.KeyCode == Keys.Delete) { DeleteSelected(); ke.Handled = true; return; }
            if (ke.KeyCode == Keys.Escape) { sel.Clear(); focusIdx = -1; Invalidate(); ke.Handled = true; return; }
            if (ke.KeyCode == Keys.Down || ke.KeyCode == Keys.Up)
            {
                if (Total <= 0) return;
                if (focusIdx < 0) focusIdx = 0;
                else focusIdx = ke.KeyCode == Keys.Down ? Math.Min(Total - 1, focusIdx + 1) : Math.Max(0, focusIdx - 1);
                EnsureVisible(focusIdx);
                Invalidate(); ke.Handled = true; return;
            }
            if (ke.KeyCode == Keys.Enter && focusIdx >= 0 && focusIdx < Total)
            {
                if (focusIdx >= store.Scenes.Count) SaveCurrent();
                else Switch(store.Scenes[focusIdx]);
                ke.Handled = true; return;
            }
            if (ke.KeyCode == Keys.E) { OpenDetailFocused(); ke.Handled = true; }
        }

        void OpenDetailFocused()
        {
            if (focusIdx < 0 || focusIdx >= store.Scenes.Count) return;
            using (var d = new SceneDetail(scenes, store.Scenes[focusIdx])) d.ShowDialog(this);
            Reload();
        }

        void EnsureVisible(int idx)
        {
            int max = MaxScroll;
            if (max <= 0) return;
            int top = Pad + HeadH + idx * (RowH + RowGap) - scrollY;
            int viewTop = Pad + HeadH, viewBottom = ClientSize.Height - Pad;
            int delta = 0;
            if (top < viewTop) delta = top - viewTop;
            else if (top + RowH > viewBottom) delta = top + RowH - viewBottom;
            if (delta != 0) scrollY = Math.Max(0, Math.Min(max, scrollY + delta));
        }

        // ==================== 场景操作 ====================
        void Switch(Scene sc)
        {
            if (inlineEdit) CancelInline();
            // 先在界面上进入“切换中”，随后把阻塞的切换放到后台线程，
            // UI 线程继续重绘，转圈才有动画。
            switchingId = sc.id; failedId = null; failReason = null;
            spinAngle = 0f; spinTimer.Start(); Invalidate(); Update();

            string targetId = sc.id, targetName = sc.name;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string log = null, err = null;
                try
                {
                    log = scenes.Switch(sc);
                    scenes.RefreshNamesNow();       // 名称刷新也在后台完成
                }
                catch (Exception ex) { err = ex.Message; }

                Log.Guard("切换完成后更新界面", delegate
                {
                    try { BeginInvoke(new Action(delegate
                    {
                        spinTimer.Stop();
                        switchingId = null;
                        if (err != null)
                        {
                            failedId = targetId; failReason = err;
                            Notify("切换失败：" + err);
                            Log.Error("切换场景失败: " + targetName + " — " + err, null);
                            Invalidate();
                            return;
                        }
                        Reload();
                        var fresh = store.FindScene(targetId);
                        bool ok = fresh != null && fresh.match;
                        if (!ok)
                        {
                            failedId = targetId;
                            failReason = string.IsNullOrEmpty(scenes.LastSwitchReason) ? log : scenes.LastSwitchReason;
                        }
                        Invalidate();
                        string sysWarn = scenes.LastSystemWarning;
                    Notify((ok ? "已切换「" : "切换未生效「") + targetName + "」 " + log +
                           (string.IsNullOrEmpty(sysWarn) ? "" : "；" + sysWarn));
                    })); }
                    catch (Exception ex) { Log.Warn("切换完成后刷新界面失败", ex); }
                });
            });
        }

        void SaveCurrent() { BeginInline(true, store.Scenes.Count); }

        /// <summary>把某一行变成行内编辑状态（isNew=true 时是"保存当前路由"行）。</summary>
        void BeginInline(bool isNew, int idx)
        {
            inlineEdit = true; inlineIsNew = isNew; inlineIdx = idx;
            editBox.Text = isNew ? ("场景 " + (store.Scenes.Count + 1)) : store.Scenes[idx].name;
            editBox.Visible = true;
            Invalidate();
            editBox.Focus();
            editBox.SelectAll();
        }

        void CancelInline()
        {
            if (!inlineEdit) return;
            inlineEdit = false; inlineIdx = -1;
            editBox.Visible = false;
            Invalidate();
        }

        void CommitInline()
        {
            if (!inlineEdit) return;
            string name = (editBox.Text ?? "").Trim();
            bool isNew = inlineIsNew; int idx = inlineIdx;
            inlineEdit = false; inlineIdx = -1;
            editBox.Visible = false;
            try
            {
                if (isNew)
                {
                    if (name.Length == 0) name = "场景 " + (store.Scenes.Count + 1);
                    var sc = scenes.Snapshot(name);
                    if (sc.links.Count == 0) { Notify("当前没有任何连线，无法保存"); Invalidate(); return; }

                    // 重复检测：与现有场景连线集合完全一致时不新建，直接更新那个场景
                    var newKeys = new HashSet<string>();
                    foreach (var l in sc.links) newKeys.Add(Store.Key(l));
                    Scene dup = null;
                    foreach (var ex in store.Scenes)
                    {
                        var exKeys = new HashSet<string>();
                        foreach (var l in ex.links) exKeys.Add(Store.Key(l));
                        if (exKeys.SetEquals(newKeys)) { dup = ex; break; }
                    }
                    if (dup != null)
                    {
                        dup.links = sc.links; dup.outs = sc.outs;
                        dup.createdAt = DateTime.UtcNow.ToString("o");
                        store.Save(); Reload();
                        Notify("与「" + dup.name + "」相同，已更新它");
                        return;
                    }

                    store.Scenes.Add(sc); store.Save(); Reload();
                    if (store.LastSaveError != null) Notify("已加入列表，但写配置失败：" + store.LastSaveError);
                    else Notify("已保存「" + name + "」（" + sc.links.Count + " 路）");
                }
                else
                {
                    if (idx < 0 || idx >= store.Scenes.Count) { Invalidate(); return; }
                    if (name.Length == 0) { Invalidate(); return; }
                    store.Scenes[idx].name = name;
                    store.Save(); Reload();
                    Notify("已重命名");
                }
            }
            catch (Exception ex) { Notify("保存失败：" + ex.Message); }
            Invalidate();
        }

        void DeleteSelected()
        {
            var ids = new List<string>(sel);
            if (ids.Count == 0) return;
            for (int i = store.Scenes.Count - 1; i >= 0; i--)
                if (sel.Contains(store.Scenes[i].id)) store.Scenes.RemoveAt(i);
            sel.Clear(); store.Save(); Reload();
            Notify("已删除");
        }

        void DeleteOne(int idx)
        {
            if (idx < 0 || idx >= store.Scenes.Count) return;
            var sc = store.Scenes[idx];
            store.Scenes.RemoveAt(idx);
            sel.Remove(sc.id);
            store.Save(); Reload();
            Notify("已删除");
        }

        void Rename(int idx)
        {
            if (idx < 0 || idx >= store.Scenes.Count) return;
            BeginInline(false, idx);
        }

        void Notify(string text)
        {
            flash = text; Invalidate();
            var t = new System.Windows.Forms.Timer { Interval = 2200 };
            t.Tick += delegate { t.Stop(); t.Dispose(); flash = ""; Invalidate(); };
            t.Start();
        }

        // ==================== 菜单 ====================
        void MenuForScene(int idx)
        {
            var sc = store.Scenes[idx];
            var items = new List<MiniMenu.Item>();
            items.Add(new MiniMenu.Item("编辑路由…", "打开路由详情，可拖拽连线 / 断开", delegate
            {
                using (var d = new SceneDetail(scenes, sc)) d.ShowDialog(this);
                Reload();
            }));
            items.Add(new MiniMenu.Item("", null, false) { Sep = true });
            items.Add(new MiniMenu.Item("用当前路由覆盖", "把 MIXLINE 现在的路由保存到该场景", delegate
            {
                var snap = scenes.Snapshot(sc.name);
                sc.links = snap.links; sc.outs = snap.outs;
                sc.createdAt = DateTime.UtcNow.ToString("o");
                store.Save(); Reload(); Notify("已覆盖为当前路由");
            }));
            items.Add(new MiniMenu.Item(sc.additive ? "切换时：保留现有连线（已开启）" : "切换时：保留现有连线（未开启）",
                "开启后切到本场景只加连线，不删除其它连线", delegate
                {
                    sc.additive = !sc.additive;
                    store.Save(); Reload();
                    Notify(sc.additive ? "已开启：只加不删" : "已恢复：会删除多余连线");
                }));
            items.Add(new MiniMenu.Item("", null, false) { Sep = true });
            // 删除已移到行尾垃圾桶按钮（行内确认），右键菜单不再提供
            var mm = new MiniMenu(items);
            mm.ShowAt(delegate { return Cursor.Position; });
        }

        void ShowAppMenu()
        {
            var items = new List<MiniMenu.Item>();
            items.Add(new MiniMenu.Item("打开 MIXLINE", delegate { Log.Guard("打开 MIXLINE", delegate { AppActions.Launch(); }); }));
            items.Add(new MiniMenu.Item("", null, false) { Sep = true });
            items.Add(new MiniMenu.Item("用本程序替换 MIXLINE 界面", "点 MIXLINE 托盘菜单里的「打开 MIXLINE」时，弹出的就是本场景切换器",
                delegate { AppActions.Integrate(this, true, store); }));
            items.Add(new MiniMenu.Item("还原 MIXLINE 原版界面", "撤销上一项，点「打开 MIXLINE」重新打开原生界面",
                delegate { AppActions.Integrate(this, false, store); }));
            items.Add(new MiniMenu.Item("刷新图标缓存", "清空 MSM-data\\icons 后重新从各应用 exe 抽取图标",
                delegate { Icons.Clear(); KickRefresh(); Notify("图标缓存已刷新"); }));
            items.Add(new MiniMenu.Item("恢复上次配置备份", "把 MSM-config.json.bak 还原回来（覆盖当前配置）",
                delegate { RestoreBackup(); }));
            items.Add(new MiniMenu.Item("", null, false) { Sep = true });
            items.Add(new MiniMenu.Item("设置…", "配置文件目录、行高", delegate { ShowSettings(); }));
            items.Add(new MiniMenu.Item("", null, false) { Sep = true });
            items.Add(new MiniMenu.Item("退出", delegate { Close(); }, true));
            var mm = new MiniMenu(items);
            mm.ShowAt(delegate { return PointToScreen(new Point(MenuRect().Right - 40, MenuRect().Bottom + 2)); });
        }

        void RestoreBackup()
        {
            try
            {
                string bak = store.ConfigPath + ".bak";
                if (!File.Exists(bak)) { Notify("没有可用的备份"); return; }
                File.Copy(bak, store.ConfigPath, true);
                store.Load(); Reload();
                Notify("已恢复上次配置备份");
            }
            catch (Exception ex) { Notify("恢复失败：" + ex.Message); }
        }

        void ShowSettings()
        {
            using (var sf = new SettingsForm(Settings.ConfigDir, store.CardSize, store.LogEnabled, store.LogMinLevel))
            {
                if (PopupHost.Run(sf) != DialogResult.OK) return;
                bool changed = sf.ChosenDir != Settings.ConfigDir;
                if (changed) store.Relocate(sf.ChosenDir);
                bool dirty = false;
                if (sf.ChosenCardSize != store.CardSize) { store.CardSize = sf.ChosenCardSize; dirty = true; }
                if (sf.ChosenLog != store.LogEnabled || sf.ChosenLevel != store.LogMinLevel)
                {
                    store.LogEnabled = sf.ChosenLog;
                    store.LogMinLevel = sf.ChosenLevel;
                    Log.Enabled = sf.ChosenLog;
                    Log.MinLevel = (Log.Level)Math.Max(0, Math.Min(3, sf.ChosenLevel));
                    dirty = true;
                    Log.Info("日志设置：开关=" + (sf.ChosenLog ? "开" : "关") + "，等级=" + sf.ChosenLevel);
                }
                if (dirty) store.Save();
                Reload();
                Notify(changed ? "配置目录已更改" : "设置已保存");
            }
        }

        // ==================== 刷新 / 生命周期 ====================
        public void Reload()
        {
            if (inlineEdit) { inlineEdit = false; inlineIdx = -1; editBox.Visible = false; }
            confirmDel = false; confirmDelIdx = -1;
            try
            {
                store.Load();
                sel.RemoveWhere(delegate (string id) { return store.FindScene(id) == null; });
                scenes.MarkMatch();
            }
            catch (Exception ex)
            {
                // 配置损坏 / 引擎不可用都不允许把程序带崩
                Log.Warn("刷新界面失败", ex);
                foreach (var sc in store.Scenes) sc.match = false;
            }
            FitHeight();
            Invalidate();
        }

        void KickRefresh()
        {
            if (Interlocked.CompareExchange(ref busyFlag, 1, 0) != 0) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                bool up = engineUp;
                // 先探测引擎状态：MarkMatch 在 MIXLINE 刚关闭时可能失败，不能让它挡住状态更新
                try { up = Engine.Running; } catch (Exception exE) { Log.Debug("读取引擎状态失败", exE); }
                try
                {
                    // 只刷新"生效状态"，不重新解析配置文件：避免覆盖用户刚做的编辑
                    scenes.MarkMatch();
                }
                catch (Exception ex) { Log.Warn("后台刷新失败", ex); }
                finally
                {
                    Interlocked.Exchange(ref busyFlag, 0);
                    Log.Guard("刷新后重绘", delegate
                    {
                        // 后台刷新只重绘，不再 FitHeight：否则用户手动拉高/压矮的窗口会被"自动纠正"，看着像自己在缩
                        BeginInvoke(new Action(delegate
                        {
                            engineUp = up;
                            Invalidate();
                        }));
                    });
                }
            });
        }
        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            Dpi.Set((int)Math.Round(e.DeviceDpiNew / 96.0 * 100));
            MinimumSize = new Size(Dpi.S(380), Dpi.S(160));
            ClientSize = new Size(Dpi.S(460), Dpi.S(240));
            scrollY = 0;
            FitHeight();
            Invalidate();
        }

        void MarkPosDirty()
        {
            if (!Visible || WindowState != FormWindowState.Normal) return;
            posDirty = true;
            if (posTimer != null) { posTimer.Stop(); posTimer.Start(); }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (scrollY > MaxScroll) scrollY = MaxScroll;
            if (!centerOnStart) MarkPosDirty();
            Invalidate();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            SavePos();
            base.OnFormClosing(e);
        }

        public void ShowWindowFront()
        {
            try
            {
                Show();
                if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
                Activate();
                TopMost = true; TopMost = false;
            }
            catch (Exception ex) { Log.Warn("显示窗口失败", ex); }
            try { Reload(); } catch (Exception ex) { Log.Warn("初次加载失败", ex); }
            // Reload 里的 FitHeight 可能改变窗口高度，这里按最终尺寸再居中一次
            if (centerOnStart) { CenterOnPrimary(); centerOnStart = false; }
            try { EngineStateRefresh(); } catch (Exception ex) { Log.Warn("读取引擎状态失败", ex); }
        }

        void EngineStateRefresh()
        {
            try { engineUp = Engine.Running; }
            catch (Exception ex) { Log.Debug("读取引擎状态失败", ex); }
            Invalidate();
        }

        public void Popup()
        {
            if (!Visible) Show();
            ShowWindowFront();
        }

        static string PosFilePath()
        {
            try
            {
                string a = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                if (string.IsNullOrEmpty(a)) return null;
                string d = System.IO.Path.Combine(a, "MIXLINE Scene Manager");
                System.IO.Directory.CreateDirectory(d);
                return System.IO.Path.Combine(d, "window-pos.txt");
            }
            catch { return null; }
        }

        void SavePosFile()
        {
            try
            {
                string p = PosFilePath();
                if (p == null) return;
                int pct = Math.Max(72, Dpi.Percent);
                int w = (int)Math.Round(ClientSize.Width * 100.0 / pct);
                int h = (int)Math.Round(ClientSize.Height * 100.0 / pct);
                System.IO.File.WriteAllText(p, Location.X + "|" + Location.Y + "|" + w + "|" + h);
            }
            catch (Exception ex) { Log.Warn("保存窗口位置文件失败", ex); }
        }

        bool TryReadPosFile(out int x, out int y, out int w, out int h)
        {
            x = y = w = h = 0;
            try
            {
                string p = PosFilePath();
                if (p == null || !System.IO.File.Exists(p)) return false;
                var parts = System.IO.File.ReadAllText(p).Split('|');
                if (parts.Length != 4) return false;
                if (!int.TryParse(parts[0], out x)) return false;
                if (!int.TryParse(parts[1], out y)) return false;
                if (!int.TryParse(parts[2], out w)) return false;
                if (!int.TryParse(parts[3], out h)) return false;
                return w >= 300 && h >= 120;
            }
            catch { return false; }
        }

        public void SavePos()
        {
            if (WindowState != FormWindowState.Normal) return;   // 最小化/最大化时保留上次普通位置
            SavePosFile();                                       // 用户目录单独存一份，配置目录不可写时也不会丢
            try
            {
                Log.Guard("保存窗口位置", delegate
                {
                    int pct = Math.Max(72, Dpi.Percent);
                    // 和 LoadPos 对称：保存客户区尺寸（逻辑像素），避免每次启动都把边框多算一遍导致窗口越来越大
                    store.SetWindow(Location, new Size(
                        (int)Math.Round(ClientSize.Width * 100.0 / pct),
                        (int)Math.Round(ClientSize.Height * 100.0 / pct)));
                    store.Save();
                });
            }
            catch (Exception ex) { Log.Warn("保存窗口位置失败", ex); }
        }

        bool TryPlace(int sx, int sy)
        {
            var pt = new Point(sx, sy);
            Screen target = null;
            foreach (var s in Screen.AllScreens)
                if (s.WorkingArea.Contains(pt)) { target = s; break; }
            if (target == null) target = Screen.FromPoint(pt);
            if (target == null) return false;
            var wa = target.WorkingArea;
            if (wa.Width < Width || wa.Height < Height) return false;
            // 只要上次位置的点还落在某块屏幕里，就沿用它并按当前窗口尺寸夹进工作区；
            // 不再因为几像素的尺寸变化就丢掉位置回主屏居中
            Location = new Point(
                Math.Max(wa.Left, Math.Min(sx, wa.Right - Width)),
                Math.Max(wa.Top, Math.Min(sy, wa.Bottom - Height)));
            return true;
        }

        void LoadPos()
        {
            bool placed = false;
            int x, y, w, h;
            if (TryReadPosFile(out x, out y, out w, out h))
            {
                ClientSize = new Size(Math.Max(Dpi.S(360), Dpi.S(w)), Math.Max(Dpi.S(120), Dpi.S(h)));
                FitHeight();
                placed = TryPlace(x, y);
            }
            if (!placed && store.HasWindow && store.WinW >= 300 && store.WinH >= 120)
            {
                ClientSize = new Size(Math.Max(Dpi.S(360), Dpi.S(store.WinW)), Math.Max(Dpi.S(120), Dpi.S(store.WinH)));
                FitHeight();
                placed = TryPlace(store.WinX, store.WinY);
                if (!placed) Log.Info("窗口位置越界，改为在主屏居中显示");
            }
            if (!placed) { CenterOnPrimary(); centerOnStart = true; }
        }

        /// <summary>把窗口放在主显示器工作区的正中间。</summary>
        void CenterOnPrimary()
        {
            var wa = Screen.PrimaryScreen.WorkingArea;
            int x = wa.Left + Math.Max(0, (wa.Width - Width) / 2);
            int y = wa.Top + Math.Max(0, (wa.Height - Height) / 2);
            Location = new Point(x, y);
        }
    }
}
