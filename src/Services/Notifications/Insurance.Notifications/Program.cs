using Insurance.BuildingBlocks.Hosting;
using Insurance.BuildingBlocks.Http;
using Insurance.BuildingBlocks.Messaging;
using Insurance.Catalog.Contracts;
using Insurance.Enrollments.Contracts;
using Insurance.Identity.Contracts;
using Insurance.Notifications.Email;
using Insurance.Notifications.Messaging;
using Insurance.Notifications.Repositories;
using Insurance.Notifications.Services;
using Insurance.Notifications.Templates;

// Notifications service: emails customers about what happens to their account, policies, payments and claims.
// It only LISTENS (RabbitMQ): no other service waits for an email or knows this service exists. It asks Identity
// (recipient), Enrollments and Catalog (plan names, dates) over HTTP with its own service token, and owns the
// customers' email preferences and a record of every email.
var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults("Notifications service");

builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection("Email"));
if (string.IsNullOrWhiteSpace(builder.Configuration["Email:Host"]))
    builder.Services.AddSingleton<IEmailSender, LoggingEmailSender>();
else
    builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();

builder.Services.AddSingleton<AppLinks>();
builder.Services.AddSingleton<EmailTemplates>();
builder.Services.AddScoped<INotificationRepository, NotificationRepository>();
builder.Services.AddScoped<INotificationDispatcher, NotificationDispatcher>();
builder.Services.AddScoped<IPolicyDetails, PolicyDetails>();

builder.Services.AddServiceClient<IUserDirectory, IdentityClient>(builder.Configuration, "Identity");
builder.Services.AddServiceClient<IEnrollmentLookup, EnrollmentLookupClient>(builder.Configuration, "Enrollments");
builder.Services.AddServiceClient<IPlanCatalog, PlanCatalogClient>(builder.Configuration, "Catalog");

builder.Services.AddMessaging(builder.Configuration, "notifications", bus =>
{
    bus.AddConsumer<CustomerRegisteredConsumer>();
    bus.AddConsumer<EmailVerificationRequestedConsumer>();
    bus.AddConsumer<EnrollmentActivatedConsumer>();
    bus.AddConsumer<EnrollmentCancelledConsumer>();
    bus.AddConsumer<ClaimSubmittedConsumer>();
    bus.AddConsumer<ClaimStatusChangedConsumer>();
    bus.AddConsumer<PaymentReceivedConsumer>();
    bus.AddConsumer<PaymentDeclinedConsumer>();
    bus.AddConsumer<PaymentReminderDueConsumer>();
});

var app = builder.Build();
await app.UseServiceDefaultsAsync();
app.Run();
