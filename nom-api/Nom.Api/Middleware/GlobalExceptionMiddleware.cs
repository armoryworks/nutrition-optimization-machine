using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace Nom.Api.Middleware
{
    /// <summary>
    /// Global exception handling middleware that catches unhandled exceptions
    /// and returns RFC 7807 Problem Details responses.
    /// </summary>
    public class GlobalExceptionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<GlobalExceptionMiddleware> _logger;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Unauthorized access");
                await WriteProblemDetails(context, StatusCodes.Status401Unauthorized, "Unauthorized", ex.Message);
            }
            catch (ArgumentNullException ex)
            {
                _logger.LogWarning(ex, "Bad request: argument null");
                await WriteProblemDetails(context, StatusCodes.Status400BadRequest, "Bad Request", ex.Message);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                _logger.LogWarning(ex, "Bad request: argument out of range");
                await WriteProblemDetails(context, StatusCodes.Status400BadRequest, "Bad Request", ex.Message);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Bad request: invalid argument");
                await WriteProblemDetails(context, StatusCodes.Status400BadRequest, "Bad Request", ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Bad request: invalid operation");
                await WriteProblemDetails(context, StatusCodes.Status400BadRequest, "Bad Request", ex.Message);
            }
            catch (Microsoft.EntityFrameworkCore.DbUpdateException ex)
            {
                // Constraint violations (FK/unique) are data conflicts, not server faults.
                _logger.LogError(ex, "Database update rejected");
                await WriteProblemDetails(context, StatusCodes.Status409Conflict, "Conflict",
                    "The change could not be saved because it conflicts with existing data.");
            }
            catch (KeyNotFoundException ex)
            {
                _logger.LogWarning(ex, "Resource not found");
                await WriteProblemDetails(context, StatusCodes.Status404NotFound, "Not Found", ex.Message);
            }
            catch (NotImplementedException ex)
            {
                _logger.LogWarning(ex, "Not implemented");
                await WriteProblemDetails(context, StatusCodes.Status501NotImplemented, "Not Implemented", ex.Message);
            }
            catch (NotSupportedException ex)
            {
                _logger.LogWarning(ex, "Not supported");
                await WriteProblemDetails(context, StatusCodes.Status501NotImplemented, "Not Implemented", ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An unhandled exception occurred");
                await WriteProblemDetails(context, StatusCodes.Status500InternalServerError, "Internal Server Error", "An unexpected error occurred.");
            }
        }

        private static async Task WriteProblemDetails(HttpContext context, int statusCode, string title, string detail)
        {
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/problem+json";

            var problem = new ProblemDetails
            {
                Status = statusCode,
                Title = title,
                Detail = detail,
                Instance = context.Request.Path
            };

            var json = JsonSerializer.Serialize(problem, JsonOptions);
            await context.Response.WriteAsync(json);
        }
    }
}
