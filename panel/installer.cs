// ---------------------------------------------------------------------------
//  Minecraft AI 控制面板 —— 安装程序（csc 编译，不依赖 Inno/NSIS）
//
//  为什么自己写：这台机器上没有 Inno Setup / NSIS，也不想去装（要管理员权限）。
//  安装包本身就把 MC-AI-Control.exe 内嵌在资源里，装的时候释放到
//  %LOCALAPPDATA%\Programs\MC-AI-Control，**全程不需要管理员权限**。
//
//  向导：欢迎 → 许可协议（必须接受）→ 安装位置 → 安装中 → 完成
//  附加：开始菜单/桌面快捷方式、控制面板「程序和功能」里的卸载项、uninstall.exe
//
//  命令行：
//    setup.exe /S            静默安装到默认目录（不弹任何窗口）
//    setup.exe /U            卸载（有界面）
//    uninstall.exe           ——  装好后会复制一份到安装目录，点它就是卸载
// ---------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

static class SetupProgram
{
    const string AppName = "Minecraft AI 控制面板";
    const string AppFolder = "MC-AI-Control";
    const string AppExe = "MC-AI-Control.exe";
    const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\MC-AI-Control";
    const string AppVersion = "1.2.0";
    const string Publisher = "dkvkvk";

    static readonly Color Bg = Color.FromArgb(0x1b, 0x1b, 0x1b);
    static readonly Color PanelBg = Color.FromArgb(0x27, 0x27, 0x27);
    static readonly Color Fg = Color.FromArgb(0xdc, 0xdc, 0xdc);
    static readonly Color Dim = Color.FromArgb(0x9a, 0x9a, 0x9a);
    static readonly Color Green = Color.FromArgb(0x5c, 0x9a, 0x3a);
    static readonly Color GreenDark = Color.FromArgb(0x3f, 0x6b, 0x26);

    static string DefaultDir
    {
        get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\" + AppFolder); }
    }

    [STAThread]
    static int Main(string[] args)
    {
        bool silent = false, uninstall = false;
        string explicitDir = null;
        foreach (string a in args)
        {
            string s = a.Trim();
            string low = s.ToLowerInvariant();
            if (low == "/s" || low == "-s" || low == "/silent") silent = true;
            if (low == "/u" || low == "-u" || low == "/uninstall") uninstall = true;
            // /D=<目录>：指定安装位置（静默安装时用，也方便脚本化测试）
            if (low.StartsWith("/d=")) explicitDir = s.Substring(3).Trim('"', ' ');
        }
        // 装好后的 uninstall.exe 也是这份二进制，靠文件名判断身份
        string self = Path.GetFileName(Application.ExecutablePath);
        if (string.Equals(self, "uninstall.exe", StringComparison.OrdinalIgnoreCase)) uninstall = true;

        if (silent)
        {
            string err;
            if (uninstall)
            {
                DoUninstall(explicitDir ?? InstallDirFromRegistry() ?? DefaultDir, true);
                return 0;
            }
            bool ok = DoInstall(explicitDir ?? DefaultDir, true, true, out err);
            if (!ok)
            {
                // 静默模式下没有窗口可看，必须留一份记录（多处尝试，总有一处能写）
                string body = DateTime.Now + "  静默安装失败\n" + err + Environment.NewLine +
                              (Warnings.Count > 0 ? "告警:\n  " + string.Join("\n  ", Warnings.ToArray()) + Environment.NewLine : "");
                foreach (string p in new[]
                {
                    Path.Combine(Path.GetTempPath(), "MC-AI-Control-setup.log"),
                    Path.Combine(Path.GetDirectoryName(Application.ExecutablePath) ?? ".", "setup-error.log"),
                    Path.Combine(explicitDir ?? DefaultDir, "setup-error.log")
                })
                {
                    try { File.WriteAllText(p, body, Encoding.UTF8); break; } catch { }
                }
            }
            return ok ? 0 : 1;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(uninstall ? (Form)new UninstallWindow() : new WizardWindow());
        return 0;
    }

    // ---------------------------------------------------------------- 安装
    static string stage = "";          // 出错时用来指明卡在哪一步
    static readonly List<string> Warnings = new List<string>();

    // 正常情况用系统目录；MCAI_STARTMENU_DIR / MCAI_DESKTOP_DIR 只在测试或特殊部署时覆盖
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

    static void Trace(string s)
    {
        try
        {
            File.AppendAllText(Path.Combine(Path.GetDirectoryName(Application.ExecutablePath) ?? ".", "setup-trace.log"),
                DateTime.Now.ToString("HH:mm:ss.fff") + "  " + s + Environment.NewLine, Encoding.UTF8);
        }
        catch { }
    }

    static bool DoInstall(string dir, bool desktopShortcut, bool startMenuShortcut, out string err)
    {
        err = null;
        Warnings.Clear();
        try
        {
            Trace("enter DoInstall dir=" + dir);
            stage = "停止正在运行的旧版本";
            KillRunning();

            Trace("stage1 ok");
            stage = "创建安装目录 " + dir;
            Directory.CreateDirectory(dir);

            Trace("stage2 ok");
            stage = "释放主程序";
            string exePath = Path.Combine(dir, AppExe);
            ExtractResource(AppExe, exePath);

            // 便携 node.exe 一起装进去（构建时带了才会内嵌），目标机就不用另装 Node
            if (HasEmbeddedResource("node.exe"))
            {
                stage = "释放 node.exe";
                ExtractResource("node.exe", Path.Combine(dir, "node.exe"));
            }

            // 卸载程序 = 自己的一份拷贝
            Trace("stage3 ok");
            stage = "写入卸载程序";
            string uninstallPath = Path.Combine(dir, "uninstall.exe");
            if (!string.Equals(Application.ExecutablePath, uninstallPath, StringComparison.OrdinalIgnoreCase))
                File.Copy(Application.ExecutablePath, uninstallPath, true);

            Trace("stage4 ok");
            // 快捷方式和注册表属于「锦上添花」：失败不该让整个安装失败，
            // 但一定要记下来，装完能告诉用户哪一步没成。
            if (startMenuShortcut)
            {
                stage = "创建开始菜单快捷方式";
                TryStep(stage, delegate
                {
                    Directory.CreateDirectory(StartMenuDir);
                    CreateShortcut(Path.Combine(StartMenuDir, AppName + ".lnk"), exePath, dir);
                });
            }
            if (desktopShortcut)
            {
                stage = "创建桌面快捷方式";
                TryStep(stage, delegate
                {
                    Directory.CreateDirectory(DesktopDir);
                    CreateShortcut(Path.Combine(DesktopDir, AppName + ".lnk"), exePath, dir);
                });
            }

            Trace("stage5 ok");
            stage = "登记卸载信息";
            TryStep(stage, delegate { RegisterUninstall(dir, exePath, uninstallPath); });
            Trace("install done");
            return true;
        }
        catch (Exception ex)
        {
            err = "[" + stage + "] " + ex.GetType().Name + ": " + ex.Message;
            Trace("EXCEPTION " + err);
            return false;
        }
    }

    static void TryStep(string what, Action action)
    {
        try { action(); }
        catch (Exception ex)
        {
            Exception inner = ex;
            while (inner.InnerException != null) inner = inner.InnerException;
            string msg = what + "失败：" + inner.GetType().Name + ": " + inner.Message;
            Warnings.Add(msg);
            Trace("WARN " + msg);
        }
    }

    static void DoUninstall(string dir, bool silent)
    {
        KillRunning();
        try
        {
            SafeDelete(Path.Combine(StartMenuDir, AppName + ".lnk"));
            SafeDelete(Path.Combine(DesktopDir, AppName + ".lnk"));
            try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); } catch { }

            // 先删自己以外的文件，再把自己删掉
            string self = Application.ExecutablePath;
            if (Directory.Exists(dir))
            {
                foreach (string f in Directory.GetFiles(dir))
                {
                    if (string.Equals(f, self, StringComparison.OrdinalIgnoreCase)) continue;
                    SafeDelete(f);
                }
                if (!string.Equals(self, Path.Combine(dir, AppExe), StringComparison.OrdinalIgnoreCase))
                    SafeDelete(Path.Combine(dir, AppExe));
                try { Directory.Delete(dir, true); } catch { }
            }
            // 自己还占着目录里的 uninstall.exe，删不掉：交给一个延迟的 cmd 收尾
            if (Directory.Exists(dir)) ScheduleSelfDelete(dir);
        }
        catch { }
    }

    static void ScheduleSelfDelete(string dir)
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

    static void RegisterUninstall(string dir, string exePath, string uninstallPath)
    {
        using (RegistryKey k = Registry.CurrentUser.CreateSubKey(UninstallKey))
        {
            k.SetValue("DisplayName", AppName);
            k.SetValue("DisplayVersion", AppVersion);
            k.SetValue("Publisher", Publisher);
            k.SetValue("DisplayIcon", exePath);
            k.SetValue("InstallLocation", dir);
            k.SetValue("UninstallString", "\"" + uninstallPath + "\"");
            k.SetValue("QuietUninstallString", "\"" + uninstallPath + "\" /S /U");
            k.SetValue("NoModify", 1, RegistryValueKind.DWord);
            k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            try
            {
                long size = 0;
                foreach (string f in Directory.GetFiles(dir)) size += new FileInfo(f).Length;
                k.SetValue("EstimatedSize", (int)(size / 1024), RegistryValueKind.DWord);
            }
            catch { }
        }
    }

    static string InstallDirFromRegistry()
    {
        try
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(UninstallKey))
                if (k != null) return k.GetValue("InstallLocation") as string;
        }
        catch { }
        return null;
    }

    static void KillRunning()
    {
        try
        {
            foreach (Process p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(AppExe)))
            {
                try { p.Kill(); p.WaitForExit(3000); } catch { }
            }
        }
        catch { }
    }

    static void SafeDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    static bool HasEmbeddedResource(string name)
    {
        try
        {
            foreach (string n in Assembly.GetExecutingAssembly().GetManifestResourceNames())
                if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) return true;
        }
        catch { }
        return false;
    }

    static void ExtractResource(string res, string target)    {
        using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(res))
        {
            if (s == null) throw new Exception("安装包损坏：缺少 " + res);
            string tmp = target + ".tmp";
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var buf = new byte[65536];
                int n;
                while ((n = s.Read(buf, 0, buf.Length)) > 0) fs.Write(buf, 0, n);
                fs.Flush(true);
            }
            if (File.Exists(target)) File.Delete(target);
            File.Move(tmp, target);
        }
    }

    // 用 WScript.Shell 建快捷方式（反射，避免额外引用）
    static void CreateShortcut(string lnkPath, string targetExe, string workDir)
    {
        Type t = Type.GetTypeFromProgID("WScript.Shell");
        if (t == null) return;
        object shell = Activator.CreateInstance(t);
        object lnk = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnkPath });
        Type lt = lnk.GetType();
        lt.InvokeMember("TargetPath", BindingFlags.SetProperty, null, lnk, new object[] { targetExe });
        lt.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, lnk, new object[] { workDir });
        lt.InvokeMember("IconLocation", BindingFlags.SetProperty, null, lnk, new object[] { targetExe + ",0" });
        lt.InvokeMember("Description", BindingFlags.SetProperty, null, lnk, new object[] { AppName });
        lt.InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
    }

    // ---------------------------------------------------------------- 许可协议
    const string EulaText =
@"Minecraft AI 控制面板 —— 最终用户许可协议

1. 授权范围
   本软件免费提供，可自由安装、使用、复制与分发。你可以把它装在自己的电脑上，
   也可以拷给朋友。不需要付费，也没有任何功能限制。

2. 源代码
   本软件是开源项目（GitHub: dkvkvk/mc-ai）的构建产物。你可以查看、修改源码，
   但再分发修改版时请保留原作者署名。

3. 第三方组件
   本软件内含并依赖若干开源组件：minecraft 机器人框架 mindcraft 与 mineflayer
   （MIT）、Node.js（MIT）、Microsoft Edge WebView2 运行时（微软许可）、
   Minecraft 服务端（Mojang EULA）。它们各自的许可条款同样适用于你的使用。
   本软件不包含 Minecraft 游戏本体或服务端 jar，也不包含任何 Mojang 资源文件。

4. 使用风险
   本软件通过自动化程序连接 Minecraft 服务器。请只在你有权连接的服务器上使用，
   并遵守对应服务器与 Minecraft 官方的规则。用自动化程序在公共服务器上挂机
   可能被封号，后果自负。

5. 免责声明
   本软件按「现状」提供，不附带任何明示或暗示的担保。作者不对因使用本软件造成
   的数据丢失、账号封禁、服务端损坏或其他任何损失承担责任。

6. 隐私
   本软件不收集任何遥测数据，不向作者回传任何信息。你在面板里填写的 API Key
   只保存在本机配置文件里。与 AI 服务商的通信由你自己配置的接口地址决定。

点击「我接受」表示你已阅读并同意以上条款。";

    // ---------------------------------------------------------------- 向导界面
    sealed class WizardWindow : Form
    {
        readonly Panel body = new Panel();
        readonly Label title = new Label();
        readonly Label subtitle = new Label();
        readonly Button back = new Button();
        readonly Button next = new Button();
        readonly Button cancel = new Button();
        readonly ProgressBar bar = new ProgressBar();
        readonly Label status = new Label();
        readonly RadioButton accept = new RadioButton();
        readonly RadioButton reject = new RadioButton();
        readonly TextBox dirBox = new TextBox();
        readonly CheckBox deskChk = new CheckBox();
        readonly CheckBox runChk = new CheckBox();
        readonly CheckBox menuChk = new CheckBox();
        readonly Label doneText = new Label();
        readonly Timer installTimer = new Timer();

        int page = 0;              // 0 欢迎 1 协议 2 位置 3 安装中 4 完成
        bool installing;
        bool installOk;
        string installErr;

        public WizardWindow()
        {
            Text = AppName + " 安装程序";
            ClientSize = new Size(660, 460);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Bg;
            ForeColor = Fg;
            Font = new Font("Microsoft YaHei UI", 9f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            // 顶部色带
            var head = new Panel { Bounds = new Rectangle(0, 0, 660, 84), BackColor = PanelBg };
            Controls.Add(head);
            title.SetBounds(24, 16, 500, 28);
            title.Font = new Font("Microsoft YaHei UI", 14f, FontStyle.Bold);
            title.ForeColor = Color.White;
            head.Controls.Add(title);
            subtitle.SetBounds(26, 46, 600, 20);
            subtitle.ForeColor = Dim;
            head.Controls.Add(subtitle);

            body.SetBounds(0, 84, 660, 316);
            body.BackColor = Bg;
            Controls.Add(body);

            // 底部按钮栏
            var foot = new Panel { Bounds = new Rectangle(0, 400, 660, 60), BackColor = PanelBg };
            Controls.Add(foot);
            back.SetBounds(300, 14, 92, 32); back.Text = "< 上一步";
            next.SetBounds(398, 14, 108, 32); next.Text = "下一步 >";
            cancel.SetBounds(512, 14, 92, 32); cancel.Text = "取消";
            StyleButton(next, true); StyleButton(back, false); StyleButton(cancel, false);
            foot.Controls.Add(back); foot.Controls.Add(next); foot.Controls.Add(cancel);

            back.Click += (s, e) => Goto(page - 1);
            next.Click += (s, e) =>
            {
                if (page == 2) { StartInstall(); return; }
                if (page == 4) { Finish(); return; }
                Goto(page + 1);
            };
            cancel.Click += (s, e) => { if (installing) return; Close(); };

            installTimer.Interval = 40;
            installTimer.Tick += (s, e) => OnInstallTick();

            Goto(0);
        }

        static void StyleButton(Button b, bool primary)
        {
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 1;
            b.FlatAppearance.BorderColor = primary ? Green : Color.FromArgb(0x55, 0x55, 0x55);
            b.BackColor = primary ? Green : Color.FromArgb(0x33, 0x33, 0x33);
            b.ForeColor = Color.White;
            b.Font = new Font("Microsoft YaHei UI", 9f, primary ? FontStyle.Bold : FontStyle.Regular);
            b.Cursor = Cursors.Hand;
        }

        void ClearBody()
        {
            body.Controls.Clear();
        }

        void Goto(int target)
        {
            if (target < 0 || target > 4) return;
            if (target == 3) return;                      // 安装中由 StartInstall 进入
            page = target;
            ClearBody();
            back.Visible = page == 1 || page == 2;
            next.Visible = page != 3;
            cancel.Visible = page != 4;

            switch (page)
            {
                case 0:
                    title.Text = "欢迎使用 " + AppName;
                    subtitle.Text = "这个向导将引导你把面板安装到本机。";
                    BuildWelcome();
                    next.Text = "下一步 >";
                    break;
                case 1:
                    title.Text = "许可协议";
                    subtitle.Text = "安装前请阅读以下条款。";
                    BuildEula();
                    next.Text = "下一步 >";
                    next.Enabled = accept.Checked;
                    break;
                case 2:
                    title.Text = "选择安装位置";
                    subtitle.Text = "装到当前用户目录，不需要管理员权限。";
                    BuildDir();
                    next.Text = "安装";
                    next.Enabled = true;
                    break;
                case 4:
                    title.Text = installOk ? "安装完成" : "安装失败";
                    subtitle.Text = installOk ? "面板已经装好了。" : "安装过程中出错了。";
                    BuildDone();
                    next.Text = "完成";
                    break;
            }
            if (page != 1) next.Enabled = true;
        }

        void BuildWelcome()
        {
            var t = new Label
            {
                Bounds = new Rectangle(28, 18, 600, 120),
                ForeColor = Fg,
                Text = "安装程序将把 " + AppName + " " + AppVersion + " 装到你的电脑上。\n\n" +
                       "· 单文件桌面程序，带自己的图标和窗口\n" +
                       "· 装到当前用户目录，不会要求管理员权限，也不改动系统目录\n" +
                       "· 自带卸载程序，可在「设置 → 应用」里卸载\n\n" +
                       "点「下一步」继续。"
            };
            body.Controls.Add(t);
            var note = new Label
            {
                Bounds = new Rectangle(28, 150, 600, 70),
                ForeColor = Dim,
                Text = "运行前提：Windows 10/11 64 位，需要 WebView2 运行时\n（Win11 自带；Win10 装了新版 Edge 也有）。\n如果系统没有 Node.js，可在安装目录放一份 node.exe。"
            };
            body.Controls.Add(note);
        }

        void BuildEula()
        {
            var box = new RichTextBox
            {
                Bounds = new Rectangle(24, 6, 612, 220),
                BackColor = Color.FromArgb(0x12, 0x12, 0x12),
                ForeColor = Fg,
                BorderStyle = BorderStyle.FixedSingle,
                ReadOnly = true,
                ScrollBars = RichTextBoxScrollBars.Vertical,
                Text = EulaText,
                Font = new Font("Microsoft YaHei UI", 8.5f),
                DetectUrls = false
            };
            body.Controls.Add(box);

            accept.SetBounds(24, 238, 200, 24);
            accept.Text = "我接受以上条款";
            accept.ForeColor = Fg;
            accept.Checked = false;
            reject.SetBounds(240, 238, 200, 24);
            reject.Text = "我不接受";
            reject.ForeColor = Fg;
            accept.CheckedChanged += (s, e) => { if (accept.Checked) reject.Checked = false; next.Enabled = accept.Checked; };
            reject.CheckedChanged += (s, e) =>
            {
                if (!reject.Checked) return;
                accept.Checked = false;
                next.Enabled = false;
                MessageBox.Show("必须接受许可协议才能继续安装。", AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            body.Controls.Add(accept);
            body.Controls.Add(reject);
        }

        void BuildDir()
        {
            var l = new Label { Bounds = new Rectangle(24, 10, 300, 20), Text = "安装到：", ForeColor = Fg };
            body.Controls.Add(l);
            dirBox.SetBounds(24, 34, 500, 26);
            dirBox.BackColor = Color.FromArgb(0x12, 0x12, 0x12);
            dirBox.ForeColor = Fg;
            dirBox.BorderStyle = BorderStyle.FixedSingle;
            dirBox.Text = DefaultDir;
            body.Controls.Add(dirBox);

            var browse = new Button { Bounds = new Rectangle(532, 33, 100, 28), Text = "浏览..." };
            StyleButton(browse, false);
            browse.Click += (s, e) =>
            {
                using (var d = new FolderBrowserDialog())
                {
                    d.Description = "选择安装位置";
                    d.SelectedPath = dirBox.Text;
                    if (d.ShowDialog(this) == DialogResult.OK) dirBox.Text = d.SelectedPath;
                }
            };
            body.Controls.Add(browse);

            menuChk.SetBounds(24, 78, 560, 24); menuChk.Text = "创建开始菜单快捷方式"; menuChk.Checked = true; menuChk.ForeColor = Fg;
            deskChk.SetBounds(24, 106, 560, 24); deskChk.Text = "创建桌面快捷方式"; deskChk.Checked = true; deskChk.ForeColor = Fg;
            body.Controls.Add(menuChk);
            body.Controls.Add(deskChk);

            var info = new Label
            {
                Bounds = new Rectangle(24, 148, 580, 60),
                ForeColor = Dim,
                Text = "面板运行时的配置、日志和机器人清单会写在数据目录里\n（默认 %LOCALAPPDATA%\\MC-AI-Control），卸载时不会删除，方便重装后继续用。"
            };
            body.Controls.Add(info);
        }

        void StartInstall()
        {
            page = 3;
            installing = true;
            ClearBody();
            title.Text = "正在安装";
            subtitle.Text = "请稍候，正在释放程序文件…";
            back.Visible = false; next.Visible = false; cancel.Visible = false;

            bar.SetBounds(28, 120, 604, 22);
            bar.Minimum = 0; bar.Maximum = 100; bar.Value = 0;
            bar.Style = ProgressBarStyle.Continuous;
            body.Controls.Add(bar);

            status.SetBounds(28, 154, 604, 22);
            status.ForeColor = Dim;
            status.Text = "准备中…";
            body.Controls.Add(status);

            installTimer.Start();
        }

        void OnInstallTick()
        {
            installTimer.Stop();
            bar.Value = 35; status.Text = "正在写入程序文件…"; Application.DoEvents();

            string dir = dirBox.Text.Trim();
            if (dir.Length == 0) dir = DefaultDir;

            bool ok = DoInstall(dir, deskChk.Checked, menuChk.Checked, out installErr);
            installOk = ok;
            installing = false;
            bar.Value = ok ? 100 : 60;
            status.Text = ok ? "完成。" : "失败：" + installErr;
            Application.DoEvents();
            Goto(4);
        }

        void BuildDone()
        {
            if (!installOk)
            {
                doneText.SetBounds(28, 20, 600, 160);
                doneText.ForeColor = Color.FromArgb(0xd9, 0x6a, 0x6a);
                doneText.Text = "安装没有完成。\n\n" + (installErr ?? "(未知错误)") +
                                "\n\n可以换一个安装位置再试，或把上面的信息发给作者。";
                body.Controls.Add(doneText);
                return;
            }
            var l = new Label
            {
                Bounds = new Rectangle(28, 16, 600, 120),
                ForeColor = Fg,
                Text = AppName + " " + AppVersion + " 已安装到：\n" + dirBox.Text.Trim() +
                       "\n\n可以在开始菜单" + (deskChk.Checked ? "或桌面" : "") + "找到它。\n" +
                       "卸载：打开「设置 → 应用」搜索「Minecraft AI」。"
            };
            body.Controls.Add(l);

            runChk.SetBounds(28, 150, 400, 24);
            runChk.Text = "立即运行 " + AppName;
            runChk.Checked = true;
            runChk.ForeColor = Fg;
            body.Controls.Add(runChk);
        }

        void Finish()
        {
            if (installOk && runChk.Checked)
            {
                try
                {
                    string exe = Path.Combine(dirBox.Text.Trim(), AppExe);
                    if (File.Exists(exe)) Process.Start(new ProcessStartInfo { FileName = exe, WorkingDirectory = dirBox.Text.Trim(), UseShellExecute = true });
                }
                catch { }
            }
            Close();
        }
    }

    // ---------------------------------------------------------------- 卸载界面
    sealed class UninstallWindow : Form
    {
        public UninstallWindow()
        {
            Text = "卸载 " + AppName;
            ClientSize = new Size(520, 240);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Bg; ForeColor = Fg;
            Font = new Font("Microsoft YaHei UI", 9f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            string dir = InstallDirFromRegistry() ?? DefaultDir;

            var t = new Label
            {
                Bounds = new Rectangle(24, 22, 470, 40),
                Text = "确定要卸载 " + AppName + " 吗？",
                Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold),
                ForeColor = Color.White
            };
            Controls.Add(t);

            var d = new Label
            {
                Bounds = new Rectangle(24, 66, 470, 70),
                ForeColor = Dim,
                Text = "将删除程序文件和快捷方式。\n数据目录里的配置、日志和机器人清单会保留（重装可继续用）。\n\n安装位置：" + dir
            };
            Controls.Add(d);

            var yes = new Button { Bounds = new Rectangle(268, 170, 108, 32), Text = "卸载" };
            var no = new Button { Bounds = new Rectangle(388, 170, 108, 32), Text = "取消" };
            WizardWindow_style(yes, true); WizardWindow_style(no, false);
            yes.Click += (s, e) =>
            {
                try { DoUninstall(dir, false); }
                catch (Exception ex) { MessageBox.Show("卸载时出错：" + ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Error); }
                MessageBox.Show("已经卸载完成。", AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                Close();
            };
            no.Click += (s, e) => Close();
            Controls.Add(yes); Controls.Add(no);
        }

        static void WizardWindow_style(Button b, bool primary)
        {
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 1;
            b.FlatAppearance.BorderColor = primary ? Green : Color.FromArgb(0x55, 0x55, 0x55);
            b.BackColor = primary ? Green : Color.FromArgb(0x33, 0x33, 0x33);
            b.ForeColor = Color.White;
            b.Cursor = Cursors.Hand;
        }
    }
}
