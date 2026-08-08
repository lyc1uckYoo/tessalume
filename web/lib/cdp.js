// 原生 CDP（Chrome DevTools Protocol）客户端，仅依赖 Node 内置 WebSocket（Node >= 21）。
// 对应原 C# 版 CdpSession / LoopbackCdpDiscovery / ThemeRuntime 的协议交互部分。
import http from 'node:http';

const inspectExpression = `(function(){var h=document.querySelector('html');if(!h)return{dark:false};var d=h.classList.contains('tessalume-color-scheme-dark')||h.classList.contains('dark')||window.matchMedia('(prefers-color-scheme: dark)').matches;return{dark:!!d};})()`;

function httpGetJson(url) {
  return new Promise((resolve, reject) => {
    const req = http.get(url, (res) => {
      let body = '';
      res.on('data', (chunk) => (body += chunk));
      res.on('end', () => {
        try {
          resolve(JSON.parse(body));
        } catch (e) {
          reject(new Error('无法解析 CDP 列表响应：' + e.message));
        }
      });
    });
    req.on('error', reject);
    req.setTimeout(2500, () => req.destroy(new Error('连接超时')));
  });
}

async function findAppPageTarget(port) {
  const list = await httpGetJson(`http://127.0.0.1:${port}/json/list`);
  const targets = Array.isArray(list) ? list : list && Array.isArray(list.targets) ? list.targets : [];
  const page = targets.find(
    (t) =>
      t.type === 'page' &&
      typeof t.url === 'string' &&
      t.url.startsWith('app://') &&
      t.webSocketDebuggerUrl
  );
  if (!page) {
    const any = targets.find((t) => t.type === 'page' && t.webSocketDebuggerUrl);
    return any || null;
  }
  return page;
}

async function isCodexReachable(port) {
  try {
    const list = await httpGetJson(`http://127.0.0.1:${port}/json/list`);
    return Array.isArray(list) && list.length > 0;
  } catch {
    return false;
  }
}

// 连接一个 CDP target 的 WebSocket 并执行一次 Runtime.evaluate。
function evaluateOnTarget(wsUrl, expression) {
  return new Promise((resolve, reject) => {
    const ws = new WebSocket(wsUrl);
    let msgId = 0;
    const pending = new Map();
    let settled = false;

    const finish = (err, value) => {
      if (settled) return;
      settled = true;
      try {
        ws.close();
      } catch {}
      if (err) reject(err);
      else resolve(value);
    };

    ws.onopen = () => {
      const id = ++msgId;
      pending.set(id, (result) => finish(null, result));
      ws.send(
        JSON.stringify({
          id,
          method: 'Runtime.evaluate',
          params: {
            expression,
            returnByValue: true,
            awaitPromise: true,
            userGesture: true,
            timeout: 20000,
          },
        })
      );
    };

    ws.onmessage = (event) => {
      let msg;
      try {
        msg = JSON.parse(event.data);
      } catch {
        return;
      }
      if (msg.id && pending.has(msg.id)) {
        const cb = pending.get(msg.id);
        pending.delete(msg.id);
        if (msg.error) {
          finish(new Error(msg.error.message || 'CDP 执行失败'));
        } else if (msg.result && msg.result.exceptionDetails) {
          // 脚本编译/运行时异常（如 SyntaxError），必须抛出，否则上层会误以为注入成功。
          const ex = msg.result.exceptionDetails.exception || {};
          finish(new Error(ex.description || ex.text || '主题脚本执行异常'));
        } else {
          cb(msg.result);
        }
      }
    };

    ws.onerror = (e) => finish(new Error(e.message || 'CDP WebSocket 错误'));
    ws.onclose = () => finish(new Error('CDP 连接在结果返回前关闭'));
    setTimeout(() => finish(new Error('CDP 执行超时')), 25000);
  });
}

// 在指定端口上执行注入/移除/切换表达式。
export async function runOnPort(port, expression) {
  const target = await findAppPageTarget(port);
  if (!target) {
    throw new Error('未找到 Codex 的 app:// 页面 target，请确认 Codex 已以调试端口启动。');
  }
  return await evaluateOnTarget(target.webSocketDebuggerUrl, expression);
}

// 重新加载目标页面，清除全局作用域中残留的 TDZ 变量（历史失败注入可能留下
// 处于暂时性死区的 const 声明，导致后续注入报 "Identifier has already been declared"）。
export async function reloadTarget(port) {
  const target = await findAppPageTarget(port);
  if (!target) return;
  return await new Promise((resolve, reject) => {
    const ws = new WebSocket(target.webSocketDebuggerUrl);
    let settled = false;
    const finish = (err, value) => {
      if (settled) return;
      settled = true;
      try { ws.close(); } catch {}
      if (err) reject(err); else resolve(value);
    };
    ws.onopen = () => {
      ws.send(JSON.stringify({ id: 1, method: 'Page.reload' }));
      // Page.reload 不返回结果帧，给短暂等待后结束。
      setTimeout(() => finish(null, true), 800);
    };
    ws.onmessage = (event) => {
      let msg;
      try { msg = JSON.parse(event.data); } catch { return; }
      if (msg.id === 1) {
        if (msg.error) finish(new Error(msg.error.message));
        else finish(null, true);
      }
    };
    ws.onerror = (e) => finish(new Error(e.message || 'CDP WebSocket 错误'));
    setTimeout(() => finish(new Error('Page.reload 超时')), 5000);
  });
}

export async function getScheme(port) {
  const res = await runOnPort(port, inspectExpression);
  const value = res && res.result && res.result.value;
  return !!(value && value.dark);
}

export async function discoverCodex(ports = [9222, 9333, 9555, 9777, 9888, 9999]) {
  const found = [];
  for (const port of ports) {
    if (await isCodexReachable(port)) found.push(port);
  }
  return found;
}

export async function probe(port) {
  return await isCodexReachable(port);
}
