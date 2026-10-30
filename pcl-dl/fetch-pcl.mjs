import puppeteer from 'puppeteer-core';
import fs from 'fs';

const CHROME = 'C:/Program Files/Google/Chrome/Application/chrome.exe';
const OUT_DIR = 'C:/Users/20646/Desktop/杂/minecraft-ai/PCL2';
const DL_DIR = 'C:/Users/20646/Desktop/杂/minecraft-ai/PCL2/_dl';
const UA = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36';
const BASE = 'https://ltcat.lanzouv.com';
fs.mkdirSync(DL_DIR, { recursive: true });

// ---- 第一步：用 node 换出真实下载地址（这步不需要过挑战）----
const folderHtml = await (await fetch(`${BASE}/b0aj6gsid`, { headers: { 'User-Agent': UA } })).text();
const pick = (re, s = folderHtml) => (s.match(re) || [])[1];
const t = pick(/var\s+ibhhex\s*=\s*'(\d+)'/), k = pick(/var\s+_hftgq\s*=\s*'([a-f0-9]+)'/);
const fid = pick(/url\s*:\s*'\/filemoreajax\.php\?file=(\d+)'/), uid = pick(/'uid'\s*:\s*'(\d+)'/), puid = pick(/'puid'\s*:\s*'([^']+)'/);
const list = (await (await fetch(`${BASE}/filemoreajax.php?file=${fid}`, {
    method: 'POST',
    headers: { 'User-Agent': UA, 'Content-Type': 'application/x-www-form-urlencoded', 'Referer': `${BASE}/b0aj6gsid`, 'X-Requested-With': 'XMLHttpRequest' },
    body: new URLSearchParams({ lx: '2', fid, uid, puid, pg: '1', rep: '0', t, k, up: '1', ls: '1', pwd: 'pcl2' }).toString()
})).json()).text || [];
const zips = list.filter(f => /PCL.*\.zip$/i.test((f.name_all || '').replace(/<[^>]+>/g, '')));
const target = zips[0];
const fileName = target.name_all.replace(/<[^>]+>/g, '').trim();
console.log('目标文件:', fileName, target.id);

const fileUrl = `${BASE}/${target.id}`;
const fileHtml = await (await fetch(fileUrl, { headers: { 'User-Agent': UA } })).text();
const ifr = (fileHtml.match(/<iframe[^>]*src="([^"]+)"/) || [])[1];
const fnHtml = await (await fetch(new URL(ifr, fileUrl).href, { headers: { 'User-Agent': UA, 'Referer': fileUrl } })).text();
const apiBase = (fnHtml.match(/var domain1 = '([^']+)'/) || [])[1];
const wpSign = (fnHtml.match(/var wp_sign = '([^']+)'/) || [])[1];
const ajaxData = (fnHtml.match(/var ajaxdata = '([^']+)'/) || [])[1];
const apiJson = await (await fetch(apiBase, {
    method: 'POST',
    headers: { 'User-Agent': UA, 'Content-Type': 'application/x-www-form-urlencoded', 'Referer': fileUrl, 'X-Requested-With': 'XMLHttpRequest' },
    body: new URLSearchParams({ action: 'downprocess', websignkey: ajaxData, signs: ajaxData, sign: wpSign, websign: '', kd: '1', ves: '1' }).toString()
})).json();
if (apiJson.zt != 1) { console.log('换地址失败', apiJson); process.exit(1); }
const dlUrl = `${apiJson.dom}/file/${apiJson.url}`;
console.log('待过挑战的地址已就绪');

// ---- 第二步：真浏览器打开它，让挑战脚本自己跑完 ----
const browser = await puppeteer.launch({
    executablePath: CHROME,
    headless: 'new',
    args: ['--no-sandbox', '--disable-blink-features=AutomationControlled']
});
const page = await browser.newPage();
await page.setUserAgent(UA);
const client = await page.createCDPSession();
await client.send('Page.setDownloadBehavior', { behavior: 'allow', downloadPath: DL_DIR.replace(/\//g, '\\') });

let downloadStarted = false;
page.on('response', (res) => {
    const cd = res.headers()['content-disposition'];
    if (cd && /attachment|filename/i.test(cd)) console.log('捕获到下载响应:', cd.slice(0, 120));
});

console.log('导航到下载地址…');
try { await page.goto(dlUrl, { waitUntil: 'domcontentloaded', timeout: 45000 }); } catch (e) { console.log('goto:', e.message); }
await new Promise(r => setTimeout(r, 6000));

// 挑战通过后页面通常会跳转或直接触发下载；看一下当前 URL 和文件
console.log('当前 URL:', page.url().slice(0, 100));
console.log('页面文本前 200:', (await page.evaluate(() => document.body?.innerText || '')).slice(0, 200).replace(/\s+/g, ' '));

for (let i = 0; i < 20; i++) {
    const files = fs.readdirSync(DL_DIR).filter(f => !f.endsWith('.crdownload'));
    if (files.length) { console.log('已落盘:', files); break; }
    await new Promise(r => setTimeout(r, 1500));
}
await browser.close();
