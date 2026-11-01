// ---------------------------------------------------------------------------
//  Minecraft AI 控制面板 —— 卸载程序
//
//  单独编译成一个很小的 exe（十几 KB），安装时释放到安装目录里。
//  为什么不直接复制安装包当卸载器：安装包里内嵌了 node.exe（80+ MB），
//  复制一份会让安装目录白白多占一倍空间。
//
//  支持 /S 静默卸载；其余情况弹一个确认窗口。
// ---------------------------------------------------------------------------
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;

static class UninstallProgram
{
    const string AppName = "Minecraft AI 控制面板";
    const string AppExe = "MC-AI-Control.exe";
    const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\MC-AI-Control";

    static string StartMenuDir
    {
        get
        {
            string o = Environment.GetEnvironmentVariable("MCAI_STARTMENU_DIR");
            if (!string.IsNullOrEmpty(o)) return o;
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                @"Microsoft\Windows\Start Menu\Programs");
        }
    }

    static string DesktopDir
    {
        get
        {
            string o = Environment.GetEnvironmentVariable("MCAI_DESKTOP_DIR");
            if (!string.IsNullOrEmpty(o)) return o;
            return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        }
    }

    static string DefaultDir
    {
        get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\MC-AI-Control"); }
    }

    [STAThread]
    static int Main(string[] args)
    {
        bool silent = false;
        string explicitDir = null;
        foreach (string a in args)
        {
            string s = a.Trim();
            string low = s.ToLowerInvariant();
            if (low == "/s" || low == "-s" || low == "/silent") silent = true;
            if (low.StartsWith("/d=")) explicitDir = s.Substring(3).Trim('"', ' ');
        }

        string dir = explicitDir ?? RegistryDir() ?? DefaultDir;

        if (silent)
        {
            Run(dir);
            return 0;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new ConfirmWindow(dir));
        return 0;
    }

    static string RegistryDir()
    {
        try
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(UninstallKey))
                if (k != null) return k.GetValue("InstallLocation") as string;
        }
        catch { }
        return null;
    }

    static void Run(string dir)
    {
        try
        {
            foreach (Process p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(AppExe)))
            {
                try { p.Kill(); p.WaitForExit(3000); } catch { }
            }
        }
        catch { }

        SafeDelete(Path.Combine(StartMenuDir, AppName + ".lnk"));
        SafeDelete(Path.Combine(DesktopDir, AppName + ".lnk"));
        try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); } catch { }

        try
        {
            if (Directory.Exists(dir))
            {
                string self = Application.ExecutablePath;
                foreach (string f in Directory.GetFiles(dir))
                    if (!string.Equals(f, self, StringComparison.OrdinalIgnoreCase)) SafeDelete(f);
                foreach (string d in Directory.GetDirectories(dir))
                    try { Directory.Delete(d, true); } catch { }
                try { Directory.Delete(dir, true); } catch { }
            }
        }
        catch { }

        // 自己还占着目录里的文件，删不掉自己：交给一个延迟的 cmd 收尾
        if (Directory.Exists(dir))
        {
            try
            {
                var psi = new ProcessStartInfo("cmd.exe", "/c ping -n 3 127.0.0.1 >nul & rd /s /q \"" + dir.TrimEnd('\\') + "\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                Process.Start(psi);
            }
            catch { }
        }
    }

    static void SafeDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    // ---------------------------------------------------------------- 确认窗口
    sealed class ConfirmWindow : Form
    {
        readonly string dir;

        public ConfirmWindow(string installDir)
        {
            dir = installDir;
            Text = "卸载 " + AppName;
            ClientSize = new Size(520, 250);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(0x1b, 0x1b, 0x1b);
            ForeColor = Color.FromArgb(0xdc, 0xdc, 0xdc);
            Font = new Font("Microsoft YaHei UI", 9f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            Controls.Add(new Label
            {
                Bounds = new Rectangle(24, 22, 470, 34),
                Text = "确定要卸载 " + AppName + " 吗？",
                Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold),
                ForeColor = Color.White
            });

            Controls.Add(new Label
            {
                Bounds = new Rectangle(24, 64, 470, 80),
                ForeColor = Color.FromArgb(0x9a, 0x9a, 0x9a),
                Text = "将删除程序文件和快捷方式。\n数据目录里的配置、日志和机器人清单会保留，重装后继续用。\n\n安装位置：" + installDir
            });

            var yes = new Button { Bounds = new Rectangle(268, 180, 108, 32), Text = "卸载" };
            var no = new Button { Bounds = new Rectangle(388, 180, 108, 32), Text = "取消" };
            Style(yes, true);
            Style(no, false);
            yes.Click += (s, e) =>
            {
                Run(dir);
                MessageBox.Show("已经卸载完成。", AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                Close();
            };
            no.Click += (s, e) => Close();
            Controls.Add(yes);
            Controls.Add(no);
        }

        static void Style(Button b, bool primary)
        {
            Color green = Color.FromArgb(0x5c, 0x9a, 0x3a);
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 1;
            b.FlatAppearance.BorderColor = primary ? green : Color.FromArgb(0x55, 0x55, 0x55);
            b.BackColor = primary ? green : Color.FromArgb(0x33, 0x33, 0x33);
            b.ForeColor = Color.White;
            b.Cursor = Cursors.Hand;
        }
    }
}
