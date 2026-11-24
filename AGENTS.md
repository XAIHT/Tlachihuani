# Tlachihuani instructions for coding agents

Read [CLAUDE.md](CLAUDE.md) and [NEXT_SESSION.md](NEXT_SESSION.md) before implementation.

Latest version: **[0.38.0](docs/releases/v0.38.0.md)**, as requested by Angela on
21 November 2026. Every current documentation pointer names 0.38.0. Historical
versions and dated verification results remain historical. This documentation
bump does not authorize builds, tags, commits or publication.

- The authoritative source checkout is `C:\Development\XAIHT\repo\Tlachihuani` (Angela's correction, 11 November 2026). Use this root for all new work; do not use the former checkout as the active codebase. The separate reference checkout is `C:\Development\XAIHT\Tlamatini`, upstream [XAIHT/Tlamatini](https://github.com/XAIHT/Tlamatini).
- A .NET 10 lifecycle foundation is implemented. Read [implementation status](docs/implementation-status.md) for evidence and remaining gates. Console-host verification does not certify an installed SCM service.
- Tlamatini controls this Windows service through **Panels → Tlachihuani**. The approved Remote-Controller adapter and website prompt route are implemented; future cross-repository code changes still require task authorization. See the current integration and dated verification documents.
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

## Hosted prompt integration · 23 November review

The website path now carries real prompts and answers to an explicitly approved
running Tlamatini account through XT's outgoing HTTPS connection and the local
Remote-Controller adapter. It does not open an inbound port or add website launch,
shutdown or phone-audio controls. Diagnostics remain inert and separate from typed
commands. Retries retain identity, receipts survive reload, and full answers use
validated account-bound chunks. Ask Execs follows the existing selective policy;
Netstat is exempt, whereas the tested Executer action displayed approval.

Physical-phone English prompt/answer, reload and approval display were observed.
Real denial/no-execution and cancellation were not completed before tests stopped.
The wording-only website candidate was built but not promoted. Read the
[current integration guide](docs/xaiht-link.md) and [dated evidence](docs/verification/2026-11-23-hosted-prompts.md) before making
installation or release-acceptance claims. This pass changes documentation only;
existing version identifiers and installed services remain unchanged.
