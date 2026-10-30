import { createRequire } from 'module';
const require = createRequire(import.meta.url);

// prismarine-viewer 的 viewer/lib/entities.js 硬性 require('canvas')（原生模块，
// 本机没装）。视觉默认关闭，所以改成用到时才 require，避免静态导入把整个进程拖垮。
let Viewer, WorldView, getBufferFromStream;
function ensureViewerDeps() {
    if (Viewer) return;
    ({ Viewer } = require('prismarine-viewer/viewer/lib/viewer.js'));
    ({ WorldView } = require('prismarine-viewer/viewer/lib/worldView.js'));
    ({ getBufferFromStream } = require('prismarine-viewer/viewer/lib/simpleUtils.js'));
}

import THREE from 'three';
// node-canvas-webgl 是原生模块，本机（Windows 无 MSVC/ANGLE）不安装。
// Camera 只在 settings.allow_vision = true 时才会被构造，所以这里用惰性报错替代静态导入。
function createCanvas() {
    throw new Error('视觉功能需要 node-canvas-webgl（本机未安装，allow_vision=false 时不会走到这里）');
}
import fs from 'fs/promises';
import { Vec3 } from 'vec3';
import { EventEmitter } from 'events';

import worker_threads from 'worker_threads';
global.Worker = worker_threads.Worker;


export class Camera extends EventEmitter {
    constructor (bot, fp) {
        super();
        this.bot = bot;
        this.fp = fp;
        this.viewDistance = 12;
        this.width = 800;
        this.height = 512;
        this.canvas = createCanvas(this.width, this.height);
        ensureViewerDeps();
        this.renderer = new THREE.WebGLRenderer({ canvas: this.canvas });
        this.viewer = new Viewer(this.renderer);
        this._init().then(() => {
            this.emit('ready');
        })
    }
  
    async _init () {
        const botPos = this.bot.entity.position;
        const center = new Vec3(botPos.x, botPos.y+this.bot.entity.height, botPos.z);
        this.viewer.setVersion(this.bot.version);
        // Load world
        const worldView = new WorldView(this.bot.world, this.viewDistance, center);
        this.viewer.listen(worldView);
        worldView.listenToBot(this.bot);
        await worldView.init(center);
        this.worldView = worldView;
    }
  
    async capture() {
        const center = new Vec3(this.bot.entity.position.x, this.bot.entity.position.y+this.bot.entity.height, this.bot.entity.position.z);
        this.viewer.camera.position.set(center.x, center.y, center.z);
        await this.worldView.updatePosition(center);
        this.viewer.setFirstPersonCamera(this.bot.entity.position, this.bot.entity.yaw, this.bot.entity.pitch);
        this.viewer.update();
        this.renderer.render(this.viewer.scene, this.viewer.camera);

        const imageStream = this.canvas.createJPEGStream({
            bufsize: 4096,
            quality: 100,
            progressive: false
        });
        
        const timestamp = new Date().toISOString().replace(/[:.]/g, '-');
        const filename = `screenshot_${timestamp}`;

        const buf = await getBufferFromStream(imageStream);
        await this._ensureScreenshotDirectory();
        await fs.writeFile(`${this.fp}/${filename}.jpg`, buf);
        console.log('saved', filename);
        return filename;
    }

    async _ensureScreenshotDirectory() {
        let stats;
        try {
            stats = await fs.stat(this.fp);
        } catch (e) {
            if (!stats?.isDirectory()) {
                await fs.mkdir(this.fp);
            }
        }
    }
}
  
