// MIXLINE Scene Manager — Protocol/MixlineEngine.cs
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
    // ============================ 引擎（HTTP/2 + gRPC） ============================
    class Engine
    {
        const string REG = @"Software\Logi\MIXLINE";
        public static string LastErr = "";

        public static int Port()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(REG))
                {
                    if (k == null) return 0;
                    object v = k.GetValue("Port");
                    return v == null ? 0 : Convert.ToInt32(v);
                }
            }
            catch { return 0; }
        }
        public static string Token()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(REG))
                {
                    if (k == null) return null;
                    object v = k.GetValue("AccessToken");
                    return v == null ? null : v.ToString();
                }
            }
            catch { return null; }
        }
        static DateTime probeAt = DateTime.MinValue; static bool probeVal;
        public static bool Running
        {
            get
            {
                if ((DateTime.UtcNow - probeAt).TotalMilliseconds < 700) return probeVal;
                probeAt = DateTime.UtcNow;
                int pt = Port();
                probeVal = pt > 0 && (CanConnect("127.0.0.1", pt) || CanConnect("::1", pt));
                return probeVal;
            }
        }
        static bool CanConnect(string host, int port)
        {
            try
            {
                using (var c = new TcpClient())
                {
                    var ar = c.BeginConnect(host, port, null, null);
                    if (!ar.AsyncWaitHandle.WaitOne(400)) return false;
                    try { c.EndConnect(ar); return true; } catch { return false; }
                }
            }
            catch { return false; }
        }
        public static string ExePath()
        {
            string p = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Logi\MIXLINE\MIXLINE\MIXLINE.exe");
            return File.Exists(p) ? p : null;
        }
        public static void Launch()
        {
            string e = ExePath();
            if (e != null) try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e) { UseShellExecute = true }); } catch (Exception exIgnored1) { Log.Warn("MixlineEngine.cs: 忽略可恢复异常", exIgnored1); }
        }

        // ---- HTTP/2 帧 ----
        static void Frame(Stream s, int type, int flags, int stream, byte[] payload)
        {
            int len = payload == null ? 0 : payload.Length;
            var h = new byte[9];
            h[0] = (byte)(len >> 16); h[1] = (byte)(len >> 8); h[2] = (byte)len;
            h[3] = (byte)type; h[4] = (byte)flags;
            h[5] = (byte)((stream >> 24) & 0x7f); h[6] = (byte)(stream >> 16); h[7] = (byte)(stream >> 8); h[8] = (byte)stream;
            s.Write(h, 0, 9);
            if (len > 0) s.Write(payload, 0, len);
            s.Flush();
        }

        // HPACK: literal header field without indexing, new name
        static void HpackString(MemoryStream m, string s)
        {
            var b = Encoding.UTF8.GetBytes(s);
            int len = b.Length;
            if (len < 127) m.WriteByte((byte)len);
            else { int v = len; var tmp = new List<byte>(); while (v >= 128) { tmp.Add((byte)((v & 0x7f) | 0x80)); v >>= 7; } tmp.Add((byte)v); foreach (var x in tmp) m.WriteByte(x); }
            m.Write(b, 0, b.Length);
        }
        static byte[] HpackHeaders(string path, int port, string token)
        {
            var m = new MemoryStream();
            Action<string, string> add = (n, v) => { m.WriteByte(0x00); HpackString(m, n); HpackString(m, v); };
            add(":method", "POST");
            add(":scheme", "http");
            add(":path", path);
            add(":authority", "127.0.0.1:" + port);
            add("content-type", "application/grpc");
            add("te", "trailers");
            add("user-agent", "asr-switcher/1");
            if (!string.IsNullOrEmpty(token)) add("authorization", "Bearer " + token);
            return m.ToArray();
        }

        static byte[] GrpcBody(byte[] msg)
        {
            int n = msg == null ? 0 : msg.Length;
            var o = new byte[5 + n];
            o[0] = 0;
            o[1] = (byte)(n >> 24); o[2] = (byte)(n >> 16); o[3] = (byte)(n >> 8); o[4] = (byte)n;
            if (n > 0) Array.Copy(msg, 0, o, 5, n);
            return o;
        }

        // 一次调用：返回响应消息体（可能为空）
        static byte[] Call(string path, byte[] req, int timeoutMs)
        {
            int port = Port(); string tok = Token();
            if (port <= 0) throw new Exception("MIXLINE 未运行（注册表无 Port）");
            using (var tcp = new TcpClient())
            {
                tcp.NoDelay = true;
                var ar = tcp.BeginConnect("127.0.0.1", port, null, null);
                if (!ar.AsyncWaitHandle.WaitOne(2500))
                {
                    Log.Warn("连接 MIXLINE 引擎超时（端口 " + port + "）");
                    throw new Exception("连接引擎超时");
                }
                tcp.EndConnect(ar);
                tcp.ReceiveTimeout = timeoutMs;
                using (var st = tcp.GetStream())
                {
                    st.ReadTimeout = timeoutMs;
                    var pre = Encoding.ASCII.GetBytes("PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n");
                    st.Write(pre, 0, pre.Length);
                    Frame(st, 4, 0, 0, null);                                    // SETTINGS
                    Frame(st, 1, 0x4, 1, HpackHeaders(path, port, tok));         // HEADERS (END_HEADERS)
                    Frame(st, 0, 0x1, 1, GrpcBody(req));                         // DATA (END_STREAM)

                    var data = new MemoryStream();
                    var hb = new byte[9];
                    var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
                    while (DateTime.UtcNow < deadline)
                    {
                        if (!ReadFull(st, hb, 9)) break;
                        int len = (hb[0] << 16) | (hb[1] << 8) | hb[2];
                        int type = hb[3], flags = hb[4];
                        int sid = ((hb[5] & 0x7f) << 24) | (hb[6] << 16) | (hb[7] << 8) | hb[8];
                        byte[] pl = null;
                        if (len > 0) { pl = new byte[len]; if (!ReadFull(st, pl, len)) break; }

                        if (type == 4 && (flags & 0x1) == 0) { Frame(st, 4, 0x1, 0, null); continue; }  // SETTINGS -> ACK
                        if (type == 0 && sid == 1) { data.Write(pl, 0, pl.Length); if ((flags & 0x1) != 0) break; continue; }
                        if (type == 1 && sid == 1) { if ((flags & 0x1) != 0) break; continue; }          // HEADERS/trailers
                        if (type == 7)
                {
                    Log.Warn("引擎返回 GOAWAY（" + path + "）");
                    throw new Exception("引擎 GOAWAY");
                }
                    }
                    // 解析 gRPC 消息（可能有多帧，取第一条）
                    var all = data.ToArray();
                    if (all.Length < 5) return new byte[0];
                    int mlen = (all[1] << 24) | (all[2] << 16) | (all[3] << 8) | all[4];
                    if (mlen <= 0 || all.Length < 5 + mlen) return new byte[0];
                    var msg = new byte[mlen];
                    Array.Copy(all, 5, msg, 0, mlen);
                    return msg;
                }
            }
        }
        static bool ReadFull(Stream s, byte[] buf, int n)
        {
            int off = 0;
            while (off < n)
            {
                int r;
                try { r = s.Read(buf, off, n - off); } catch { return false; }
                if (r <= 0) return false;
                off += r;
            }
            return true;
        }

        // ---- 高层 API ----
        public static Snap GetGraph()
        {
            var body = Call("/protocol.audio.GraphService/GetGraph", new byte[0], 6000);
            var snap = new Snap();
            var r = new PbR(body);
            while (!r.End)
            {
                ulong key = r.Varint(); int no = (int)(key >> 3), wt = (int)(key & 7);
                if (wt != 2) { r.Skip(wt); continue; }
                var sub = new PbR(r.Bytes());
                if (no == 1)
                {
                    var n = new Node();
                    var sr = new PbR(sub.Bytes());
                    while (!sr.End)
                    {
                        ulong k2 = sr.Varint(); int n2 = (int)(k2 >> 3), w2 = (int)(k2 & 7);
                        if (w2 == 2 && n2 == 1) n.id = sr.Str();
                        else if (w2 == 2 && n2 == 10) n.kind = "app";
                        else if (w2 == 2 && n2 == 11) n.kind = "mic";
                        else sr.Skip(w2);
                    }
                    snap.ins.Add(n);
                }
                else if (no == 2)
                {
                    var n = new Node();
                    var sr = new PbR(sub.Bytes());
                    while (!sr.End)
                    {
                        ulong k2 = sr.Varint(); int n2 = (int)(k2 >> 3), w2 = (int)(k2 & 7);
                        if (w2 == 2 && n2 == 1) n.id = sr.Str();
                        else if (w2 == 2 && n2 == 10) n.kind = "speaker";
                        else if (w2 == 2 && n2 == 11) n.kind = "stream";
                        else sr.Skip(w2);
                    }
                    snap.outs.Add(n);
                }
                else if (no == 3)
                {
                    var l = new GLink();
                    var sr = new PbR(sub.Bytes());
                    while (!sr.End)
                    {
                        ulong k2 = sr.Varint(); int n2 = (int)(k2 >> 3), w2 = (int)(k2 & 7);
                        if (w2 == 2 && n2 == 1) l.id = sr.Str();
                        else if (w2 == 2 && n2 == 2) l.inId = sr.Str();
                        else if (w2 == 2 && n2 == 3) l.outId = sr.Str();
                        else if (w2 == 2 && n2 == 4)
                        {
                            var vr = new PbR(sr.Bytes());
                            while (!vr.End)
                            {
                                ulong k3 = vr.Varint(); int n3 = (int)(k3 >> 3), w3 = (int)(k3 & 7);
                                if (w3 == 5 && n3 == 1) l.level = vr.Fixed32();
                                else if (w3 == 0 && n3 == 2) l.muted = vr.Varint() != 0;
                                else vr.Skip(w3);
                            }
                        }
                        else sr.Skip(w2);
                    }
                    snap.links.Add(l);
                }
                else r.Skip(wt);
            }
            // 补 key（需要设备/应用 id 从子消息取，简化：用 id 自身做 key 不可行）
            return snap;
        }

        // 因为 InputComponent/OutputComponent 的 key 在嵌套子消息里，单独解析一遍拿 key
        public static Dictionary<string, string> GetApplicationNames(out Dictionary<string, string> exePaths)
        {
            var map = new Dictionary<string, string>();
            exePaths = new Dictionary<string, string>();
            try
            {
                var body = Call("/protocol.audio.ApplicationService/GetApplications", new byte[0], 6000);
                var r = new PbR(body);
                while (!r.End)
                {
                    ulong k = r.Varint(); int no = (int)(k >> 3), wt = (int)(k & 7);
                    if (wt != 2) { r.Skip(wt); continue; }
                    var app = r.Bytes();
                    if (no != 1) continue;              // Applications.applications
                    string id = null, nm = null, exe = null;
                    var ar = new PbR(app);
                    while (!ar.End)
                    {
                        ulong k2 = ar.Varint(); int n2 = (int)(k2 >> 3), w2 = (int)(k2 & 7);
                        if (w2 == 2 && n2 == 1) id = ar.Str();
                        else if (w2 == 2 && n2 == 3) exe = ar.Str();
                        else if (w2 == 2 && n2 == 4) nm = ar.Str();
                        else ar.Skip(w2);
                    }
                    if (string.IsNullOrEmpty(id)) continue;
                    string disp = !string.IsNullOrEmpty(nm) ? nm :
                                  (!string.IsNullOrEmpty(exe) ? System.IO.Path.GetFileNameWithoutExtension(exe) : null);
                    if (!string.IsNullOrEmpty(disp)) map[id] = disp;
                    if (!string.IsNullOrEmpty(exe)) exePaths[id] = exe;
                }
            }
            catch (Exception exIgnored2) { Log.Warn("MixlineEngine.cs: 忽略可恢复异常", exIgnored2); }
            return map;
        }

        public static Dictionary<string, string> GetDeviceNames()
        {
            var map = new Dictionary<string, string>();
            var body = Call("/protocol.audio.DeviceService/GetDevices", new byte[0], 6000);
            var r = new PbR(body);
            while (!r.End)
            {
                ulong k = r.Varint(); int no = (int)(k >> 3), wt = (int)(k & 7);
                if (wt != 2) { r.Skip(wt); continue; }
                var dev = r.Bytes();
                if (no != 3) continue;                 // field 3 = repeated Device
                string id = null, name = null, desc = null;
                var dr = new PbR(dev);
                while (!dr.End)
                {
                    ulong k2 = dr.Varint(); int n2 = (int)(k2 >> 3), w2 = (int)(k2 & 7);
                    if (w2 == 2 && n2 == 1) id = dr.Str();
                    else if (w2 == 2 && n2 == 2) name = dr.Str();
                    else if (w2 == 2 && n2 == 3) desc = dr.Str();
                    else dr.Skip(w2);
                }
                if (string.IsNullOrEmpty(id)) continue;
                string disp = string.IsNullOrEmpty(name) ? (desc ?? id.Substring(0, 8)) :
                              (string.IsNullOrEmpty(desc) ? name : name + " · " + desc);
                map[id] = disp;
            }
            return map;
        }

        public static byte[] DumpRpc(string path, byte[] req)
        {
            return Call(path, req, 6000);
        }
        public static Snap GetGraphFull()
        {
            var snap = new Snap();
            var body = Call("/protocol.audio.GraphService/GetGraph", new byte[0], 6000);
            var r = new PbR(body);
            while (!r.End)
            {
                ulong key = r.Varint(); int no = (int)(key >> 3), wt = (int)(key & 7);
                if (wt != 2) { r.Skip(wt); continue; }
                var raw = r.Bytes();
                if (no == 1) { var n = ParseInput(raw); if (n != null) snap.ins.Add(n); }
                else if (no == 2) { var n = ParseOutput(raw); if (n != null) snap.outs.Add(n); }
                else if (no == 3) { var l = ParseLink(raw); if (l != null) snap.links.Add(l); }
            }
            return snap;
        }
        static Node ParseInput(byte[] raw)
        {
            var n = new Node(); var r = new PbR(raw);
            while (!r.End)
            {
                ulong k = r.Varint(); int no = (int)(k >> 3), wt = (int)(k & 7);
                if (wt == 2 && no == 1) n.id = r.Str();
                else if (wt == 2 && (no == 10 || no == 11))
                {
                    var sub = r.Bytes(); var sr = new PbR(sub);
                    n.kind = no == 10 ? "app" : "mic";
                    while (!sr.End)
                    {
                        ulong k2 = sr.Varint(); int n2 = (int)(k2 >> 3), w2 = (int)(k2 & 7);
                        if (w2 == 2 && n2 == 1) n.key = sr.Str(); else sr.Skip(w2);
                    }
                }
                else r.Skip(wt);
            }
            return string.IsNullOrEmpty(n.id) ? null : n;
        }
        static Node ParseOutput(byte[] raw)
        {
            var n = new Node(); var r = new PbR(raw);
            while (!r.End)
            {
                ulong k = r.Varint(); int no = (int)(k >> 3), wt = (int)(k & 7);
                if (wt == 2 && no == 1) n.id = r.Str();
                else if (wt == 2 && (no == 10 || no == 11))
                {
                    var sub = r.Bytes(); var sr = new PbR(sub);
                    n.kind = no == 10 ? "speaker" : "stream";
                    while (!sr.End)
                    {
                        ulong k2 = sr.Varint(); int n2 = (int)(k2 >> 3), w2 = (int)(k2 & 7);
                        if (w2 == 2 && n2 == 1) n.key = sr.Str(); else sr.Skip(w2);
                    }
                }
                else r.Skip(wt);
            }
            return string.IsNullOrEmpty(n.id) ? null : n;
        }
        static GLink ParseLink(byte[] raw)
        {
            var l = new GLink(); var r = new PbR(raw);
            while (!r.End)
            {
                ulong k = r.Varint(); int no = (int)(k >> 3), wt = (int)(k & 7);
                if (wt == 2 && no == 1) l.id = r.Str();
                else if (wt == 2 && no == 2) l.inId = r.Str();
                else if (wt == 2 && no == 3) l.outId = r.Str();
                else if (wt == 2 && no == 4)
                {
                    var vr = new PbR(r.Bytes());
                    while (!vr.End)
                    {
                        ulong k3 = vr.Varint(); int n3 = (int)(k3 >> 3), w3 = (int)(k3 & 7);
                        if (w3 == 5 && n3 == 1) l.level = vr.Fixed32();
                        else if (w3 == 0 && n3 == 2) l.muted = vr.Varint() != 0;
                        else vr.Skip(w3);
                    }
                }
                else r.Skip(wt);
            }
            return string.IsNullOrEmpty(l.id) ? null : l;
        }

        static byte[] Req(string v) { var w = new PbW(); w.Str(1, v); return w.Bytes(); }
        public static Node AddMic(string dev) { return ParseInput(Call("/protocol.audio.GraphService/AddMicrophoneInput", Req(dev), 6000)); }
        public static Node AddApp(string app) { return ParseInput(Call("/protocol.audio.GraphService/AddAppCaptureInput", Req(app), 6000)); }
        public static Node AddSpeaker(string dev) { return ParseOutput(Call("/protocol.audio.GraphService/AddSpeakerOutput", Req(dev), 6000)); }
        public static Node AddStream(string dev) { return ParseOutput(Call("/protocol.audio.GraphService/AddStreamOutput", Req(dev), 6000)); }

        public static void AddLink(string inId, string outId)
        {
            var w = new PbW(); w.Str(1, inId); w.Str(2, outId);
            Call("/protocol.audio.GraphService/AddLink", w.Bytes(), 6000);
        }
        public static void RemoveLink(string linkId)
        {
            var w = new PbW(); w.Str(1, linkId);
            Call("/protocol.audio.GraphService/RemoveLink", w.Bytes(), 6000);
        }
        // 单向（client-streaming RPC，不等响应）
        public static void SetLinkVolume(string linkId, float level, bool muted)
        {
            try
            {
                var w = new PbW(); w.Str(1, linkId); w.Msg(2, PbW.Volume(level, muted));
                OneWay("/protocol.audio.GraphService/SetLinkVolume", w.Bytes());
            }
            catch (Exception exIgnored3) { Log.Warn("MixlineEngine.cs: 忽略可恢复异常", exIgnored3); }
        }
        public static void SetOutputVolume(string outId, float level, bool muted)
        {
            try
            {
                var w = new PbW(); w.Str(1, outId); w.Msg(2, PbW.Volume(level, muted));
                OneWay("/protocol.audio.GraphService/SetOutputVolume", w.Bytes());
            }
            catch (Exception exIgnored4) { Log.Warn("MixlineEngine.cs: 忽略可恢复异常", exIgnored4); }
        }
        static void OneWay(string path, byte[] req)
        {
            int port = Port(); string tok = Token();
            if (port <= 0) return;
            using (var tcp = new TcpClient())
            {
                tcp.NoDelay = true;
                var ar = tcp.BeginConnect("127.0.0.1", port, null, null);
                if (!ar.AsyncWaitHandle.WaitOne(1500)) return;
                tcp.EndConnect(ar);
                using (var st = tcp.GetStream())
                {
                    var pre = Encoding.ASCII.GetBytes("PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n");
                    st.Write(pre, 0, pre.Length);
                    Frame(st, 4, 0, 0, null);
                    Frame(st, 1, 0x4, 1, HpackHeaders(path, port, tok));
                    Frame(st, 0, 0x1, 1, GrpcBody(req));
                    // 读回：等服务端响应（或 300ms 超时）再关闭连接，替代固定 Sleep(60)
                    try
                    {
                        st.ReadTimeout = 300;
                        var buf = new byte[128];
                        st.Read(buf, 0, buf.Length);
                    }
                    catch (Exception exRead) { Log.Debug("OneWay 等待响应超时/中断", exRead); }
                }
            }
        }
    }

}
