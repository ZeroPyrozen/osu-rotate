# Owner login

Status: done

UC10. Log in with osu! (OAuth authorization code flow, scope `identify`, callback `/signin-osu`),
check `/api/v2/me` against the configured owner's osu! user id, and keep a session cookie only for
that user. Anyone else gets 403 and no session. Every dashboard page needs the owner; `/healthz`
does not. Sign out. The OAuth client is the same osu! app as the API client (phase 4).

Done when tests drive the whole flow against a fake osu!: the owner gets in, another user gets 403
with no cookie, a forged or replayed callback fails, and signing out ends the session.

## Outcome

`src/OsuRotate.Web/Auth/OwnerAuthentication.cs`: cookie authentication plus ASP.NET Core's OAuth
handler for osu! (`/oauth/authorize`, `/oauth/token`, scope `identify`, callback `/signin-osu`).
After the token exchange it asks `/api/v2/me` who logged in and keeps only the osu! id and name; the
token is not saved. `OnTicketReceived` refuses anyone but `Dashboard:OwnerUserId` with 403 and no
cookie. A fallback authorization policy makes every endpoint need the owner, except `/healthz`,
static assets, the error pages and `/signed-out`; the same check runs on every request, so a
session stops working if the owner id changes. Sign out is a form post to `/signout` with an
antiforgery token.

The session cookie (`osu-rotate`) is HttpOnly, SameSite=Lax, Secure (Secure only follows the request
in Development, for local HTTP), and lasts seven days, sliding. Without the OAuth app or the owner
id, logging in answers 503 naming the settings to add, instead of failing at startup.
`Dashboard__OwnerUserId` is in `deploy/osu-rotate.env.example`.

Tests: 21 tests drive the real flow against a fake osu! back channel (`FakeOsu`): the redirect and
its parameters, the owner getting in, the cookie's flags, no token in the session, another user
getting 403 and no cookie, a forged state, a callback from another browser, a replayed callback, a
failed token exchange, sign out and its antiforgery check, anonymous endpoints, other pages, and
missing configuration. All 14 mutants are caught.

Not tried against the real osu! yet: that needs the OAuth app registered with a callback URL, which
waits on the dashboard hostname.
