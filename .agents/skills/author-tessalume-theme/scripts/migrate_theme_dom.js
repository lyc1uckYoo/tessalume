#!/usr/bin/env node
"use strict";

const fs = require("fs");
const path = require("path");
const { execFileSync } = require("child_process");

const voidTags = new Set([
  "area", "base", "br", "circle", "col", "ellipse", "embed", "hr", "img", "input",
  "line", "link", "meta", "param", "path", "polygon", "polyline", "source", "stop",
  "track", "use", "wbr",
]);

function parseHtml(html) {
  const roots = [];
  const stack = [];
  const pattern = /<!--[^]*?-->|<\/?[A-Za-z][^>]*>/g;
  for (const match of html.matchAll(pattern)) {
    const source = match[0];
    if (source.startsWith("<!--")) continue;
    const closing = source.startsWith("</");
    const nameMatch = source.match(/^<\/?\s*([A-Za-z][A-Za-z0-9:-]*)/);
    if (!nameMatch) continue;
    const tag = nameMatch[1].toLowerCase();
    if (closing) {
      let node = stack.pop();
      while (node && node.tag !== tag) node = stack.pop();
      if (!node) throw new Error(`Unexpected closing tag: ${source}`);
      node.endStart = match.index;
      node.end = match.index + source.length;
      continue;
    }
    const node = {
      tag,
      source,
      start: match.index,
      contentStart: match.index + source.length,
      endStart: match.index + source.length,
      end: match.index + source.length,
      children: [],
    };
    if (stack.length) stack[stack.length - 1].children.push(node);
    else roots.push(node);
    const selfClosing = /\/\s*>$/.test(source) || voidTags.has(tag);
    if (!selfClosing) stack.push(node);
  }
  if (stack.length) throw new Error(`Unclosed HTML tag: ${stack.at(-1).tag}`);
  return roots;
}

function flatten(nodes) {
  return nodes.flatMap((node) => [node, ...flatten(node.children)]);
}

function attribute(node, name) {
  const pattern = new RegExp(`${name}\\s*=\\s*["']([^"']*)["']`, "i");
  return node.source.match(pattern)?.[1] || "";
}

function findTemplateEnd(source, start) {
  const skipQuoted = (index, quote) => {
    for (let cursor = index + 1; cursor < source.length; cursor += 1) {
      if (source[cursor] === "\\") cursor += 1;
      else if (source[cursor] === quote) return cursor + 1;
    }
    throw new Error("Unterminated JavaScript string");
  };
  const skipTemplate = (index) => {
    for (let cursor = index + 1; cursor < source.length; cursor += 1) {
      if (source[cursor] === "\\") cursor += 1;
      else if (source[cursor] === "`") return cursor + 1;
      else if (source[cursor] === "$" && source[cursor + 1] === "{") {
        cursor = skipExpression(cursor + 2) - 1;
      }
    }
    throw new Error("Unterminated nested template literal");
  };
  const skipExpression = (index) => {
    let depth = 1;
    for (let cursor = index; cursor < source.length; cursor += 1) {
      const character = source[cursor];
      if (character === '"' || character === "'") cursor = skipQuoted(cursor, character) - 1;
      else if (character === "`") cursor = skipTemplate(cursor) - 1;
      else if (character === "{") depth += 1;
      else if (character === "}" && --depth === 0) return cursor + 1;
    }
    throw new Error("Unterminated template expression");
  };
  for (let cursor = start; cursor < source.length; cursor += 1) {
    if (source[cursor] === "\\") cursor += 1;
    else if (source[cursor] === "`") return cursor;
    else if (source[cursor] === "$" && source[cursor + 1] === "{") {
      cursor = skipExpression(cursor + 2) - 1;
    }
  }
  throw new Error("Unterminated template literal");
}

function slotSource(html, node, key) {
  if (!node) throw new Error(`Missing Template 1.0 slot: ${key}`);
  const className = attribute(node, "class");
  const inner = html.slice(node.contentStart, node.endStart).trim();
  return `        ${key}: { tag: ${JSON.stringify(node.tag)}, className: ${JSON.stringify(className)}, html: \`${inner}\` },`;
}

function decorationSource(html, nodes, key) {
  const content = nodes.map((node) => html.slice(node.start, node.end).trim()).filter(Boolean).join("\n");
  return content ? `        ${key}: \`${content}\`,` : "";
}

function extractMarkup(script) {
  const assignments = ["context.root.innerHTML = `", "root.innerHTML = `"];
  const assignment = assignments.find((candidate) => script.includes(candidate));
  const start = assignment ? script.indexOf(assignment) : -1;
  if (start < 0) throw new Error("Missing root markup assignment");
  const contentStart = start + assignment.length;
  const contentEnd = findTemplateEnd(script, contentStart);
  let html = script.slice(contentStart, contentEnd);
  let replacementEnd = script.indexOf(";", contentEnd) + 1;
  const insertToken = 'root.insertAdjacentHTML("beforeend", `';
  const insertStart = script.indexOf(insertToken, replacementEnd);
  if (insertStart >= 0) {
    const insertContentStart = insertStart + insertToken.length;
    const insertContentEnd = findTemplateEnd(script, insertContentStart);
    html += `\n${script.slice(insertContentStart, insertContentEnd)}`;
    replacementEnd = script.indexOf(");", insertContentEnd) + 2;
  }
  return { start, replacementEnd, html };
}

function decorations(html) {
  const roots = parseHtml(html);
  const nodes = flatten(roots);
  const stage = nodes.find((node) => /\bdata-theme-stage(?:\s|=|>)/i.test(node.source));
  if (!stage) throw new Error("Missing data-theme-stage");
  const stageNodes = stage.children.filter((node) => !attribute(node, "data-theme-role"));
  const rootNodes = roots.filter((node) =>
    node !== stage && !attribute(node, "data-theme-role"));
  return { stage, stageNodes, rootNodes, nodes };
}

function migrate(file) {
  let script = fs.readFileSync(file, "utf8");
  if (!script.includes("root.innerHTML = `") && !script.includes("context.root.innerHTML = `")) {
    if (script.includes("context.renderTemplateV1(")) return;
    throw new Error(`Missing root markup assignment: ${file}`);
  }
  const { start, replacementEnd, html } = extractMarkup(script);
  const { stage, stageNodes, rootNodes, nodes } = decorations(html);
  const role = (name) => nodes.find((node) => attribute(node, "data-theme-role") === name);
  const right = (priority) => nodes.find((node) =>
    attribute(node, "data-theme-role") === "task-right" &&
    attribute(node, "data-theme-priority") === priority);
  const replacement = [
    "context.renderTemplateV1({",
    `        stageClass: ${JSON.stringify(attribute(stage, "class"))},`,
    decorationSource(html, stageNodes, "stageDecorations"),
    decorationSource(html, rootNodes, "rootDecorations"),
    slotSource(html, role("hero"), "hero"),
    slotSource(html, role("identity"), "identity"),
    slotSource(html, role("task-left"), "taskLeft"),
    slotSource(html, right("secondary"), "taskSecondary"),
    slotSource(html, right("primary"), "taskPrimary"),
    slotSource(html, role("memory"), "memory"),
    slotSource(html, role("sync-panel"), "syncPanel"),
    slotSource(html, role("composer-accessory"), "composerAccessory"),
    "      });",
  ].filter(Boolean).join("\n");
  script = script.slice(0, start) + replacement + script.slice(replacementEnd);
  fs.writeFileSync(file, script, "utf8");
}

function restoreDecorations(file, baselineRef) {
  let script = fs.readFileSync(file, "utf8");
  if (script.includes("stageDecorations:") || script.includes("rootDecorations:")) return;
  const relative = path.relative(process.cwd(), file).replaceAll(path.sep, "/");
  const baseline = execFileSync("git", ["show", `${baselineRef}:${relative}`], { encoding: "utf8" });
  const { html } = extractMarkup(baseline);
  const { stageNodes, rootNodes } = decorations(html);
  const additions = [
    decorationSource(html, stageNodes, "stageDecorations"),
    decorationSource(html, rootNodes, "rootDecorations"),
  ].filter(Boolean).join("\n");
  if (!additions) return;
  const marker = /(^\s*stageClass\s*:\s*[^\n]+\n)/m;
  if (!marker.test(script)) throw new Error(`Missing stageClass in ${file}`);
  script = script.replace(marker, `$1${additions}\n`);
  fs.writeFileSync(file, script, "utf8");
}

const restoring = process.argv[2] === "--restore-decorations";
const baselineRef = restoring ? process.argv[3] : "";
const file = restoring ? process.argv[4] : process.argv[2];
if (!file) {
  console.error("usage: migrate_theme_dom.js <theme.js> | --restore-decorations <git-ref> <theme.js>");
  process.exit(2);
}
if (restoring) restoreDecorations(path.resolve(file), baselineRef);
else migrate(path.resolve(file));
