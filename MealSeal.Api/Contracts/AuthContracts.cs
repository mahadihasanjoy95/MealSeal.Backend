namespace MealSeal.Api.Contracts;

public sealed record LoginRequest(string Email, string Password);

public sealed record CreateUserRequest(string Email, string Password, string Role, string? RestaurantId);

public sealed record ResetPasswordRequest(string NewPassword);
