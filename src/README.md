# Source components

`Tlachihuani.slnx` builds seven production projects plus the executable verification suite. Service, Control and Tray are independent executable entry points. Contracts/Core do not reference Windows GUI code; Client and Windows share the fixed-service control implementation. No project imports Tlamatini's Python, database, agents or assemblies.

`Tlachihuani.Elevated` remains a design placeholder. See [implementation status](../docs/implementation-status.md) before treating any planned feature as implemented.

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
[current integration guide](../docs/xaiht-link.md) and [dated evidence](../docs/verification/2026-11-23-hosted-prompts.md) before making
installation or release-acceptance claims. This pass changes documentation only;
existing version identifiers and installed services remain unchanged.
