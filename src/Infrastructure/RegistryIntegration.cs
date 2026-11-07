// MIXLINE Scene Manager — Infrastructure/RegistryIntegration.cs
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
    // ============================ 应用动作 ============================
    static class AppActions
    {
        public static void Launch() { Engine.Launch(); }

        public static void Integrate(IWin32Window owner, bool install, Store store)
        {
            const string KEY = @"Software\Logi\MIXLINE\UI";
            string path = Application.ExecutablePath;
            bool done = false;
            string denied = null;

            foreach (var view in new RegistryView[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, view))
                    {
                        using (var k = baseKey.OpenSubKey(KEY, true))
                        {
                            if (k == null) continue;
                            object cur = k.GetValue("FilePath");
                            string curS = cur == null ? "" : cur.ToString();
                            if (install)
                            {
                                store.UiBackup = curS; store.Save();
                                k.SetValue("FilePath", path, RegistryValueKind.String);
                            }
                            else
                            {
                                if (string.IsNullOrEmpty(store.UiBackup))
                                {
                                    MessageBox.Show(owner, "没有备份，无法还原。", "MIXLINE Scene Manager");
                                    return;
                                }
                                k.SetValue("FilePath", store.UiBackup, RegistryValueKind.String);
                            }
                            done = true;
                            break;
                        }
                    }
                }
                catch (UnauthorizedAccessException ex) { denied = ex.Message; }
                catch (Exception ex) { denied = ex.Message; }
            }

            if (done)
            {
                MessageBox.Show(owner,
                    install ? "已接入。\n\n之后在 MIXLINE 托盘菜单里点「打开 MIXLINE」，打开的就是本切换器；\n想恢复原界面，点「还原 MIXLINE 原版界面」。"
                            : "已还原，点「打开 MIXLINE」会重新打开原生界面。",
                    "MIXLINE Scene Manager");
                return;
            }

            MessageBox.Show(owner,
                "修改注册表被拒绝" + (denied == null ? "" : "（" + denied + "）") +
                "\n\n解决办法：右键本程序 → 以管理员身份运行一次，" +
                "\n重新点这一项后再正常双击启动即可。\n\n原理：把 " +
                "HKCU\\Software\\Logi\\MIXLINE\\UI 的 FilePath，\n这样在 MIXLINE 里点「打开 MIXLINE」启动的就是本切换器。\n不用这一项也行，其它功能不受影响。",
                "MIXLINE Scene Manager");
        }
    }

}
