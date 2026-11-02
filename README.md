# MealSeal .NET backend

ASP.NET Core 10 API for cryptographically signed, single-use meal vouchers. It replaces the old Node/Firestore backend while preserving the JSON contract used by the existing Expo mobile client.

## Architecture

- **MealSeal owns authentication**: email/password accounts live in this API's own PostgreSQL `users` table (PBKDF2-HMAC-SHA256 password hashing). Sign-in (`POST /auth/login`) issues a short-lived ES256-signed access token (JWT) — its own signing key, separate from the voucher-signing key below — which the mobile app then sends as a bearer token.
- **PostgreSQL for all API data**: users/roles, vouchers, redemption and revocation state, and audit events are stored in PostgreSQL.
- **ES256 voucher signatures**: ECDSA P-256 + SHA-256, using the same fixed payload order and DER/base64url signature format as the former Node backend.
- **One-time redemption**: a conditional database update makes concurrent or replayed redemption attempts fail.
- **No public self-registration**: only a `CHARITY_ADMIN` can create further `CHARITY_ADMIN`/`RESTAURANT_STAFF` accounts (`POST /auth/users`) or reset a password (`POST /auth/users/{userId}/reset-password`); the very first admin comes from `Seed__AdminEmail`/`Seed__AdminPassword` at startup.

## API surface

| Method | Route | Access | Purpose |
|---|---|---|---|
| GET | `/health` | Public | API and database health |
| GET | `/public-key` | Public | ES256 voucher-verification key |
| POST | `/auth/login` | Public | Sign in, receive an access token |
| GET | `/auth/me` | Signed-in user | Current API role/profile |
| POST | `/auth/users` | Charity admin | Create an admin or staff account |
| POST | `/auth/users/{userId}/reset-password` | Charity admin | Set a new password for an account |
| POST | `/vouchers/create` | Charity admin | Issue and sign voucher |
| GET | `/vouchers` | Charity admin | List vouchers |
| GET | `/vouchers/{id}` | Charity admin | Voucher status/details |
| GET | `/vouchers/{id}/signed` | Charity admin | Original signed QR object |
| GET | `/vouchers/{id}/qr` | Charity admin | QR code as PNG |
| POST | `/vouchers/{id}/revoke` | Charity admin | Revoke active voucher |
| POST | `/vouchers/verify` | Restaurant staff | Verify signature, restaurant, expiry, and state |
| POST | `/vouchers/redeem` | Restaurant staff | Atomically redeem once |

The mobile app's voucher routes (`create`, `list`, `signed`, `verify`, `redeem`) retain their existing response shape; only the identity layer changed.

## Interactive API testing

- Local Swagger UI: `http://localhost:5203/swagger`
- Deployed Swagger UI: `https://mealseal-api-349396488653.australia-southeast1.run.app/swagger`
- Import `docs/MealSeal.postman_collection.json` into Postman for sign-in and the complete admin/staff voucher workflow.
- See `docs/POSTMAN.md` for setup and the recommended test order.

Protected endpoints use a short-lived MealSeal access token from `POST /auth/login`. Swagger's **Authorize** dialog expects only the token and adds the `Bearer` prefix. The repository and Postman collection intentionally contain no test passwords, access tokens, database URLs, or private signing keys.

## Local setup

Requirements: .NET 10 SDK, PostgreSQL, and either JetBrains Rider or VS Code with C# Dev Kit.

1. Copy `.env.example` values into Rider Run Configuration environment variables (or export them in your shell).
2. Pick a bootstrap admin email/password in `Seed__AdminEmail`/`Seed__AdminPassword`. Set `Seed__StaffPassword` too and the three café-staff accounts in `DatabaseSeeder.StaffAccounts` (`hillside@`/`riverside@`/`corner@mealseal.local`, matching the mobile app's café picker) get seeded with that same password — leave it unset to skip seeding any café-staff accounts. All of this is hashed into the database on first startup; change a password afterward via `POST /auth/users/{userId}/reset-password`, signed in as the admin.
3. Generate signing keys once (both the voucher-signing pair and the separate access-token-signing pair):

   ```sh
   dotnet run --project MealSeal.Api -- --generate-keys
   ```

   Back up all four files securely. Losing or changing the voucher key invalidates issued QR vouchers; losing or changing the auth key invalidates every signed-in session (everyone just signs in again — no other data is affected).

4. Start the API:

   ```sh
   dotnet run --project MealSeal.Api
   ```

The EF Core migrations run automatically on startup. Try requests with `MealSeal.Api/MealSeal.Api.http` in Rider.

Run verification with `dotnet test MealSeal.Backend.sln`.

## Deployment: Neon + Google Cloud Run

The API is deployed as a container on **Google Cloud Run** (project `mealseal`, region `australia-southeast1`), built via **Cloud Build** and pushed to **Artifact Registry**. All secrets live in **Secret Manager**, not in environment files or source control. (Render was used early on; it's no longer part of the deployment — its free tier slept after inactivity and its free Postgres expired after 30 days, neither of which Cloud Run/Neon have.)

1. Use the linked Neon project `MealSeal` (`muddy-feather-76535250`). Its `main` branch already contains the schema; EF Core's auto-migration-on-startup applies any new migrations on next deploy.
2. One-time setup (already done for this project, kept here for a fresh environment):
   - `gcloud artifacts repositories create mealseal --repository-format=docker --location=australia-southeast1`
   - Create one Secret Manager secret per sensitive value, each holding the raw value as its `latest` version: `mealseal-auth-private-key`, `mealseal-auth-public-key`, `mealseal-voucher-private-key`, `mealseal-voucher-public-key`, `mealseal-database-url` (Neon's pooled URL), `mealseal-database-url-unpooled` (Neon's direct URL), `mealseal-seed-admin-password`, `mealseal-seed-staff-password`.
3. Build, test and push a new image — `cloudbuild.yaml` runs the full `dotnet test` suite as a gate before the image is built, so a broken test blocks the deploy:

   ```sh
   gcloud builds submit --config cloudbuild.yaml --project mealseal .
   ```

4. Deploy the new image to the existing Cloud Run service:

   ```sh
   gcloud run deploy mealseal-api \
     --image australia-southeast1-docker.pkg.dev/mealseal/mealseal/mealseal-api:latest \
     --region australia-southeast1 --project mealseal
   ```

   This reuses the service's existing env vars/secret bindings (`ASPNETCORE_ENVIRONMENT`, `Seed__AdminEmail`, the `AllowedOrigins__N` list, and the eight secrets above wired as `valueFrom.secretKeyRef`) — only the container image changes. To add or change an env var without rebuilding, use `gcloud run services update mealseal-api --region australia-southeast1 --update-env-vars KEY=value`.
5. Confirm `https://mealseal-api-349396488653.australia-southeast1.run.app/health` returns `status: ok`.

The API accepts Neon's native `postgresql://` URLs and converts them to Npgsql configuration internally. It also continues to accept `ConnectionStrings__Postgres` and `ConnectionStrings__PostgresMigration` in Npgsql keyword format. Never commit any of these values.

Only after deployment should the separate mobile phase replace `API_BASE_URL` with the Cloud Run HTTPS URL. Never put private signing keys in the mobile app.

## iOS and Android testing choices

- **Current iPhone, free and fastest**: use Expo Go while the app remains compatible with Expo Go.
- **iOS Simulator, free on this Mac**: install Xcode, then run the Expo iOS simulator workflow.
- **Local development build on iPhone**: Xcode can install a local build using a free Apple ID, with normal free-signing limitations.
- **TestFlight/App Store or EAS cloud iOS device builds**: requires Apple Developer Program membership, so it is not a zero-cost route.
- **Android without owning a phone**: install Android Studio and create a free Android emulator on the Mac. The backend itself is platform-independent.

For phone testing, the hosted HTTPS API is easiest. For a local API, the phone and Mac must share a network and the base URL must use the Mac's LAN address, not `localhost`.

## Security notes

- Passwords are hashed with PBKDF2-HMAC-SHA256 (ASP.NET Core's `PasswordHasher`), never stored or logged in plain text.
- Access tokens are ES256-signed JWTs with a short lifetime; they carry only the user's ID — role and restaurant assignment are re-read from the database on every request, so deactivating a user takes effect immediately.
- The access-token signing key is separate from the voucher-signing key (separation of duties).
- `POST /auth/login` has its own, stricter per-IP rate limit to slow down password guessing.
- Authorization is server-side (`CHARITY_ADMIN` or `RESTAURANT_STAFF`), never trusted from QR input.
- Staff can verify/redeem only vouchers for their assigned restaurant.
- Signed payloads are compared with the immutable database record.
- Request size and rate limits are enabled; errors do not return stack traces.
- Private keys are excluded by `.gitignore`.
