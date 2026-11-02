using System.Text.Json.Serialization;
using Insurance.BuildingBlocks.Data;
using Insurance.BuildingBlocks.Exceptions;
using Insurance.BuildingBlocks.Http;
using Insurance.BuildingBlocks.Security;
using Insurance.BuildingBlocks.Time;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;

namespace Insurance.BuildingBlocks.Hosting;

/// <summary>What every service's Program.cs starts with, so the six services behave the same way.</summary>
public static class ServiceDefaults
{
    public static WebApplicationBuilder AddServiceDefaults(this WebApplicationBuilder builder, string title)
    {
        BusinessTime.Configure(builder.Configuration);   // "today" in the insurer's time zone, not UTC

        builder.Services.AddControllers()
            .AddJsonOptions(o =>
                // Enums travel as names ("Admin"), and unknown names/numbers are rejected with a 400.
                o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false)));

        builder.Services.AddExceptionHandler<ApiExceptionHandler>();   // typed exceptions -> 400/404/409
        builder.Services.AddProblemDetails();                           // anything else -> clean 500 JSON
        builder.Services.AddHealthChecks();

        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo { Title = title, Version = "v1" });
            c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Paste the JWT only. Swagger adds the 'Bearer ' prefix."
            });
            c.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } },
                    Array.Empty<string>()
                }
            });
        });

        builder.Services.AddSingleton<IDbConnectionFactory, SqlConnectionFactory>();
        builder.Services.AddServiceClientDefaults();
        builder.Services.AddInsuranceJwt(builder.Configuration);
        return builder;
    }

    /// <summary>Creates or upgrades the service's own database, then maps the standard pipeline.</summary>
    public static async Task<WebApplication> UseServiceDefaultsAsync(this WebApplication app)
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseMigrator");
        await DatabaseMigrator.MigrateAsync(app.Configuration, System.Reflection.Assembly.GetEntryAssembly()!, logger);

        app.UseExceptionHandler();

        if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("EnableSwagger"))
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        app.MapHealthChecks("/health");
        return app;
    }
}
