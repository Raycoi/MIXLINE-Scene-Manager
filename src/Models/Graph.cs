// MIXLINE Scene Manager — Models/Graph.cs
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
    // ============================ 图模型 ============================
    class Node { public string id, kind, key; }

    class GLink { public string id, inId, outId; public float level = 1; public bool muted; }

    class Snap { public List<Node> ins = new List<Node>(); public List<Node> outs = new List<Node>(); public List<GLink> links = new List<GLink>(); }

}
