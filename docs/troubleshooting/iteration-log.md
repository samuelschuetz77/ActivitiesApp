# Iteration Log

Read this first every conversation. It records what happened each test run so you don't have to re-describe symptoms.

---

## Iteration 1 — 2026-04-06 (Blazor infinite load, MAUI no images on category click)

**Tested:** Blazor + MAUI

**Symptoms:**
- Recently viewed worked on both platforms and pulled images correctly.
- Blazor: clicking a category card showed "Loading..." forever — never displayed results.
- MAUI: clicking a category card populated cards instantly but they never had images (emoji only).

**Root cause:**
- Blazor: `PreloadActivitiesAsync` fetched ALL 9 tags sequentially before any could display. A category click fell into this full-warmup path and blocked until every tag completed. If any single tag's discover call was slow, the entire UI froze.
- MAUI images: activities were stored with `ImageUrl = /api/photos/place/{placeId}?maxwidth=400`. This endpoint does 2 Google roundtrips (Place Details → Photo fetch) per image. On a cold load with 20 cards, that's 40 Google calls before images appear. Many timed out or were rate-limited, so only some loaded.

**Fixes applied:**
- `Home.razor`: replaced `PreloadActivitiesAsync` (all-tags-at-once) with `EnsureTagLoadedAsync` (single tag on demand) + `StartBackgroundWarmAsync` (remaining tags in background). Category click now fetches only the selected tag.
- `OfflineActivityService.cs`: added `withImages` and `sampleImages` to discover logs so image presence is visible without user description.
- `ActivityRestClient.cs`: added `withImages` count to discover response logs.
- API `Program.cs`: added per-request `discoverRequestId`, `DbUpdateConcurrencyException` handling, per-tag Google type logging.
- Created `ImageUrlResolver.cs`: shared helper that normalizes relative `/api/photos/...` URLs to absolute URLs for MAUI.

**Status:** Blazor resolved. MAUI images still slow (addressed in iteration 2).

---

## Iteration 2 — 2026-04-06 (MAUI images slow, only half loading)

**Tested:** MAUI + Blazor

**Symptoms:**
- Blazor working great after iteration 1 fix.
- MAUI: images did load eventually, but very slowly. Roughly half the cards showed images, the rest stayed on emoji fallback.

**Root cause:**
- The API stored `ImageUrl = /api/photos/place/{placeId}` which requires a Google Place Details call + a photo fetch per image (2 roundtrips). Google's Nearby Search already returns a `PhotoReference` in the initial response, but we were throwing it away and re-fetching from scratch per card.

**Fixes applied:**
- `GooglePlacesService.cs`: was already building a `PhotoUrl` from `PhotoReference` via `BuildPhotoUrl()` — this produces `/api/photos?r={ref}&maxwidth=400` which only needs 1 Google roundtrip (direct photo fetch, no Place Details).
- API `Program.cs`: added `GetPreferredPlaceImageUrl(place)` helper — uses `place.PhotoUrl` (1-roundtrip path) when available, falls back to `/api/photos/place/{placeId}` (2-roundtrip path) only when no photo reference exists.
- `ApplyGooglePlaceData()` and new-activity creation both use `GetPreferredPlaceImageUrl()`.
- `ActivityGrpcService.cs`: same fix for the gRPC discover path.
- API photo proxy endpoints: added `ILogger<Program>` injection, cache hit/miss/fetch/fail logging, `Cache-Control: public, max-age=86400, immutable` response header, ETags.
- API discover endpoint: added background photo pre-warm — after returning results, fires off `Task.Run` to pre-fetch all photo references into `IMemoryCache` so images are cached server-side before the client requests them.
- `Home.razor`: added `loading="lazy"` to filtered result images.
- Blazor `App.razor` + MAUI `index.html`: changed geolocation to `enableHighAccuracy: false, timeout: 5000, maximumAge: 300000` for faster location fix.
- `Home.razor` startup: waits up to 2s for location before starting cache warm, preventing a throwaway warm that gets wiped when location arrives.

**Status:** Resolved (pending verification). Compile error hit — see iteration 3.

---

## Iteration 3 — 2026-04-06 (Compile error: GetPreferredPlaceImageUrl)

**Tested:** Build only

**Symptoms:**
- `error CS0103: The name 'GetPreferredPlaceImageUrl' does not exist in the current context`

**Root cause:**
- The helper function was referenced in `Program.cs` line 389 but the function definition wasn't added.

**Fix:**
- Added `static string GetPreferredPlaceImageUrl(GooglePlacesService.NearbyPlace place)` to `Program.cs` near the other helpers. Also updated `ApplyGooglePlaceData` to use it instead of inline logic.

**Status:** Resolved. Build succeeds.

---

## Iteration 4 — 2026-04-06 (MAUI slideshow, Blazor re-fetching cached categories)

**Tested:** MAUI + Blazor

**Symptoms:**
- MAUI: images loaded, but kept changing. Each card was like a slideshow — cycling between no-image, emoji, and different actual images. Extremely distracting.
- Blazor: after switching between categories, some triggered network re-fetches instead of serving from cache instantly.

**Root cause — MAUI slideshow:**
- `OfflineActivityService.DiscoverActivitiesAsync` returns stale cache immediately, then fires a background REST refresh. When the refresh completes, it calls `DataChanged?.Invoke()`.
- `Home.razor` `OnDataChanged` handler was calling `ResetTagCache()` which wiped ALL cached tags, then `StartBackgroundWarmAsync()` which re-fetched all 9 tags.
- Each tag re-fetch called `DiscoverActivitiesAsync` again, each firing ANOTHER background refresh, each firing `DataChanged` again.
- Result: exponential cascade. Every cycle re-rendered cards with different data/image states.
- Additionally, `ActivityCacheService.AddOrUpdate()` fired `DataChanged` per-activity during the bulk update loop — so a 20-activity refresh fired `DataChanged` 20 times before the service-level `DataChanged` even fired.

**Root cause — Blazor re-fetching:**
- `LocationService.UpdateLocationAsync()` fired `LocationChanged` every 3 minutes (timer interval) even when position hadn't moved at all.
- `OnLocationChanged` handler called `ResetTagCache()`, wiping all cached tags.
- Next category click found an empty cache → network fetch instead of instant display.

**Fixes applied:**
- `Home.razor` `OnDataChanged`: no longer calls `ResetTagCache` or `StartBackgroundWarmAsync`. Only refreshes recently viewed and calls `StateHasChanged()`. Tag cache stays intact.
- `ActivityCacheService.cs`: `AddOrUpdate` now accepts `suppressNotify` parameter. Added `NotifyDataChanged()` for explicit single notification.
- `OfflineActivityService.cs`: bulk updates during background refresh use `suppressNotify: true`. Single `DataChanged` fires after all updates.
- `LocationService.cs`: `UpdateLocationAsync` now compares new position to previous. Only fires `LocationChanged` when position moves >0.001 degrees (~100m). Logs `Location unchanged` when skipping.
- `Home.razor`: added render stability tracking — `renderCount`, `dataChangedCount`, `filteredListVersion`, `lastRenderedImageUrls` dictionary. `CountImageChanges()` logs per-activity image URL changes with `Home image changed: activity=... prev=... next=...`.

**Status:** Needs verification. The cascade should be broken but hasn't been tested yet.

**What to check next run:**
- `dataChangedCount` should stay at 1-2 per session, not climb
- `imageChanges` should be 0 after initial load for any tag
- `Home image changed` lines should not appear after initial warm
- `Location unchanged` should appear every 3 min (not `Location updated`)
- Switching categories should show `cache_hit` logs, not `tag_click_cache_miss`
- No `Home cache reset` logs should appear after startup unless user changes location manually

---

## Iteration 5 — 2026-04-06 (Logging specificity improvements)

**Tested:** Build only (code changes are logging-only)

**Symptoms:**
- Blazor "not working great" — user reported but couldn't pinpoint specifics
- Iteration 4 fixes (slideshow cascade, location spam) still unverified
- Existing logs had gaps: couldn't tell if UI was stuck on spinner, whether cache hit/miss on tag click, or if no-location fallback was silently used

**Root cause:**
- 8 decision points and state transitions had no log output, making it impossible to diagnose from logs alone

**Fixes applied:**
- `Home.razor` — 5 new log points:
  - `OnTagSelected`: logs `Home tag selected: tag=..., cacheHit=..., cachedTagCount=...` at the decision point
  - `EnsureTagLoadedAsync` no-location path: logs `Home tag load: no active location, falling back to full list`
  - `isLoadingFiltered` transitions: logs `Home loading state: isLoadingFiltered=true/false, tag=..., trigger=...`
  - `StartBackgroundWarmAsync` per-tag skip: logs `Home background warm skipping tag=..., reason=already_completed`
  - After startup: logs `Home startup complete: cachedTags=..., hasLocation=..., hasActiveLocation=..., recentlyViewedCount=...`
- `ActivityRestClient.cs` — 2 changes:
  - Added `Stopwatch` elapsed time to discover response log
  - Added try/catch with `REST DiscoverActivities FAILED: tag=..., elapsed=..., error=...`
- `LocationService.cs` — 1 change:
  - Catch block now logs `Location failed: wasLocated=..., firingChanged=...`

**Status:** Build succeeds. Logging improvements only — no behavior changes. Iteration 4 fixes still need verification.

**What to check next run:**
- All iteration 4 checks still apply (dataChangedCount, imageChanges, cache_hit on switch, etc.)
- New logs to look for:
  - `Home tag selected` — confirms whether tag click used cache or triggered fetch
  - `Home loading state` — if `isLoadingFiltered=true` appears without a corresponding `=false`, the UI is stuck on spinner
  - `Home startup complete` — confirms startup finished and shows initial state
  - `REST DiscoverActivities completed` with `elapsed` — identifies slow API calls
  - `REST DiscoverActivities FAILED` — catches HTTP errors that were previously silent
  - `Location failed` — shows whether a location failure triggered cache wipe via LocationChanged

---

## Iteration 6 — 2026-04-06 (Missing home images + back button)

**Tested:** User observation from MAUI test run

**Symptoms:**
- MAUI: some cards (Southtowne Theatre, Tri-Grace Ministries) showed no image on home page but had images when clicking into the detail page.
- Both platforms: clicking back from detail page always went to `/activities` instead of the page the user came from (e.g., Home `/`).

**Root cause — missing images:**
- Activities without a `PhotoReference` from Google Nearby Search get the slow-path URL: `/api/photos/place/{placeId}?maxwidth=400` (2 Google roundtrips: Place Details → Photo fetch).
- The photo pre-warm only warmed `/api/photos?r=...` (fast-path) URLs. Slow-path URLs were never pre-warmed, so the first client request was cold — slow or timed out, `onerror` fired, emoji shown.
- On the detail page, the same URL worked because the API had cached it from the home page's attempt by then.

**Root cause — back button:**
- `ActivityDetail.razor` `GoBack()` was hardcoded to `Navigation.NavigateTo("/activities")`.

**Fixes applied:**
- `Program.cs` photo pre-warm: now collects both `/api/photos?r=` and `/api/photos/place/` URLs. Place-based pre-warm extracts placeId, calls `GetPlaceDetailsAsync` → `FetchPhotoAsync` → caches under `photo_place:{placeId}:{width}`.
- `Program.cs` discover log: added `imagePaths: fast={N}, slow={N}, none={N}` to track which path each activity's image uses.
- `ActivityDetail.razor`: `GoBack()` now calls `JS.InvokeVoidAsync("history.back")` instead of hardcoded nav.
- `Home.razor` tag load log: added `fastImages` and `slowImages` counts.
- `ActivityRestClient.cs` discover log: added `fastImages` and `slowImages` counts.

**Status:** Build succeeds. Pending verification.

**What to check next run:**
- Cards that previously showed emoji on home should now show images (pre-warm covers both paths)
- Back button from detail page should return to Home when that's where you came from
- `imagePaths: fast=N, slow=N` in API logs — if `slow` is high, many places lack PhotoReference from Nearby Search
- `fastImages` / `slowImages` in client and Home logs — confirms which path the client sees
- Pre-warm log should show total = fast + place URLs warmed

---

## Iteration 7 — 2026-04-06 (Blazor re-fetches everything on back-navigation, 30s load)

**Tested:** Blazor (user observation)

**Symptoms:**
- Blazor: navigating to a detail page and hitting back reloaded Home from scratch — full 30-second warm with 9 sequential API calls.
- Blazor: clicking a category card while warm was still running showed "Searching nearby..." and took a long time.
- MAUI: working perfectly (has persistent cache via OfflineActivityService).

**Root cause:**
- `Home.razor` stored all cached data (`cachedActivitiesByTag`, `backgroundWarmCompletedTags`) in component instance fields.
- In Blazor Server, navigating away destroys the component. Navigating back creates a brand new instance with empty cache.
- `firstRender` was `true` again → full startup: 2s location wait + 9 sequential discover API calls.
- `LocationService` is scoped (per-circuit) so it survived navigation, but the cache didn't.
- MAUI didn't have this problem because `OfflineActivityService` + `ActivityCacheService` are singletons.

**Fixes applied:**
- Created `DiscoverCacheService` (`ActivitiesApp.Shared/Services/DiscoverCacheService.cs`) — scoped service that stores tag cache + location, survives component disposal.
- Registered as `AddScoped` in Blazor Web (`Program.cs`) and `AddSingleton` in MAUI (`MauiProgram.cs`).
- `Home.razor` startup: checks `DiscoverCache.HasCacheForLocation(activeLat, activeLng)` first. If cache exists for same location, restores `cachedActivitiesByTag` and `backgroundWarmCompletedTags` from the service — skips location wait and all API calls. Logs `Home startup: restored from DiscoverCache`.
- `Home.razor` `EnsureTagLoadedAsync`: saves each tag to `DiscoverCache.SaveTag()` after loading.
- `Home.razor` `ResetTagCache`: calls `DiscoverCache.Reset()` to clear the service cache too.

**Status:** Build succeeds (file-lock warnings from running app, no compile errors). Pending verification.

**What to check next run:**
- Navigate Home → click card → back → Home should be instant (no API calls)
- Log should show `Home startup: restored from DiscoverCache, cachedTags=9` on back-navigation
- Log should show `Home startup: location resolved=...` only on first visit (cold start)
- Clicking category cards after back-navigation should show `cacheHit=True` immediately
- No "Searching nearby..." spinner on cached tags
- Changing location (ZIP) should still trigger full re-warm (cache invalidated by location mismatch)

---

## Iteration 8 — 2026-04-06 (Revert iteration 7, new caching approach)

**Tested:** MAUI + Blazor (user observation)

**Symptoms:**
- MAUI: worked perfectly at home location. After switching ZIP to Oakland, no categories returned results ("no Food & Drink activities found nearby" etc). Nothing loaded in any category.
- Blazor: didn't work at all — nothing loaded.

**Root cause:**
- Iteration 7 introduced `DiscoverCacheService` injected into `Home.razor`. This added a new dependency that both Blazor and MAUI needed to resolve. The approach was fragile — it changed the startup flow in Home.razor with an early `return` that skipped event wiring and warm logic. The exact failure mode on ZIP switch is unclear but likely related to the cache service returning stale data for the wrong location or the early return path missing critical initialization.

**Action taken:**
- **Fully reverted iteration 7**: deleted `DiscoverCacheService.cs`, removed all references from Home.razor, Web Program.cs, and MauiProgram.cs. Home.razor startup is back to the pre-iteration-7 state.

**New approach — cache inside `ActivityRestClient` (Blazor-only, no Home.razor changes):**
- `ActivityRestClient` is already scoped per-circuit in Blazor Server — it survives component navigation.
- Added `_discoverCache` dictionary keyed by tag name inside `ActivityRestClient`.
- `DiscoverActivitiesAsync` checks cache first. If location moved >0.001 degrees, clears all cached tags. Returns cached result on hit, fetches from API on miss.
- Logs: `REST DiscoverCache invalidated` on location change, `REST DiscoverActivities cache hit` on cache hit.
- Home.razor still re-creates on navigation and runs its startup, but the 9 discover calls hit the client-side cache instantly instead of the API.
- Also: Home.razor now skips the 2s location wait if `hasActiveLocation` is already true (LocationService is scoped, survives navigation). Logs `Home startup: location already known, skipping wait`.

**Why this is safer than iteration 7:**
- No new service, no new DI registration, no changes to Home.razor's flow or event wiring.
- Cache lives inside the existing REST client — same lifecycle, same scope.
- Home.razor still runs its full startup path (events, warm, etc.) — just faster because API calls are cached.
- MAUI is completely unaffected (uses OfflineActivityService, not ActivityRestClient).

**Status:** Build succeeds. Pending verification.

**What to check next run:**
- Blazor: Home loads and shows categories with results
- Blazor: navigate Home → card → back → Home should be fast (logs show `cache hit` for each tag, no 2s location wait)
- Blazor: switch ZIP → cache should invalidate → fresh API calls for new location
- Blazor: categories at new location should show results (not empty)
- MAUI: unchanged behavior, should still work perfectly
- Log `REST DiscoverCache invalidated` should appear on ZIP change
- Log `Home startup: location already known` should appear on back-navigation

---

## Iteration 9 — 2026-04-06 (No cards after ZIP change — attempt 1, one-shot flag)

**Tested:** User observation after ZIP change

**Symptoms:**
- Changing location (ZIP) works — location updates correctly.
- After ZIP change, no cards load in any category. Tags show "No activities found nearby."
- BUT navigating to Activities page loads cards, and coming back to Home shows them.
- MAUI-specific: Blazor REST client blocks for fresh data; MAUI cache-first returns empty.

**Root cause:**
- MAUI `OfflineActivityService.DiscoverActivitiesAsync` returns cached data immediately, then background-refreshes from the API. After a ZIP change to a new area, the local cache has NO activities near the new location → returns empty list.
- `Home.razor` caches those empty results in `cachedActivitiesByTag` and marks them in `backgroundWarmCompletedTags`.
- Background refresh completes → `DataChanged` fires → but `OnDataChanged` deliberately did NOT reset tag cache (to prevent slideshow cascade from iteration 4).
- Result: tag cache stays poisoned with empty results. User clicks a tag → cache hit on empty list → "No activities found nearby."

**Fix attempt 1 (insufficient):**
- Added `_awaitingBackgroundRefresh` one-shot flag. Set on location change, cleared on first `DataChanged`.
- Problem: on MAUI, each of the 9 tag background refreshes fires `DataChanged` independently. The flag clears on the FIRST `DataChanged` and re-warms, but that re-warm also gets mostly empty results (only 1 tag's refresh completed). The other 8 tags stay empty because subsequent `DataChanged` events see the flag as `false`.

**Fix attempt 2 (current):**
- Replaced one-shot flag with `_emptyTagRetryCount` counter (limit 10).
- `OnDataChanged` now checks for cached tags with 0 results. If any exist and retry limit not reached, removes them from cache and re-fetches the selected tag.
- `_emptyTagRetryCount` resets in `ResetTagCache` (called on location change and startup).
- Empty tags get evicted and retried each time `DataChanged` fires, regardless of what triggered it.
- Once a tag gets non-empty results, it stays cached and is no longer retried.
- Cascade stops naturally: once all tags have data, no empty tags → no retries.

**Also added (from attempt 1, kept):**
- `LogWarning` when `EnsureTagLoadedAsync` returns 0 results with lat/lng/tag
- `LogWarning` in `OfflineActivityService` when returning empty cache with lat/lng/tag
- Improved manual location log

**Status:** Build succeeds. Pending verification.

**What to check next run:**
- MAUI: change ZIP → click category → cards should load (possibly after a brief delay while background refreshes arrive)
- `Home OnDataChanged: retry #N, M empty tags` should appear, with retry count climbing and empty count dropping
- Once all tags populated: `Home OnDataChanged: no empty tags` should appear for subsequent DataChanged events
- `_emptyTagRetryCount` should NOT exceed 10
- Slideshow should NOT return (retries only touch empty tags, not tags that already have data)
- Blazor: unaffected (REST client blocks for API data, tags never cached empty, no retries triggered)
