using System.Text.Json;

namespace MealSeal.Api;

public sealed class ApiException(int statusCode, string code, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}

public sealed class ApiExceptionMiddleware(RequestDelegate next, ILogger<ApiExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (ApiException exception)
        {
            if (context.Response.HasStarted) throw;
            context.Response.StatusCode = exception.StatusCode;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new
            {
                success = false,
                error = exception.Code,
                code = exception.Code,
                message = exception.Message
            }, cancellationToken: context.RequestAborted);
        }
        catch (BadHttpRequestException exception)
        {
            logger.LogWarning(exception, "Invalid API request.");
            if (context.Response.HasStarted) throw;
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new
            {
                success = false, error = "INVALID_REQUEST", code = "INVALID_REQUEST", message = "The request body is invalid."
            }, cancellationToken: context.RequestAborted);
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "Invalid JSON request.");
            if (context.Response.HasStarted) throw;
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new
            {
                success = false, error = "INVALID_JSON", code = "INVALID_JSON", message = "The request contains invalid JSON."
            }, cancellationToken: context.RequestAborted);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unhandled API error.");
            if (context.Response.HasStarted) throw;
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(new
            {
                success = false, error = "INTERNAL_ERROR", code = "INTERNAL_ERROR", message = "An unexpected server error occurred."
            }, cancellationToken: context.RequestAborted);
        }
    }
}
