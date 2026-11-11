# Tlachihuani instructions for coding agents

Read [CLAUDE.md](CLAUDE.md) and [NEXT_SESSION.md](NEXT_SESSION.md) before implementation.

Current documentation: **[0.3.3 pre-release](docs/releases/v0.3.3.md)**, explicitly
requested by Angela on 11 November 2026. Preserve this pre-release designation.
Historical versions and dated verification results remain historical. This
documentation bump does not authorize builds, tags, commits or publication.

- The authoritative source checkout is `C:\Development\XAIHT\repo\Tlachihuani` (Angela's correction, 11 November 2026). Use this root for all new work; do not use the former checkout as the active codebase. The separate reference checkout is `C:\Development\XAIHT\Tlamatini`, upstream [XAIHT/Tlamatini](https://github.com/XAIHT/Tlamatini).
- A .NET 10 lifecycle foundation is implemented. Read [implementation status](docs/implementation-status.md) for evidence and remaining gates. Console-host verification does not certify an installed SCM service.
- Tlamatini will control this Windows service through **Panels → Tlachihuani**. Changes to that checkout remain deferred until Angela explicitly authorizes them. Reading it is allowed.
- GitHub repository: [angelahack1/Tlachihuani](https://github.com/angelahack1/Tlachihuani) (`https://github.com/angelahack1/Tlachihuani.git`). Do not create additional remotes or push without Angela's explicit instruction.
- Use the exact name **Tlachihuani**. Do not propagate the occasional spelling `Tlachihani`.
- Development commands, diagnostics, scripts, builds, tests, and browsers must run in a verified visible foreground window on Angela's real desktop. Read [the local execution policy](docs/development-execution.md). Headless execution is forbidden. Leave consoles open afterward and monitor long runs every 5–15 seconds.
- Product service/tray/helper processes must not acquire extra consoles or steal focus merely to satisfy development visibility. Windows SCM owns the noninteractive service. Development verification of it remains visible.
- Preserve unrelated files and author attribution. Never rewrite Git history. Do not modify Tlamatini database backup/restore mechanics or run its destructive `build.py` for this project.
- Do not register a workflow agent, tool, skill, MCP, database row, or prompt example merely to expose a service-control panel.
- The service accepts a closed, typed operation set. No arbitrary command, executable path, service name, or PID supplied by a browser may be executed or killed.
- Use owned process handles and verified identities for termination; never kill by image name or a stale PID.
- Do not add dependencies until needed. Pin and review them during implementation, not by copying Tlamatini's large dependency set.
- Angela disabled Snyk on 2026-10-11. Do not invoke Snyk tools, skills, hooks, or scans. Use the pinned package lock files and NuGet audit for dependency checks.
