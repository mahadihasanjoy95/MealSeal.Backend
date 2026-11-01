# MealSeal .NET backend

ASP.NET Core 10 API for cryptographically signed, single-use meal vouchers. It replaces the old Node/Firestore backend while preserving the JSON contract used by the existing Expo mobile client.

## Architecture

- **Firebase Authentication remains**: the mobile app already signs in with Firebase and sends an ID token. The API verifies it with Firebase Admin.
- **PostgreSQL replaces Firestore for API data**: users/roles, vouchers, redemption and revocation state, and audit events are stored in PostgreSQL.
- **ES256 voucher signatures**: ECDSA P-256 + SHA-256, using the same fixed payload order and DER/base64url signature format as the former Node backend.
- **One-time redemption**: a conditional database update makes concurrent or replayed redemption attempts fail.
- **No mobile code is changed by this backend project.**

## API surface (11 endpoints)

| Method | Route | Access | Purpose |
|---|---|---|---|
| GET | `/health` | Public | API and database health |
| GET | `/public-key` | Public | ES256 verification key |
| GET | `/auth/me` | Signed-in user | Current API role/profile |
| POST | `/vouchers/create` | Charity admin | Issue and sign voucher |
| GET | `/vouchers` | Charity admin | List vouchers |
| GET | `/vouchers/{id}` | Charity admin | Voucher status/details |
| GET | `/vouchers/{id}/signed` | Charity admin | Original signed QR object |
| GET | `/vouchers/{id}/qr` | Charity admin | QR code as PNG |
| POST | `/vouchers/{id}/revoke` | Charity admin | Revoke active voucher |
| POST | `/vouchers/verify` | Restaurant staff | Verify signature, restaurant, expiry, and state |
| POST | `/vouchers/redeem` | Restaurant staff | Atomically redeem once |

The five routes already called by the mobile app (`create`, `list`, `signed`, `verify`, and `redeem`) retain its expected response shape.

## Interactive API testing

- Local Swagger UI: `http://localhost:5203/swagger`
- Deployed Swagger UI: `https://mealseal-api.onrender.com/swagger`
- Import `docs/MealSeal.postman_collection.json` into Postman for Firebase sign-in and the complete admin/staff voucher workflow.
- See `docs/POSTMAN.md` for setup and the recommended test order.

Protected endpoints use a short-lived Firebase ID token. Swagger's **Authorize** dialog expects only the token and adds the `Bearer` prefix. The repository and Postman collection intentionally contain no test passwords, Firebase tokens, service-account credentials, database URLs, or private signing keys.

## Local setup

Requirements: .NET 10 SDK, PostgreSQL, and either JetBrains Rider or VS Code with C# Dev Kit.

1. Copy `.env.example` values into Rider Run Configuration environment variables (or export them in your shell).
2. In Firebase Console, create/download a service-account key. Never commit it. Set `Firebase__ServiceAccountPath` locally, or `Firebase__ServiceAccountJson` in hosting.
3. Copy the existing admin and staff Firebase UIDs into `Seed__AdminUid` and `Seed__StaffUid`; set the staff restaurant ID. The client still needs its existing Firestore `users/{uid}` role document for login routing during this backend-only phase.
4. Generate signing keys once:

   ```sh
   dotnet run --project MealSeal.Api -- --generate-keys
   ```

   Back up both files securely. Losing or changing the key invalidates issued QR vouchers. Existing vouchers can only be retained if their original private/public PEM key pair is reused.

5. Start the API:

   ```sh
   dotnet run --project MealSeal.Api
   ```

The initial EF Core migration runs automatically. Try requests with `MealSeal.Api/MealSeal.Api.http` in Rider.

Run verification with `dotnet test MealSeal.Backend.sln`.

## Free deployment: Neon + Render

1. Use the linked Neon project `MealSeal` (`muddy-feather-76535250`). Its `main` branch already contains the initial schema.
2. Push this backend repository to GitHub.
3. In Render, create a Blueprint from the repository. `render.yaml` defines a free Docker web service.
4. Enter the secret environment variables requested by Render:
   - `DATABASE_URL`: Neon's pooled URL (hostname contains `-pooler`) for normal API traffic.
   - `DATABASE_URL_UNPOOLED`: Neon's direct URL for EF Core migrations.
   - `Firebase__ServiceAccountJson`: complete Firebase service-account JSON.
   - `MealSeal__PrivateKeyPem` and `MealSeal__PublicKeyPem`: generated PEM values.
   - The three `Seed__...` user/restaurant values.
5. Confirm `https://YOUR-SERVICE.onrender.com/health` returns `status: ok`.

The API accepts Neon's native `postgresql://` URLs and converts them to Npgsql configuration internally. It also continues to accept `ConnectionStrings__Postgres` and `ConnectionStrings__PostgresMigration` in Npgsql keyword format. Never commit any of these values.

Render's free web service sleeps after inactivity, so the first request can be slow. Neon is preferable to Render's free PostgreSQL because Render's free database expires after 30 days. Do not use SQLite on Render: its filesystem is ephemeral.

Only after deployment should the separate mobile phase replace `API_BASE_URL` with the Render HTTPS URL. Never put Firebase service-account JSON or private signing keys in the mobile app.

## iOS and Android testing choices

- **Current iPhone, free and fastest**: use Expo Go while the app remains compatible with Expo Go.
- **iOS Simulator, free on this Mac**: install Xcode, then run the Expo iOS simulator workflow.
- **Local development build on iPhone**: Xcode can install a local build using a free Apple ID, with normal free-signing limitations.
- **TestFlight/App Store or EAS cloud iOS device builds**: requires Apple Developer Program membership, so it is not a zero-cost route.
- **Android without owning a phone**: install Android Studio and create a free Android emulator on the Mac. The backend itself is platform-independent.

For phone testing, the hosted HTTPS API is easiest. For a local API, the phone and Mac must share a network and the base URL must use the Mac's LAN address, not `localhost`.

## Security notes

- Firebase ID tokens are verified with revocation checking.
- Authorization is server-side (`CHARITY_ADMIN` or `RESTAURANT_STAFF`), never trusted from QR input.
- Staff can verify/redeem only vouchers for their assigned restaurant.
- Signed payloads are compared with the immutable database record.
- Request size and rate limits are enabled; errors do not return stack traces.
- Private keys and service-account credentials are excluded by `.gitignore`.
