const { Client, GatewayIntentBits } = require('discord.js');

const BOT_TOKEN = process.env.BOT_TOKEN;
const BOT_SECRET = process.env.BOT_SECRET;
const SERVER_URL = (process.env.SERVER_URL || 'http://localhost:5000').replace(/\/$/, '');
const OWNER_ID = process.env.BOT_OWNER_ID;

if (!BOT_TOKEN) {
  console.log('[Bot] BOT_TOKEN not set — bot will not start.');
  process.exit(0);
}

const client = new Client({
  intents: [
    GatewayIntentBits.Guilds,
    GatewayIntentBits.GuildMessages,
    GatewayIntentBits.MessageContent,
  ],
});

const adminHeaders = {
  Authorization: `Bearer ${BOT_SECRET}`,
  'Content-Type': 'application/json',
};

async function api(method, endpoint, body) {
  const res = await fetch(`${SERVER_URL}${endpoint}`, {
    method,
    headers: adminHeaders,
    body: body ? JSON.stringify(body) : undefined,
  });
  return res.json();
}

client.once('ready', () => {
  console.log(`[Bot] Logged in as ${client.user.tag}`);
});

client.on('messageCreate', async (message) => {
  if (message.author.bot) return;
  if (OWNER_ID && message.author.id !== OWNER_ID) return;

  const content = message.content.trim();
  if (!content.startsWith('!')) return;

  const [cmd, ...args] = content.split(/\s+/);

  try {
    switch (cmd.toLowerCase()) {

      case '!genkey': {
        const note = args.join(' ') || null;
        const data = await api('POST', '/api/admin/genkey', { note });
        if (data.error) { await message.reply(`❌ ${data.error}`); return; }
        await message.reply(
          `🔑 **New access key generated:**\n\`\`\`\n${data.key}\n\`\`\`` +
          (note ? `\n📝 Note: ${note}` : '')
        );
        break;
      }

      case '!revoke': {
        const key = args[0];
        if (!key) { await message.reply('Usage: `!revoke <key>`'); return; }
        const data = await api('POST', '/api/admin/revokekey', { key });
        if (data.error) { await message.reply(`❌ ${data.error}`); return; }
        await message.reply(`✅ Key \`${key}\` has been revoked.`);
        break;
      }

      case '!keys': {
        const data = await api('GET', '/api/admin/keys');
        if (data.error) { await message.reply(`❌ ${data.error}`); return; }
        const entries = Object.entries(data.keys || {});
        if (!entries.length) { await message.reply('No keys found.'); return; }
        const lines = entries.map(([k, v]) =>
          `${v.active ? '🟢' : '🔴'} \`${k}\`${v.note ? ` — ${v.note}` : ''}`
        );
        const chunks = [];
        let chunk = `**Keys (${entries.length}):**\n`;
        for (const line of lines) {
          if (chunk.length + line.length > 1900) { chunks.push(chunk); chunk = ''; }
          chunk += line + '\n';
        }
        if (chunk) chunks.push(chunk);
        for (const c of chunks) await message.reply(c);
        break;
      }

      case '!check': {
        const key = args[0];
        if (!key) { await message.reply('Usage: `!check <key>`'); return; }
        const data = await fetch(`${SERVER_URL}/api/validate`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ key }),
        }).then(r => r.json());
        await message.reply(
          data.valid
            ? `✅ Key \`${key}\` is **valid**.`
            : `❌ Key \`${key}\` is **invalid or revoked**.`
        );
        break;
      }

      case '!help': {
        await message.reply([
          '**RedSea Bot — Commands:**',
          '`!genkey [note]` — Generate a new access key',
          '`!revoke <key>` — Revoke an access key',
          '`!keys` — List all keys and their status',
          '`!check <key>` — Check if a key is valid',
        ].join('\n'));
        break;
      }

    }
  } catch (err) {
    await message.reply(`❌ Error: ${err.message}`);
  }
});

client.login(BOT_TOKEN);
