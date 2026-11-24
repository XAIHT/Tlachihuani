# XT tray companion

Right-click XT and choose **Start Services** to bring both Windows services and the Torrenter REST API online. XT turns from gray to color when the API is ready. **Stop Services** shuts down the Jobs host, its provider processes and the primary service. The gray tray icon stays available to start them again. **Show panel** and double-click open the individual controls. The last entry closes only the tray. It reads **Exit tray** once both services are confirmed stopped or absent, and **Exit tray (service keeps running)** otherwise.

[TrayMenu.cs](TrayMenu.cs) owns the menu and the opt-in switches for the six former rows. See [the tray contract](../../docs/panel-and-tray.md#tray) for reactivation and [packaging](../../packaging/windows/README.md) for the optional loopback link format. Force-stop and consent IPC are not implemented.

**Services → See log** shows Tlachihuani.log ([TlachihuaniLog.cs](TlachihuaniLog.cs), [LogViewerForm.cs](LogViewerForm.cs)). The **xaiht.org** tab connects XT to the Tlamatini-RC page on xaiht.org ([XaihtLink.cs](XaihtLink.cs)); see [the xaiht.org link](../../docs/xaiht-link.md).

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
