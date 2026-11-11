#!/usr/bin/env node
// jev-kit: testes com o Jev (TypeSafe System One) em qualquer repositório do lukscf.
//
// O Jev dá os JUÍZOS ("esta tela está em branco?", "o PR promete algo que o diff não faz?") com
// probabilidade calibrada; quem DECIDE se a checagem passa é o código de cada checagem (`decide`),
// como uma tabela-verdade. É a mesma divisão do grok_crew/jev.py.
//
// Por que existe: julgar print, texto ou diff com um LLM grande custa milhares de tokens e
// minutos por pergunta. O Jev responde em ~1 s e custa centavos por milhão de tokens; então o
// Jev pode fazer essas perguntas em todo PR (não há CI: quem roda é o bin/tank4-jev do tank4),
// e o agente lê o veredito em vez de abrir o print.
//
// Uso:   node .jev/jev.mjs run [--all] [--only nome] [--dry] [--base SHA --head SHA]
//        node .jev/jev.mjs list            lista as checagens que rodariam
//        node .jev/jev.mjs version
// As checagens ficam em .jev/checks.mjs (ver GUIA.md no tank4: ops/jev-kit/GUIA.md).
//
// Saída: 0 = nada reprovou; 1 = alguma checagem reprovou (ou JEV_STRICT=1 e houve erro);
// 2 = configuração inválida. Falha da API NUNCA vira resposta: vira status "erro".
//
// Fonte única: lukscf/tank4, ops/jev-kit/jev.mjs. Não edite a cópia em .jev/ de outro repo;
// atualize com `/data/data/com.termux/files/home/tank4/bin/jev-kit update <repo>`.

import { createHash } from 'node:crypto';
import { execFileSync } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

export const VERSION = '1.1.0';
export const MODEL = process.env.JEV_MODEL || 'jev-1.14.0'; // fixo: limiares calibrados nele
export const URL = process.env.JEV_URL || 'https://api.typesafe.ai/v1/systemone';
export const YES = 0.7, NO = 0.3; // acima de YES é sim, abaixo de NO é não, no meio não decidiu
const PRICE_PER_M = 0.042; // US$ por milhão de tokens de entrada (2026-11); só para estimativa
const MAX_CHECKS = +(process.env.JEV_MAX_CHECKS || 200); // teto de custo por run
const CONCURRENCY = +(process.env.JEV_CONCURRENCY || 4);
const TIMEOUT_MS = +(process.env.JEV_TIMEOUT_MS || 20000);
// Prazo do run inteiro, abaixo do timeout-minutes: 15 do workflow. API lenta não pode segurar o
// runner do celular: passou do prazo, o que falta vira "erro" (aviso) e o job termina.
const BUDGET_MS = +(process.env.JEV_BUDGET_MS || 8 * 60000);
let deadline = Date.now() + BUDGET_MS;
const RETRY_MS = +(process.env.JEV_RETRY_MS || 1000);

// ── perguntas ────────────────────────────────────────────────────────────────
export const noul = (instructions, t = '', f = '') =>
  t || f ? { type: 'noul', instructions, criteria: { true: t, false: f } } : { type: 'noul', instructions };
export const choice = (instructions, criteria) => ({ type: 'choice', instructions, criteria });
export const score = (instructions, levels) => ({ type: 'score', instructions, criteria: levels });
export const box = (instructions) => ({ type: 'box', instructions });

// Imagem: use no `state` (ex.: { tela: image('out/menu.png') }). PNG ou JPEG, 280 a 1568 px por lado.
export const image = (file) => ({ __jevImage: file });

// ── vereditos (o que `decide` devolve) ─────────────────────────────────────────
export const pass = (msg = '') => ({ status: 'passou', msg });
export const fail = (msg) => ({ status: 'reprovou', msg });
export const warn = (msg) => ({ status: 'aviso', msg });
export const yes = (p) => p >= YES;
export const no = (p) => p <= NO;
export const unsure = (p) => p > NO && p < YES;
const pct = (p) => (typeof p === 'number' ? `${Math.round(p * 100)}%` : String(p));

// ── utilitários de repositório (ctx) ─────────────────────────────────────────
function git(root, args) {
  return execFileSync('git', args, { cwd: root, encoding: 'utf8', maxBuffer: 64 << 20, stdio: ['ignore', 'pipe', 'ignore'] });
}

export function globToRegex(glob) {
  if (glob.endsWith('/')) glob += '**';
  let re = '';
  for (let i = 0; i < glob.length; i++) {
    const c = glob[i];
    if (c === '*' && glob[i + 1] === '*') { re += '.*'; i++; if (glob[i + 1] === '/') i++; }
    else if (c === '*') re += '[^/]*';
    else if (c === '?') re += '[^/]';
    else re += c.replace(/[.+^${}()|[\]\\]/g, '\\$&');
  }
  return new RegExp('^' + re + '$');
}
export const matches = (file, globs) => [].concat(globs).some((g) => globToRegex(g).test(file));

// Segredo não sai do repositório: linha com cara de credencial e sequência longa aleatória são
// trocadas antes do envio. O gitleaks do CI é quem barra; isto é só para não vazar para a TypeSafe.
const SECRET_LINE = /((?:api[_-]?key|secret|token|passw(?:or)?d|senha|authorization|private[_-]?key)["']?\s*(?:=>|:=|[:=])\s*["']?)(?:(?:Bearer|Basic|token)\s+)?([^\s"',;]{6,})/gi;
const AUTH_SCHEME = /\b(Bearer|Basic)\s+[A-Za-z0-9._~+/=-]{8,}/g;
const URL_CRED = /\b([a-z][a-z0-9+.-]*:\/\/[^\s:@/]+:)[^\s@/]+@/gi;
const AWS_KEY = /\b(?:AKIA|ASIA)[0-9A-Z]{16}\b/g;
// Sequência longa com cara de chave: maiúscula+minúscula+dígito, ou hex longo que não é SHA-1
// de commit. Caminho de arquivo e nome-com-hífen ficam: o Jev precisa deles para ler o diff.
const LONG_TOKEN = /[A-Za-z0-9_+=-]{32,}/g;
const looksRandom = (t) => (/[A-Z]/.test(t) && /[a-z]/.test(t) && /\d/.test(t)) || (/^[0-9a-f]+$/i.test(t) && t.length !== 40);
export const redact = (s) => s.replace(SECRET_LINE, '$1[redigido]').replace(AUTH_SCHEME, '$1 [redigido]')
  .replace(URL_CRED, '$1[redigido]@').replace(AWS_KEY, '[redigido]').replace(LONG_TOKEN, (t) => (looksRandom(t) ? '[redigido]' : t));

function makeCtx(root, base, head, event) {
  let changedCache;
  const ctx = {
    root, base, head, event,
    /** Arquivos mudados no PR; null quando não há base (roda tudo). */
    get changed() {
      if (changedCache !== undefined) return changedCache;
      if (!base) return (changedCache = null);
      try { changedCache = git(root, ['diff', '--name-only', `${base}...${head || 'HEAD'}`]).split('\n').filter(Boolean); }
      catch {
        // Base fora do clone (checkout raso?): roda tudo em vez de pular calado. Use fetch-depth: 0.
        console.log(`::warning title=Jev::não consegui comparar com ${String(base).slice(0, 12)}; rodando todas as checagens (use fetch-depth: 0 no checkout)`);
        changedCache = null;
      }
      return changedCache;
    },
    changedMatching: (globs) => (ctx.changed || []).filter((f) => matches(f, globs)),
    exists: (f) => fs.existsSync(path.join(root, f)),
    read: (f, max = 0) => { const t = fs.readFileSync(path.join(root, f), 'utf8'); return max && t.length > max ? t.slice(0, max) : t; },
    json: (f) => JSON.parse(fs.readFileSync(path.join(root, f), 'utf8')),
    files: (globs) => git(root, ['ls-files']).split('\n').filter((f) => f && matches(f, globs)),
    /** Diff do PR (sem base: vazio). Devolve { text, truncated }. */
    diff: ({ paths = [], exclude = [], max = 60000 } = {}) => {
      if (!base) return { text: '', truncated: false };
      const spec = [...paths, ...exclude.map((e) => `:(exclude)${e}`)];
      let text;
      try { text = git(root, ['diff', '--unified=3', `${base}...${head || 'HEAD'}`, '--', ...spec]); }
      catch { throw new Error('git diff falhou (checkout raso? use fetch-depth: 0)'); }
      return text.length > max ? { text: text.slice(0, max), truncated: true } : { text, truncated: false };
    },
    /** Só as linhas adicionadas de um arquivo no PR. */
    added: (file, max = 20000) => {
      const { text } = ctx.diff({ paths: [file], max: max * 2 });
      const out = text.split('\n').filter((l) => l.startsWith('+') && !l.startsWith('+++')).map((l) => l.slice(1)).join('\n');
      return out.length > max ? out.slice(0, max) : out;
    },
    prTitle: event?.pull_request?.title || '',
    prBody: event?.pull_request?.body || '',
  };
  return ctx;
}

// ── chave ────────────────────────────────────────────────────────────────────
// Ordem: variável de ambiente (secret do Actions) → JEV_KEY_FILE → ~/desktop-agent/.env do tank4.
// Dentro do Actions vale SÓ o secret: o runner do celular enxerga o .env do bot, e um PR roda o
// .jev/checks.mjs dele mesmo; ler o .env ali daria a chave do bot a qualquer PR e faria "sem
// secret, pula" virar mentira.
export function apiKey() {
  if (process.env.TYPESAFE_API_KEY) return process.env.TYPESAFE_API_KEY.trim();
  if (process.env.GITHUB_ACTIONS === 'true' || process.env.JEV_KEY_FILES === '0') return '';
  const files = [process.env.JEV_KEY_FILE, path.join(os.homedir(), 'desktop-agent', '.env'), '/data/data/com.termux/files/home/desktop-agent/.env', '/home/optus/desktop-agent/.env'].filter(Boolean);
  for (const f of files) {
    try {
      for (const line of fs.readFileSync(f, 'utf8').split(/\r?\n/))
        if (line.startsWith('TYPESAFE_API_KEY=')) return line.slice(17).trim().replace(/^["']|["']$/g, '');
    } catch { /* tenta o próximo */ }
  }
  return '';
}

// ── montagem da requisição ───────────────────────────────────────────────────
function pngSize(buf) {
  if (buf.length > 24 && buf.readUInt32BE(0) === 0x89504e47) return [buf.readUInt32BE(16), buf.readUInt32BE(20)];
  return null;
}
function jpegSize(buf) {
  if (buf[0] !== 0xff || buf[1] !== 0xd8) return null;
  for (let i = 2; i < buf.length - 9;) {
    if (buf[i] !== 0xff) { i++; continue; }
    const m = buf[i + 1], len = buf.readUInt16BE(i + 2);
    if (m >= 0xc0 && m <= 0xcf && m !== 0xc4 && m !== 0xc8 && m !== 0xcc) return [buf.readUInt16BE(i + 7), buf.readUInt16BE(i + 5)];
    i += 2 + len;
  }
  return null;
}

/** Troca image() por referência, limpa segredos dos textos e devolve { body, images }. */
export function buildRequest(root, check, state) {
  const assets = {};
  let n = 0;
  const walk = (v) => {
    if (typeof v === 'string') return redact(v);
    if (Array.isArray(v)) return v.map(walk);
    if (v && typeof v === 'object') {
      if (v.__jevImage) {
        const file = path.resolve(root, v.__jevImage);
        const buf = fs.readFileSync(file);
        const isPng = !!pngSize(buf);
        const [w, h] = pngSize(buf) || jpegSize(buf) || [];
        if (!w) throw new Error(`${v.__jevImage}: não é PNG nem JPEG`);
        if (w < 280 || h < 280 || w > 1568 || h > 1568 || w * h > 2458624)
          throw new Error(`${v.__jevImage}: ${w}x${h} fora do limite do Jev (280 a 1568 px por lado)`);
        // Referência = hash do conteúdo: estável (o cache funciona) e nunca substring de outra.
        const ref = `img_${createHash('sha256').update(buf).digest('hex').slice(0, 24)}`;
        n++;
        assets[ref] = { type: 'image', format: isPng ? 'png' : 'jpeg', data_base64: buf.toString('base64') };
        return ref;
      }
      return Object.fromEntries(Object.entries(v).map(([k, x]) => [k, walk(x)]));
    }
    return v;
  };
  const body = { model: check.model || MODEL, state: walk(state), questions: walk(check.questions || {}) };
  if (n) body.assets = assets;
  const hasBox = Object.values(check.questions || {}).some((q) => q.type === 'box');
  return { body, images: n > 0 || hasBox };
}

// ── chamada à API ────────────────────────────────────────────────────────────
export class JevError extends Error {}
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

export async function ask(body, { key, images = false, fetchImpl = globalThis.__jevFetch || fetch } = {}) {
  const headers = { Authorization: `Bearer ${key}`, 'Content-Type': 'application/json', 'User-Agent': `jev-kit/${VERSION}` };
  if (images) headers['TypeSafe-Danger-Zone-Images'] = 'true';
  for (let attempt = 0; ; attempt++) {
    const left = deadline - Date.now();
    if (left < 1000) throw new JevError('prazo do run esgotado (JEV_BUDGET_MS)');
    let res;
    try {
      res = await fetchImpl(URL, { method: 'POST', headers, body: JSON.stringify(body), signal: AbortSignal.timeout(Math.min(TIMEOUT_MS, left)) });
    } catch (e) {
      if (attempt < 2) { await sleep(RETRY_MS * 2 ** attempt); continue; }
      throw new JevError(`rede: ${e.name}`);
    }
    if ((res.status === 429 || res.status >= 500) && attempt < 2) { await sleep(RETRY_MS * 1.5 * 2 ** attempt); continue; }
    if (!res.ok) {
      // Só o código: o corpo de um 400/422 ecoa o state, que pode ser código privado.
      let code = '';
      try { const j = await res.json(); code = j?.error?.code || j?.code || j?.error?.type || ''; } catch { /* sem corpo */ }
      throw new JevError(`HTTP ${res.status}${code ? ' ' + code : ''}`);
    }
    const data = await res.json();
    const answers = data.answers || {};
    const missing = Object.keys(body.questions).filter((q) => !(q in answers));
    if (missing.length) throw new JevError(`sem resposta para ${missing.join(', ')}`);
    // noul vira número (probabilidade de sim); os outros tipos ficam com a resposta inteira.
    const out = {};
    for (const [k, a] of Object.entries(answers)) out[k] = a.type === 'noul' ? a.noul : a;
    return { answers: out, usage: data.usage || {}, model: data.model };
  }
}

// ── cache ────────────────────────────────────────────────────────────────────
// Mesma pergunta sobre o mesmo state = mesma resposta. Nos runners do tank4 o HOME persiste,
// então re-run de PR sem mudança não paga de novo.
const CACHE_DIR = process.env.JEV_CACHE_DIR || path.join(os.homedir(), '.cache', 'jev-kit');
const cacheKey = (body) => createHash('sha256').update(JSON.stringify(body)).digest('hex');
function cacheGet(k) { try { return JSON.parse(fs.readFileSync(path.join(CACHE_DIR, k + '.json'), 'utf8')); } catch { return null; } }
function cachePut(k, v) {
  if (process.env.JEV_NO_CACHE === '1') return;
  try { fs.mkdirSync(CACHE_DIR, { recursive: true }); fs.writeFileSync(path.join(CACHE_DIR, k + '.json'), JSON.stringify(v)); } catch { /* cache é opcional */ }
}

// ── execução ─────────────────────────────────────────────────────────────────
// Progresso ao vivo (JEV_PROGRESS=1): uma linha `::jev-progress::{json}` por mudança de
// estado de cada checagem (fila, run, depois o status final). O tank4-jev e o jev-auto
// leem essas linhas e mostram no chat da Crew o que o Jev está testando enquanto roda.
export function progress(name, estado, msg = '') {
  if (process.env.JEV_PROGRESS !== '1') return;
  console.log(`::jev-progress::${JSON.stringify({ name, estado, msg: String(msg || '').replace(/\r?\n/g, ' ').slice(0, 200) })}`);
}

async function pool(items, n, fn) {
  const out = new Array(items.length);
  let i = 0;
  await Promise.all(Array.from({ length: Math.min(n, items.length) }, async () => {
    while (i < items.length) { const j = i++; out[j] = await fn(items[j], j); }
  }));
  return out;
}

function validate(c, i) {
  const where = c?.name ? `checagem "${c.name}"` : `checagem #${i + 1}`;
  if (!c || typeof c !== 'object') throw new Error(`${where}: não é um objeto`);
  if (!/^[a-z0-9][a-z0-9._-]*$/i.test(c.name || '')) throw new Error(`${where}: name obrigatório (letras, números, . _ -)`);
  if (c.questions !== undefined && (typeof c.questions !== 'object' || !Object.keys(c.questions).length)) throw new Error(`${where}: questions vazio (omita para checagem só de código)`);
  if (typeof c.decide !== 'function') throw new Error(`${where}: sem decide(answers)`);
  if (c.state === undefined) throw new Error(`${where}: sem state`);
}

/** Carrega .jev/checks.mjs e devolve as checagens que se aplicam a este PR. */
export async function loadChecks(ctx, { all = false, only = null, file } = {}) {
  const cfg = file || path.join(ctx.root, '.jev', 'checks.mjs');
  if (!fs.existsSync(cfg)) throw new Error(`não achei ${path.relative(ctx.root, cfg)}`);
  const mod = await import(pathToFileURL(cfg).href + `?t=${Date.now()}`);
  let checks = typeof mod.default === 'function' ? await mod.default(ctx) : mod.default;
  checks = (checks || []).flat(Infinity).filter(Boolean);
  checks.forEach(validate);
  const names = new Set();
  for (const c of checks) { if (names.has(c.name)) throw new Error(`nome repetido: ${c.name}`); names.add(c.name); }
  if (only) checks = checks.filter((c) => matches(c.name, only.split(',')));
  // `files`: a checagem só roda se o PR mexeu em algum desses arquivos. Sem base, roda tudo.
  if (!all && ctx.changed) checks = checks.filter((c) => !c.files || ctx.changed.some((f) => matches(f, c.files)));
  return checks;
}

function normalize(v) {
  const verdict = typeof v === 'string' ? { status: v, msg: '' } : v;
  if (!verdict || !['passou', 'reprovou', 'aviso'].includes(verdict.status)) throw new Error('decide deve devolver pass(), fail() ou warn()');
  return verdict;
}

export async function runChecks(ctx, checks, { dry = false, key = apiKey() } = {}) {
  deadline = Date.now() + BUDGET_MS;
  if (checks.length > MAX_CHECKS) {
    console.log(`::warning title=Jev::${checks.length} checagens; rodando só as ${MAX_CHECKS} primeiras (JEV_MAX_CHECKS).`);
    checks = checks.slice(0, MAX_CHECKS);
  }
  for (const c of checks) progress(c.name, 'fila');
  const results = await pool(checks, CONCURRENCY, async (c) => {
    progress(c.name, 'run');
    const r = await runOne(c);
    progress(c.name, r.status, r.msg);
    return r;
  });
  return results;

  async function runOne(c) {
    const r = { name: c.name, status: 'erro', msg: '', answers: null, cached: false, tokens: 0 };
    let req;
    try {
      const state = typeof c.state === 'function' ? await c.state(ctx) : c.state;
      if (state === null) return { ...r, status: 'pulou', msg: 'não se aplica' };
      req = buildRequest(ctx.root, c, state);
    } catch (e) { return { ...r, msg: `montagem: ${e.message}` }; }
    if (!c.questions) { // checagem só de código: nada vai para o Jev
      try { Object.assign(r, normalize(c.decide({}, ctx))); } catch (e) { r.msg = `decide: ${e.message}`; }
      return r;
    }
    if (dry) return { ...r, status: 'seco', msg: 'não enviado (--dry)', payload: req.body };
    const k = cacheKey(req.body);
    let got = cacheGet(k);
    if (got) r.cached = true;
    else {
      if (!key) return { ...r, status: 'pulou', msg: 'sem TYPESAFE_API_KEY' };
      try { got = await ask(req.body, { key, images: req.images }); cachePut(k, got); }
      catch (e) { return { ...r, msg: e instanceof JevError ? e.message : `${e.name}` }; }
    }
    r.answers = got.answers;
    r.tokens = r.cached ? 0 : (got.usage?.input_tokens || 0);
    try { Object.assign(r, normalize(c.decide(got.answers, ctx))); } catch (e) { r.status = 'erro'; r.msg = `decide: ${e.message}`; }
    return r;
  }
}

const ICON = { passou: '✅', reprovou: '❌', aviso: '⚠️', erro: '💥', pulou: '⏭️', seco: '📝' };

function fmtAnswers(a) {
  if (!a) return '';
  return Object.entries(a).map(([k, v]) => `${k}=${typeof v === 'number' ? pct(v) : v.choice ?? (v.score !== undefined ? v.score.toFixed(2) : v.box ? 'box' : '?')}`).join(' ');
}

export function report(results, ctx, { out } = {}) {
  const count = (s) => results.filter((r) => r.status === s).length;
  const tokens = results.reduce((n, r) => n + r.tokens, 0);
  const summary = {
    version: VERSION, model: MODEL, base: ctx.base || null, head: ctx.head || null,
    passou: count('passou'), reprovou: count('reprovou'), aviso: count('aviso'), erro: count('erro'), pulou: count('pulou'),
    cache: results.filter((r) => r.cached).length, tokens, custo_usd_estimado: +(tokens * PRICE_PER_M / 1e6).toFixed(6),
    results: results.map(({ payload, ...r }) => r),
  };
  for (const r of results) {
    console.log(`${ICON[r.status] || '?'} ${r.name}${r.msg ? ' — ' + r.msg : ''}${r.answers ? '  [' + fmtAnswers(r.answers) + ']' : ''}${r.cached ? ' (cache)' : ''}`);
    if (r.payload) console.log(JSON.stringify(r.payload, (k, v) => (k === 'data_base64' ? `<${v.length} b64>` : v), 2));
    const msg = (r.msg || '').replace(/\r?\n/g, ' ');
    if (r.status === 'reprovou') console.log(`::error title=Jev: ${r.name}::${msg}`);
    if (r.status === 'aviso' || r.status === 'erro') console.log(`::warning title=Jev: ${r.name}::${msg}`);
  }
  console.log(`\nJev ${MODEL}: ${summary.passou} passou, ${summary.reprovou} reprovou, ${summary.aviso} aviso, ${summary.erro} erro, ${summary.pulou} pulou · ${tokens} tokens (~US$ ${summary.custo_usd_estimado}) · ${summary.cache} do cache`);
  const file = out || process.env.JEV_REPORT || path.join(process.env.RUNNER_TEMP || path.join(ctx.root, '.jev', 'out'), 'jev-report.json');
  try { fs.mkdirSync(path.dirname(file), { recursive: true }); fs.writeFileSync(file, JSON.stringify(summary, null, 2)); console.log(`Relatório: ${file}`); } catch { /* opcional */ }
  // No Actions, uma cópia fica em ~/.cache/jev-kit/reports/<repo>/<pr ou sha>.json: no runner do
  // celular o HOME persiste e o agente lê dali, sem depender de artefato (o runner não consegue
  // baixar a action upload-artifact). Relatórios com mais de 14 dias são apagados.
  if (process.env.GITHUB_ACTIONS === 'true' && process.env.GITHUB_REPOSITORY) {
    try {
      const dir = path.join(CACHE_DIR, 'reports', process.env.GITHUB_REPOSITORY.replace(/[^\w.-]+/g, '_'));
      fs.mkdirSync(dir, { recursive: true });
      const id = ctx.event?.pull_request?.number ? `pr-${ctx.event.pull_request.number}` : (process.env.GITHUB_SHA || 'run').slice(0, 12);
      fs.writeFileSync(path.join(dir, `${id}.json`), JSON.stringify(summary, null, 2));
      for (const f of fs.readdirSync(dir)) { const fp = path.join(dir, f); if (Date.now() - fs.statSync(fp).mtimeMs > 14 * 864e5) fs.rmSync(fp, { recursive: true, force: true }); }
      console.log(`Cópia no runner: ${path.join(dir, `${id}.json`)}`);
    } catch { /* opcional */ }
  }
  if (process.env.GITHUB_STEP_SUMMARY) {
    const rows = results.map((r) => `| ${ICON[r.status]} | \`${r.name}\` | ${(r.msg || '').replace(/\|/g, '\\|').replace(/\r?\n/g, ' ')} | ${fmtAnswers(r.answers)} |`);
    fs.appendFileSync(process.env.GITHUB_STEP_SUMMARY,
      `### Jev (${MODEL})\n\n${summary.passou} ✅ · ${summary.reprovou} ❌ · ${summary.aviso} ⚠️ · ${summary.erro} 💥 · ${tokens} tokens\n\n| | checagem | motivo | respostas |\n|---|---|---|---|\n${rows.join('\n')}\n`);
  }
  return summary;
}

// ── presets: checagens prontas que servem para qualquer repositório ─────────────
// Juízo novo começa como aviso; só vira reprovação depois de calibrado com `jev-kit probe`.
const PR_EXCLUDE = ['*.lock', 'package-lock.json', '*.min.js', '*.svg', '*.png', '*.jpg', '*.webp', '*.gif', '*.ico', '*.woff2', '*.ttf'];
export const presets = {
  /** O texto do PR promete algo que o diff não faz? (o caso do relatório inventado de 18/08) */
  prMatchesDiff(ctx) {
    if (!ctx.base || !(ctx.prBody || '').trim()) return [];
    return {
      name: 'pr-promete-o-que-o-diff-faz',
      state: () => {
        const d = ctx.diff({ exclude: PR_EXCLUDE });
        if (!d.text) return null;
        const facts = [`These files changed in the pull request: ${(ctx.changed || []).slice(0, 200).join(', ')}.`,
          'Binary files, lock files, minified files and images are left out of `diff` but are part of the change.'];
        if (d.truncated) facts.push('`diff` was cut at 60k characters; the rest of the change is not shown.');
        return { pr_title: ctx.prTitle, pr_description: ctx.prBody.slice(0, 6000), facts, diff: d.text };
      },
      questions: {
        promete_ausente: noul('Does `pr_description` claim a change that `diff` does not contain?',
          'The description says something was added, fixed, removed, or changed, and no part of `diff` does it.',
          'Every change the description claims can be found in `diff`, or the description only gives background.'),
      },
      decide(a) {
        const p = a.promete_ausente;
        if (yes(p)) return warn(`o texto do PR parece prometer algo que o diff não faz (${pct(p)})`);
        if (unsure(p)) return warn(`não ficou claro se o PR faz tudo o que promete (${pct(p)})`);
        return pass();
      },
    };
  },

  /** A entrada nova do CHANGELOG descreve o diff? */
  changelogMatchesDiff(ctx, file = 'CHANGELOG.md') {
    if (!ctx.changed?.includes(file)) return [];
    return {
      name: 'changelog-descreve-o-diff',
      files: [file],
      state: () => {
        const entry = ctx.added(file, 6000);
        const d = ctx.diff({ exclude: [file] });
        if (!entry.trim() || !d.text) return null;
        return { changelog_entry: entry, diff: d.text };
      },
      questions: {
        cita_ausente: noul('Does `changelog_entry` mention a change that `diff` does not make?',
          'The entry lists a fix, feature, or change that no part of `diff` implements.',
          'Each item in the entry corresponds to something `diff` changes.'),
        principal_coberto: noul('Is the main change in `diff` described in `changelog_entry`?',
          'A reader of the entry would expect the most important thing `diff` changes.',
          'The most important change in `diff` is missing from the entry.'),
      },
      decide(a) {
        if (yes(a.cita_ausente)) return fail(`o CHANGELOG cita mudança que o diff não faz (${pct(a.cita_ausente)})`);
        if (no(a.principal_coberto)) return warn(`a mudança principal não aparece no CHANGELOG (${pct(a.principal_coberto)})`);
        return pass();
      },
    };
  },

  /** A documentação contradiz o workflow que mudou? (o README que dizia o oposto do CI) */
  docsMatchWorkflows(ctx, docs = ['README.md', 'docs/**/*.md', 'ops/**/*.md']) {
    const wfs = ctx.changedMatching(['.github/workflows/*.yml', '.github/workflows/*.yaml']).filter(ctx.exists);
    const docFiles = ctx.files(docs);
    return wfs.map((wf) => {
      const base = path.basename(wf);
      // Código escolhe os trechos candidatos; o Jev só julga.
      const paras = [];
      for (const d of docFiles)
        for (const p of ctx.read(d).split(/\n\s*\n/))
          if (p.includes(base)) paras.push(`[${d}] ${p.trim().slice(0, 1500)}`); // só o que cita ESTE workflow
      if (!paras.length) return null;
      return {
        name: `docs-batem-com-${base.replace(/\.ya?ml$/, '')}`,
        state: { workflow_file: wf, workflow: ctx.read(wf, 30000), docs: paras.slice(0, 20) },
        questions: {
          contradiz: noul('Does a passage in `docs` describe this workflow differently from what `workflow` does?',
            'A passage states a trigger, job, runner, or step behaviour for this workflow that the YAML in `workflow` does differently.',
            'The passages agree with `workflow`, or talk about other workflows or about CI in general.'),
        },
        decide: (a) => (yes(a.contradiz) ? warn(`a documentação parece descrever ${wf} diferente do que ele faz (${pct(a.contradiz)})`) : pass()),
      };
    }).filter(Boolean);
  },

  /** Depuração temporária esquecida nas linhas novas (só aviso). */
  noDebugLeft(ctx, globs = ['**/*.js', '**/*.mjs', '**/*.py', '**/*.php', '**/*.cs'], limit = 15) {
    return ctx.changedMatching(globs).filter(ctx.exists).filter((f) => !/(^|\/)(tests?|spec|selftest)|_selftest\.|\.test\.|\.min\./.test(f)).slice(0, limit).map((f) => ({
      name: `sem-depuracao-${f.replace(/[^a-z0-9._-]+/gi, '_')}`.slice(0, 120),
      files: [f],
      state: () => { const code = ctx.added(f, 12000); return code.trim() ? { file: f, added_code: code } : null; },
      questions: {
        depuracao: noul('Do the lines in `added_code` contain temporary debugging output that was left in by mistake?',
          'For example a console.log, print, var_dump, or Console.WriteLine that dumps a variable with no message meant for users or for the logs.',
          'Output is part of the program\'s normal behaviour, logging with a clear purpose, or there is no output at all.'),
      },
      decide: (a) => (yes(a.depuracao) ? warn(`${f}: parece depuração esquecida (${pct(a.depuracao)})`) : pass()),
    }));
  },

  /** Página publicada com texto provisório ou recado para desenvolvedor. */
  pageText(ctx, globs = ['**/*.html', '**/*.php'], limit = 20) {
    const strip = (h) => h.replace(/<script[\s\S]*?<\/script>|<style[\s\S]*?<\/style>|<\?php[\s\S]*?(\?>|$)/gi, ' ')
      .replace(/<[^>]+>/g, ' ').replace(/&nbsp;/g, ' ').replace(/&amp;/g, '&').replace(/[ \t]+/g, ' ').replace(/\n\s*\n+/g, '\n').trim();
    const fora = ['.github/**', '**/tests/**', '**/test/**', 'vendor/**', '**/vendor/**', '**/api/**', '**/cron/**'];
    return ctx.changedMatching(globs).filter(ctx.exists).filter((f) => !matches(f, fora)).slice(0, limit).map((f) => ({
      name: `texto-da-pagina-${f.replace(/[^a-z0-9._-]+/gi, '_')}`.slice(0, 120),
      files: [f],
      state: () => {
        const src = ctx.read(f, 200000);
        // Só página que o visitante vê: PHP de backend (sem HTML fora do <?php) não entra.
        const html = src.replace(/<\?php[\s\S]*?(\?>|$)/gi, '');
        if ((html.match(/<(p|div|h[1-6]|section|main|body|a|span|li)\b/gi) || []).length < 3) return null;
        const t = strip(src).slice(0, 15000);
        return t.length > 40 ? { page: f, page_text: t } : null;
      },
      questions: {
        provisorio: noul('Does `page_text` contain placeholder text that was meant to be replaced before publishing?',
          'For example lorem ipsum, "texto aqui", "TODO", fake names like Fulano de Tal, or a sentence that stops in the middle.',
          'All the text reads as final content written for visitors.'),
        recado_dev: noul('Does `page_text` contain a note written for developers rather than for visitors?',
          'For example an internal reminder, debugging output, a file path, or instructions about how to edit the page.',
          'All the text is addressed to people visiting the page.'),
      },
      decide(a) {
        if (yes(a.provisorio)) return warn(`${f}: parece haver texto provisório na página (${pct(a.provisorio)})`);
        if (yes(a.recado_dev)) return warn(`${f}: recado para desenvolvedor visível na página (${pct(a.recado_dev)})`);
        return pass();
      },
    }));
  },

  /** Extensão de navegador: a política de privacidade explica cada permissão do manifest? */
  extensionPrivacy(ctx, privacy = 'PRIVACY.md', manifest = 'manifest.json') {
    if (!ctx.exists(manifest) || !ctx.exists(privacy)) return [];
    const m = ctx.json(manifest);
    const perms = [...(m.permissions || []), ...(m.optional_permissions || []), ...(m.host_permissions || [])];
    if (!perms.length) return [];
    const ids = perms.map((p, i) => [`p${i + 1}`, p]);
    return {
      name: 'privacidade-explica-permissoes',
      files: [manifest, privacy],
      state: { privacy_policy: ctx.read(privacy, 30000), extension_name: m.name || '', permissions: Object.fromEntries(ids) },
      questions: Object.fromEntries(ids.map(([k, p]) => [k, noul(
        `Does \`privacy_policy\` explain why the extension needs the \`${p}\` permission, or what it does with the access that \`${p}\` gives?`,
        `A reader of the policy learns what \`${p}\` is used for, by name or by describing that access.`,
        `The policy never mentions \`${p}\` or the access it gives.`)])),
      decide(a) {
        const missing = ids.filter(([k]) => no(a[k])).map(([, p]) => p);
        const doubt = ids.filter(([k]) => unsure(a[k])).map(([, p]) => p);
        if (missing.length) return fail(`${privacy} não explica: ${missing.join(', ')} (a Chrome Web Store pede justificativa de cada permissão)`);
        if (doubt.length) return warn(`${privacy} talvez não explique: ${doubt.join(', ')}`);
        return pass();
      },
    };
  },

  /** Todos os presets genéricos de uma vez. */
  all(ctx) { return [presets.prMatchesDiff(ctx), presets.changelogMatchesDiff(ctx), presets.docsMatchWorkflows(ctx), presets.noDebugLeft(ctx)]; },
};

// ── linha de comando ─────────────────────────────────────────────────────────
function parseArgs(argv) {
  const o = { cmd: argv[0] || 'run', all: false, dry: false, only: null, base: process.env.JEV_BASE || '', head: process.env.JEV_HEAD || '', root: process.cwd(), out: null };
  for (let i = 1; i < argv.length; i++) {
    const a = argv[i];
    if (a === '--all') o.all = true;
    else if (a === '--dry') o.dry = true;
    else if (a === '--only') o.only = argv[++i];
    else if (a === '--base') o.base = argv[++i];
    else if (a === '--head') o.head = argv[++i];
    else if (a === '--root') o.root = path.resolve(argv[++i]);
    else if (a === '--out') o.out = argv[++i];
    else throw new Error(`argumento desconhecido: ${a}`);
  }
  return o;
}

export async function main(argv = process.argv.slice(2)) {
  let o;
  try { o = parseArgs(argv); } catch (e) { console.error(e.message); return 2; }
  if (o.cmd === 'version') { console.log(`jev-kit ${VERSION} (${MODEL})`); return 0; }
  let event = null;
  try { if (process.env.GITHUB_EVENT_PATH) event = JSON.parse(fs.readFileSync(process.env.GITHUB_EVENT_PATH, 'utf8')); } catch { /* fora do Actions */ }
  if (!o.base && event?.pull_request) { o.base = event.pull_request.base.sha; o.head = event.pull_request.head.sha; }
  const ctx = makeCtx(o.root, o.base, o.head, event);
  let checks;
  try { checks = await loadChecks(ctx, o); } catch (e) { console.error(`jev-kit: ${e.message}`); return 2; }
  if (o.cmd === 'list') { for (const c of checks) console.log(`${c.name}${c.files ? '  (' + [].concat(c.files).join(', ') + ')' : ''}`); console.log(`${checks.length} checagem(ns)`); return 0; }
  if (o.cmd !== 'run') { console.error(`comando desconhecido: ${o.cmd} (use run, list ou version)`); return 2; }
  const key = apiKey();
  if (!key && !o.dry) console.log('::warning title=Jev::sem TYPESAFE_API_KEY: checagens fora do cache serão puladas. No tank4 a chave fica no ~/desktop-agent/.env e o tank4-jev a entrega; local, use jev-kit probe.');
  if (!checks.length) { console.log('Jev: nenhuma checagem se aplica a esta mudança.'); report([], ctx, o); return 0; }
  const results = await runChecks(ctx, checks, { dry: o.dry, key });
  const s = report(results, ctx, o);
  if (!key && !o.dry && process.env.JEV_REQUIRED === '1') return 1;
  if (s.reprovou) return 1;
  if (s.erro && process.env.JEV_STRICT === '1') return 1;
  return 0;
}

if (import.meta.url === pathToFileURL(process.argv[1] || '').href) main().then((c) => process.exit(c));
