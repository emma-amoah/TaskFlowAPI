using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using TaskFlowAPI.DTOs;

namespace TaskFlowAPI.Middleware;

public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;
    private readonly IHostEnvironment _env;

    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger, IHostEnvironment env)
    {
        _next = next;
        _logger = logger;
        _env = env;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            // Pass execution context onto the next middleware in the pipeline chain
            await _next(context);
        }
        catch (Exception ex)
        {
            // Log full exception stack diagnostics safely on your secure server console logs
            _logger.LogError(ex, "An unhandled execution crash occurred in the API request pipeline: {Message}", ex.Message);

            // Handle creating the structured client response payload
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";

        // Default to a generic Internal Server Error (500) status token
        var statusCode = (int)HttpStatusCode.InternalServerError;
        var message = "An unexpected error occurred on the server. Please contact support.";

        // Handle specific custom exception cases if you scale out domain validation exceptions later
        if (exception is UnauthorizedAccessException)
        {
            statusCode = (int)HttpStatusCode.Unauthorized;
            message = "Access Denied: You are not authorized to access this resource.";
        }
        else if (exception is InvalidOperationException)
        {
            statusCode = (int)HttpStatusCode.BadRequest;
            message = exception.Message;
        }

        context.Response.StatusCode = statusCode;

        var response = new ErrorResponse
        {
            StatusCode = statusCode,
            Message = message,
            // Security Constraint: Expose internal engine stack details ONLY when running in local Development mode
            Details = _env.IsDevelopment() ? exception.ToString() : "Detailed stack information is restricted in this environment.",
            Timestamp = DateTime.UtcNow
        };

        // Enforce camelCase formatting to match standard web client JSON parsing conventions
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var jsonResponse = JsonSerializer.Serialize(response, options);

        await context.Response.WriteAsync(jsonResponse);
    }
}
