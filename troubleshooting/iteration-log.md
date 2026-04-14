# Iteration Log

## 2026-04-13 — 401 audience error diagnostics + fix

### Problem
Creating an activity returns:
> `401 — Wrong audience. The audience '(null)' is invalid`

The token's `aud` claim is null/missing when it reaches the API.

### Root Cause
The Azure App Registration for `6d3dc4ee-33ce-4656-95c8-702a38464687` likely does not have
"Expose an API" properly configured. Without the Application ID URI set to
`api://6d3dc4ee-33ce-4656-95c8-702a38464687` and the `access_as_user` scope enabled,
Azure AD cannot issue a properly-scoped access token — it returns a token with a null/missing
`aud` claim instead of rejecting the request outright.

### Azure Portal Fix Required (manual steps)
1. Go to Azure Portal → Microsoft Entra ID → App Registrations → `6d3dc4ee-33ce-4656-95c8-702a38464687`
2. Click **"Expose an API"**
3. Set **Application ID URI** to exactly: `api://6d3dc4ee-33ce-4656-95c8-702a38464687`
4. Click **"Add a scope"**:
   - Scope name: `access_as_user`
   - Who can consent: Admins and users
   - State: Enabled
5. Click **"Authentication"** → verify redirect URIs include all active environments
6. If using a `ClientSecret`, verify it hasn't expired under **"Certificates & secrets"**

### Code Changes Made
- `src/ActivitiesApp.Web/Services/ActivityRestClient.cs`:
  - Added `BuildErrorMessage` / `Diagnose401` / `ExtractWwwAuthParam` / `Truncate` helpers
  - `CreateActivityAsync` now: catches `MicrosoftIdentityWebChallengeUserException` for token
    acquisition failures; throws `InvalidOperationException` with 8-level 401 diagnosis +
    raw `WWW-Authenticate` header appended to every branch
- `src/ActivitiesApp.Shared/Pages/CreateActivity.razor`:
  - Catches `InvalidOperationException` first and shows `ex.Message` directly in the UI
  - `HttpRequestException` and generic `Exception` also now show `ex.Message` / type info
    instead of the opaque "Failed to create activity" fallback
