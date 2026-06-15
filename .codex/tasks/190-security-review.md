# Task: Repo-wide review 02 — Security, auth, secrets, and privacy

**GitHub issue:** #190
**Type:** Review only — do not make code changes, do not open a PR
**Output:** Post report as a comment on issue #190 if possible; otherwise write to `.codex/reports/190-security.md`

## Instructions

Perform a focused repository-wide security and privacy review. Do not print actual secret values — identify only the file/path, setting name, and secret type.

## Focus areas

- Authentication and authorization on all API endpoints (`[Authorize]`, policy requirements, `X-Api-Key` validation)
- Insecure direct object references
- Input validation at trust boundaries (API controllers, ingest endpoints)
- SQL injection — raw SQL, string-concatenated queries
- XSS, CSRF, SSRF where relevant
- Unsafe file handling, path traversal, open redirects
- Secret exposure: hardcoded credentials, tokens, API keys, connection strings in source code, `appsettings.json`, or docker-compose files
- PII or sensitive data in log output
- Weak cryptography or custom crypto
- CORS configuration
- Dependency vulnerability indicators
- Missing audit trails for sensitive actions

## Key security context for this repo

- `HVO.WebSite.v9` is Azure-hosted — must have auth/authz on all endpoints
- Gateway apps are on local Pi network — auth is simpler but `X-Api-Key` headers must be validated on ingest endpoints
- `.env` files must be gitignored — verify with `git check-ignore -v deploy/pi-gateways/*/.env`
- API keys and secrets must live in `.env` only, not in `appsettings.json` or source

## Output format

1. Executive summary
2. Top security risks
3. Findings grouped by severity (P0, P1, P2, P3)
4. Quick wins
5. Items needing human/security review
6. Suggested remediation sequence

## Key files to check

- `src/HVO.WebSite.v9/` — all controllers, endpoints, auth policies
- `src/HVO.Hardware.*/` and `src/HVO.Gateway.*/` — ingest endpoints, `X-Api-Key` handling
- `deploy/pi-gateways/*/.env.example` — should not contain real secrets
- `appsettings*.json` — should not contain secrets
- `docker-compose.yml` files — secret handling
- `.gitignore` — `.env` exclusions
- `src/HVO.DataModels/` — EF Core queries, raw SQL if any
