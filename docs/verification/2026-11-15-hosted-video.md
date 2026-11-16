# Hosted RC recorded walkthrough · 15 November 2026

Angela requested video instead of long instructions. Delivered actual headed
Chrome recordings, narrated in English (3:32) and Spanish (3:50), with twelve
chapters each, VTT/SRT captions, an offline HTML player, and a shareable ZIP.

Outputs are under `artifacts/rc-hosted-video/watch`; the ZIP is
`artifacts/rc-hosted-video/Tlamatini-RC-video-tutorials.zip`.
Source and reproduction instructions are in `tests/tutorial/README.md` and
`scripts/Record-HostedWalkthrough.ps1`.

The recorded combined flow uses a real Keycloak 26.8.2 container, real OIDC
authorization, isolated RC/server/cloud binaries, and the actual outbound
connector and browser client. The local configuration/approval page is clearly
labelled as a tutorial test harness. Test identities and main-adapter responses
are synthetic. No production setup wizard or live model is implied.

All 30 recorded assertions passed, across both languages: generated configuration,
connector configuration, owner-only directory, enrollment gating, exact
fingerprint comparison/approval, formatted response, recent history, original
CSV/SVG/PDF bytes and Unicode filenames, mobile-width layout, saved-file results,
offline directory state, reconnect without reenrollment, and logout. Six files
were downloaded through the real relay and compared against the original SHA-256
hash and byte count. See `run-01/results.json` for individual evidence.

FFprobe verified H.264/AAC streams, dimensions, duration and twelve embedded
chapters per MP4; FFmpeg decoded both complete files without errors. Headed
playback checks covered both languages, ten chapter seeks and captions. The
player was also tested as a direct local file, without a server; captions are
embedded in its HTML. All 24 source narration tracks contain non-silent audio.
Recorded action timing is retained; final frames are held when narration is
longer. The player brings the video back into view when choosing a chapter.

Owned test servers, browser sessions and the labelled container were stopped;
test-port/container checks passed. The visible recording console stays open.
No installed service, real account, Torrenter, website deployment or version was
changed. No public Internet, endpoint encryption, physical-phone, installed SCM,
live-model or production account-provisioning acceptance is claimed.
