// 主题加载与校验（对应原 C# ThemePackageLoader / ThemeFingerprintCalculator / ThemePayloadBuilder）。
// 本模块复用仓库原生的主题资源与兼容层文件，行为与原版保持一致。
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { runOnPort } from './cdp.js';

const MANIFEST_FILE = 'manifest.json';
const LATEST_SCHEMA = 2;
const SUPPORTED_ENGINE = 2;
const MAX_MANIFEST = 256 * 1024;
const MAX_CSS = 2 * 1024 * 1024;
const MAX_SCRIPT = 2 * 1024 * 1024;
const MAX_ASSET = 25 * 1024 * 1024;
const MAX_TOTAL_ASSETS = 100 * 1024 * 1024;

const RASTER = new Set(['.png', '.jpg', '.jpeg', '.webp', '.gif', '.avif', '.bmp', '.ico']);
const OPEN_ASSET = new Set([
  ...RASTER, '.svg', '.woff', '.woff2', '.ttf', '.otf',
  '.json', '.txt', '.md', '.mp3', '.wav', '.ogg', '.m4a', '.mp4', '.webm',
]);
const THEME_ID_RE = /^[a-z0-9][a-z0-9.-]{2,63}$/;
const ASSET_NAME_RE = /^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$/;

function makeValidation() {
  return { errors: [], warnings: [] };
}
function addError(v, code, message, where) {
  v.errors.push({ code, message, where: where || null });
}
function addWarning(v, code, message, where) {
  v.warnings.push({ code, message, where: where || null });
}
const isValid = (v) => v.errors.length === 0;

// 校验清单，返回 { manifest, validation }
function validateManifest(raw, manifestPath, v) {
  if (raw.schemaVersion !== LATEST_SCHEMA) {
    addError(v, 'manifest.schema.unsupported', `Schema 版本 ${raw.schemaVersion} 不受支持，需要 ${LATEST_SCHEMA}。`);
  }
  if (!THEME_ID_RE.test(raw.id || '')) {
    addError(v, 'manifest.id.invalid', '主题 id 须为 3-64 位小写字母/数字/点/连字符，且以字母或数字开头。');
  }
  if (!raw.name) addError(v, 'manifest.name.missing', '主题名称必填。');
  if (!/^\d+(\.\d+)*$/.test(raw.version || '')) {
    addError(v, 'manifest.version.invalid', '版本号必须为数字格式，例如 1.0.0。');
  }
  if ((raw.engineVersion || 0) > SUPPORTED_ENGINE) {
    addError(v, 'manifest.engine.unsupported', `主题需要引擎版本 ${raw.engineVersion}，但本程序支持 ${SUPPORTED_ENGINE}。`);
  }
  if (!(raw.capabilities && (raw.capabilities.light || raw.capabilities.dark))) {
    addError(v, 'manifest.capabilities.empty', '主题至少需支持浅色或深色之一。');
  }
  if (!/^advanced$/i.test(raw.type || '')) {
    addError(v, 'manifest.type.invalid', '仅支持 advanced 类型主题。');
  }
  const usesShared =
    raw.template &&
    /^flagship$/i.test(raw.template.id || '') &&
    /^1\.0$/i.test(raw.template.version || '') &&
    /^shared$/i.test(raw.template.style || '');
  if (raw.template && !usesShared) {
    addError(v, 'manifest.template.unsupported', "共享主题须声明 template id 'flagship'、version '1.0'、style 'shared'。");
  }
  return { usesShared: !!usesShared };
}

function resolveContainedFile(root, rel, field, requiredExt, v) {
  if (!rel) {
    addError(v, 'path.missing', `${field} 必填。`);
    return null;
  }
  if (path.isAbsolute(rel)) {
    addError(v, 'path.rooted', `${field} 必须是相对路径。`, rel);
    return null;
  }
  const candidate = path.resolve(root, rel);
  const rel2 = path.relative(root, candidate);
  if (rel2 === '..' || rel2.startsWith(`..${path.sep}`) || path.isAbsolute(rel2)) {
    addError(v, 'path.outside-package', `${field} 越出了主题目录。`, rel);
    return null;
  }
  if (!fs.existsSync(candidate)) {
    addError(v, 'path.file.missing', `${field} 不存在。`, rel);
    return null;
  }
  if (fs.lstatSync(candidate).isSymbolicLink()) {
    addError(v, 'path.reparse-point', `${field} 不能是符号链接。`, rel);
    return null;
  }
  if (requiredExt && path.extname(candidate).toLowerCase() !== requiredExt.toLowerCase()) {
    addError(v, 'path.extension.invalid', `${field} 必须是 ${requiredExt} 文件。`, rel);
    return null;
  }
  return candidate;
}

function validatePreview(root, rel, field, v) {
  if (!rel) return null;
  const p = resolveContainedFile(root, rel, field, null, v);
  if (p && !RASTER.has(path.extname(p).toLowerCase())) {
    addError(v, 'preview.extension.unsupported', '预览图必须是位图（png/jpg/webp 等）。', rel);
    return null;
  }
  return p;
}

// 加载并校验一个主题目录，返回与原 ThemeLoadResult 等价的对象。
function loadTheme(themeDir, v = makeValidation()) {
  const root = path.resolve(themeDir);
  if (!fs.existsSync(root) || !fs.statSync(root).isDirectory()) {
    addError(v, 'package.directory.missing', '主题目录不存在。', root);
    return { package: null, validation: v };
  }
  const manifestPath = path.join(root, MANIFEST_FILE);
  if (!fs.existsSync(manifestPath)) {
    addError(v, 'manifest.missing', `缺少 ${MANIFEST_FILE}。`, manifestPath);
    return { package: null, validation: v };
  }
  if (fs.statSync(manifestPath).size > MAX_MANIFEST) {
    addError(v, 'manifest.too-large', '清单文件超过 256 KiB。', manifestPath);
    return { package: null, validation: v };
  }
  let raw;
  try {
    raw = JSON.parse(fs.readFileSync(manifestPath, 'utf8'));
  } catch (e) {
    addError(v, 'manifest.invalid-json', '清单不是合法 JSON：' + e.message, manifestPath);
    return { package: null, validation: v };
  }

  const { usesShared } = validateManifest(raw, manifestPath, v);

  let cssPath = null;
  if (raw.entryPoints && raw.entryPoints.css) {
    cssPath = resolveContainedFile(root, raw.entryPoints.css, 'entryPoints.css', '.css', v);
    if (cssPath) {
      if (fs.statSync(cssPath).size > MAX_CSS) addError(v, 'css.too-large', '主题 CSS 超过 2 MiB。', cssPath);
      const css = fs.readFileSync(cssPath, 'utf8');
      if (/@import\s/i.test(css)) addError(v, 'css.import.forbidden', 'CSS 不允许使用 @import。', cssPath);
      if (/url\s*\(\s*['"]?\s*(?:https?:|file:|data:text\/html)/i.test(css))
        addError(v, 'css.remote-url.forbidden', 'CSS 不允许引用远程 / file / HTML data URL。', cssPath);
      if (/javascript\s*:/i.test(css)) addError(v, 'css.javascript.forbidden', 'CSS 不允许使用 javascript: URL。', cssPath);
      if (/(?:behavior|-moz-binding)\s*:/i.test(css))
        addError(v, 'css.behavior.forbidden', 'CSS 不允许使用可执行绑定。', cssPath);
    }
  }

  let scriptPath = null;
  if (raw.entryPoints && raw.entryPoints.script) {
    scriptPath = resolveContainedFile(root, raw.entryPoints.script, 'entryPoints.script', '.js', v);
    if (scriptPath && fs.statSync(scriptPath).size > MAX_SCRIPT)
      addError(v, 'script.too-large', '主题脚本超过 2 MiB。', scriptPath);
  } else {
    addError(v, 'entry.script.missing', '主题需要 entryPoints.script。');
  }

  const assets = {};
  let totalAssets = 0;
  for (const [name, rel] of Object.entries(raw.assets || {})) {
    if (!ASSET_NAME_RE.test(name)) {
      addError(v, 'asset.name.invalid', '资源名只能用字母、数字、点、下划线、连字符。', name);
      continue;
    }
    const ap = resolveContainedFile(root, rel, `assets.${name}`, null, v);
    if (!ap) continue;
    const ext = path.extname(ap).toLowerCase();
    if (!OPEN_ASSET.has(ext)) {
      addError(v, 'asset.extension.unsupported', '该资源类型不受支持。', rel);
      continue;
    }
    const size = fs.statSync(ap).size;
    if (size > MAX_ASSET) {
      addError(v, 'asset.too-large', '单个资源不能超过 25 MiB。', rel);
      continue;
    }
    totalAssets += size;
    assets[name] = ap;
  }
  if (totalAssets > MAX_TOTAL_ASSETS) addError(v, 'assets.total-too-large', '资源总大小不能超过 100 MiB。');

  const previewLightPath = validatePreview(root, raw.previews && raw.previews.light, 'previews.light', v);
  const previewDarkPath = validatePreview(root, raw.previews && raw.previews.dark, 'previews.dark', v);

  if (!isValid(v)) return { package: null, validation: v };

  const manifest = {
    id: raw.id || '',
    name: raw.name || '',
    version: raw.version || '',
    author: raw.author || '',
    description: raw.description || '',
    engineVersion: raw.engineVersion || 0,
    type: raw.type || '',
    usesSharedTemplateV1: usesShared,
    capabilities: raw.capabilities || { light: false, dark: false },
    entryPoints: raw.entryPoints || {},
    previews: raw.previews || {},
    assets: raw.assets || {},
    config: raw.config || {},
    compatibility: raw.compatibility || {},
  };

  return {
    package: {
      rootDirectory: root,
      manifestPath,
      manifest,
      cssPath,
      scriptPath,
      assetPaths: assets,
      previewLightPath,
      previewDarkPath,
      isAdvanced: scriptPath != null,
    },
    validation: v,
  };
}

// 计算主题指纹（对应 ThemeFingerprintCalculator）。
function calculateFingerprint(pkg) {
  const hash = crypto.createHash('sha256');
  const files = { manifest: pkg.manifestPath };
  if (pkg.cssPath) files.css = pkg.cssPath;
  if (pkg.scriptPath) files.script = pkg.scriptPath;
  if (pkg.previewLightPath) files['preview.light'] = pkg.previewLightPath;
  if (pkg.previewDarkPath) files['preview.dark'] = pkg.previewDarkPath;
  for (const [name, p] of Object.entries(pkg.assetPaths)) files[`asset.${name}`] = p;

  for (const key of Object.keys(files).sort()) {
    hash.update(`${key}\0${path.relative(pkg.rootDirectory, files[key])}\0`);
    hash.update(fs.readFileSync(files[key]));
  }
  return hash.digest('hex');
}

function calculateEffectiveFingerprint(pkg, sharedTemplatePath) {
  const base = calculateFingerprint(pkg);
  const hash = crypto.createHash('sha256');
  hash.update(`package\0${base}\0shared.template-v1\0`);
  hash.update(fs.readFileSync(sharedTemplatePath));
  return hash.digest('hex');
}

const MIME = {
  '.png': 'image/png', '.webp': 'image/webp', '.gif': 'image/gif', '.avif': 'image/avif',
  '.bmp': 'image/bmp', '.ico': 'image/x-icon', '.svg': 'image/svg+xml',
  '.woff': 'font/woff', '.woff2': 'font/woff2', '.ttf': 'font/ttf', '.otf': 'font/otf',
  '.json': 'application/json', '.mp3': 'audio/mpeg', '.wav': 'audio/wav', '.ogg': 'audio/ogg',
  '.m4a': 'audio/mp4', '.mp4': 'video/mp4', '.webm': 'video/webm', '.txt': 'text/plain', '.md': 'text/plain',
};

function dataUrl(p) {
  const ext = path.extname(p).toLowerCase();
  const mime = MIME[ext] || 'image/jpeg';
  return `data:${mime};base64,${fs.readFileSync(p).toString('base64')}`;
}

// 组装最终注入脚本（对应 ThemePayloadBuilder.BuildAsync）。
// skipAssets=true 时不内联资源(base64 体积可达数十 MB),改为依赖
// window.__TESSALUME_STAGED_ASSETS__ 预置(运行时 bootstrap 已支持该回退)。
// 这样可避免单条 Runtime.evaluate 发送超大脚本导致 Chromium 解析异常。
export function buildPayload(pkg, compatRoot, options = {}) {
  // runtime 从 options.runtimeDir（web/.runtime，由 composeRuntime 剥离信封生成）读取；
  // profile / template 仍从原生 compatRoot 读取。二者不可混用，否则会读到含信封标记的旧文件。
  const runtimeDir = options.runtimeDir || compatRoot;
  const runtimePath = path.join(runtimeDir, 'theme-runtime-v2.js');
  const templatePath = path.join(compatRoot, 'theme-template-v1.css');
  const profilePath = path.join(compatRoot, 'compatibility-profile-v3.json');

  if (!fs.existsSync(runtimePath)) throw new Error('未找到组装后的 theme-runtime-v2.js，请确认兼容层资源已就绪。');
  if (!fs.existsSync(profilePath)) throw new Error('未找到 compatibility-profile-v3.json。');

  let runtime = fs.readFileSync(runtimePath, 'utf8');
  const profileRaw = fs.readFileSync(profilePath, 'utf8');

  const templateCss = pkg.manifest.usesSharedTemplateV1 ? fs.readFileSync(templatePath, 'utf8') : '';
  const css = pkg.cssPath ? fs.readFileSync(pkg.cssPath, 'utf8') : '';
  const script = pkg.scriptPath ? fs.readFileSync(pkg.scriptPath, 'utf8') : '';

  const assets = {};
  for (const [name, p] of Object.entries(pkg.assetPaths)) assets[name] = dataUrl(p);

  const fingerprint = pkg.manifest.usesSharedTemplateV1
    ? calculateEffectiveFingerprint(pkg, templatePath)
    : calculateFingerprint(pkg);

  const json = (v) => JSON.stringify(v ?? null);
  const assetsValue = options.skipAssets ? 'null' : json(assets);
  let payload = runtime
    .replaceAll('__TESSALUME_PAYLOAD_THEME_ID_JSON__', json(pkg.manifest.id))
    .replaceAll('__TESSALUME_PAYLOAD_COMPATIBILITY_PROFILE_JSON__', profileRaw)
    .replaceAll('__TESSALUME_PAYLOAD_TEMPLATE_CSS_JSON__', json(templateCss))
    .replaceAll('__TESSALUME_PAYLOAD_CSS_JSON__', json(css))
    .replaceAll('__TESSALUME_PAYLOAD_SCRIPT_JSON__', json(script))
    .replaceAll('__TESSALUME_PAYLOAD_ASSETS_JSON__', assetsValue)
    .replaceAll('__TESSALUME_PAYLOAD_CONFIG_JSON__', json(pkg.manifest.config))
    .replaceAll('__TESSALUME_PAYLOAD_ALLOW_PET_OVERLAY__', pkg.manifest.compatibility.petOverlay ? 'true' : 'false')
    .replaceAll('__TESSALUME_PAYLOAD_FINGERPRINT_JSON__', json(fingerprint));

  if (payload.includes('__DREAM_') || payload.includes('__TESSALUME_PAYLOAD_')) {
    throw new Error('主题运行时脚本存在未替换的占位符。');
  }
  return payload;
}

// 把主题资源分块写入 window.__TESSALUME_STAGED_ASSETS__（避免单条超大 Runtime.evaluate）。
const STAGE_CHUNK_CHARS = 4 * 1024 * 1024; // 每块约 4MB 字符，远低于触发 Chromium 异常的阈值。
export async function stageAssetsForPkg(port, pkg) {
  const assets = {};
  for (const [name, p] of Object.entries(pkg.assetPaths)) assets[name] = dataUrl(p);

  let pending = {};
  const sendPending = async () => {
    if (Object.keys(pending).length === 0) return;
    const expr = `(function(){window.__TESSALUME_STAGED_ASSETS__=window.__TESSALUME_STAGED_ASSETS__||{};var s=${JSON.stringify(pending)};for(var k in s)window.__TESSALUME_STAGED_ASSETS__[k]=s[k];return Object.keys(window.__TESSALUME_STAGED_ASSETS__).length;})()`;
    await runOnPort(port, expr);
    pending = {};
  };
  for (const [name, dataUrl] of Object.entries(assets)) {
    pending[name] = dataUrl;
    if (JSON.stringify(pending).length > STAGE_CHUNK_CHARS) {
      await sendPending();
    }
  }
  await sendPending();
  return Object.keys(assets).length;
}

// 移除主题表达式（对应 ThemeRuntime.RemoveThemeExpression）。
// 优先调用运行时自带的 dispose（清理 observer/interval/标记），再兜底移除 class/style。
export const REMOVE_EXPRESSION = `(async()=>{try{if(window.__TESSALUME_RUNTIME__&&window.__TESSALUME_RUNTIME__.dispose){await window.__TESSALUME_RUNTIME__.dispose();}}catch(e){}var h=document.documentElement;h.className=h.className.split(' ').filter(c=>c.indexOf('tessalume')!==0&&c.indexOf('ae3')!==0).join(' ');var r=document.getElementById('tessalume-theme-root');if(r)r.remove();delete window.__TESSALUME_THEME_ID__;delete window.__TESSALUME_STAGED_ASSETS__;return true;})()`;

// 切换深/浅色表达式（对应 ThemeRuntime.ColorSchemeToggleExpression）。
export const TOGGLE_EXPRESSION = `(()=>{const h=document.documentElement;const dark=h.classList.toggle('tessalume-color-scheme-dark');h.classList.toggle('tessalume-color-scheme-light',!dark);return dark;})()`;

export { loadTheme, isValid, makeValidation };
