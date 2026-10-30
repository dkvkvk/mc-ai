import puppeteer from 'puppeteer-core';

const CHROME = 'C:/Program Files/Google/Chrome/Application/chrome.exe';
console.log('测试有头 Chrome 能否创建窗口…');
const browser = await puppeteer.launch({
    executablePath: CHROME,
    headless: false,                       // 关键：真的开窗口
    args: ['--no-sandbox', '--disable-gpu', '--window-size=800,600'],
});
console.log('✅ 有头 Chrome 启动成功, version=', await browser.version());
const page = (await browser.pages())[0] || await browser.newPage();
await page.setViewport({ width: 700, height: 400 });
await page.goto('data:text/html,<body style="background:%23222;color:%23fff;font:20px sans-serif"><h1>headed ok</h1></body>');
await new Promise((r) => setTimeout(r, 1500));
await page.screenshot({ path: 'C:/Users/20646/Desktop/杂/minecraft-ai/panel/shots/headed-chrome.png' });
console.log('✅ 有头 Chrome 渲染并截图成功');
await browser.close();
console.log('带窗口的 Chromium 在本机可用 → WebView2 方案可行');
