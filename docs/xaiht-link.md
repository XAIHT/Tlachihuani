# xaiht.org link and Tlachihuani.log

Latest version: **[0.37.0](releases/v0.37.0.md)**. Requested by Angela López Mendoza
on 19 November 2026.

A signed-in user of **xaiht.org** opens **Tlamatini-RC**, writes a message and
presses **SEND**. XT on her computer receives it as JSON and writes it to
**Tlachihuani.log**, which **Services → See log** shows. The page reports when the
message is written. In this version a message is only logged: Tlamatini does not
act on it, and nothing in it is executed.

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
unreachable website is logged once, not on every retry. At 5 MB the file moves
to `Tlachihuani.1.log`, keeping one previous file.

**Services → See log** opens the log in XT. It shows the newest 2 MB and follows
new lines while the cursor stays at the end, like Tlamatini's own log. **Copy
all**, **Open folder** and **Refresh** are in the footer.

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

## Where the code lives

- **XT** (this repository): [TlachihuaniLog.cs](../src/Tlachihuani.Tray/TlachihuaniLog.cs),
  [LogViewerForm.cs](../src/Tlachihuani.Tray/LogViewerForm.cs),
  [XaihtLink.cs](../src/Tlachihuani.Tray/XaihtLink.cs), the xaiht.org tab in
  [ServicesLayout.cs](../src/Tlachihuani.Tray/ServicesLayout.cs) and the Connect
  dialog in [ServiceDialogs.cs](../src/Tlachihuani.Tray/ServiceDialogs.cs). The
  link starts with XT in [TrayContext.cs](../src/Tlachihuani.Tray/TrayContext.cs).
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
- Messages are logged only. Acting on them in Tlamatini is later work.
- The [19 November evidence](verification/2026-11-19-xaiht-link.md) used a local
  copy of the page with a memory store; the first test against xaiht.org's Cloud
  SQL database is Angela's production test.
