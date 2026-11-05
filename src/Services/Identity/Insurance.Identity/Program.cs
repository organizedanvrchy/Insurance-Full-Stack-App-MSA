using Insurance.BuildingBlocks.Hosting;
using Insurance.BuildingBlocks.Messaging;
using Insurance.Identity.Contracts;
using Insurance.Identity.Messaging;
using Insurance.Identity.Repositories;
using Insurance.Identity.Services;

// Identity service: users, sign-in and the JWTs every other service trusts.
// Calls no other service: the gateway's BFF adds policy and claim counts to the admin customer list.
// Publishes account events (verification links, email changes) for the Notifications service to email.
var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults("Identity service");

builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<ICustomerRepository, CustomerRepository>();

builder.Services.AddScoped<ITokenService, JwtTokenService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<ICustomerService, CustomerService>();
builder.Services.AddScoped<IUserDirectory, UserDirectory>();

builder.Services.Configure<EmailVerificationOptions>(builder.Configuration.GetSection("EmailVerification"));
builder.Services.Configure<CustomerOptions>(builder.Configuration.GetSection("Customers"));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IEmailTokenRepository, EmailTokenRepository>();
builder.Services.AddScoped<IEmailVerificationService, EmailVerificationService>();
builder.Services.AddScoped<IIdentityEvents, BusIdentityEvents>();

builder.Services.AddMessaging(builder.Configuration, "identity");   // publishes only

var app = builder.Build();
await app.UseServiceDefaultsAsync();
app.Run();
