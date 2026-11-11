# Verification

The **[0.3.3 pre-release](../docs/releases/v0.3.3.md)** documentation incorporates
the [10 November source recovery results](../docs/verification/2026-11-10-torrenter-recovery.md):
64 automated checks and 48 visible browser checks. Those dated results are not
certification of a newly built installer.

`Tlachihuani.Checks` is a dependency-free executable suite invoked by the visible `scripts/Build.ps1 -Mode Check`. It covers framing attacks, protocol bounds, event retention, lifecycle idempotence/order/timeouts, startup races, native local IPC, server-image rejection, 32 concurrent real-host clients, heartbeat, correlated version refusal and graceful console shutdown.

The unit, desktop and integration directories contain the table, real-peer,
metadata queue, delivery, release packaging and setup tests. Start with the
[integration guide](integration/README.md) and [headed workspace guide](desktop/torrenter-table.md).
The [foundation results](../docs/verification/2026-10-11-foundation.md) remain
historical evidence. SCM registration, cross-account security and shell tray
behavior require separate installed acceptance.
