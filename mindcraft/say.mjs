// 不开游戏也能给 bot 派活：node say.mjs <说话人> <内容...>
// 走 MindServer(8080) 的 send-message 事件，等价于你在游戏里打一句话
import { io } from 'socket.io-client';

const args = process.argv.slice(2);
const from = args[0] || 'Player';
const message = args.slice(1).join(' ');
if (!message) {
    console.error('用法: node say.mjs <说话人> <内容...>');
    process.exit(1);
}

const socket = io('http://localhost:8080', { transports: ['websocket'] });
socket.on('connect', () => {
    console.log(`[${from}] -> AIBot: ${message}`);
    socket.emit('send-message', 'AIBot', { from, message });
    setTimeout(() => { socket.close(); process.exit(0); }, 800);
});
socket.on('connect_error', (e) => { console.error('连接 MindServer 失败:', e.message); process.exit(1); });
