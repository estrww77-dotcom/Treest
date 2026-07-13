const express = require('express');
const path = require('path');
const fs = require('fs');
const crypto = require('crypto');

const app = express();
const PORT = 5000;
const HOST = '0.0.0.0';

app.use(express.json());

const SECURITY_HEADERS = {
  'X-Content-Type-Options': 'nosniff',
  'X-Frame-Options': 'SAMEORIGIN',
  'X-XSS-Protection': '1; mode=block',
  'Referrer-Policy': 'strict-origin-when-cross-origin',
  'Permissions-Policy': 'camera=(), microphone=(), geolocation=()',
};

app.use((req, res, next) => {
  Object.entries(SECURITY_HEADERS).forEach(([k, v]) => res.setHeader(k, v));
  next();
});

app.use(express.static(path.join(__dirname, 'public')));

// ── Key system ──────────────────────────────────────────────────────────────
const KEYS_FILE = path.join(__dirname, 'keys.json');

function loadKeys() {
  try {
    if (fs.existsSync(KEYS_FILE)) return JSON.parse(fs.readFileSync(KEYS_FILE, 'utf8'));
  } catch {}
  return {};
}

function saveKeys(keys) {
  fs.writeFileSync(KEYS_FILE, JSON.stringify(keys, null, 2));
}

function generateKey() {
  const chars = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';
  const rand = (n) => Array.from({ length: n }, () => chars[crypto.randomInt(chars.length)]).join('');
  return `RS-${rand(4)}-${rand(4)}-${rand(4)}`;
}

function isValidKey(key) {
  if (!key) return false;
  const keys = loadKeys();
  const entry = keys[key];
  return !!(entry && entry.active);
}

function requireKey(req, res, next) {
  const key = req.query.key || req.headers['x-key'];
  if (!isValidKey(key)) {
    return res.status(401).json({ error: 'Invalid or missing access key.' });
  }
  next();
}

function requireAdmin(req, res, next) {
  const secret = process.env.BOT_SECRET;
  if (!secret) return res.status(503).json({ error: 'Admin not configured.' });
  const auth = req.headers['authorization'];
  if (auth !== `Bearer ${secret}`) return res.status(403).json({ error: 'Forbidden.' });
  next();
}

// POST /api/validate — check if a key is valid (no consumption)
app.post('/api/validate', (req, res) => {
  const key = req.body?.key || req.query.key;
  res.json({ valid: isValidKey(key) });
});

// POST /api/admin/genkey — generate a new key
app.post('/api/admin/genkey', requireAdmin, (req, res) => {
  const note = req.body?.note || null;
  const keys = loadKeys();
  let key;
  let attempts = 0;
  do { key = generateKey(); attempts++; } while (keys[key] && attempts < 100);
  keys[key] = { active: true, createdAt: new Date().toISOString(), note };
  saveKeys(keys);
  res.json({ key, note });
});

// POST /api/admin/revokekey — revoke a key
app.post('/api/admin/revokekey', requireAdmin, (req, res) => {
  const { key } = req.body || {};
  if (!key) return res.status(400).json({ error: 'No key provided.' });
  const keys = loadKeys();
  if (!keys[key]) return res.status(404).json({ error: 'Key not found.' });
  keys[key].active = false;
  saveKeys(keys);
  res.json({ success: true });
});

// GET /api/admin/keys — list all keys
app.get('/api/admin/keys', requireAdmin, (req, res) => {
  res.json({ keys: loadKeys() });
});

// ── Game data caches ─────────────────────────────────────────────────────────
let depotKeysCache = null;
let depotKeysCacheTime = 0;
const CACHE_TTL_MS = 7 * 24 * 60 * 60 * 1000;

let gameListCache = null;
let gameListCacheTime = 0;
const GAME_CACHE_TTL_MS = 24 * 60 * 60 * 1000;

const DEPOT_KEYS_URLS = [
  'https://gitlab.com/steamautocracks/manifesthub/-/raw/main/depotkeys.json',
  'https://api.993499094.xyz/depotkeys.json',
];
const GAME_LIST_URL = 'https://raw.githubusercontent.com/SteamTools-Team/GameList/refs/heads/main/games.json';
const STEAMPROOF_API = 'https://api.steamproof.net';

async function fetchDepotKeys() {
  const now = Date.now();
  if (depotKeysCache && now - depotKeysCacheTime < CACHE_TTL_MS) return depotKeysCache;
  for (const url of DEPOT_KEYS_URLS) {
    try {
      const res = await fetch(url, { headers: { 'User-Agent': 'RedSea/3.0.0' } });
      if (res.ok) { depotKeysCache = await res.json(); depotKeysCacheTime = now; return depotKeysCache; }
    } catch { continue; }
  }
  return depotKeysCache || {};
}

async function fetchGameList() {
  const now = Date.now();
  if (gameListCache && now - gameListCacheTime < GAME_CACHE_TTL_MS) return gameListCache;
  try {
    const res = await fetch(GAME_LIST_URL, { headers: { 'User-Agent': 'RedSea/3.0.0' } });
    if (res.ok) { gameListCache = await res.json(); gameListCacheTime = now; return gameListCache; }
  } catch {}
  return gameListCache || [];
}

// GET /api/games
app.get('/api/games', async (req, res) => {
  try {
    const q = (req.query.q || '').trim();
    const games = await fetchGameList();
    if (!q) return res.json({ games: games.slice(0, 40) });
    const isNumeric = /^\d+$/.test(q);
    let results;
    if (isNumeric) {
      results = games.filter(g => g.appid === q);
    } else {
      const lower = q.toLowerCase();
      results = games.filter(g => g.name && g.name.toLowerCase().includes(lower)).slice(0, 40);
    }
    res.json({ games: results });
  } catch (err) {
    res.status(500).json({ error: err.message });
  }
});

// GET /api/generate/:appId — requires valid key
app.get('/api/generate/:appId', requireKey, async (req, res) => {
  const appId = parseInt(req.params.appId);
  if (!appId || isNaN(appId)) return res.status(400).json({ error: 'Invalid appId' });

  try {
    const [depotsRes, depotKeys] = await Promise.all([
      fetch(`${STEAMPROOF_API}/apps/depots?ids=${appId}`, { headers: { 'User-Agent': 'RedSea/3.0.0' } }),
      fetchDepotKeys(),
    ]);

    if (!depotsRes.ok) return res.status(502).json({ error: `API returned ${depotsRes.status}` });

    const data = await depotsRes.json();
    const apps = data.apps || [];
    if (apps.length === 0) return res.status(404).json({ error: 'No depot data found for this AppID.' });

    const app0 = apps[0];
    const depots = app0.depots || [];
    const lines = [`-- RedSea v3.0.0`, `addappid(${appId})`];
    const manifestLines = [];

    for (const depot of depots) {
      const depotId = depot.depotId;
      if (!depotId) continue;
      const key = depotKeys[String(depotId)];
      lines.push(key ? `addappid(${depotId},1,"${key}")` : `addappid(${depotId},0,"")`);
      const manifests = depot.manifests || {};
      let manifestId = null;
      if (manifests.public?.manifestId) manifestId = manifests.public.manifestId;
      else for (const b of Object.values(manifests)) { if (b?.manifestId) { manifestId = b.manifestId; break; } }
      if (manifestId) {
        const maxSize = depot.maxSize;
        manifestLines.push(maxSize ? `setManifestid(${depotId},"${manifestId}",${maxSize})` : `setManifestid(${depotId},"${manifestId}")`);
      }
    }

    let lua = lines.join('\n');
    if (manifestLines.length > 0) {
      const ts = new Date().toISOString().slice(0, 16).replace('T', ' ') + ' UTC';
      lua += `\n\n-- Manifests (updated ${ts})\n` + manifestLines.join('\n');
    }
    lua += '\n';
    res.json({ lua, appId, depotCount: depots.length });
  } catch (err) {
    res.status(500).json({ error: err.message });
  }
});

// GET /api/news
app.get('/api/news', async (req, res) => {
  try {
    const r = await fetch('https://raw.githubusercontent.com/estrww77-dotcom/Treest/refs/heads/master/News', {
      headers: { 'User-Agent': 'RedSea/3.0.0' },
    });
    if (r.ok) return res.json({ news: (await r.text()).trim() });
    res.json({ news: null });
  } catch {
    res.json({ news: null });
  }
});

app.listen(PORT, HOST, () => {
  console.log(`RedSea running at http://${HOST}:${PORT}`);
});
