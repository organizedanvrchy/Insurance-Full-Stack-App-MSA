using Insurance.Billing.Contracts;
using Insurance.BuildingBlocks.Hosting;
using Insurance.BuildingBlocks.Http;
using Insurance.Catalog.Contracts;
using Insurance.Claims.Contracts;
using Insurance.Claims.Repositories;
using Insurance.Claims.Services;
using Insurance.Enrollments.Contracts;
using Insurance.Identity.Contracts;

// Claims service: filing claims and the admin review workflow.
// Checks eligibility by asking Enrollments, Billing and Catalog over HTTP; asks Identity for names.
var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults("Claims service");

builder.Services.AddScoped<IClaimRepository, ClaimRepository>();
builder.Services.AddScoped<IClaimService, ClaimService>();
builder.Services.AddScoped<IClaimStatistics, ClaimStatistics>();
builder.Services.AddScoped<IClaimDocumentRepository, ClaimDocumentRepository>();
builder.Services.AddScoped<IClaimDocumentService, ClaimDocumentService>();
builder.Services.AddSingleton<IDocumentStorage, LocalDocumentStorage>();

builder.Services.AddServiceClient<IEnrollmentLookup, EnrollmentLookupClient>(builder.Configuration, "Enrollments");
builder.Services.AddServiceClient<IPremiumSummaries, PremiumSummariesClient>(builder.Configuration, "Billing");
builder.Services.AddServiceClient<IPlanCatalog, PlanCatalogClient>(builder.Configuration, "Catalog");
builder.Services.AddServiceClient<IUserDirectory, IdentityClient>(builder.Configuration, "Identity");

var app = builder.Build();
await app.UseServiceDefaultsAsync();
app.Run();
