import settings from '../settings.js';
import { createRequire } from 'module';
const require = createRequire(import.meta.url);

// prismarine-viewer 最终会 require('canvas')（原生模块，本机未装）。
// render_bot_view 默认关闭，所以只在真正需要时才加载它。
export function addBrowserViewer(bot, count_id) {
    if (!settings.render_bot_view) return;
    const prismarineViewer = require('prismarine-viewer');
    prismarineViewer.mineflayer(bot, { port: 3000+count_id, firstPerson: true, });
}
