// ---------------------------------------------------------------------------
//  Minecraft AI 控制面板 —— 桌面启动器（WinForms + WebView2）
//
//  历史：最早用 Electron 套壳，但这台机器上 electron.exe 一启动就 STATUS_BREAKPOINT，
//        而 Chromium 本身正常 —— 所以改成「系统浏览器当壳」的临时方案（Edge --app）。
//        但那样终究是个浏览器窗口：任务栏是 Edge 的图标、能拖出新标签、关不干净。
//
//  现在：自己开一个 WinForms 窗体，里面嵌 WebView2（Edge 内核，机器上已装运行时）。
//        窗口是本进程的、图标/标题/任务栏都是这个程序，没有地址栏、没有标签页。
//        运行时若 WebView2 不可用（缺运行时/DLL 加载失败），自动回退到 Edge 窗口，
//        并且一定在日志里写明原因。
// ---------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

static class Program
{
    // 数据目录要挑一个「真的能写」的地方：
    // 默认 %LOCALAPPDATA%，但个别机器上这个目录会被安全软件或残留权限锁成只读，
    // 那时启动器只会静默失败（用户看到的就是「双击没反应」）。
    // 所以启动时按顺序试：%LOCALAPPDATA% → exe 同目录（便携）→ %TEMP%。
    static readonly string DataDir = ResolveDataDir();
    static readonly string AppDir = Path.Combine(DataDir, "app");
    static readonly string BrowserDir = Path.Combine(DataDir, "browser");
    static readonly string RendererDir = Path.Combine(AppDir, "renderer");

    static string ResolveDataDir()
    {
        string local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MC-AI-Control");
        string portable = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MC-AI-Control-data");
        string temp = Path.Combine(Path.GetTempPath(), "MC-AI-Control");
        foreach (string c in new[] { local, portable, temp })
        {
            try
            {
                Directory.CreateDirectory(c);
                string probe = Path.Combine(c, ".write-probe");
                File.WriteAllText(probe, "x");
                File.Delete(probe);
                return c;
            }
            catch { }
        }
        return local;   // 全都不行就用默认的，让后面的报错把原因说清楚
    }

    // 内嵌资源 → 目标文件
    static readonly string[,] Assets = new string[,]
    {
        { "server.js",            "server.js" },
        { "index.html",           @"renderer\index.html" },
        { "styles.css",           @"renderer\styles.css" },
        { "app.js",               @"renderer\app.js" },
        { "README.txt",           "使用说明.txt" },
    };

    // 日志可能落在三处：%LOCALAPPDATA%（正常）、exe 同目录（便携/被重定向时）、%TEMP%（兜底）。
    // 只写一处是很危险的：那处不可写时，用户双击后什么都不会发生，现场一点线索都不留。
    static readonly string LogFileData = Path.Combine(DataDir, "launcher.log");
    static readonly string LogFileExe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "launcher.log");
    static readonly string LogFileTmp = Path.Combine(Path.GetTempPath(), "MC-AI-Control-launcher.log");
    static string ActiveLog;

    static void Log(string msg)    {
        string line = DateTime.Now.ToString("HH:mm:ss.fff") + "  " + msg + Environment.NewLine;
        string[] candidates = { ActiveLog, LogFileData, LogFileExe, LogFileTmp };
        foreach (string f in candidates)
        {
            if (string.IsNullOrEmpty(f)) continue;
            try
            {
                File.AppendAllText(f, line, Encoding.UTF8);
                ActiveLog = f;
                return;
            }
            catch { }
        }
    }

    // 本次自己拉起的后端（窗口没开成时不能留一个看不见的进程占着端口）
    static Process StartedBackend;

    static void Fatal(string title, string detail)
    {
        Log("错误: " + detail.Replace(Environment.NewLine, " | "));
        try
        {
            if (StartedBackend != null && !StartedBackend.HasExited)
            {
                StartedBackend.Kill();
                Log("启动失败，已回收后端进程");
            }
        }
        catch { }
        string where = ActiveLog ?? "(日志不可写)";
        MessageBox.Show(detail + "\n\n日志: " + where + "\n数据目录: " + DataDir,
            title, MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    [STAThread]
    static void Main(string[] args)
    {
        if (args.Length > 0 && (args[0] == "--doctor" || args[0] == "-d"))
        {
            RunDoctor();
            return;
        }

        try
        {
            Log("======== 启动 ========");
            Log("exe: " + Application.ExecutablePath);

            try
            {
                Directory.CreateDirectory(AppDir);
                Directory.CreateDirectory(RendererDir);
                Directory.CreateDirectory(BrowserDir);
            }
            catch (Exception ex)
            {
                Fatal("Minecraft AI 控制面板",
                    "无法创建数据目录：\n" + DataDir + "\n\n" + ex.Message +
                    "\n\n常见原因：目录被安全软件锁住，或该路径没有写权限。\n" +
                    "可以手动删除这个目录后重试，或者把面板放到别的磁盘。");
                return;
            }
            Log("目录已就绪: " + AppDir);

            List<string> failed = ExtractAssets();
            Log("资源释放完成" + (failed.Count > 0 ? "（失败 " + failed.Count + " 项）" : ""));
            if (failed.Count > 0)
            {
                // 资源没写进去 = 跑的是旧后端，症状会莫名其妙（比如改了的行为不生效）。
                // 这必须让用户看见，不能吞掉。
                Fatal("Minecraft AI 控制面板",
                    "面板文件没能写入：\n" + string.Join("\n", failed.ToArray()) +
                    "\n\n通常是安全软件拦截了对这个目录的写入。\n" +
                    "请把 " + DataDir + " 加入杀软白名单，然后删除该目录重试。");
                return;
            }

            // 1) 已有后端在跑就直接复用，避免开出两个
            int port = FindRunningBackend();
            Process backend = null;
            Log("已有后端端口: " + port);

            if (port == 0)
            {
                string node = FindNode();
                Log("node.exe: " + (node ?? "(未找到)"));
                if (node == null)
                {
                    Fatal("Minecraft AI 控制面板",
                        "没有找到 node.exe。\n\n请先安装 Node.js（https://nodejs.org），\n" +
                        "或把 node.exe 的完整路径写进：\n" + Path.Combine(DataDir, "node.txt"));
                    return;
                }
                backend = StartBackend(node);
                port = WaitForBackend(25);
                Log("后端端口: " + port);
                if (port == 0)
                {
                    Fatal("Minecraft AI 控制面板",
                        "后端启动失败（端口无响应）。\n\n可手动排查：\n  cd \"" + AppDir + "\"\n  \"" + node + "\" server.js");
                    return;
                }
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new AppWindow(port, backend));
        }
        catch (Exception ex)
        {
            Fatal("Minecraft AI 控制面板", "启动出错：" + ex.Message + "\n\n" + ex.GetType().Name);
        }
    }

    // ---------------------------------------------------------------- 主窗口
    // 一个真正的应用窗口：本进程所有，图标/标题/任务栏都是自己，没有地址栏和标签页。
    sealed class AppWindow : Form
    {
        readonly int port;
        readonly Process backend;
        WebView2 web;
        bool webReady;

        public AppWindow(int port, Process backend)
        {
            this.port = port;
            this.backend = backend;

            Text = "Minecraft AI 控制面板";
            BackColor = Color.FromArgb(0x12, 0x12, 0x12);
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(980, 660);
            Font = new Font("Microsoft YaHei UI", 9f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            RestoreBoundsFromDisk();

            web = new WebView2 { Dock = DockStyle.Fill };
            try
            {
                string wvData = Path.Combine(DataDir, "webview");
                Directory.CreateDirectory(wvData);
                web.CreationProperties = new CoreWebView2CreationProperties { UserDataFolder = wvData };
            }
            catch (Exception ex) { Log("WebView2 用户数据目录准备失败: " + ex.Message); }
            Controls.Add(web);

            Shown += OnShown;
            FormClosing += OnClosing;
        }

        async void OnShown(object sender, EventArgs e)
        {
            string url = "http://127.0.0.1:" + port + "/";
            try
            {
                await web.EnsureCoreWebView2Async(null);
                CoreWebView2 core = web.CoreWebView2;
                core.Settings.AreDevToolsEnabled = false;
                core.Settings.IsStatusBarEnabled = false;
                core.Settings.AreDefaultContextMenusEnabled = false;
                core.Settings.IsZoomControlEnabled = false;
                core.Settings.AreBrowserAcceleratorKeysEnabled = false;   // 屏蔽 Ctrl+P / Ctrl+F 这类浏览器快捷键
                core.NewWindowRequested += (s2, ev) =>
                {
                    // 面板里的外链交给系统浏览器，别在应用窗口里长出第二个窗口
                    ev.Handled = true;
                    try { Process.Start(ev.Uri); } catch { }
                };
                web.Source = new Uri(url);
                webReady = true;
                Log("窗口已就绪（WebView2）: " + url);
            }
            catch (Exception ex)
            {
                // WebView2 运行时缺失 / loader 加载失败：退回 Edge 应用窗口，至少能用
                Log("WebView2 初始化失败，回退 Edge 窗口: " + ex.Message);
                OpenPanelWindow(port);
                Close();
            }
        }

        void OnClosing(object sender, FormClosingEventArgs e)
        {
            SaveBoundsToDisk();
            try
            {
                if (backend != null && !backend.HasExited)
                {
                    backend.Kill();          // 自己起的后端跟着窗口一起退
                    Log("窗口关闭，已结束后端进程");
                }
                else Log("窗口关闭");
            }
            catch (Exception ex) { Log("结束后端失败: " + ex.Message); }
            if (!webReady) Log("（本次是 Edge 回退模式）");
        }

        // 记住窗口大小/位置，下次打开还原 —— 桌面软件的基本礼貌
        string UiFile { get { return Path.Combine(DataDir, "ui.txt"); } }

        void RestoreBoundsFromDisk()
        {
            try
            {
                if (!File.Exists(UiFile)) { Size = new Size(1240, 872); return; }
                string[] parts = File.ReadAllText(UiFile).Split(',');
                if (parts.Length == 4)
                {
                    var r = new Rectangle(int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]), int.Parse(parts[3]));
                    if (r.Width >= MinimumSize.Width && r.Height >= MinimumSize.Height)
                    {
                        var screen = Screen.FromRectangle(r).WorkingArea;
                        if (screen.IntersectsWith(r)) { StartPosition = FormStartPosition.Manual; Bounds = r; return; }
                    }
                }
            }
            catch { }
            Size = new Size(1240, 872);
        }

        void SaveBoundsToDisk()
        {
            try
            {
                Rectangle r = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
                File.WriteAllText(UiFile, r.X + "," + r.Y + "," + r.Width + "," + r.Height);
            }
            catch { }
        }
    }

    // ---------------------------------------------------------------- 自检
    // 双击没反应时用得上：MC-AI-Control.exe --doctor
    static void RunDoctor()
    {
        var sb = new StringBuilder();
        sb.AppendLine("MC-AI-Control 自检  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        sb.AppendLine("exe        : " + Application.ExecutablePath);
        sb.AppendLine("数据目录   : " + DataDir);
        sb.AppendLine("node       : " + (FindNode() ?? "(未找到)"));
        sb.AppendLine("WebView2   : " + WebView2Version());
        sb.AppendLine("Edge       : " + (FindBrowser() ?? "(未找到)"));
        sb.AppendLine("已有后端   : " + FindRunningBackend());
        sb.AppendLine("日志       : " + (ActiveLog ?? "(未写入)"));
        sb.AppendLine();
        foreach (string d in new[] { DataDir, AppDir, BrowserDir })
            sb.AppendLine("可写 " + (CanWrite(d) ? "是" : "否") + "  " + d);
        sb.AppendLine();
        sb.AppendLine("--- 已安装的面板文件 ---");
        foreach (string f in new[] { "server.js", @"renderer\index.html", @"renderer\styles.css", @"renderer\app.js", "使用说明.txt" })
        {
            string p = Path.Combine(AppDir, f);
            sb.AppendLine((File.Exists(p) ? "有" : "缺") + "  " + f + (File.Exists(p) ? "  " + new FileInfo(p).Length + "B  " + File.GetLastWriteTime(p).ToString("MM-dd HH:mm") : ""));
        }
        string outFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "doctor.txt");
        try { File.WriteAllText(outFile, sb.ToString(), new UTF8Encoding(false)); }
        catch { outFile = LogFileTmp; try { File.WriteAllText(outFile, sb.ToString(), new UTF8Encoding(false)); } catch { } }
        try { Process.Start("notepad.exe", "\"" + outFile + "\""); } catch { }
    }

    static string WebView2Version()
    {
        try
        {
            string v = CoreWebView2Environment.GetAvailableBrowserVersionString();
            return string.IsNullOrEmpty(v) ? "(运行时未安装)" : v;
        }
        catch (Exception ex) { return "(不可用: " + ex.Message + ")"; }
    }

    static bool CanWrite(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            string probe = Path.Combine(dir, ".write-probe");
            File.WriteAllText(probe, "x");
            File.Delete(probe);
            return true;
        }
        catch { return false; }
    }

    // ---------------------------------------------------------------- 资源释放
    static List<string> ExtractAssets()
    {
        var failed = new List<string>();
        var asm = Assembly.GetExecutingAssembly();
        for (int i = 0; i < Assets.GetLength(0); i++)
        {
            string res = Assets[i, 0];
            string target = Path.Combine(AppDir, Assets[i, 1]);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                using (Stream s = asm.GetManifestResourceStream(res))
                {
                    if (s == null) { failed.Add(Assets[i, 1] + "（exe 内缺少该资源）"); continue; }
                    using (var sr = new StreamReader(s, Encoding.UTF8))
                    {
                        string content = sr.ReadToEnd();
                        // 内容有变化才覆盖（避免破坏用户改动 & 省 IO）
                        if (File.Exists(target) && File.ReadAllText(target, Encoding.UTF8) == content) continue;
                        File.WriteAllText(target, content, new UTF8Encoding(false));
                    }
                }
            }
            catch (Exception ex)
            {
                failed.Add(Assets[i, 1] + "  →  " + ex.Message);
            }
        }
        return failed;
    }

    // ---------------------------------------------------------------- node 定位
    static string FindNode()
    {
        // 1) 便携模式：exe 旁边自带的 node.exe（build.ps1 会顺手拷一份）
        try
        {
            string beside = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "node.exe");
            if (File.Exists(beside)) return beside;
        }
        catch { }

        // 2) 用户手工指定的路径
        string cfg = Path.Combine(DataDir, "node.txt");
        try
        {
            if (File.Exists(cfg))
            {
                string p = File.ReadAllText(cfg).Trim();
                if (p.Length > 0 && File.Exists(p)) return p;
            }
        }
        catch { }

        // 3) PATH
        string pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (string dir in pathVar.Split(';'))
        {
            if (dir.Trim().Length == 0) continue;
            try
            {
                string c = Path.Combine(dir.Trim(), "node.exe");
                if (File.Exists(c)) return c;
            }
            catch { }
        }

        // 4) 常见安装位置
        string[] guesses =
        {
            @"D:\nodejs\node.exe",
            @"C:\Program Files\nodejs\node.exe",
            @"C:\Program Files (x86)\nodejs\node.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\nodejs\node.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"npm\node.exe")
        };
        foreach (string g in guesses) if (File.Exists(g)) return g;
        return null;
    }

    // ---------------------------------------------------------------- 后端进程
    static Process StartBackend(string node)
    {
        var psi = new ProcessStartInfo
        {
            FileName = node,
            Arguments = "server.js",
            WorkingDirectory = AppDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        // 把 exe 所在目录告诉后端：配置丢了/换机器时它能从这里往上找到 server/ 与 mindcraft/
        psi.EnvironmentVariables["MCAI_ROOT_HINT"] = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
        Process p = Process.Start(psi);
        StartedBackend = p;
        // 异步把输出丢掉，避免管道写满把后端卡死
        p.OutputDataReceived += (s, e) => { };
        p.ErrorDataReceived += (s, e) => { };
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        return p;
    }

    static int FindRunningBackend()
    {
        for (int port = 4599; port < 4615; port++)
            if (Ping(port)) return port;
        return 0;
    }

    static int WaitForBackend(int seconds)
    {
        var end = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < end)
        {
            for (int port = 4599; port < 4615; port++)
                if (Ping(port)) return port;
            Thread.Sleep(400);
        }
        return 0;
    }

    static bool Ping(int port)
    {
        try
        {
            using (var tcp = new TcpClient())
            {
                var ar = tcp.BeginConnect("127.0.0.1", port, null, null);
                if (!ar.AsyncWaitHandle.WaitOne(300)) return false;
                tcp.EndConnect(ar);

                using (var wc = new WebClient())
                {
                    wc.Encoding = Encoding.UTF8;
                    string s = wc.DownloadString("http://127.0.0.1:" + port + "/api/status");
                    return s.Contains("\"server\"");
                }
            }
        }
        catch { return false; }
    }

    // ---------------------------------------------------------------- 兜底窗口
    // 只在 WebView2 不可用时用：退回 Edge 应用窗口（老方案），别让用户没得用
    static void OpenPanelWindow(int port)
    {
        string url = "http://127.0.0.1:" + port + "/";
        string browser = FindBrowser();
        if (browser == null)
        {
            Log("没找到 Edge/Chrome，用默认浏览器打开");
            try { Process.Start(url); } catch (Exception ex) { Fatal("Minecraft AI 控制面板", "面板已启动但窗口打不开。\n\n" + ex.Message + "\n\n请手动在浏览器打开：\n" + url); }
            return;
        }
        var psi = new ProcessStartInfo
        {
            FileName = browser,
            Arguments = "--app=" + url +
                        " --window-size=1200,860 --window-position=120,60" +
                        " --user-data-dir=\"" + BrowserDir + "\"" +
                        " --no-first-run --no-default-browser-check --disable-features=Translate",
            UseShellExecute = false,
        };
        try { Process.Start(psi); Log("已用 Edge 应用窗口打开（回退）: " + browser); }
        catch (Exception ex) { Log("Edge 也失败了: " + ex.Message); }
    }

    static string FindBrowser()
    {
        string[] paths =
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Microsoft\Edge\Application\msedge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Microsoft\Edge\Application\msedge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Google\Chrome\Application\chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Google\Chrome\Application\chrome.exe"),
        };
        foreach (string p in paths) if (File.Exists(p)) return p;
        return null;
    }
}
