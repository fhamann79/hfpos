using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pos.Backend.Api.Core.Models;

namespace Pos.Backend.Api.WebApi.Middleware;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<ExceptionHandlingMiddleware> logger)
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
        catch (Exception exception)
        {
            await HandleExceptionAsync(context, exception, _logger);
        }
    }

    private static async Task HandleExceptionAsync(
        HttpContext context,
        Exception exception,
        ILogger<ExceptionHandlingMiddleware> logger)
    {
        var (statusCode, response) = MapException(exception);
        var errorCode = response.Error;

        logger.LogError(exception, "Unhandled exception {ErrorCode}", errorCode);

        if (context.Response.HasStarted)
        {
            return;
        }

        context.Response.Clear();
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";

        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await context.Response.WriteAsync(json);
    }

    private static (int StatusCode, ApiErrorResponse Response) MapException(Exception exception)
    {
        return exception switch
        {
            OperationalContextException operationalContextException => (
                StatusCodes.Status400BadRequest,
                CreateErrorResponse(
                    operationalContextException.ErrorCode,
                    operationalContextException.Details)),

            KeyNotFoundException keyNotFoundException => (
                StatusCodes.Status404NotFound,
                CreateErrorResponse(
                    keyNotFoundException.Message,
                    keyNotFoundException.InnerException?.Message)),

            InvalidOperationException invalidOperationException => (
                StatusCodes.Status400BadRequest,
                CreateErrorResponse(
                    invalidOperationException.Message,
                    invalidOperationException.InnerException?.Message)),

            DbUpdateException dbUpdateException when TryGetPostgresException(dbUpdateException, out var postgresException)
                && postgresException.SqlState == "23505" => (
                StatusCodes.Status409Conflict,
                CreateErrorResponse(
                    MapUniqueViolationError(postgresException.ConstraintName),
                    postgresException.Detail ?? postgresException.MessageText)),

            DbUpdateException dbUpdateException when TryGetPostgresException(dbUpdateException, out var postgresException)
                && postgresException.SqlState == "23503" => (
                StatusCodes.Status409Conflict,
                CreateErrorResponse(
                    "FOREIGN_KEY_VIOLATION",
                    postgresException.Detail ?? postgresException.MessageText)),

            DbUpdateException dbUpdateException => (
                StatusCodes.Status500InternalServerError,
                CreateErrorResponse(
                    "DB_UPDATE_ERROR",
                    dbUpdateException.InnerException?.Message ?? dbUpdateException.Message)),

            _ => (
                StatusCodes.Status500InternalServerError,
                CreateErrorResponse(
                    "INTERNAL_SERVER_ERROR",
                    exception.Message))
        };
    }

    private static ApiErrorResponse CreateErrorResponse(string error, string? details)
        => new()
        {
            Error = error,
            Details = details
        };

    private static bool TryGetPostgresException(DbUpdateException exception, out PostgresException postgresException)
    {
        postgresException = exception.InnerException as PostgresException
            ?? exception.GetBaseException() as PostgresException
            ?? null!;

        return postgresException is not null;
    }

    private static string MapUniqueViolationError(string? constraintName)
        => constraintName switch
        {
            "IX_Products_CompanyId_Barcode" => "PRODUCT_BARCODE_ALREADY_EXISTS",
            "IX_Products_CompanyId_InternalCode" => "PRODUCT_INTERNAL_CODE_ALREADY_EXISTS",
            _ => "UNIQUE_VIOLATION"
        };
}
