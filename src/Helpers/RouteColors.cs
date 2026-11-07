// MIXLINE Scene Manager — Helpers/RouteColors.cs
// 输入源配色：同一个输入设备在所有场景、所有界面中始终使用同一种颜色。
using System;
using System.Collections.Generic;
using System.Drawing;

namespace Asr
{
    static class RouteColors
    {
        static readonly Dictionary<string, int> map = new Dictionary<string, int>();
        static readonly object gate = new object();
        static int next;

        /// <summary>按输入设备 key 取稳定颜色索引（首次出现时按顺序分配，6 色循环）。</summary>
        public static int IndexOf(string key)
        {
            if (string.IsNullOrEmpty(key)) return 0;
            lock (gate)
            {
                int i;
                if (map.TryGetValue(key, out i)) return i;
                i = next++;
                map[key] = i;
                return i;
            }
        }

        public static Color For(string key)
        {
            return CurveColors.For(IndexOf(key));
        }
    }
}
