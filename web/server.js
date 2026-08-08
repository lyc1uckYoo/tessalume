// Tessalume Web 控制台 —— 纯 Node.js，零运行时依赖（需 Node >= 21，提供全局 WebSocket）。
// 通过本机 CDP 为 Codex Desktop 注入主题。
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import { fileURLToPath } from 'node:url';
import { runOnPort, getScheme, discoverCodex, probe, reloadTarget } from './lib/cdp.js';
import {
  loadTheme, isValid, buildPayload, REMOVE_EXPRESSION, TOGGLE_EXPRESSION,
} from './lib/themes.js';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
// 仓库根目录（web/ 的上级）。资源与主题直接引用原生文件，保持单一数据源。
const REPO_ROOT = path.resolve(__dirname, '..');

// 可在环境变量覆盖资源 / 主题目录位置。
const COMPAT_ROOT = process.env.TESSALUME_COMPAT || path.join(REPO_ROOT, 'src', 'Tessalume.App', 'Compatibility');
// 组装出的运行时文件写到 web 目录内，避免污染源码树。
const RUNTIME_OUTPUT_DIR = path.join(__dirname, '.runtime');
fs.mkdirSync(RUNTIME_OUTPUT_DIR, { recursive: true });
const BUILTIN_THEMES = process.env.TESSALUME_THEMES || path.join(REPO_ROOT, 'themes');
const CUSTOM_THEMES = path.join(os.homedir(), '.tessalume', 'themes');
fs.mkdirSync(CUSTOM_THEMES, { recursive: true });

const PORT = Number(process.env.PORT || 5173);
const HOST = '127.0.0.1'; // 仅本机回环，主题只能注入同一台 Mac 上的 ChatGPT。
const CDP_PORT = Number(process.env.TESSALUME_CDP_PORT || 9222); // ChatGPT 调试端口默认值。

// 启动时按 runtime-bundle.json 把分段 js 组装为 theme-runtime-v2.js（对应 CompatibilityRuntimeComposer）。
// 关键：片段中的 TESSALUME_STANDALONE_ENVELOPE 包裹的是"单片段独立运行"的包装缝
// （如片段间的 `})()` / `(async () => {`）。拼接成一个连续函数体时必须把这些包装缝剥掉，
// 否则会产生多个断开的 IIFE，导致变量作用域错乱、mount 抛错后 dispose 把样式又移除。
function stripStandaloneEnvelope(src) {
  // 删除 // TESSALUME_STANDALONE_ENVELOPE_START ... // TESSALUME_STANDALONE_ENVELOPE_END 之间的整段（含标记）。
  return src.replace(/\/\/\s*TESSALUME_STANDALONE_ENVELOPE_START[\s\S]*?\/\/\s*TESSALUME_STANDALONE_ENVELOPE_END/g, '');
}

function composeRuntime() {
  const bundle = path.join(COMPAT_ROOT, 'Runtime', 'runtime-bundle.json');
  if (!fs.existsSync(bundle)) return;
  const manifest = JSON.parse(fs.readFileSync(bundle, 'utf8'));
  const runtimeDir = path.dirname(bundle);
  const parts = [];
  for (const frag of manifest.fragments || []) {
    const p = path.join(runtimeDir, frag);
    if (fs.existsSync(p)) parts.push(stripStandaloneEnvelope(fs.readFileSync(p, 'utf8')).replace(/\r?\n$/, ''));
  }
  fs.writeFileSync(path.join(RUNTIME_OUTPUT_DIR, 'theme-runtime-v2.js'), parts.join('\n') + '\n', 'utf8');
}

function scanThemes() {
  const result = [];
  for (const root of [BUILTIN_THEMES, CUSTOM_THEMES]) {
    if (!fs.existsSync(root)) continue;
    for (const entry of fs.readdirSync(root, { withFileTypes: true })) {
      if (!entry.isDirectory()) continue;
      const dir = path.join(root, entry.name);
      const { package: pkg, validation } = loadTheme(dir);
      const issues = validation.errors.map((e) => e.message);
      result.push({
        directory: dir,
        source: root === BUILTIN_THEMES ? 'builtin' : 'custom',
        manifest: pkg ? pkg.manifest : null,
        isValid: isValid(validation),
        issues,
        previewLightPath: pkg ? pkg.previewLightPath : null,
        previewDarkPath: pkg ? pkg.previewDarkPath : null,
        isAdvanced: pkg ? pkg.isAdvanced : false,
      });
    }
  }
  result.sort((a, b) => (a.manifest?.name || '').localeCompare(b.manifest?.name || ''));
  return result;
}

function sendJson(res, status, data) {
  const body = JSON.stringify(data);
  res.writeHead(status, { 'Content-Type': 'application/json; charset=utf-8', 'Cache-Control': 'no-store' });
  res.end(body);
}

async function handleApi(req, res, url) {
  const route = url.pathname;

  if (route === '/api/themes' && req.method === 'GET') {
    return sendJson(res, 200, scanThemes());
  }

  if (route === '/api/themes/preview' && req.method === 'GET') {
    const p = url.searchParams.get('path');
    if (!p || !fs.existsSync(p)) return sendJson(res, 404, { error: 'not found' });
    const ext = path.extname(p).toLowerCase();
    const mime = ext === '.png' ? 'image/png' : ext === '.jpg' || ext === '.jpeg' ? 'image/jpeg' : ext === '.webp' ? 'image/webp' : ext === '.gif' ? 'image/gif' : 'image/png';
    res.writeHead(200, { 'Content-Type': mime });
    return fs.createReadStream(p).pipe(res);
  }

  if (route === '/api/probe' && req.method === 'GET') {
    const port = Number(url.searchParams.get('port')) || CDP_PORT;
    return sendJson(res, 200, { reachable: await probe(port) });
  }

  if (route === '/api/discover' && req.method === 'GET') {
    return sendJson(res, 200, { ports: await discoverCodex() });
  }

  if (route === '/api/scheme' && req.method === 'GET') {
    const port = Number(url.searchParams.get('port')) || CDP_PORT;
    try {
      return sendJson(res, 200, { isDark: await getScheme(port) });
    } catch (e) {
      return sendJson(res, 400, { error: e.message });
    }
  }

  if (route === '/api/acceptance' && req.method === 'GET') {
    // 读取注入后的页面状态快照（复用 CDP 的 inspectExpression）。
    const port = Number(url.searchParams.get('port')) || CDP_PORT;
    try {
      const r = await runOnPort(port, `(function(){var h=document.documentElement;return{applied:h.classList.contains('tessalume-theme-active'),themeId:h.className.match(/tessalume-theme-([a-z0-9.-]+)/i)?RegExp.\$1:null,dark:h.classList.contains('tessalume-color-scheme-dark')||h.classList.contains('electron-dark'),light:h.classList.contains('tessalume-color-scheme-light')};})()`);
      return sendJson(res, 200, r.result?.value || {});
    } catch (e) {
      return sendJson(res, 400, { error: e.message });
    }
  }

  if (route === '/api/apply' && req.method === 'POST') {
    const body = await readJson(req);
    const port = Number(body.port) || CDP_PORT;
    const { package: pkg, validation } = loadTheme(body.themeDirectory);
    if (!pkg || !isValid(validation)) {
      return sendJson(res, 400, { error: (validation.errors[0] || {}).message || '主题校验失败。' });
    }
    try {
      const { buildPayload, stageAssetsForPkg } = await import('./lib/themes.js');
      // 之前失败注入可能在全局作用域留下处于 TDZ 的 const 声明，
      // 导致后续注入报 "Identifier has already been declared"。先 reload 清理。
      await reloadTarget(port);
      await new Promise((r) => setTimeout(r, 1500)); // 等待页面 DOM 重建
      // 资源体积可达数十 MB：先分块写入 window.__TESSALUME_STAGED_ASSETS__，
      // 再发送不含资源内联的精简运行时，避免单条超大 Runtime.evaluate 触发 Chromium 解析异常。
      await stageAssetsForPkg(port, pkg);
      const expression = buildPayload(pkg, COMPAT_ROOT, { skipAssets: true, runtimeDir: RUNTIME_OUTPUT_DIR });
      await runOnPort(port, expression);
      return sendJson(res, 200, { message: '主题已应用' });
    } catch (e) {
      return sendJson(res, 400, { error: e.message });
    }
  }

  if (route === '/api/remove' && req.method === 'POST') {
    const body = await readJson(req);
    const port = Number(body.port) || CDP_PORT;
    try {
      await runOnPort(port, REMOVE_EXPRESSION);
      return sendJson(res, 200, { message: '已恢复 Codex 默认外观' });
    } catch (e) {
      return sendJson(res, 400, { error: e.message });
    }
  }

  if (route === '/api/toggle-scheme' && req.method === 'POST') {
    const body = await readJson(req);
    const port = Number(body.port) || CDP_PORT;
    try {
      const r = await runOnPort(port, TOGGLE_EXPRESSION);
      return sendJson(res, 200, { isDark: !!(r.result && r.result.value) });
    } catch (e) {
      return sendJson(res, 400, { error: e.message });
    }
  }

  return sendJson(res, 404, { error: 'not found' });
}

function readJson(req) {
  return new Promise((resolve, reject) => {
    let data = '';
    req.on('data', (c) => (data += c));
    req.on('end', () => {
      try {
        resolve(data ? JSON.parse(data) : {});
      } catch (e) {
        reject(e);
      }
    });
    req.on('error', reject);
  });
}

const PUBLIC_DIR = path.join(__dirname, 'public');

const server = http.createServer((req, res) => {
  const url = new URL(req.url, `http://${req.headers.host}`);
  if (url.pathname.startsWith('/api/')) {
    handleApi(req, res, url).catch((e) => sendJson(res, 500, { error: e.message }));
    return;
  }
  // 静态文件：默认 index.html。
  let rel = decodeURIComponent(url.pathname);
  if (rel === '/') rel = '/index.html';
  const filePath = path.join(PUBLIC_DIR, rel);
  if (!filePath.startsWith(PUBLIC_DIR) || !fs.existsSync(filePath)) {
    const fallback = path.join(PUBLIC_DIR, 'index.html');
    res.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8' });
    return fs.createReadStream(fallback).pipe(res);
  }
  const ext = path.extname(filePath).toLowerCase();
  const mime = { '.html': 'text/html', '.js': 'text/javascript', '.css': 'text/css' }[ext] || 'application/octet-stream';
  res.writeHead(200, { 'Content-Type': mime });
  fs.createReadStream(filePath).pipe(res);
});

composeRuntime();
server.listen(PORT, HOST, () => {
  console.log(`Tessalume Web 控制台已启动`);
  console.log(`  本地访问: http://${HOST}:${PORT}`);
  console.log(`  兼容层:   ${COMPAT_ROOT}`);
  console.log(`  内置主题: ${BUILTIN_THEMES}`);
  console.log(`  自定义主题: ${CUSTOM_THEMES}`);
});
