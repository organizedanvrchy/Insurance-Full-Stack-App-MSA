using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Insurance.BuildingBlocks.Exceptions;

/// <summary>
/// The one place that turns our typed exceptions into HTTP status codes.
/// Controllers just call the service; anything thrown below lands here.
/// Services derive their own exceptions (ClaimConflictException, ...) from these base types,
/// so this handler never needs to know about a service. Typed exceptions also cross service boundaries:
/// the HTTP clients turn another service's 400/403/404/409/429 back into the same exception (see ServiceClients).
/// Unknown exceptions return false, so ASP.NET's default 500 handling still applies.
/// </summary>
public class ApiExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title) = exception switch
        {
            // 400: the caller's input broke a rule
            BusinessRuleException e => (StatusCodes.Status400BadRequest, e.Message),

            // 404: it doesn't exist (or isn't yours, which we deliberately don't distinguish)
            NotFoundException e => (StatusCodes.Status404NotFound, e.Message),

            // 403: signed in, but not allowed to do this (yet), e.g. before verifying their email address
            ForbiddenException e => (StatusCodes.Status403Forbidden, e.Message),

            // 409: valid request, but it conflicts with the current state
            ConflictException e => (StatusCodes.Status409Conflict, e.Message),

            // 429: asked for the same thing too often
            TooManyRequestsException e => (StatusCodes.Status429TooManyRequests, e.Message),

            // 503: another service this request needs is down or failed. Retrying later can succeed.
            HttpRequestException => (StatusCodes.Status503ServiceUnavailable,
                "A service needed for this request is temporarily unavailable. Please try again shortly."),

            _ => (0, string.Empty)
        };

        if (status == 0) return false;      // not ours: let the default 500 handling run

        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(
            new ProblemDetails { Status = status, Title = title }, 
            cancellationToken
        );
        
        return true;
    }
}