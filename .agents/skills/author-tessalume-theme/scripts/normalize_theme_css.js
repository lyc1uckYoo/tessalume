#!/usr/bin/env node
"use strict";

const fs = require("fs");
const path = require("path");
const childProcess = require("child_process");

const runtimeSuffixes = new Set([
  "theme", "is-home", "is-task", "is-settings", "has-output", "main", "sidebar",
  "home", "window-bar", "markdown", "message-assistant", "message-user", "chat-paper",
  "task-header", "task-title", "output-section", "output-header", "output-panel",
  "project-heading", "expand-label", "expand-row", "section-label", "settings-surface",
]);

const geometryProperties = new Set([
  "position", "inset", "inset-inline", "inset-block", "top", "right", "bottom", "left",
  "width", "min-width", "max-width", "height", "min-height", "max-height", "display",
  "z-index", "box-sizing", "overflow-x", "align-items", "justify-content",
  "flex", "flex-direction", "gap", "margin", "margin-top", "margin-right", "margin-bottom",
  "margin-left", "padding", "padding-top", "padding-right", "padding-bottom", "padding-left",
  "pointer-events", "white-space",
]);

const sharedCanvasProperties = new Set([
  "content", "position", "inset", "z-index", "pointer-events", "isolation",
]);

const sharedTransparentFillProperties = new Set([
  "background", "background-color", "background-image", "backdrop-filter",
  "-webkit-backdrop-filter",
]);

const sectionOrder = [
  "tokens", "app", "sidebar", "home", "identity", "task", "memory", "sync",
  "message", "composer", "components", "media", "keyframes",
];

const sectionTitles = {
  tokens: "01 Light and dark tokens",
  app: "02 App and native planes",
  sidebar: "03 Sidebar skin",
  home: "04 Home skin",
  identity: "05 Identity skin",
  task: "06 Task-card skin",
  memory: "07 Memory skin",
  sync: "08 Sync-panel skin",
  message: "09 Message and output frames",
  composer: "10 Composer skin",
  components: "11 Character components",
  media: "12 Theme-only media behavior",
  keyframes: "13 Character keyframes",
};

function stripComments(css) {
  return css.replace(/\/\*[\s\S]*?\*\//g, "");
}

function findOpenBrace(text, start) {
  let quote = "";
  let escaped = false;
  for (let index = start; index < text.length; index += 1) {
    const character = text[index];
    if (quote) {
      if (escaped) escaped = false;
      else if (character === "\\") escaped = true;
      else if (character === quote) quote = "";
      continue;
    }
    if (character === '"' || character === "'") quote = character;
    else if (character === "{") return index;
  }
  return -1;
}

function findCloseBrace(text, open) {
  let depth = 1;
  let quote = "";
  let escaped = false;
  for (let index = open + 1; index < text.length; index += 1) {
    const character = text[index];
    if (quote) {
      if (escaped) escaped = false;
      else if (character === "\\") escaped = true;
      else if (character === quote) quote = "";
      continue;
    }
    if (character === '"' || character === "'") quote = character;
    else if (character === "{") depth += 1;
    else if (character === "}" && --depth === 0) return index;
  }
  throw new Error("Unbalanced CSS block");
}

function parseBlocks(css) {
  const nodes = [];
  let cursor = 0;
  while (cursor < css.length) {
    while (cursor < css.length && /\s/.test(css[cursor])) cursor += 1;
    if (cursor >= css.length) break;
    const open = findOpenBrace(css, cursor);
    if (open < 0) {
      if (css.slice(cursor).trim()) throw new Error(`Unexpected CSS tail: ${css.slice(cursor, cursor + 80)}`);
      break;
    }
    const header = css.slice(cursor, open).trim().replace(/\s+/g, " ");
    const close = findCloseBrace(css, open);
    const body = css.slice(open + 1, close);
    if (/^@media\b/i.test(header)) {
      nodes.push({ type: "media", header, children: parseBlocks(body) });
    } else if (/^@keyframes\b/i.test(header)) {
      nodes.push({ type: "keyframes", header, children: parseBlocks(body) });
    } else if (header.startsWith("@")) {
      nodes.push({ type: "raw", header, body: body.trim() });
    } else {
      nodes.push({ type: "rule", selector: normalizeSelector(header), declarations: parseDeclarations(body) });
    }
    cursor = close + 1;
  }
  return nodes;
}

function splitTopLevel(text, separator) {
  const result = [];
  let start = 0;
  let depth = 0;
  let quote = "";
  let escaped = false;
  for (let index = 0; index < text.length; index += 1) {
    const character = text[index];
    if (quote) {
      if (escaped) escaped = false;
      else if (character === "\\") escaped = true;
      else if (character === quote) quote = "";
      continue;
    }
    if (character === '"' || character === "'") quote = character;
    else if (character === "(" || character === "[") depth += 1;
    else if (character === ")" || character === "]") depth -= 1;
    else if (character === separator && depth === 0) {
      result.push(text.slice(start, index));
      start = index + 1;
    }
  }
  result.push(text.slice(start));
  return result;
}

function parseDeclarations(body) {
  const declarations = [];
  for (const item of splitTopLevel(body, ";")) {
    const text = item.trim();
    if (!text) continue;
    let colon = -1;
    let depth = 0;
    let quote = "";
    for (let index = 0; index < text.length; index += 1) {
      const character = text[index];
      if (quote) {
        if (character === quote && text[index - 1] !== "\\") quote = "";
      } else if (character === '"' || character === "'") quote = character;
      else if (character === "(") depth += 1;
      else if (character === ")") depth -= 1;
      else if (character === ":" && depth === 0) { colon = index; break; }
    }
    if (colon < 1) throw new Error(`Invalid declaration: ${text}`);
    declarations.push({ property: text.slice(0, colon).trim(), value: text.slice(colon + 1).trim() });
  }
  return declarations;
}

function normalizeSelector(selector) {
  return selector
    .replace(/\s+/g, " ")
    .replace(/\s*([>+~])\s*/g, "$1")
    .replace(/\s*,\s*/g, ",")
    .trim();
}

function splitSelectors(selector) {
  return splitTopLevel(selector, ",").map((item) => item.trim()).filter(Boolean);
}

function collectUsedClasses(script, prefix) {
  const used = new Set([...runtimeSuffixes].map((suffix) => `${prefix}-${suffix}`));
  for (const match of script.matchAll(/class=["']([^"']+)["']/g)) {
    for (const className of match[1].split(/\s+/)) if (className) used.add(className);
  }
  for (const match of script.matchAll(/(?:className|stageClass)\s*:\s*["']([^"']+)["']/g)) {
    for (const className of match[1].split(/\s+/)) if (className) used.add(className);
  }
  for (const match of script.matchAll(/["'`]\.([a-z][a-z0-9_-]+)/gi)) used.add(match[1]);
  return used;
}

function filterUnusedSelectors(selector, prefix, used) {
  const branches = splitSelectors(selector).filter((branch) => {
    const own = [...branch.matchAll(new RegExp(`\\.(${prefix}-[a-z0-9_-]+)`, "gi"))].map((match) => match[1]);
    return own.length === 0 || own.every((className) => used.has(className));
  });
  return branches.join(",");
}

function collectOwnedGeometryClasses(script, prefix) {
  // Only the stage and the classes assigned to Template 1.0 outer slots are
  // shared-geometry owners. Classes inside slot.html (hero-motion, sync
  // instruments, message frames, SVG parts, and so on) remain theme-owned.
  const result = new Set([`${prefix}-stage`]);
  for (const match of script.matchAll(/(?:className|stageClass)\s*:\s*["']([^"']+)["']/g)) {
    for (const className of match[1].split(/\s+/)) {
      if (className.startsWith(`${prefix}-`)) result.add(className);
    }
  }
  return result;
}

function isCommonGeometrySelector(selector, prefix, owned) {
  if (selector.includes("[data-theme-role=") || selector.includes("[data-theme-stage]")) return true;
  if (selector.includes("cts-code-review-open")) return true;
  return splitSelectors(selector).some((branch) => {
    if (branch.includes("::")) return false;
    return [...owned].some((className) => {
      const match = branch.match(new RegExp(`\\.${className}(?![A-Za-z0-9_-])`));
      if (!match) return false;
      const tail = branch.slice(match.index + match[0].length);
      return /^(?:\[[^\]]+\]|:[A-Za-z-]+(?:\([^)]*\))?)*$/.test(tail);
    });
  });
}

function cleanRule(node, prefix, used, owned, preserveSelectors = false) {
  if (!preserveSelectors) node.selector = filterUnusedSelectors(node.selector, prefix, used);
  if (!node.selector) return null;
  if (isCommonGeometrySelector(node.selector, prefix, owned)) {
    node.declarations = node.declarations.filter(({ property }) => !geometryProperties.has(property.toLowerCase()));
  }
  const canvasSelector = `main.${prefix}-main`;
  if (node.selector.includes(canvasSelector)) {
    if (node.selector.includes("::before") || node.selector.includes("::after")) {
      node.declarations = node.declarations.filter(({ property }) =>
        !sharedCanvasProperties.has(property.toLowerCase()));
    } else if (node.selector.includes(`${prefix}-is-task`)) {
      node.declarations = node.declarations.filter(({ property }) =>
        !["position", "isolation"].includes(property.toLowerCase()));
    }
  }
  const chatPaper = node.selector.includes(`.${prefix}-chat-paper`);
  const messageFill =
    (node.selector.includes(`.${prefix}-message-assistant`) && node.selector.includes(`.${prefix}-markdown`)) ||
    (node.selector.includes(`.${prefix}-message-user`) &&
      (node.selector.includes(`.${prefix}-markdown`) || node.selector.includes("data-user-message-bubble")));
  if (chatPaper || messageFill) {
    node.declarations = node.declarations.filter(({ property }) =>
      !sharedTransparentFillProperties.has(property.toLowerCase()));
  }
  const directChatPaper = chatPaper && splitSelectors(node.selector).every((branch) =>
    new RegExp(`\\.${prefix}-chat-paper$`).test(branch));
  if (directChatPaper) {
    node.declarations = node.declarations.filter(({ property }) =>
      !["position", "isolation", "padding-left", "padding-right"].includes(property.toLowerCase()));
  }
  if (chatPaper && node.selector.includes("::before")) {
    node.declarations = node.declarations.filter(({ property }) =>
      !["content", "display"].includes(property.toLowerCase()));
  }
  if (node.selector.includes(`.${prefix}-chat-paper>*`)) {
    node.declarations = node.declarations.filter(({ property }) =>
      !["position", "z-index"].includes(property.toLowerCase()));
  }
  if (node.selector.includes("thread-scroll-container")) {
    node.declarations = node.declarations.filter(({ property }) =>
      !["padding", "padding-top"].includes(property.toLowerCase()));
  }
  if (node.selector.includes("[data-app-action-sidebar-project-row]") &&
      !node.selector.includes("::")) {
    node.declarations = node.declarations.filter(({ property }) =>
      !["position", "height", "margin"].includes(property.toLowerCase()));
  }
  if (node.selector.includes("[data-app-action-sidebar-thread-row]") &&
      !node.selector.includes("::")) {
    node.declarations = node.declarations.filter(({ property }) =>
      !["position", "min-height", "margin", "padding-left", "padding-right"].includes(property.toLowerCase()));
  }
  if (node.selector.includes(`.${prefix}-home`)) {
    node.declarations = node.declarations.filter(({ property }) =>
      !["--thread-content-max-width", "--cts-v1-home-hero-height"].includes(property.toLowerCase()));
  }
  const homeHeroSurface = node.selector.includes(`.${prefix}-home>div:first-child>div:first-child>div:first-child`);
  if (homeHeroSurface && (node.selector.includes("::before") || node.selector.includes("::after"))) {
    node.declarations = node.declarations.filter(({ property }) =>
      !["position", "z-index", "inset", "pointer-events"].includes(property.toLowerCase()));
  }
  const homeSuggestion = node.selector.includes("group\\/home-suggestions");
  if (homeSuggestion) {
    const sharedSuggestionProperties = new Set();
    if (/group\\\/home-suggestions$/.test(node.selector)) {
      ["position", "z-index", "gap"].forEach((property) => sharedSuggestionProperties.add(property));
    }
    if (/group\\\/home-suggestions button$/.test(node.selector)) {
      ["position", "overflow", "min-height", "padding"].forEach((property) => sharedSuggestionProperties.add(property));
    }
    if (/group\\\/home-suggestions button::before$/.test(node.selector)) {
      ["position", "z-index", "left", "top", "display", "place-items", "width", "height"].forEach((property) => sharedSuggestionProperties.add(property));
    }
    if (/group\\\/home-suggestions button::after$/.test(node.selector)) {
      ["position", "z-index", "right", "top"].forEach((property) => sharedSuggestionProperties.add(property));
    }
    if (/group\\\/home-suggestions button>(?:\*|div)$/.test(node.selector)) {
      ["position", "z-index"].forEach((property) => sharedSuggestionProperties.add(property));
    }
    node.declarations = node.declarations.filter(({ property }) =>
      !sharedSuggestionProperties.has(property.toLowerCase()));
  }
  if (node.selector.includes(`[data-testid="home-icon"]`)) {
    node.declarations = node.declarations.filter(({ property }) => property.toLowerCase() !== "display");
  }
  return node.declarations.length ? node : null;
}

const baselineAssetAliases = {
  "aemeath-star-voyage": {
    "task-signal": "task-left",
    "task-voyage": "task-right-secondary",
    "task-tunneler": "task-right-primary",
  },
  "danya.bubble-void-duality": {
    "sidebar-light-v3": "sidebar-light",
    "sidebar-dark-v3": "sidebar-dark",
    "danya-light": "task-left",
    "danya-dark": "task-left-dark",
    "portrait-light": "task-left",
    "portrait-dark": "task-left-dark",
    "task-alt-light": "task-right-secondary",
    "task-alt-dark": "task-right-secondary-dark",
    "companion-light": "task-right-primary",
    "companion-dark": "task-right-primary-dark",
  },
  "hiyuki.crimson-snow": {
    "task-vow": "task-left",
    "task-present": "task-right-secondary",
    "task-foreclaimed": "task-right-primary",
  },
  "qingxiao.cloudsword-gate": {
    "task-guard": "task-left",
    "task-array": "task-right-secondary",
    "task-draw": "task-right-primary",
    "hecate-light": "task-right-primary",
    "hecate-dark": "task-right-primary",
    "task-qxoolova": "task-left",
    "task-qxoolova-right": "task-right-secondary",
  },
  "shorekeeper.tethys-reverie": {
    "task-echo": "task-left",
    "task-butterfly": "task-right-secondary",
    "task-tethys": "task-right-primary",
  },
  "suisui.inkscape-dawn": {
    "task-flight": "task-left",
    "task-scroll": "task-right-secondary",
    "task-chongming": "task-right-primary",
  },
  "xin.moonfox-sovereign": {
    "task-oracle": "task-left",
    "task-human": "task-right-secondary",
    "task-fox": "task-right-primary",
  },
  "yangyang.xuanling-echo": {
    "task-sword": "task-left",
    "task-yangyang": "task-right-secondary",
    "task-bird": "task-right-primary",
  },
};

function cleanNodesPreservingCascade(nodes, prefix, used, owned) {
  const result = [];
  for (const node of nodes) {
    if (node.type === "rule") {
      const cleaned = cleanRule(node, prefix, used, owned, true);
      if (cleaned) result.push(cleaned);
    } else if (node.type === "media") {
      const children = cleanNodesPreservingCascade(node.children, prefix, used, owned);
      if (children.length) result.push({ ...node, children });
    } else {
      result.push(node);
    }
  }
  return result;
}

function normalizeFromBaseline(themeDirectory, prefix, baselineRef) {
  const themeName = path.basename(themeDirectory);
  const repoRoot = path.resolve(__dirname, "../../../..");
  const baselinePath = `themes/${themeName}/theme.css`;
  let source = childProcess.execFileSync(
    "git",
    ["show", `${baselineRef}:${baselinePath}`],
    { cwd: repoRoot, encoding: "utf8", maxBuffer: 8 * 1024 * 1024 },
  );
  const surfaceMarker = source.indexOf("/* TESSALUME_TEMPLATE_V1_SURFACE_START */");
  if (surfaceMarker < 0) throw new Error(`Baseline surface marker is missing: ${baselinePath}`);
  source = source.slice(0, surfaceMarker).trim();
  const aliases = Object.entries(baselineAssetAliases[themeName] || {})
    .sort(([left], [right]) => right.length - left.length);
  for (const [oldName, newName] of aliases) {
    source = source.replaceAll(`--cts-asset-${oldName}`, `--cts-asset-${newName}`);
  }
  if (themeName === "hiyuki.crimson-snow") {
    source = source.replace(
      "background:var(--hy3-chat-art) center center/contain no-repeat!important",
      "background:var(--hy3-chat-art) center center/cover no-repeat!important",
    );
  }
  if (themeName === "danya.bubble-void-duality") {
    source = source.replace(/animation:dny-(?:pearl|void)-bubble[^;}]*;?/g, "");
  }

  const script = fs.readFileSync(path.join(themeDirectory, "theme.js"), "utf8");
  const used = collectUsedClasses(script, prefix);
  const owned = collectOwnedGeometryClasses(script, prefix);
  let nodes = cleanNodesPreservingCascade(parseBlocks(stripComments(source)), prefix, used, owned);
  if (themeName === "danya.bubble-void-duality") {
    nodes = nodes.filter((node) =>
      !["@keyframes dny-pearl-bubble", "@keyframes dny-void-bubble"].includes(node.header));
  }
  if (themeName === "xin.moonfox-sovereign") {
    const upsert = (node, property, value) => {
      const existing = node.declarations.find((item) => item.property === property);
      if (existing) existing.value = value;
      else node.declarations.push({ property, value });
    };
    let titleRestyled = false;
    for (const node of nodes) {
      if (node.type !== "rule") continue;
      if (node.selector === ".xmf-task-header") {
        upsert(node, "border", "0!important");
        upsert(node, "border-radius", "0!important");
        upsert(node, "background", "transparent!important");
        upsert(node, "box-shadow", "none!important");
      }
      if (node.selector === ".xmf-task-title" && !titleRestyled) {
        titleRestyled = true;
        upsert(node, "display", "inline-flex!important");
        upsert(node, "align-items", "center");
        upsert(node, "min-height", "32px");
        upsert(node, "padding-left", "10px!important");
        upsert(node, "border", "1px solid var(--xmf-line-gold)!important");
        upsert(node, "border-radius", "18px 3px 18px 3px!important");
        upsert(node, "background", "linear-gradient(90deg,color-mix(in srgb,var(--xmf-blue) 12%,var(--xmf-solid)),color-mix(in srgb,var(--xmf-solid) 80%,transparent))!important");
        upsert(node, "box-shadow", "0 7px 21px color-mix(in srgb,var(--xmf-blue) 13%,transparent)!important");
      }
    }
  }
  const sectionIndex = sectionOrder
    .map((name) => `/* ${sectionTitles[name]} */`)
    .join("\n");
  const body = nodes.map((node) => formatNode(node)).join("\n\n");
  const output = `${sectionIndex}\n\n/* Character cascade: original visual order preserved. */\n${body}\n`;
  const manifest = JSON.parse(fs.readFileSync(path.join(themeDirectory, "manifest.json"), "utf8"));
  fs.writeFileSync(path.join(themeDirectory, manifest.entryPoints.css), output, "utf8");
}

function mergeRules(nodes) {
  const lastBySelector = new Map();
  nodes.forEach((node, index) => {
    if (node.type === "rule") lastBySelector.set(node.selector, index);
  });
  const declarationsBySelector = new Map();
  nodes.forEach((node, index) => {
    if (node.type !== "rule") return;
    let record = declarationsBySelector.get(node.selector);
    if (!record) declarationsBySelector.set(node.selector, record = new Map());
    for (const declaration of node.declarations) {
      record.set(declaration.property.toLowerCase(), { ...declaration, sequence: index });
    }
  });
  return nodes.filter((node, index) => node.type !== "rule" || lastBySelector.get(node.selector) === index)
    .map((node) => node.type !== "rule" ? node : {
      ...node,
      declarations: [...declarationsBySelector.get(node.selector).values()].map(({ property, value }) => ({ property, value })),
    });
}

function normalizeNodes(nodes, prefix, used, owned) {
  const cleaned = [];
  for (const node of nodes) {
    if (node.type === "rule") {
      const rule = cleanRule(node, prefix, used, owned);
      if (rule) cleaned.push(rule);
    } else if (node.type === "media") {
      const children = normalizeNodes(node.children, prefix, used, owned);
      if (children.length) cleaned.push({ ...node, children });
    } else if (node.type === "keyframes") {
      cleaned.push({ ...node, children: mergeRules(node.children) });
    } else cleaned.push(node);
  }
  let merged = mergeRules(cleaned);

  const mediaGroups = new Map();
  const keyframes = new Map();
  merged.forEach((node, index) => {
    if (node.type === "media") {
      const existing = mediaGroups.get(node.header) || { index, children: [] };
      existing.index = index;
      existing.children.push(...node.children);
      mediaGroups.set(node.header, existing);
    } else if (node.type === "keyframes") {
      keyframes.set(node.header.replace(/\s+/g, " "), { index, node });
    }
  });
  merged = merged.filter((node, index) => {
    if (node.type === "media") return mediaGroups.get(node.header).index === index;
    if (node.type === "keyframes") return keyframes.get(node.header.replace(/\s+/g, " ")).index === index;
    return true;
  }).map((node) => {
    if (node.type !== "media") return node;
    return { ...node, children: mergeRules(mediaGroups.get(node.header).children) };
  });
  return merged;
}

function animationNames(nodes) {
  const names = new Set();
  const visit = (items) => items.forEach((node) => {
    if (node.type === "rule") {
      node.declarations.filter((item) => item.property.startsWith("animation")).forEach((item) => {
        for (const match of item.value.matchAll(/\b([a-z][a-z0-9_-]+)\b/gi)) names.add(match[1]);
      });
    } else if (node.children) visit(node.children);
  });
  visit(nodes);
  return names;
}

function removeUnusedKeyframes(nodes, script) {
  const used = animationNames(nodes);
  return nodes.filter((node) => {
    if (node.type !== "keyframes") return true;
    const name = node.header.replace(/^@keyframes\s+/i, "").trim();
    return used.has(name) || script.includes(name);
  });
}

function removeUnusedThemeVariables(nodes, script, prefix) {
  const referenced = new Set([...script.matchAll(/var\(\s*(--[a-z0-9_-]+)/gi)].map((match) => match[1]));
  const visitValues = (items) => items.forEach((node) => {
    if (node.type === "rule") {
      node.declarations.forEach(({ value }) => {
        for (const match of value.matchAll(/var\(\s*(--[a-z0-9_-]+)/gi)) referenced.add(match[1]);
      });
    }
    if (node.children) visitValues(node.children);
  });
  visitValues(nodes);
  const ownPrefix = `--${prefix}-`;
  const prune = (items) => items.map((node) => {
    if (node.type === "rule") {
      node.declarations = node.declarations.filter(({ property }) =>
        !property.startsWith(ownPrefix) || referenced.has(property));
    }
    if (node.children) node.children = prune(node.children);
    return node;
  }).filter((node) => node.type !== "rule" || node.declarations.length);
  return prune(nodes);
}

function category(node, prefix) {
  if (node.type === "media") return "media";
  if (node.type === "keyframes") return "keyframes";
  if (node.type !== "rule") return "components";
  const selector = node.selector.toLowerCase();
  if (node.declarations.every((item) => item.property.startsWith("--") || item.property === "color-scheme")) return "tokens";
  if (/sidebar|project-heading|section-label|expand-|thread-row|project-row/.test(selector)) return "sidebar";
  if (/hero|home|suggestions|kicker/.test(selector)) return "home";
  if (/identity|window-bar/.test(selector)) return "identity";
  if (/task-(?:card|left|right|secondary|primary)|task-title/.test(selector)) return "task";
  if (/memory/.test(selector)) return "memory";
  if (/sync/.test(selector)) return "sync";
  if (/message|markdown|chat-paper|output|task-header|turn-diff/.test(selector)) return "message";
  if (/composer/.test(selector)) return "composer";
  if (new RegExp(`html\\.${prefix}-theme|\\.${prefix}-theme body|\\.${prefix}-theme #root|\\.${prefix}-main`).test(selector)) return "app";
  return "components";
}

function formatRule(node, indent = "") {
  const body = node.declarations.map(({ property, value }) => `${indent}  ${property}:${value};`).join("\n");
  return `${indent}${node.selector} {\n${body}\n${indent}}`;
}

function formatNode(node, indent = "") {
  if (node.type === "rule") return formatRule(node, indent);
  if (node.type === "raw") return `${indent}${node.header} {\n${indent}  ${node.body}\n${indent}}`;
  const children = node.children.map((child) => formatNode(child, `${indent}  `)).join("\n\n");
  return `${indent}${node.header} {\n${children}\n${indent}}`;
}

function normalizeTheme(themeDirectory, prefix) {
  const manifest = JSON.parse(fs.readFileSync(path.join(themeDirectory, "manifest.json"), "utf8"));
  const cssPath = path.join(themeDirectory, manifest.entryPoints.css);
  const scriptPath = path.join(themeDirectory, "theme.js");
  const script = fs.readFileSync(scriptPath, "utf8");
  const used = collectUsedClasses(script, prefix);
  const owned = collectOwnedGeometryClasses(script, prefix);
  let nodes = parseBlocks(stripComments(fs.readFileSync(cssPath, "utf8")));
  nodes = normalizeNodes(nodes, prefix, used, owned);
  nodes = removeUnusedKeyframes(nodes, script);
  nodes = removeUnusedThemeVariables(nodes, script, prefix);
  const sections = new Map(sectionOrder.map((name) => [name, []]));
  nodes.forEach((node) => sections.get(category(node, prefix)).push(node));
  const output = sectionOrder
    .map((name) => `/* ${sectionTitles[name]} */${sections.get(name).length ? `\n${sections.get(name).map((node) => formatNode(node)).join("\n\n")}` : ""}`)
    .join("\n\n") + "\n";
  fs.writeFileSync(cssPath, output, "utf8");
}

const themeDirectory = process.argv[2];
const prefix = process.argv[3];
const baselineIndex = process.argv.indexOf("--baseline-ref");
const baselineRef = baselineIndex >= 0 ? process.argv[baselineIndex + 1] : "";
if (!themeDirectory || !/^[a-z][a-z0-9]*$/.test(prefix || "")) {
  console.error("usage: normalize_theme_css.js <theme-directory> <namespace> [--baseline-ref <ref>]");
  process.exit(2);
}
if (baselineRef) normalizeFromBaseline(path.resolve(themeDirectory), prefix, baselineRef);
else normalizeTheme(path.resolve(themeDirectory), prefix);
