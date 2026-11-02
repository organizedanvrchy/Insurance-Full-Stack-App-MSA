using Insurance.Billing.Contracts;
using Insurance.Billing.Messaging;
using Insurance.Billing.Repositories;
using Insurance.Billing.Services;
using Insurance.Billing.Services.Reminders;
using Insurance.BuildingBlocks.Hosting;
using Insurance.BuildingBlocks.Http;
using Insurance.BuildingBlocks.Messaging;
using Insurance.Enrollments.Contracts;

// Billing service: premium schedules and payments.
// Creates and removes schedules in reaction to enrollment events; asks Enrollments (HTTP) who owns a policy.
// Publishes PaymentReceived (receipts) and, from an hourly job, PaymentReminderDue, through a transactional outbox.
var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults("Billing service");

builder.Services.AddScoped<IPaymentRepository, PaymentRepository>();
builder.Services.AddScoped<IPaymentService, PaymentService>();
builder.Services.AddScoped<IPremiumSummaries, PremiumSummaries>();

builder.Services.AddServiceClient<IEnrollmentLookup, EnrollmentLookupClient>(builder.Configuration, "Enrollments");

builder.Services.AddMessaging(builder.Configuration, "billing", bus =>
{
    bus.AddConsumer<EnrollmentRequestedConsumer>();
    bus.AddConsumer<EnrollmentCancelledConsumer>();
    bus.AddConsumer<EnrollmentFailedConsumer>();
});

builder.Services.AddSqlOutbox();

builder.Services.Configure<ReminderOptions>(builder.Configuration.GetSection("Reminders"));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IPaymentReminderService, PaymentReminderService>();
builder.Services.AddHostedService<PaymentReminderJob>();

var app = builder.Build();
await app.UseServiceDefaultsAsync();
app.Run();
