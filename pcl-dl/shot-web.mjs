import puppeteer from 'puppeteer-core';
import http from 'http';
import fs from 'fs';
import path from 'path';

const ROOT = 'C:/Users/20646/Desktop/杂/minecraft-ai/panel';
const OUT = path.join(ROOT, 'shots');
fs.mkdirSync(OUT, { recursive: true });

const MIME = { '.html': 'text/html; charset=utf-8', '.css': 'text/css; charset=utf-8', '.js': 'text/javascript; charset=utf-8' };
const server = http.createServer((req, res) => {
    const p = path.join(ROOT, decodeURIComponent(req.url.split('?')[0]));
    fs.readFile(p, (err, data) => {
        if (err) { res.writeHead(404); res.end('404'); return; }
        res.writeHead(200, { 'Content-Type': MIME[path.extname(p)] || 'application/octet-stream' });
        res.end(data);
    });
});
await new Promise((r) => server.listen(4599, '127.0.0.1', r));
console.log('静态服务已启动 http://127.0.0.1:4599');

const CHROME = 'C:/Program Files/Google/Chrome/Application/chrome.exe';
const browser = await puppeteer.launch({
    executablePath: CHROME,
    headless: 'new',
    args: ['--no-sandbox', '--disable-gpu', '--hide-scrollbars', '--force-device-scale-factor=1'],
});
const page = await browser.newPage();
await page.setViewport({ width: 1180, height: 780 });

// 注入假的 api（渲染进程本该从 preload 拿到）
const NOW = Date.now();
await page.evaluateOnNewDocument((now) => {
    const cfg = {
        serverDir: 'C:/Users/20646/Desktop/杂/minecraft-ai/server',
        mindcraftDir: 'C:/Users/20646/Desktop/杂/minecraft-ai/mindcraft',
        javaPath: 'D:/Java/bin/java.exe',
        javaArgs: '-Xms1G -Xmx2G',
        serverPort: 25565,
        mindserverPort: 8080,
        botName: 'AIBot',
    };
    const logs = [
        { t: now - 42000, src: 'panel', line: '启动服务器：D:/Java/bin/java.exe -Xms1G -Xmx2G -jar server.jar nogui' },
        { t: now - 38000, src: 'server', line: 'Starting minecraft server version 1.20.1' },
        { t: now - 33000, src: 'server', line: 'Done (6.942s)! For help, type "help"' },
        { t: now - 30000, src: 'server', line: 'Player joined the game' },
        { t: now - 26000, src: 'server', line: 'AIBot joined the game' },
        { t: now - 20000, src: 'server', line: '<AIBot> hello world, I\'m AIBot.' },
        { t: now - 15000, src: 'panel', line: '[派活] Player → 去砍三个木头' },
        { t: now - 9000, src: 'bot', line: 'Collected 3 oak_log.' },
        { t: now - 4000, src: 'server', line: '<AIBot> 我在平原上，坐标大约是 x -30.5、y 88、z -23.5。' },
    ];
    const chats = [
        { t: now - 20000, from: 'AIBot', text: "hello world, I'm AIBot." },
        { t: now - 15000, from: 'Player', text: '去砍三个木头' },
        { t: now - 9000, from: 'AIBot', text: '砍完了，背包里有 3 个橡木。' },
        { t: now - 4000, from: 'AIBot', text: '我在平原上，坐标大约是 x -30.5、y 88、z -23.5，附近还有一只猪。' },
    ];
    window.api = {
        getConfig: async () => cfg,
        setConfig: async () => ({ ok: true, msg: '配置已保存' }),
        status: async () => ({
            server: { up: true, owned: true, pid: 22204, port: 25565 },
            bot: { up: true, running: true, pid: 14220, name: 'AIBot' },
            players: ['Player', 'AIBot'],
            paths: { serverDir: cfg.serverDir, mindcraftDir: cfg.mindcraftDir, serverDirOk: true, mindcraftDirOk: true, javaOk: true },
            configFile: 'C:/Users/20646/AppData/Roaming/MC-AI-Control/config.json',
        }),
        history: async () => ({ logs, chats }),
        startServer: async () => ({ ok: true, msg: '服务器启动中' }),
        stopServer: async () => ({ ok: true, msg: '服务器已停止' }),
        startBot: async () => ({ ok: true, msg: '机器人启动中' }),
        stopBot: async () => ({ ok: true, msg: '机器人已停止' }),
        say: async () => ({ ok: true, msg: '已发送' }),
        clearLogs: async () => ({ ok: true }),
        pickDir: async () => null,
        openPath: async () => ({ ok: true }),
        onLog: () => {},
        onChat: () => {},
    };
    window.__injected = true;
}, NOW);

await page.goto('http://127.0.0.1:4599/renderer/index.html', { waitUntil: 'networkidle0' });
await new Promise((r) => setTimeout(r, 2000));

const errors = [];
page.on('pageerror', (e) => errors.push(e.message));
page.on('console', (m) => { if (m.type() === 'error') errors.push(m.text()); });

const views = ['overview', 'server', 'bot', 'logs', 'settings'];
for (const v of views) {
    await page.evaluate((name) => document.querySelector(`.nav-item[data-view="${name}"]`).click(), v);
    await new Promise((r) => setTimeout(r, 600));
    await page.screenshot({ path: path.join(OUT, `panel-${v}.png`) });
    console.log('截图完成:', `panel-${v}.png`);
}

const probe = await page.evaluate(() => ({
    injected: !!window.__injected,
    title: document.getElementById('view-title')?.textContent,
    serverState: document.getElementById('ov-server-state')?.textContent,
    botState: document.getElementById('ov-bot-state')?.textContent,
    players: [...document.querySelectorAll('#player-slots .slot')].map((e) => e.textContent),
    logLines: document.querySelectorAll('#ov-log .log-line').length,
    chatRows: document.querySelectorAll('#chat-box .chat-row').length,
    nav: [...document.querySelectorAll('.nav-item')].map((b) => b.textContent.trim()),
}));
console.log('页面探针:', JSON.stringify(probe, null, 2));
console.log('渲染错误:', errors.length ? errors : '无');

await browser.close();
server.close();
