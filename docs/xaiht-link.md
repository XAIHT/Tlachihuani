# xaiht.org link, remote prompts and Tlachihuani.log

Latest version: **[1.0.1](releases/v1.0.1.md)**. Requested by Angela López Mendoza
on 19 November 2026.

A signed-in user of **xaiht.org** opens **Tlamatini-RC**, selects her computer
and an explicitly approved Tlamatini account, then sends a prompt. XT relays
typed JSON commands to Remote-Controller, which dispatches them to that account's
existing local chat engine. Answers, execution approval requests and cancellation
travel back through the same outgoing HTTPS connection.

Diagnostic messages remain a separate, inert log-only feature. A diagnostic
message marked `type: "command"` is never treated as an executable prompt.
This source change requires an updated website, XT and Remote-Controller binary;
editing source alone does not upgrade already running installations.

## Ask Execs: protected actions, not every tool

The source label is **Ask before protected actions**; the earlier deployed page
may still say **Ask before executing tools**. Both send the same boolean option.
With Multi-Turn enabled, Tlamatini applies its existing selective gate. Monitor
Netstat and direct Netstat do not require approval. Desktop/hardware and messaging
also retain their deliberate exceptions; do not promise a dialog for every action.

An actual installed English-account phone request using Executer displayed its
approval dialog. Angela stopped testing before a denial/no-execution or cancellation
result was established. The Netstat request carried Ask Execs true and registered
the broker; it was an exemption, not a dropped option. The corrected label passed
focused source checks and its cloud candidate built, but it was not deployed.
See [the evidence boundary](verification/2026-11-23-hosted-prompts.md).

XT does not log prompt/answer bodies; the website stores those bodies and local
Tlamatini retains its normal history/logs. HTTPS is not end-to-end/PQ encryption.

## How xaiht.org reaches the computer

The computer opens no port. XT asks xaiht.org for messages over ordinary
outgoing HTTPS (long polling):

1. XT sends `GET https://xaiht.org/api/rc/poll?wait=25` with its computer key.
2. xaiht.org holds the request for up to 25 seconds. When SEND is pressed it
   answers at once with the message; otherwise it answers with none after 25
   seconds, and XT asks again immediately.
3. XT writes each message to Tlachihuani.log, then sends
   `POST https://xaiht.org/api/rc/ack` with the message ids. The page then shows
   **Written to Tlachihuani.log**.

For real prompts, XT advertises `X-Tlachihuani-Command-Protocol: 1` and the poll
response additionally contains `commands`. Older XT versions continue receiving
diagnostic messages only. Each command delivery has a nonce and a 60-second
lease. XT posts ordered snapshots to `/api/rc/result` while Remote-Controller
processes the prompt. The website follows those snapshots until completion.

Because only outgoing HTTPS is used, the same path works on a home or office
LAN, Wi-Fi, a hotel network, a phone's shared connection or a dial-up line, and
behind any router or proxy that allows browsing. No other domain, tunnel or paid
service is involved: the messages pass through the existing xaiht.org website.

**Online** on the page means XT is asking right now, or asked within the last 40
seconds. When XT exits, the computer loses its connection or Windows sleeps, the
page shows **Offline** within about 40 to 65 seconds. XT's first request after it
starts or reconnects answers at once, so XT and the page show Online together.

## Connect a computer

1. Sign in at xaiht.org with Google, open **Tlamatini-RC** in the menu (shown
   only after sign-in), type a name under **Add a computer** and choose
   **Create key**. The key starts with `xrc_` and is shown only once.
2. In XT open **Services… → xaiht.org → Connect…**. Leave the website as
   `https://xaiht.org`, paste the key and choose **Connect**.
3. The xaiht.org tab shows **Online** with the computer's name, and the page
   shows the computer online with its Windows name, XT version and address.

**Disconnect** in the same tab stops asking and deletes the saved key. Use
**Remove** on the page to make a key stop working at once, for example for a
lost computer. A new key needs a new **Connect…**.
Removing the website computer or disconnecting its transport does not undo work
already dispatched to Tlamatini. Use **Cancel** while still connected, or revoke
the local account grant in **Accounts…** to withdraw that grant's authority.

## Allow an account and send a real prompt

1. Start **Remote-Controller** in XT. Start the intended Tlamatini edition and
   sign in to the account you want to use, such as **angela**.
2. In Tlamatini open **Panels → Tlachihuani · Remote-Controller** and enable
   that account using its existing account-consent screen.
3. In **XT → Services → xaiht.org → Accounts…**, check that exact account and
   choose **Save account access**. English and Spanish accounts have separate
   immutable bindings, even when their display names match. Nothing is selected
   automatically. This approval grants chat and monitoring only.
4. On the phone, refresh the account list, choose the computer and account, then
   send the prompt. The application must remain running and connected.
5. Review any execution request before choosing **Proceed** or **Deny**. A normal
   status update does not dismiss a still-pending request. **Cancel** requests
   cancellation through the existing Tlamatini adapter.

Uncheck an account in **Accounts…** to revoke website access. This cancels queued
work and withdraws authority from work already running. Disconnecting the website
link stops transport and forgets its key; reconnecting with a different key requires
fresh account approval. Remote launch, remote shutdown, audio and arbitrary shell
commands are not added to this website command interface.

## Reliable command and answer delivery

- The executable schema is `xaiht.tlamatini-rc.command/1`; it carries a request
  ID, computer identity, website user, expiry, operation, binding and payload.
  The website allows only typed account, status, conversation, permission,
  cancellation, history, operation lookup and full-response operations.
- Each native request uses a durable idempotency key derived from its approved
  connection and request ID. A lost acknowledgement or redelivery finds the
  existing operation. A changed payload with the same ID is rejected. Expired
  requests cannot start new work; a known request can still be reconciled.
- A local pipe interruption keeps the website delivery alive while XT queries
  the same operation again. A service restart can report `outcome_unknown`; it
  does not replay a prompt whose execution may already have begun.
- Native frames remain bounded at 64 KiB and use UTF-8 without expanding accented
  or non-Latin text into unnecessary escapes. Prompt text is limited to 16,000
  characters and 24,000 bytes for its JSON-encoded UTF-8 string.
- Full answers use the existing account-bound response ticket. XT fetches only
  the registered loopback adapter endpoint, checks authorization again, and returns
  chunks of at most 24 KiB with byte offsets, total length and SHA-256. The client
  verifies the complete JSON, bounded to 8 MiB. No arbitrary URL is accepted.
  XT holds the first answer JSON in a bounded memory snapshot so regenerated
  signed file links cannot change the hash halfway through delivery. The cache
  allows four answers / 32 MiB, expires idle snapshots after two minutes (and
  all snapshots after thirty), and is cleared on disconnect. Every chunk still
  validates the actual signed ticket with Tlamatini and rechecks the native grant;
  missing snapshots require restarting from byte zero rather than mixing content.
- Controls have their own bounded capacity: a full prompt queue cannot starve
  Cancel or an execution decision. Account/status discovery also retains a small
  separate lane, so refreshing the phone page does not hide the way to cancel.
  Cancelling a prompt that has not reached Tlamatini durably cancels its queue
  entry immediately, even if the application has since disconnected.
- A delivery nonce and increasing sequence prevent stale or reordered snapshots
  from replacing a newer answer. Prompt bodies, answers and native RC frame
  contents are excluded from the XT diagnostic log, including trace mode.

## Send a message

The form has the computer, **Deliver to** (Tlachihuani, Tlamatini or
Tlamatini-Spanish), **Type** (text, ping or command; a command is only logged),
**Level** (information, warning or error), an optional **Subject**, how long to
keep the message if the computer is offline (5 minutes to 1 week) and the
**message** itself (up to 4,000 characters). The page shows the JSON before SEND,
built by the same code as the real message:

```json
{
  "schema": "xaiht.tlamatini-rc.message/1",
  "id": 1,
  "sent_at": "2026-11-19T21:47:27.090Z",
  "expires_at": "2026-11-19T22:47:27.090Z",
  "from": { "site": "xaiht.org", "user_id": 1, "name": "Angela López Mendoza" },
  "computer": { "id": 1, "name": "Angela laptop" },
  "target": "tlachihuani",
  "type": "text",
  "level": "warning",
  "subject": "First message",
  "message": "Hola Tlachihuani"
}
```

**Recent messages** follows each one: **Waiting for the computer**, **Delivered,
being written to the log**, **Written to Tlachihuani.log at …** or **Expired
before the computer was online**. A message sent while the computer is offline
waits and arrives when XT connects again. A message handed to XT without a
receipt is offered again after 60 seconds; XT recognizes a repeat and does not
log it twice.

## Tlachihuani.log and See log

XT writes `%LOCALAPPDATA%\XAIHT\Tlachihuani\Tlachihuani.log` for the signed-in
Windows user, in UTF-8. Each entry is one line with the local time, level and
source, followed by an optional block indented by four spaces:

```text
2026-11-19 15:47:27.099 -06:00  INFO   xaiht.org  Message #1 received from xaiht.org
    {
      "schema": "xaiht.tlamatini-rc.message/1",
      ...
    }
```

The block is the message exactly as sent, including accents and emoji, and stays
valid JSON. XT also logs its start and exit, a key being saved or removed, and
changes of the link state (connected, unreachable, key not accepted); an
unreachable website is logged once, not on every retry. XT's log rotation is
256 MB per file with a retention setting of 40 files; older files are numbered
`Tlachihuani.1.log`, `Tlachihuani.2.log`, and so on.

**Services → See log** opens the log in XT. The updated viewer pauses when you
scroll or select text, offers **Older / Newer**, retained-file selection and
cancellable parallel search through all retained logs. **Search all log files**
is enabled by default and can be cleared for a single-file search. The view
returns to new entries only with **Follow live**.
It reads bounded pages rather than accumulating new text while you read slowly.
See [the browsing guide](log-viewer.md) and its source/installed verification
boundary before expecting these controls in an older installed binary.

## Security

- A key is 32 random bytes (`xrc_` + 43 characters). xaiht.org stores only its
  SHA-256 and the last four characters for display.
- XT keeps the key in `xaiht-link.json` next to the log, protected with Windows
  DPAPI for the current user, so another Windows account cannot read it.
- XT accepts only an `https://` website (plain `http://` only for this computer,
  for local tests). The key travels only in the `Authorization` header.
- Each xaiht.org user sees and sends only to the computers they added.
- A firewall in front of the website that answers **403** is reported as such;
  only xaiht.org's own **401** is reported as a key that was not accepted.
- Website access additionally requires the local operator's approval for the
  exact origin, connection key fingerprint, computer ID, website user ID and
  immutable Tlamatini binding. Its dedicated grant cannot establish a local
  browser session or acquire launch, stop or administrative permissions.
- The hosted website is trusted with prompt and answer plaintext. This is
  ordinary authenticated HTTPS, **not end-to-end or post-quantum encryption**.
  The separately designed PQ transport is not claimed by this implementation.
- Redirects are disabled when XT sends its computer key or fetches local answer
  content. No port forwarding or external listener is added to the PC.

## Where the code lives

- **XT** (this repository): [TlachihuaniLog.cs](../src/Tlachihuani.Tray/TlachihuaniLog.cs),
  [LogViewerForm.cs](../src/Tlachihuani.Tray/LogViewerForm.cs),
  [XaihtLink.cs](../src/Tlachihuani.Tray/XaihtLink.cs), the xaiht.org tab in
  [ServicesLayout.cs](../src/Tlachihuani.Tray/ServicesLayout.cs) and the Connect
  dialog in [ServiceDialogs.cs](../src/Tlachihuani.Tray/ServiceDialogs.cs). The
  link starts with XT in [TrayContext.cs](../src/Tlachihuani.Tray/TrayContext.cs).
- **Typed command relay and local approval:**
  [XaihtCommands.cs](../src/Tlachihuani.Tray/XaihtCommands.cs),
  [XaihtAccountsForm.cs](../src/Tlachihuani.Tray/XaihtAccountsForm.cs),
  [XaihtNative.cs](../src/Tlachihuani.RC.Server/XaihtNative.cs) and
  [XaihtAccess.cs](../src/Tlachihuani.RC/XaihtAccess.cs).
- **xaiht.org** (the XAIHT `web-app` repository): `api/rc/` (poll and receipt
  routes, keys, storage), `api/rc-router.ts` (the page's operations),
  `contracts/rc.ts` (the JSON message), `src/pages/TlamatiniRC.tsx` and the
  `rc_computers` and `rc_messages` tables in `db/schema.ts`. The website creates
  both tables on first use, with the same columns `drizzle-kit push` would, so a
  deploy needs no manual database step.

## Troubleshooting

| XT shows | Meaning and what to do |
|---|---|
| Not connected | No key saved. Use **Connect…**. |
| Connecting… | XT is making its first request. |
| Offline / retrying | xaiht.org could not be reached; XT retries every 2 to 60 seconds. Check the network. **See log** names the cause. |
| Key not accepted | The computer was removed on xaiht.org or the key is wrong. Create a new key and connect again. |
| Key unreadable | The saved key cannot be read by this Windows user. Connect again. |

## Limits

- XT must be running in the user's Windows session. A sleeping or switched-off
  computer is offline; nothing here wakes it.
- Diagnostic messages are logged only. Real prompts use the separate approved
  command channel and require both Remote-Controller and Tlamatini to be running.
- The [19 November evidence](verification/2026-11-19-xaiht-link.md) used a local
  copy of the page with a memory store; the first test against xaiht.org's Cloud
  SQL database is Angela's production test.
