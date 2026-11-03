using System.Security.Claims;
using System.Text.Json.Serialization;
using Insurance.BuildingBlocks.Exceptions;
using Insurance.BuildingBlocks.Http;
using Insurance.BuildingBlocks.Security;
using Insurance.Claims.Contracts;
using Insurance.Enrollments.Contracts;
using Insurance.Identity.Contracts;

// API gateway + BFF. The Angular app talks only to this; the services are not reachable from outside.
//   Routing:     /api/* is proxied to the owning service (YARP, see "ReverseProxy" in appsettings.json).
//   JWT:         every token is validated here, and the account must still exist, be active and have the same role,
//                so deactivating a customer takes effect on their next request. Services validate the token again.
//   Composition: /api/admin/customers combines Identity, Enrollments and Claims (Controllers/AdminCustomersController).
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false)));
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();

builder.Services.AddServiceClient<IUserDirectory, IdentityClient>(builder.Configuration, "Identity");
builder.Services.AddServiceClient<ICustomerDirectory, IdentityClient>(builder.Configuration, "Identity");
builder.Services.AddServiceClient<IEnrollmentLookup, EnrollmentLookupClient>(builder.Configuration, "Enrollments");
builder.Services.AddServiceClient<IClaimStatistics, ClaimStatisticsClient>(builder.Configuration, "Claims");

builder.Services.AddInsuranceJwt(builder.Configuration, events => events.OnTokenValidated = async ctx =>
{
    var idClaim = ctx.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
    if (!int.TryParse(idClaim, out var userId))
    {
        ctx.Fail("Invalid token.");
        return;
    }

    // One call to the Identity service per request (the caller's own token is forwarded with it).
    var users = ctx.HttpContext.RequestServices.GetRequiredService<IUserDirectory>();
    var user = await users.GetAsync(userId);

    if (user is null || !user.IsActive)
        ctx.Fail("Account is inactive.");
    else if (!ctx.Principal!.IsInRole(user.Role.ToString()))
        ctx.Fail("Role has changed. Please sign in again.");
    else
        ctx.HttpContext.Items[nameof(UserAccount)] = user;   // for the password-reset lock below
});

builder.Services.AddReverseProxy().LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// CORS: the frontend origins allowed to call the API
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
builder.Services.AddCors(o => o.AddPolicy("Angular", p => p.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

app.UseExceptionHandler();
app.UseCors("Angular");
app.UseAuthentication();

// A request that carries a token must carry a valid one for an active account: 401 otherwise, even on routes
// that don't need a token. Signing in, registering and email links are the exception, so a stale token can't block them.
app.Use(async (context, next) =>
{
    var path = context.Request.Path;
    var isSignIn = path.StartsWithSegments("/api/auth/login") || path.StartsWithSegments("/api/auth/register")
                   || path.StartsWithSegments("/api/auth/verify-email");   // the emailed link works signed in or not

    if (!isSignIn && context.Request.Headers.Authorization.Count > 0 && context.User.Identity?.IsAuthenticated != true)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return;
    }
    await next();
});

// After an admin reset, the customer must choose a new password before doing anything else: until then only "who am I"
// and "change password" get through. Checked against the account on every request, so it also locks sessions that
// were already signed in when the reset happened.
app.Use(async (context, next) =>
{
    var path = context.Request.Path;
    if (context.Items[nameof(UserAccount)] is UserAccount { MustChangePassword: true }
        && !path.StartsWithSegments("/api/auth/me") && !path.StartsWithSegments("/api/auth/change-password"))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new Microsoft.AspNetCore.Mvc.ProblemDetails
        {
            Status = 403,
            Title = "Your password was reset. Please choose a new password to continue."
        });
        return;
    }
    await next();
});

app.UseAuthorization();
app.MapControllers();
app.MapReverseProxy();
app.MapHealthChecks("/health");
app.Run();
