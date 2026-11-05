using Insurance.Billing.Contracts;
using Insurance.BuildingBlocks.Hosting;
using Insurance.BuildingBlocks.Http;
using Insurance.BuildingBlocks.Messaging;
using Insurance.Catalog.Contracts;
using Insurance.Enrollments.Contracts;
using Insurance.Enrollments.Messaging;
using Insurance.Enrollments.Repositories;
using Insurance.Enrollments.Services;
using Insurance.Identity.Contracts;
using Insurance.Quotes.Contracts;

// Enrollments service: the policies customers hold, and the orchestrator of the enrollment saga.
// Calls Identity, Quotes, Catalog and Billing over HTTP; publishes its events through a transactional outbox.
var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults("Enrollments service");

builder.Services.Configure<EnrollmentOptions>(builder.Configuration.GetSection("Enrollment"));
builder.Services.AddScoped<IEnrollmentRepository, EnrollmentRepository>();
builder.Services.AddScoped<IEnrollmentService, EnrollmentService>();
builder.Services.AddScoped<IEnrollmentLookup, EnrollmentLookup>();

builder.Services.AddServiceClient<IQuoteReservations, QuoteReservationsClient>(builder.Configuration, "Quotes");
builder.Services.AddServiceClient<IPlanCatalog, PlanCatalogClient>(builder.Configuration, "Catalog");
builder.Services.AddServiceClient<IPremiumSummaries, PremiumSummariesClient>(builder.Configuration, "Billing");
builder.Services.AddServiceClient<IUserDirectory, IdentityClient>(builder.Configuration, "Identity");

builder.Services.AddMessaging(builder.Configuration, "enrollments", bus =>
{
    bus.AddConsumer<PremiumScheduleCreatedConsumer>();
    bus.AddConsumer<EnrollmentRequestedFaultConsumer>();
});
builder.Services.AddSqlOutbox();

var app = builder.Build();
await app.UseServiceDefaultsAsync();
app.Run();
