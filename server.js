const express = require('express');
const path = require('path');

const app = express();
const PORT = 5000;
const HOST = '0.0.0.0';

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

// In-memory depot keys cache
let depotKeysCache = null;
let depotKeysCacheTime = 0;
const CACHE_TTL_MS = 7 * 24 * 60 * 60 * 1000;

// In-memory game list cache
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
  if (depotKeysCache && now - depotKeysCacheTime < CACHE_TTL_MS) {
    return depotKeysCache;
  }
  for (const url of DEPOT_KEYS_URLS) {
    try {
      const res = await fetch(url, { headers: { 'User-Agent': 'RedSea/3.0.0' } });
      if (res.ok) {
        depotKeysCache = await res.json();
        depotKeysCacheTime = now;
        return depotKeysCache;
      }
    } catch { continue; }
  }
  return depotKeysCache || {};
}

async function fetchGameList() {
  const now = Date.now();
  if (gameListCache && now - gameListCacheTime < GAME_CACHE_TTL_MS) {
    return gameListCache;
  }
  try {
    const res = await fetch(GAME_LIST_URL, { headers: { 'User-Agent': 'RedSea/3.0.0' } });
    if (res.ok) {
      gameListCache = await res.json();
      gameListCacheTime = now;
      return gameListCache;
    }
  } catch {}
  return gameListCache || [];
}

// GET /api/games?q=searchterm
app.get('/api/games', async (req, res) => {
  try {
    const q = (req.query.q || '').trim();
    const games = await fetchGameList();
    if (!q) {
      return res.json({ games: games.slice(0, 40) });
    }
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

// GET /api/generate/:appId — generate real Lua script
app.get('/api/generate/:appId', async (req, res) => {
  const appId = parseInt(req.params.appId);
  if (!appId || isNaN(appId)) return res.status(400).json({ error: 'Invalid appId' });

  try {
    const [depotsRes, depotKeys] = await Promise.all([
      fetch(`${STEAMPROOF_API}/apps/depots?ids=${appId}`, {
        headers: { 'User-Agent': 'RedSea/3.0.0' },
      }),
      fetchDepotKeys(),
    ]);

    if (!depotsRes.ok) {
      return res.status(502).json({ error: `SteamProof API returned ${depotsRes.status}` });
    }

    const data = await depotsRes.json();
    const apps = data.apps || [];
    if (apps.length === 0) {
      return res.status(404).json({ error: 'No depot data found for this AppID. The game may not be supported.' });
    }

    const app0 = apps[0];
    const depots = app0.depots || [];

    const lines = [`-- RedSea Lua Generator v3.0.0`, `addappid(${appId})`];
    const manifestLines = [];

    for (const depot of depots) {
      const depotId = depot.depotId;
      if (!depotId) continue;
      const key = depotKeys[String(depotId)];
      if (key) {
        lines.push(`addappid(${depotId},1,"${key}")`);
      } else {
        lines.push(`addappid(${depotId},0,"")`);
      }

      const manifests = depot.manifests || {};
      let manifestId = null;

      if (manifests.public && manifests.public.manifestId) {
        manifestId = manifests.public.manifestId;
      } else {
        for (const branch of Object.values(manifests)) {
          if (branch && branch.manifestId) { manifestId = branch.manifestId; break; }
        }
      }

      if (manifestId) {
        const maxSize = depot.maxSize;
        if (maxSize) {
          manifestLines.push(`setManifestid(${depotId},"${manifestId}",${maxSize})`);
        } else {
          manifestLines.push(`setManifestid(${depotId},"${manifestId}")`);
        }
      }
    }

    let lua = lines.join('\n');
    if (manifestLines.length > 0) {
      const ts = new Date().toISOString().slice(0, 16).replace('T', ' ') + ' UTC';
      lua += `\n\n-- SteamProof Manifests (updated ${ts})\n` + manifestLines.join('\n');
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
    const r = await fetch('https://raw.githubusercontent.com/Abrahamqb/OpenSteamMore-Dev/refs/heads/main/News', {
      headers: { 'User-Agent': 'RedSea/3.0.0' },
    });
    if (r.ok) {
      const text = await r.text();
      return res.json({ news: text.trim() });
    }
    res.json({ news: null });
  } catch {
    res.json({ news: null });
  }
});

app.listen(PORT, HOST, () => {
  console.log(`RedSea running at http://${HOST}:${PORT}`);
});
