using Insurance.Billing.Contracts;
using Insurance.Billing.Gateway;
using Insurance.Billing.Messaging;
using Insurance.Billing.Repositories;
using Insurance.Billing.Services;
using Insurance.BuildingBlocks.Hosting;
using Insurance.BuildingBlocks.Http;
using Insurance.BuildingBlocks.Messaging;
using Insurance.Enrollments.Contracts;
using Insurance.Identity.Contracts;

// Billing service: premium schedules and payments.
// Creates and removes schedules in reaction to enrollment events; asks Enrollments (HTTP) who owns a policy.
// Takes payments through a payment gateway (simulated), and keeps the payment log.
var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults("Billing service");

builder.Services.AddScoped<IPaymentRepository, PaymentRepository>();
builder.Services.AddScoped<ITransactionRepository, TransactionRepository>();
builder.Services.AddScoped<ISavedMethodRepository, SavedMethodRepository>();
builder.Services.AddScoped<IPaymentService, PaymentService>();
builder.Services.AddScoped<ICheckoutService, CheckoutService>();
builder.Services.AddScoped<IPaymentMethodService, PaymentMethodService>();
builder.Services.AddScoped<IPremiumSummaries, PremiumSummaries>();

// The payment processor. Only the simulation exists; a real one is a new IPaymentGateway class and a new case here.
var provider = builder.Configuration["Payments:Provider"] ?? "Mock";
switch (provider)
{
    case "Mock":
        builder.Services.Configure<MockGatewayOptions>(builder.Configuration.GetSection("Payments:Mock"));
        builder.Services.AddSingleton<IPaymentGateway, MockPaymentGateway>();   // singleton: it remembers idempotency keys
        break;
    default:
        throw new InvalidOperationException($"Unknown Payments:Provider '{provider}'.");
}

builder.Services.AddServiceClient<IEnrollmentLookup, EnrollmentLookupClient>(builder.Configuration, "Enrollments");
builder.Services.AddServiceClient<IUserDirectory, IdentityClient>(builder.Configuration, "Identity");   // names in the admin payment log

builder.Services.AddMessaging(builder.Configuration, "billing", bus =>
{
    bus.AddConsumer<EnrollmentRequestedConsumer>();
    bus.AddConsumer<EnrollmentCancelledConsumer>();
    bus.AddConsumer<EnrollmentFailedConsumer>();
});

var app = builder.Build();
await app.UseServiceDefaultsAsync();
app.Run();
