import puppeteer from 'puppeteer-core';
import fs from 'fs';

const URL = 'http://127.0.0.1:4599/';
const OUT = 'C:/Users/20646/Desktop/杂/minecraft-ai/panel/shots';
fs.mkdirSync(OUT, { recursive: true });

const browser = await puppeteer.launch({
    executablePath: 'C:/Program Files/Google/Chrome/Application/chrome.exe',
    headless: 'new',
    args: ['--no-sandbox', '--disable-gpu', '--hide-scrollbars'],
});
const page = await browser.newPage();
await page.setViewport({ width: 1180, height: 820 });
const errors = [];
page.on('pageerror', (e) => errors.push(e.message));

await page.goto(URL, { waitUntil: 'networkidle0' });
await new Promise((r) => setTimeout(r, 3000));

for (const v of ['overview', 'server', 'bot', 'logs', 'settings']) {
    await page.evaluate((n) => document.querySelector(`.nav-item[data-view="${n}"]`).click(), v);
    await new Promise((r) => setTimeout(r, 700));
    await page.screenshot({ path: `${OUT}/live-${v}.png` });
}

const probe = await page.evaluate(() => ({
    title: document.getElementById('view-title')?.textContent,
    serverState: document.getElementById('ov-server-state')?.textContent,
    botState: document.getElementById('ov-bot-state')?.textContent,
    players: [...document.querySelectorAll('#player-slots .slot')].map((e) => e.textContent),
    logCount: document.querySelectorAll('#ov-log .log-line').length,
}));
console.log('实时面板探针:', JSON.stringify(probe, null, 2));
console.log('页面错误:', errors.length ? errors : '无');
await browser.close();
