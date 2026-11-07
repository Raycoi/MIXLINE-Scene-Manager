// MIXLINE Scene Manager — tests/TestFramework.cs
// 极简测试框架：无第三方依赖，直接 csc 编译。被测试代码必须离线可运行。
using System;
using System.Collections.Generic;
using System.Text;

namespace Asr.Tests
{
    static class T
    {
        static int passed, failed, skipped;
        static readonly List<string> failures = new List<string>();
        static string currentGroup = "";

        public static void Group(string name)
        {
            currentGroup = name;
            Console.WriteLine();
            Console.WriteLine("== " + name + " ==");
        }

        public static void Case(string name, Action body)
        {
            try
            {
                body();
                passed++;
                Console.WriteLine("  [PASS] " + name);
            }
            catch (AssertException ex)
            {
                failed++;
                string line = name + " :: " + ex.Message;
                failures.Add(line);
                Console.WriteLine("  [FAIL] " + line);
            }
            catch (Exception ex)
            {
                failed++;
                string line = name + " :: 未预期异常 " + ex.GetType().Name + ": " + ex.Message;
                failures.Add(line);
                Console.WriteLine("  [FAIL] " + line);
            }
        }

        public static void Skip(string name, string reason)
        {
            skipped++;
            Console.WriteLine("  [SKIP] " + name + " (" + reason + ")");
        }

        public static bool True(bool cond, string message)
        {
            if (!cond) throw new AssertException(message);
            return true;
        }

        public static void False(bool cond, string message) { True(!cond, message); }

        public static void Eq(object expected, object actual, string message)
        {
            bool same = expected == null ? actual == null : expected.Equals(actual);
            if (!same) throw new AssertException(message + "：期望 [" + Show(expected) + "] 实际 [" + Show(actual) + "]");
        }

        public static void NotNull(object v, string message)
        {
            if (v == null) throw new AssertException(message + "：为 null");
        }

        public static void Null(object v, string message)
        {
            if (v != null) throw new AssertException(message + "：应为 null");
        }

        static string Show(object o) { return o == null ? "null" : o.ToString(); }

        public static int Report()
        {
            Console.WriteLine();
            Console.WriteLine("--------------------------------------------------");
            Console.WriteLine("通过 " + passed + " · 失败 " + failed + " · 跳过 " + skipped);
            if (failures.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine("失败明细：");
                foreach (var f in failures) Console.WriteLine("  - " + f);
                return 1;
            }
            return 0;
        }

        public class AssertException : Exception
        {
            public AssertException(string message) : base(message) { }
        }
    }
}