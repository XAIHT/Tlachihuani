# Future Tlamatini adapter contract

This folder is for cross-project contract fixtures and integration notes. Actual future Django views, template, JavaScript and CSS belong in `C:\Development\XAIHT\Tlamatini` only after Angela authorizes changes there. Do not create a shadow copy of Tlamatini here.

Upstream: [github.com/XAIHT/Tlamatini](https://github.com/XAIHT/Tlamatini). See [the integration map](../../docs/tlamatini-integration.md).

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
[current integration guide](../../docs/xaiht-link.md) and [dated evidence](../../docs/verification/2026-11-23-hosted-prompts.md) before making
installation or release-acceptance claims. This pass changes documentation only;
existing version identifiers and installed services remain unchanged.
