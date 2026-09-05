# Authorization and visibility

## The promise

A frame is delivered only to a caller who may play the video it comes from, and an
Item the caller cannot see is indistinguishable from an Item that does not exist.

## What breaks without it

- Library-level hiding leaks. A caller denied a library could still obtain frames
  from it by asking the plugin directly, because the plugin serves frames Jellyfin's
  own trickplay routes would refuse.
- Items become enumerable. If a hidden Item and a missing Item answered differently,
  a caller could walk a library they cannot see by response alone.
- Alternate versions become a way around playback limits. A video the caller may not
  play could be reached through a Media Source they may.

## Why this shape

**The visibility gate precedes the playback gate.** An Item the caller cannot see is
refused as nonexistent before any question about playing it is asked, so the refusal
carries no information about why. The reverse order would tell a caller "this exists
and you may not play it", which is exactly the leak.

**The lookup is scoped to the calling user, not to the server.** An administrator, or
the plugin's own in-process position, can see everything; that reach is not evidence
about the caller. Authorization is therefore evaluated as *this user, this Item*,
never as *does this Item exist*.

**GET refuses a server API key rather than inventing a user.** An API key is an
unscoped administrator credential, not user-scoped playback authority. GET therefore
refuses it instead of mapping it to some implied user. The Frame Probe answers a
different question: after Jellyfin's ordinary endpoint policy accepts the request, it
can calculate against user-independent Item and Media Source facts without asserting
that any user may see or play them.

**There is no second playback check on the Source Video.** This is the rejected
alternative, and it was the plugin's original behaviour. Re-checking playback
authorization on the effective Source Video refuses alternate versions whose own
access differs from the logical video's, for reasons that have nothing to do with
the caller's rights. Membership in a logical video the caller may already play *is*
the authorization; asking twice produces refusals that look like bugs and are not.

**Refusals collapse.** Invisible, absent, and unlisted are one status, and the plugin
does not distinguish them for anyone. The distinctions exist internally, in logs, and
nowhere in the response.

## Where it is enforced

[Source resolution](../lifecycle/source-resolution.md), in the five gates and their
order.

## How a caller observes it

For GET, `401` identifies an unauthenticated caller, `403` an API-key caller without a
current user or a user who may not play the video, and `404` an Item that is invisible
or absent or a Media Source that is not a member. HEAD is governed by Jellyfin's
ordinary endpoint policy and uses `404` only when the requested Item, Source Video, or
source membership cannot be established independently of a user. A HEAD success is
never permission evidence.
