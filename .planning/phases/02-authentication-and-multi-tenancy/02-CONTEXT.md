# Phase 2: Authentication and Multi-Tenancy - Context

**Gathered:** 2026-03-03
**Status:** Ready for planning

<domain>
## Phase Boundary

User accounts with email/password login and session isolation between players. Users can sign up, log in, receive a JWT token, and only see their own game sessions. No social login, no email verification, no password reset — those are future enhancements.

</domain>

<decisions>
## Implementation Decisions

### Identity Provider
- ASP.NET Identity — uses existing EF Core + SQLite stack
- Email/password only, no social login (Discord/Google can be added later)
- No email verification required — register and play immediately
- Email is just an identifier, not verified

### Session Strategy
- JWT with refresh tokens — short-lived access token + longer refresh token
- Stateless auth compatible with Phase 3 API layer and SPA frontend in Phase 4
- Token stored client-side (Phase 4 concern, but JWT decision enables it)

### Multi-Tenancy Isolation
- UserId foreign key on Campaign table
- Filter queries by authenticated user
- Works with existing aggregate structure — Campaign already has Characters/Encounters as List<Guid>

### Account Recovery
- Minimal — no password reset flow, no account lockout policy
- Pre-release with small audience, keep scope tight
- Add recovery features when there are real users

### Claude's Discretion
- JWT token expiry durations (access + refresh)
- Identity table naming/schema choices
- Whether to create a separate Web API project or extend existing infrastructure
- Middleware/filter design for auth enforcement
- Test strategy for auth flows

</decisions>

<specifics>
## Specific Ideas

No specific requirements — open to standard ASP.NET Identity + JWT approaches.

</specifics>

<code_context>
## Existing Code Insights

### Reusable Assets
- EF Core DbContext with SQLite (WretchedWhispers.Infrastructure) — Identity tables can be added to existing context
- ServiceCollectionExtensions pattern — DI registration for auth services follows established pattern
- JSON blob persistence pattern (Guid Id PK + string Data TEXT) — used for aggregates, Identity uses its own table structure

### Established Patterns
- Primary constructors for services — auth services should follow
- Repository interfaces in Core — may need IUsersRepository or can use Identity's UserManager directly
- Transient service lifetime for SK plugin compatibility

### Integration Points
- Campaign aggregate: needs UserId property added
- Infrastructure project: DbContext gets Identity tables
- New Web API project likely needed for HTTP endpoints (currently console apps only)
- Phase 3 API layer will consume the auth middleware built here

</code_context>

<deferred>
## Deferred Ideas

- Social login (Discord, Google, GitHub) — future enhancement
- Email verification — add when audience grows
- Password reset via email — requires SMTP, defer to production readiness phase
- Account lockout policy — defer to production readiness phase

</deferred>

---

*Phase: 02-authentication-and-multi-tenancy*
*Context gathered: 2026-03-03*
