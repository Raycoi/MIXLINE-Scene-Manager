// MIXLINE Scene Manager — Models/Scene.cs
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
    // ============================ 场景存储 ============================
    class SceneLink { public string sk, sv, dk, dv; public float level = 1; public bool muted; }

    class Scene
    {
        public string id, name, createdAt;
        public List<SceneLink> links = new List<SceneLink>();
        // 系统默认设备（Windows 端点）：显式选择时才有值；为空表示"不单独指定"
        public string sysInNode = "", sysInId = "", sysInName = "";
        public string sysOutNode = "", sysOutId = "", sysOutName = "";
        public List<SceneLink> outs = new List<SceneLink>();
        public bool match;
        public bool additive;   // 切换时保留现有连线，只增不删
    }

}
