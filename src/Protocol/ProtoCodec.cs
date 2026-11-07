// MIXLINE Scene Manager — Protocol/ProtoCodec.cs
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
    // ============================ protobuf ============================
    class PbW
    {
        readonly MemoryStream ms = new MemoryStream();
        public void Byte(int b) { ms.WriteByte((byte)b); }
        public void Varint(ulong v) { do { int b = (int)(v & 0x7f); v >>= 7; if (v != 0) b |= 0x80; ms.WriteByte((byte)b); } while (v != 0); }
        public void Tag(int no, int wt) { Varint((ulong)((no << 3) | wt)); }
        public void Str(int no, string s)
        {
            if (s == null) return;
            var b = Encoding.UTF8.GetBytes(s);
            Tag(no, 2); Varint((ulong)b.Length); ms.Write(b, 0, b.Length);
        }
        public void Float(int no, float f)
        {
            Tag(no, 5); var b = BitConverter.GetBytes(f); ms.Write(b, 0, 4);
        }
        public void Bool(int no, bool v) { Tag(no, 0); Varint(v ? 1UL : 0UL); }
        public void Msg(int no, byte[] body) { Tag(no, 2); Varint((ulong)body.Length); ms.Write(body, 0, body.Length); }
        public byte[] Bytes() { return ms.ToArray(); }
        public static byte[] Volume(float level, bool muted)
        {
            var w = new PbW(); w.Float(1, level); w.Bool(2, muted); return w.Bytes();
        }
    }

    class PbR
    {
        readonly byte[] b; int i;
        public PbR(byte[] buf) { b = buf; i = 0; }
        public bool End { get { return i >= b.Length; } }
        public ulong Varint()
        {
            ulong r = 0; int shift = 0; byte c;
            do { if (i >= b.Length) return r; c = b[i++]; r |= (ulong)(c & 0x7f) << shift; shift += 7; } while ((c & 0x80) != 0);
            return r;
        }
        public byte[] Bytes()
        {
            ulong n = Varint(); int len = (int)n;
            if (i + len > b.Length) len = b.Length - i;
            var o = new byte[len]; Array.Copy(b, i, o, 0, len); i += len; return o;
        }
        public string Str() { return Encoding.UTF8.GetString(Bytes()); }
        public float Fixed32() { float f = BitConverter.ToSingle(b, i); i += 4; return f; }
        public void Skip(int wt)
        {
            if (wt == 0) Varint();
            else if (wt == 1) i += 8;
            else if (wt == 2) Bytes();
            else if (wt == 5) i += 4;
            else i = b.Length;
        }
        public int Idx { get { return i; } }
    }

}
