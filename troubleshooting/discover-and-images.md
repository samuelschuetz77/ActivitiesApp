# Discover And Images

## What Changed

Home page category results are now warmed into a tag-specific cache in shared UI code. The shared page is used by both Blazor and MAUI, but the backing services differ:

- Blazor: `ActivityRestClient` calling REST endpoints
- MAUI: `OfflineActivityService` using local cache plus REST background refresh

Because of that split, logs from all three layers matter:

- Shared home page logs
- Client service logs
- API discover/photo logs

## Log Prefixes To Watch

### Shared Home Page

These come from `Home.razor`:

- `Home startup: location resolved=... in ...ms` — shows if location was ready before warmup
- `Home cache reset: generation=... trigger=...` — cache was wiped (startup or location change only)
- `Home background warm started/completed: ... elapsed=...ms` — full tag pre-warm timing
- `Home tag load started/completed: tag=... elapsed=...ms, withImages=...` — per-tag fetch timing and image counts
- `Home tag load skipped: ... reason=cache_hit` — tag was already cached
- `Home tag applied: tag=... listVersion=... imageChanges=...` — how many card images changed vs previous render
- `Home image changed: activity=... prev=... next=...` — specific card that switched images (the "slideshow" culprit)
- `Home render #N: selectedTag=... dataChangedCount=...` — total render count and DataChanged event count
- `Home OnLocationChanged: ...` — location change triggered a cache reset
- `Home OnDataChanged fired: count=... — refreshing recently viewed only` — DataChanged no longer resets tag cache
- `Location updated (moved): ...` / `Location unchanged: ...` — location only fires when position actually moves

Interpretation:

- If `location resolved=False`, the 2s wait expired before GPS/WiFi fix → first warm uses no location.
- If `elapsed` on tag load is high (>3s), the API discover call is slow for that tag.
- If `withImages=0` on a completed tag, the API is not returning photo URLs for those activities.
- If `cache_hit` logs appear on click, the background warm worked — instant display.
- If `imageChanges > 0`, cards are flickering. Check the `Home image changed` lines to see which activities are switching URLs.
- If `dataChangedCount` is high (>2 per session), background refreshes are over-firing.
- If `renderCount` climbs rapidly (>20 in first 10 seconds), something is triggering unnecessary re-renders.
- If you see multiple `Home cache reset` lines without a location change, something is resetting the cache unnecessarily.
- If `Home tag selected` shows `cacheHit=False` after warmup completed, the warm didn't cache that tag properly.
- If `Home loading state: isLoadingFiltered=true` appears without a corresponding `=false`, the UI is stuck showing "Searching nearby..." forever.
- If `Home startup complete` shows `hasActiveLocation=False`, warmup ran without location — results may be empty or unfiltered.
- If `Home tag load: no active location, falling back to full list` appears, the discover call was skipped entirely — only the full activity list was used.

### Blazor REST Client

These come from `ActivityRestClient`:

- `REST DiscoverActivities at (...) radius=... tag=...`
- `REST DiscoverActivities completed: tag=... count=... withImages=... fastImages=... slowImages=... elapsed=...ms`
- `REST DiscoverActivities FAILED: tag=... elapsed=... error=...`
- `REST ListActivities returned ... withImages=...`

Interpretation:

- If `withImages=0`, image URLs are missing or not being normalized correctly.
- If tag-specific discover logs return `0` for a category that should exist, inspect API logs next.
- If `elapsed` is high (>3000ms), the API discover endpoint or Google Places is slow for that tag.
- If `FAILED` appears, the HTTP call threw — check network, API health, or timeout settings.

### MAUI Offline Service

These come from `OfflineActivityService`:

- `DiscoverActivitiesAsync: returning ... cached items ... withImages=... sampleImages=...`
- `DiscoverActivities background refresh complete for tag ... elapsed=...ms, withImages=...`
- `GetActivityAsync(...): ...ms (cache), hasImage=..., imageUrl=...`

Interpretation:

- If MAUI shows activities but Blazor does not, compare MAUI cached-count logs to Blazor REST returned-count logs.
- If `withImages=0` on both cached and refreshed results, images are missing from the API response.
- If `sampleImages` shows relative URLs (e.g., `/api/photos?r=...`), normalization failed — they should be absolute.
- If `elapsed` on background refresh is high, the API discover call or DB save is slow.

### API Discover

These come from `/api/discover`:

- `REST Discover <id> started ...`
- `REST Discover <id> got ... Google places`
- `REST Discover tag ... for Google type ... returned ... places`
- `REST Discover <id> hit a concurrency conflict ...`
- `REST Discover <id> returning ... withImages=...`
- `REST Discover <id> returning ... imagePaths: fast=... slow=... none=...`
- `REST Discover <id> photo pre-warm: .../... photos (... fast + ... place) in ...ms`
- `Photo proxy cache hit/miss: ref=... width=...`
- `Photo proxy fetched: ref=... bytes=...`
- `Photo proxy fetch failed: ref=...`
- `Photo place cache hit/miss: placeId=...`

Interpretation:

- A concurrency warning means overlapping discover requests tried to update the same Cosmos rows. The endpoint now logs and still returns results.
- If Google type logs are all zero for a tag, the issue is upstream Google coverage for that location, not the UI cache.
- `photo pre-warm` shows how many images were cached before the client requests them. If `warmed` is low, most were already cached.
- `Photo proxy cache miss` followed by `fetch failed` means the Google photo reference is invalid or expired.
- `Photo place cache miss` means a `/api/photos/place/` request had to do a full Google Place Details + Photo fetch (slow, 2 roundtrips).

## Image Delivery Design

Industry-standard approach in this app:

1. Store stable Google `PlaceId` on activities.
2. Never expose expiring direct Google photo URLs to clients.
3. Serve images through the API proxy endpoints:
   - `/api/photos`
   - `/api/photos/place/{placeId}`
4. Normalize relative proxy URLs into absolute URLs for MAUI clients.

If a card falls back to emoji instead of an image:

- Check whether the activity has a non-empty `ImageUrl`
- Check whether that URL is absolute on MAUI
- Check whether `/api/photos/place/{placeId}` returns `200`

## What To Paste When Reporting A Failure

Paste these together:

1. Shared home page lines containing `Home tag load`, `Home tag applied`, `Home image changed`, `Home cache reset`
2. Blazor or MAUI client lines containing `DiscoverActivities`
3. API lines containing `REST Discover`
4. Any image request failures for `/api/photos` or `/api/photos/place`
5. Any `Home OnDataChanged` or `Location updated/unchanged` lines

That set is enough to determine whether the problem is:

- no Google results
- cache warmup failure
- REST/MAUI divergence
- Cosmos concurrency noise
- image proxy failure

## Local Verification

On localhost:

1. Open browser DevTools and filter network requests by `discover` and `photos`.
2. Load the home page and wait for warmup.
3. Confirm tag discover calls happen during warmup, not on card click.
4. Click a category and confirm the UI switches without a new discover request.
5. Open a card and confirm image requests hit the photo proxy endpoint successfully.
6. Repeat after changing ZIP/location.
