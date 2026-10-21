using DocumentProcessing.Infrastructure.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace DocumentProcessing.Api.Hosting;

public sealed class CorrelationMiddleware(RequestDelegate next, ILogger<CorrelationMiddleware> logger)
{
    public const string HeaderName = "X-Correlation-Id";
    public const string ItemKey = "CorrelationId";

    // Role: Assigns one correlation id for this HTTP request. Next: DocumentsController.Upload in DocumentsController.cs.
    public async Task Invoke(HttpContext context)
    {
        var header = context.Request.Headers[HeaderName].FirstOrDefault();
        var correlationId = Guid.TryParse(header, out var parsed) ? parsed : Guid.NewGuid();
        context.Items[ItemKey] = correlationId;
        context.Response.Headers[HeaderName] = correlationId.ToString();
        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            await next(context);
        }
    }
}

public sealed class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    // Role: Turns exceptions into HTTP 400, 404, or 409. Next: CorrelationMiddleware.Invoke in this file.
    public async Task Invoke(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception exception)
        {
            var (status, title, detail) = exception switch
            {
                DocumentValidationException => (StatusCodes.Status400BadRequest, "Validation failed", exception.Message),
                DocumentNotFoundException => (StatusCodes.Status404NotFound, "Document not found", exception.Message),
                InvalidWorkflowTransitionException => (StatusCodes.Status409Conflict, "Workflow conflict", exception.Message),
                DuplicateProcessingException => (StatusCodes.Status409Conflict, "Duplicate processing", exception.Message),
                _ => (StatusCodes.Status500InternalServerError, "Unexpected error", "An unexpected error occurred.")
            };

            if (status == StatusCodes.Status500InternalServerError)
            {
                logger.LogError(exception, "Unhandled exception {ExceptionType}", exception.GetType().Name);
            }
            else
            {
                logger.LogWarning(exception, "Request failed with {ExceptionType}", exception.GetType().Name);
            }

            context.Response.StatusCode = status;
            context.Response.ContentType = "application/problem+json";
            var problem = new ProblemDetails
            {
                Status = status,
                Title = title,
                Detail = detail
            };
            if (exception is DocumentValidationException validation && validation.Errors.Count > 0)
            {
                problem.Extensions["errors"] = validation.Errors;
            }

            await context.Response.WriteAsJsonAsync(problem);
        }
    }
}
