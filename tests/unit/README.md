# Unit checks

Run `node --test tests/unit/workspace_table.test.cjs` from a verified visible
foreground console. The 30 checks cover numeric/natural ordering, unknown values,
multi-sort, filtering, pagination, completion labels and recovery-action eligibility.
See the [0.3.3 pre-release evidence](../../docs/verification/2026-11-10-torrenter-recovery.md).

Contract bounds, lifecycle transitions, timeouts, idempotency and permission
decisions are covered by the separate `Tlachihuani.Checks` executable. Running it
through `scripts/Build.ps1 -Mode Check` builds code and requires build authorization.
