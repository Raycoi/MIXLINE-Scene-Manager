// MIXLINE Scene Manager — tests/TestProgram.cs
// 测试入口：只加载非 UI 逻辑（不含 Program.cs），因此完全离线运行。
using System;
using System.Text;

namespace Asr.Tests
{
    static class TestProgram
    {
        static int Main(string[] args)
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch { }
            Console.WriteLine("MIXLINE Scene Manager — 离线单元测试");
            Console.WriteLine("（不连接 MIXLINE 引擎；配置读写在临时目录进行）");
            int code = Tests.RunAll();
            Console.WriteLine();
            Console.WriteLine(code == 0 ? "结果：全部通过" : "结果：存在失败用例");
            return code;
        }
    }
}