# Testing MealSeal with Postman and Swagger

## Import the collection

Import `docs/MealSeal.postman_collection.json` into Postman. Open the collection's **Variables** tab and set these current values locally:

| Variable | Value |
|---|---|
| `baseUrl` | `https://mealseal-api.onrender.com` |
| `firebaseApiKey` | Firebase Console → Project settings → General → Web API Key |
| `adminEmail` / `adminPassword` | Firebase charity-admin test account |
| `staffEmail` / `staffPassword` | Firebase restaurant-staff test account |

Never commit or share the passwords, Firebase ID tokens, service-account JSON, database URL, or signing private key. The collection deliberately contains no credentials.

## Recommended test order

1. Run **Authentication → Sign in - admin**.
2. Run **Authentication → Admin profile** and confirm `CHARITY_ADMIN`.
3. Run **Authentication → Sign in - staff**.
4. Run **Authentication → Staff profile** and confirm `RESTAURANT_STAFF` and `CAFE-01`.
5. Run **Admin vouchers → Create voucher**. Its scripts save `voucherId` and `signedVoucher` automatically.
6. Run **Staff vouchers → Verify voucher**.
7. Run **Staff vouchers → Redeem voucher** once; it should succeed.
8. Run it a second time; it should return `409 ALREADY_REDEEMED`.

Firebase ID tokens expire. Rerun the appropriate sign-in request when a protected request returns `401`.

## Swagger UI

Open `https://mealseal-api.onrender.com/swagger`.

Swagger does not collect Firebase email/password credentials. To call protected endpoints:

1. Run a Firebase sign-in request in Postman.
2. Copy the `idToken` from its JSON response.
3. In Swagger, click **Authorize** and paste only the token. Do not type `Bearer` because Swagger adds it.
4. Admin tokens can call admin voucher routes; staff tokens can call verify/redeem routes.

The public `/health` and `/public-key` routes do not require authorization.
