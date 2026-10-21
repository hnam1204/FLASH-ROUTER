# Codex CLI quota availability

FLASH ROUTER does not automatically fetch Codex quota. The previously explored `https://chatgpt.com/backend-api/wham/usage` route is undocumented and is not treated as a supported public API.

The refresh action fails closed with `CODEX_CLI_SOURCE_UNSUPPORTED`. It does not read the current user's `.codex/auth.json`, access tokens, browser cookies, or invoke the undocumented route. It also does not attach the machine's active Codex session to whichever account is selected in FLASH ROUTER. Existing Codex provider observations remain owned by their existing account connection and are rendered only when their account binding is present.

To view live Codex usage, open an active Codex CLI session and enter `/status`. FLASH ROUTER does not import that output automatically. A future import needs an explicitly supported, structured source and a strong account ID binding; email matching alone is insufficient.
