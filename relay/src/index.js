// Relay between the overlay and the Discord channels that receive bug reports and country pings.
//
// A Discord webhook address is a key: whoever has it can post anything to the channel, and also
// rename or delete the webhook. An address built into a public program can be dug out of it, so
// the program is given this relay's address instead, and only the relay knows the webhooks
// (stored as secrets with `wrangler secret put`). The relay passes on only what the overlay
// itself sends, in the overlay's own format, and only so often:
//
//   POST /ping    {"content": "DK 0.7.20"}                       - a country code and a version
//   POST /report  multipart: payload_json + files[0] (a .zip)    - a bug report from "Report a bug"
//
// Anything else is refused. Mentions are always switched off, so nothing posted can ping anyone.

const PING = /^([A-Z]{2}|\?\?) \d{1,3}\.\d{1,3}\.\d{1,3}$/;
const REPORT_HEADING = /^\*\*Bug report\*\* - version \d{1,3}\.\d{1,3}\.\d{1,3}(\n|$)/;
const REPORT_FILE = /^[\w\-. ]{1,100}\.zip$/;
const MAX_ZIP = 9_500_000; // what the overlay itself sends at most; Discord takes 10 MB
const MAX_TEXT = 1_800;    // the overlay cuts the description at 1500; Discord takes 2000

// Reports per address per day, and for everyone together per day (counted in KV, so roughly).
const REPORTS_PER_IP_PER_DAY = 6;
const REPORTS_PER_DAY = 200;

const NO_MENTIONS = { parse: [] };

export default {
  async fetch(request, env) {
    if (request.method !== "POST") return answer(404, "not here");
    if (!(request.headers.get("User-Agent") ?? "").startsWith("LastEpochHelper/")) return answer(403, "not from the overlay");
    const path = new URL(request.url).pathname;
    const ip = request.headers.get("CF-Connecting-IP") ?? "unknown";
    try {
      if (path === "/ping") return await ping(request, env, ip);
      if (path === "/report") return await report(request, env, ip);
      return answer(404, "not here");
    } catch (e) {
      console.log("error", String(e));
      return answer(400, "could not read the request");
    }
  },
};

async function ping(request, env, ip) {
  if (!(await allowed(env.PING_PER_IP, ip)) || !(await allowed(env.PING_ALL, "all"))) return answer(429, "too many");
  if (Number(request.headers.get("Content-Length") ?? 0) > 1_000) return answer(413, "too large");
  const body = await request.json();
  const content = typeof body?.content === "string" ? body.content.trim() : "";
  if (!PING.test(content)) return answer(400, "not a country and a version");
  return forward(env.PING_WEBHOOK, JSON.stringify({ content, allowed_mentions: NO_MENTIONS }), { "Content-Type": "application/json" });
}

async function report(request, env, ip) {
  if (!(await allowed(env.REPORT_PER_IP, ip))) return answer(429, "too many");
  if (Number(request.headers.get("Content-Length") ?? Infinity) > MAX_ZIP + 10_000) return answer(413, "too large");

  const form = await request.formData();
  const payload = JSON.parse(String(form.get("payload_json") ?? "{}"));
  const content = typeof payload?.content === "string" ? payload.content : "";
  const file = form.get("files[0]");
  if (!REPORT_HEADING.test(content) || content.length > MAX_TEXT) return answer(400, "not a bug report");
  if (!(file instanceof File) || !REPORT_FILE.test(file.name) || file.size > MAX_ZIP || file.size < 22) return answer(400, "not a report file");
  const head = new Uint8Array(await file.slice(0, 4).arrayBuffer());
  if (head[0] !== 0x50 || head[1] !== 0x4b || head[2] !== 0x03 || head[3] !== 0x04) return answer(400, "not a zip file");

  // Daily caps last: they cost a KV write, so only a report that would be passed on counts.
  const day = new Date().toISOString().slice(0, 10);
  if (!(await underDailyCap(env, `report:${day}:${ip}`, REPORTS_PER_IP_PER_DAY))
      || !(await underDailyCap(env, `report:${day}:all`, REPORTS_PER_DAY))) return answer(429, "too many today");

  const out = new FormData();
  out.append("payload_json", JSON.stringify({ content, allowed_mentions: NO_MENTIONS }));
  out.append("files[0]", file, file.name);
  return forward(env.REPORT_WEBHOOK, out);
}

/** Cloudflare's rate limiter (a few per minute); missing in a setup without it, then everything passes. */
async function allowed(limiter, key) {
  if (!limiter) return true;
  const { success } = await limiter.limit({ key });
  return success;
}

async function underDailyCap(env, key, cap) {
  if (!env.COUNTS) return true;
  const count = Number((await env.COUNTS.get(key)) ?? 0);
  if (count >= cap) return false;
  await env.COUNTS.put(key, String(count + 1), { expirationTtl: 2 * 24 * 3600 });
  return true;
}

async function forward(webhook, body, headers = {}) {
  if (!webhook) return answer(503, "no channel set up");
  const response = await fetch(webhook, { method: "POST", body, headers });
  // Discord's own answer would include the message and the channel; the overlay only needs yes or no.
  return response.ok ? answer(204) : answer(response.status === 429 ? 429 : 502, "the channel did not take it");
}

function answer(status, text) {
  if (status >= 400) console.log("refused", status, text); // seen with `npx wrangler tail`
  return new Response(status === 204 ? null : text, { status });
}
