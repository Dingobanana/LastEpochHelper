# Relay for bug reports and country pings

"Report a bug" and the country question send to Discord channels. A Discord webhook address is a key:
whoever has it can post anything to the channel, rename the webhook or delete it. An address built into a
public program can be dug out of the exe, so the program gets this relay's address instead, and only the relay
knows the webhooks.

The relay is a Cloudflare Worker (free plan). It passes on only what the overlay itself sends:

| Path | Takes | Limits |
| --- | --- | --- |
| `POST /ping` | `{"content": "DK 0.7.20"}` - two letters and a version, nothing else | 3 a minute per address, 40 a minute in all |
| `POST /report` | `payload_json` starting `**Bug report** - version x.y.z` + one `.zip` up to 9.5 MB | 2 a minute and 6 a day per address, 200 a day in all |

Everything else is refused, requests must carry the overlay's `User-Agent` (`LastEpochHelper/x.y.z`), and
mentions are always switched off, so nothing that gets through can ping anyone. Someone determined can still
send fake pings or junk zips inside those limits, but can no longer flood the channels or delete the webhooks.

## Setting it up (once)

```
cd relay
npx wrangler login                          # opens the browser; a free Cloudflare account is enough
npx wrangler kv namespace create COUNTS     # put the id it prints into wrangler.toml
npx wrangler secret put REPORT_WEBHOOK      # paste a NEW webhook for the bug report channel
npx wrangler secret put PING_WEBHOOK        # paste a NEW webhook for the country channel
npx wrangler deploy                         # prints https://leh-relay.<name>.workers.dev
```

Then, in the repository root (both files are ignored by git):

- `report-endpoint.local.txt`: `https://leh-relay.<name>.workers.dev/report`
- `usage-endpoint.local.txt`: `https://leh-relay.<name>.workers.dev/ping`

Release as usual; `tools/release.ps1` warns if either file still holds a Discord webhook.

The relay is live since 0.7.20. The webhooks that versions 0.7.4 to 0.7.19 had built in were deleted on
2026-10-06; those copies save bug reports to the desktop instead, and their update is not affected. If a
webhook ever leaks, replace the secret with `npx wrangler secret put` - no release is needed.

## Watching and testing

- `npx wrangler tail` shows what is refused and why, live.
- Locally: put `REPORT_WEBHOOK=...` and `PING_WEBHOOK=...` (something harmless) in `relay/.dev.vars`, run
  `npx wrangler dev`, and run the overlay's test with `LEH_RELAY_URL=http://127.0.0.1:8787`
  (`tests/LastEpochHelper.Tests/RelayTests.cs`), which sends a real report and ping the way the overlay does.
