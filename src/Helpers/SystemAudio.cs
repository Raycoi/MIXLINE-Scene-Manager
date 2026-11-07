// MIXLINE Scene Manager — Helpers/SystemAudio.cs
// 读取/设置 Windows 默认音频设备（播放=输出，录制=输入）。
// 枚举用文档化的 IMMDeviceEnumerator；设置默认设备用广泛使用的未公开 IPolicyConfig。
// 所有方法都做异常兜底：失败返回 false + 原因，绝不让音频操作拖垮主流程。
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Asr
{
    class AudioEndpoint
    {
        public string Id;
        public string Name;
        public bool IsInput;
        public override string ToString() { return Name; }
    }

    static class SystemAudio
    {
        // ---------- COM 枚举 ----------
        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        class MMDeviceEnumerator { }

        enum EDataFlow { eRender = 0, eCapture = 1, eAll = 2 }
        enum ERole { eConsole = 0, eMultimedia = 1, eCommunications = 2 }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDeviceEnumerator
        {
            int EnumAudioEndpoints(EDataFlow dataFlow, int dwStateMask, out IMMDeviceCollection devices);
            int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice endpoint);
            int GetDevice(string id, out IMMDevice device);
            int RegisterEndpointNotificationCallback(IntPtr client);
            int UnregisterEndpointNotificationCallback(IntPtr client);
        }

        [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDeviceCollection
        {
            int GetCount(out int count);
            int Item(int index, out IMMDevice device);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDevice
        {
            int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, out IntPtr iface);
            int OpenPropertyStore(int stgmAccess, out IPropertyStore properties);
            int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
            int GetState(out int state);
        }

        [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IPropertyStore
        {
            int GetCount(out uint count);
            int GetAt(uint index, out PROPERTYKEY key);
            int GetValue(ref PROPERTYKEY key, out PROPVARIANT value);
            int SetValue(ref PROPERTYKEY key, ref PROPVARIANT value);
            int Commit();
        }

        [StructLayout(LayoutKind.Sequential)]
        struct PROPERTYKEY { public Guid fmtid; public uint pid; }

        [StructLayout(LayoutKind.Explicit)]
        struct PROPVARIANT
        {
            [FieldOffset(0)] public ushort vt;
            [FieldOffset(8)] public IntPtr pointer;
        }

        [DllImport("ole32.dll")] static extern int PropVariantClear(ref PROPVARIANT pvar);

        static PROPERTYKEY PkeyFriendlyName()
        {
            var k = new PROPERTYKEY();
            k.fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0");
            k.pid = 14;
            return k;
        }

        const int DEVICE_STATE_ACTIVE = 0x1;
        const int STGM_READ = 0;

        // ---------- IPolicyConfig（设置默认设备；未公开但长期稳定） ----------
        [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
        class PolicyConfigClient { }

        [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IPolicyConfig
        {
            int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceName, out IntPtr format);
            int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceName, bool defaultFormat, out IntPtr format);
            int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceName);
            int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceName, IntPtr endpointFormat, IntPtr mixFormat);
            int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceName, bool defaultPeriod, out long defaultPeriodOut, out long minimumPeriodOut);
            int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceName, ref long period);
            int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceName, out IntPtr mode);
            int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceName, IntPtr mode);
            int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceName, ref PROPERTYKEY key, out PROPVARIANT value);
            int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceName, ref PROPERTYKEY key, ref PROPVARIANT value);
            int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceName, ERole role);
            int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string deviceName, bool visible);
        }

        // ---------- 对外 API ----------

        /// <summary>列出当前接入的播放（输出）或录制（输入）设备。</summary>
        public static List<AudioEndpoint> List(bool input)
        {
            var list = new List<AudioEndpoint>();
            try
            {
                var en = (IMMDeviceEnumerator)new MMDeviceEnumerator();
                IMMDeviceCollection col;
                int hr = en.EnumAudioEndpoints(input ? EDataFlow.eCapture : EDataFlow.eRender, DEVICE_STATE_ACTIVE, out col);
                if (hr != 0 || col == null) return list;
                int count;
                if (col.GetCount(out count) != 0) return list;
                for (int i = 0; i < count; i++)
                {
                    IMMDevice dev;
                    if (col.Item(i, out dev) != 0 || dev == null) continue;
                    string id;
                    if (dev.GetId(out id) != 0) continue;
                    string name = FriendlyName(dev);
                    list.Add(new AudioEndpoint { Id = id, Name = name, IsInput = input });
                }
            }
            catch (Exception ex) { Log.Warn("枚举音频设备失败", ex); }
            return list;
        }

        static string FriendlyName(IMMDevice dev)
        {
            string name = "";
            try
            {
                IPropertyStore props;
                if (dev.OpenPropertyStore(STGM_READ, out props) == 0 && props != null)
                {
                    var key = PkeyFriendlyName();
                    PROPVARIANT v;
                    if (props.GetValue(ref key, out v) == 0)
                    {
                        if (v.vt == 31 && v.pointer != IntPtr.Zero) name = Marshal.PtrToStringUni(v.pointer);
                        PropVariantClear(ref v);
                    }
                }
            }
            catch (Exception ex) { Log.Debug("读取设备名失败", ex); }
            return name ?? "";
        }

        /// <summary>当前默认设备（某角色）。</summary>
        public static string CurrentDefault(bool input, int role)
        {
            try
            {
                var en = (IMMDeviceEnumerator)new MMDeviceEnumerator();
                IMMDevice dev;
                if (en.GetDefaultAudioEndpoint(input ? EDataFlow.eCapture : EDataFlow.eRender, (ERole)role, out dev) != 0 || dev == null) return null;
                string id;
                return dev.GetId(out id) == 0 ? id : null;
            }
            catch (Exception ex) { Log.Debug("读取默认音频设备失败", ex); return null; }
        }

        public static string CurrentDefaultName(bool input, int role)
        {
            string id = CurrentDefault(input, role);
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var e in List(input)) if (e.Id == id) return e.Name;
            return null;
        }

        /// <summary>把指定端点设为默认设备（Console/Multimedia/Communications 三个角色都设）。</summary>
        public static bool SetDefault(string endpointId, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(endpointId)) { error = "设备 ID 为空"; return false; }
            try
            {
                var policy = (IPolicyConfig)new PolicyConfigClient();
                bool ok = false;
                for (int r = 0; r < 3; r++)
                {
                    int hr = policy.SetDefaultEndpoint(endpointId, (ERole)r);
                    if (hr == 0) ok = true;
                    else if (error == null) error = "SetDefaultEndpoint HRESULT=0x" + hr.ToString("X8");
                }
                if (!ok && error == null) error = "设置默认设备失败";
                return ok;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Log.Warn("设置默认音频设备失败", ex);
                return false;
            }
        }

        /// <summary>设置后读回校验（三个角色里至少 Console 要对上）。</summary>
        public static bool VerifyDefault(string endpointId)
        {
            string cur = CurrentDefault(false, 0);
            if (cur == endpointId) return true;
            string curIn = CurrentDefault(true, 0);
            return curIn == endpointId;
        }

        // ---------- 名称匹配 ----------

        static readonly char[] Sep = { ' ', '\t', '·', '(', ')', '（', '）', '[', ']', '-', '_', '.', ',', '/', '\\', ':', ';', '+', '*' };

        /// <summary>把设备名拆成小写词元（保留中文/字母/数字，按分隔符切开）。</summary>
        public static List<string> Tokens(string name)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(name)) return list;
            foreach (var raw in name.Split(Sep, StringSplitOptions.RemoveEmptyEntries))
            {
                string t = raw.Trim().ToLowerInvariant();
                if (t.Length >= 2) list.Add(t);
            }
            return list;
        }

        /// <summary>want 的词元有多少比例出现在 cand 里（0~1）。</summary>
        public static double MatchScore(string want, string cand)
        {
            var w = Tokens(want);
            if (w.Count == 0) return 0;
            var c = new HashSet<string>(Tokens(cand));
            int hit = 0;
            foreach (var t in w) if (c.Contains(t)) hit++;
            return (double)hit / w.Count;
        }

        /// <summary>
        /// 在系统设备里找与 MIXLINE 完整描述（store.Names[key]，形如 "厂商型号 · 扬声器"）对应的端点。
        /// kind = speaker（播放）/ stream 或 mic（录制）。
        /// </summary>
        public static AudioEndpoint Resolve(string kind, string mixlineName, out string error)
        {
            error = null;
            bool input = kind != "speaker";
            var all = List(input);
            if (all.Count == 0) { error = input ? "系统里没有可用的录音设备" : "系统里没有可用的播放设备"; return null; }
            if (string.IsNullOrEmpty(mixlineName)) { error = "设备名称为空"; return null; }

            AudioEndpoint best = null; double bestScore = 0; int bestCount = 0;
            foreach (var e in all)
            {
                double sc = MatchScore(mixlineName, e.Name);
                if (sc > bestScore + 0.0001) { best = e; bestScore = sc; bestCount = 1; }
                else if (Math.Abs(sc - bestScore) <= 0.0001 && sc > 0) bestCount++;
            }
            if (best != null && bestScore >= 0.6 && bestCount == 1) return best;
            if (best != null && bestScore >= 0.6 && bestCount > 1)
            {
                error = "系统里有多个相似设备（" + mixlineName + "），请检查设备是否重名";
                return null;
            }
            error = "系统里找不到设备：" + mixlineName;
            return null;
        }

        /// <summary>把 MIXLINE 输出名设为系统默认播放设备。</summary>
        public static bool ApplyOutput(string mixlineName, out string matchedName, out string error)
        {
            matchedName = null;
            var ep = Resolve("speaker", mixlineName, out error);
            if (ep == null) return false;
            matchedName = ep.Name;
            return SetDefault(ep.Id, out error);
        }

        /// <summary>把 MIXLINE 输入名（stream/mic）设为系统默认录音设备。</summary>
        public static bool ApplyInput(string kind, string mixlineName, out string matchedName, out string error)
        {
            matchedName = null;
            var ep = Resolve(kind, mixlineName, out error);
            if (ep == null) return false;
            matchedName = ep.Name;
            return SetDefault(ep.Id, out error);
        }    }
}
