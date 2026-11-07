// MIXLINE Scene Manager — Storage/IconCache.cs
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
    // ============================ 图标缓存 ============================
    static class Icons
    {
        public static string Dir;
        static readonly Dictionary<string, Image> cache = new Dictionary<string, Image>();

        public static void Init(string baseDir)
        {
            Dir = System.IO.Path.Combine(baseDir, "icons");
            try { System.IO.Directory.CreateDirectory(Dir); } catch (Exception exIgnored1) { Log.Warn("IconCache.cs: 忽略可恢复异常", exIgnored1); }
        }

        public static Image Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            Image im;
            if (cache.TryGetValue(id, out im)) return im;
            try
            {
                string f = System.IO.Path.Combine(Dir, id + ".png");
                if (System.IO.File.Exists(f))
                {
                    byte[] bytes = System.IO.File.ReadAllBytes(f);
                    var ms = new MemoryStream(bytes);
                    im = Image.FromStream(ms);
                    cache[id] = im;
                    return im;
                }
            }
            catch (Exception exIgnored2) { Log.Warn("IconCache.cs: 忽略可恢复异常", exIgnored2); }
            return null;
        }

        public static void Ensure(string id, string exePath)
        {
            try
            {
                if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(exePath)) return;
                if (!System.IO.File.Exists(exePath)) return;
                string f = System.IO.Path.Combine(Dir, id + ".png");
                if (System.IO.File.Exists(f)) return;
                Icon ic2 = ExtractBigIcon(exePath);
                if (ic2 != null)
                {
                    using (ic2) using (var bmp = ic2.ToBitmap()) bmp.Save(f, System.Drawing.Imaging.ImageFormat.Png);
                    return;
                }
                using (var ic = Icon.ExtractAssociatedIcon(exePath))
                {
                    if (ic == null) return;
                    using (var bmp = ic.ToBitmap()) bmp.Save(f, System.Drawing.Imaging.ImageFormat.Png);
                }
            }
            catch (Exception exIgnored3) { Log.Warn("IconCache.cs: 忽略可恢复异常", exIgnored3); }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern uint PrivateExtractIcons(string szFileName, int nIconIndex, int cxIcon, int cyIcon,
            IntPtr[] phicon, uint[] piconid, uint nIcons, uint flags);

        public static Icon ExtractBigIcon(string exePath)
        {
            try
            {
                var handles = new IntPtr[1];
                var ids = new uint[1];
                uint got = PrivateExtractIcons(exePath, 0, 64, 64, handles, ids, 1, 0);
                if (got > 0 && handles[0] != IntPtr.Zero)
                {
                    var ic = Icon.FromHandle(handles[0]);
                    return (Icon)ic.Clone();
                }
            }
            catch (Exception exIgnored4) { Log.Warn("IconCache.cs: 忽略可恢复异常", exIgnored4); }
            return null;
        }

        public static int NativeSide(string id)
        {
            var im = Get(id);
            if (im == null) return 0;
            return Math.Min(im.Width, im.Height);
        }

        public static void Clear()
        {
            cache.Clear();
            try
            {
                if (Dir == null || !System.IO.Directory.Exists(Dir)) return;
                foreach (var f in System.IO.Directory.GetFiles(Dir, "*.png")) System.IO.File.Delete(f);
            }
            catch (Exception exIgnored5) { Log.Warn("IconCache.cs: 忽略可恢复异常", exIgnored5); }
        }
    }

}
