using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace Insurance.Architecture.Tests;

// These tests keep the microservices independent: a service may use BuildingBlocks and other services'
// *.Contracts (events, internal API shapes and HTTP clients), never another service's code or database.

public class ServiceBoundaryTests
{
    private const string BuildingBlocks = "Insurance.BuildingBlocks";

    // Service assembly -> the one schema its database scripts may touch.
    private static readonly Dictionary<Assembly, string> Services = new()
    {
        [typeof(Insurance.Identity.Services.AuthService).Assembly] = "accounts",
        [typeof(Insurance.Catalog.Services.PlanCatalog).Assembly] = "catalog",
        [typeof(Insurance.Quotes.Services.QuoteService).Assembly] = "quotes",
        [typeof(Insurance.Enrollments.Services.EnrollmentService).Assembly] = "enrollments",
        [typeof(Insurance.Billing.Services.PaymentService).Assembly] = "billing",
        [typeof(Insurance.Claims.Services.ClaimService).Assembly] = "claims",
        [typeof(Insurance.Notifications.Services.NotificationDispatcher).Assembly] = "notifications",
    };

    // Services nothing else calls have no Contracts project: other services only listen to their events, or the
    // browser talks to them through the gateway.
    private static readonly HashSet<string> WithoutContracts = new() { "Insurance.Notifications" };

    private static readonly Assembly Gateway = typeof(Insurance.Gateway.Controllers.AdminCustomersController).Assembly;

    public static TheoryData<string> Deployables => new(Services.Keys.Append(Gateway).Select(a => a.GetName().Name!));
    public static TheoryData<string> ServiceNames => new(Services.Keys.Select(a => a.GetName().Name!));
    public static TheoryData<string> ServicesWithContracts =>
        new(Services.Keys.Select(a => a.GetName().Name!).Where(n => !WithoutContracts.Contains(n)));

    private static IEnumerable<string> InsuranceReferences(string assemblyName) =>
        Assembly.Load(assemblyName).GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(n => n.StartsWith("Insurance."));

    [Theory]
    [MemberData(nameof(Deployables))]
    public void Services_and_the_gateway_reference_only_building_blocks_and_contracts(string assembly)
    {
        var illegal = InsuranceReferences(assembly)
            .Where(n => n != BuildingBlocks && !n.EndsWith(".Contracts"))
            .ToList();

        Assert.True(illegal.Count == 0, $"{assembly} references another service's implementation: {string.Join(", ", illegal)}");
    }

    [Theory]
    [MemberData(nameof(ServicesWithContracts))]
    public void Contracts_reference_only_building_blocks(string service)
    {
        var contracts = service + ".Contracts";
        var illegal = InsuranceReferences(contracts).Where(n => n != BuildingBlocks).ToList();

        Assert.True(illegal.Count == 0, $"{contracts} references {string.Join(", ", illegal)}");
    }

    [Theory]
    [MemberData(nameof(ServiceNames))]
    public void Database_scripts_touch_only_the_services_own_schema(string service)
    {
        var (assembly, ownSchema) = Services.Single(s => s.Key.GetName().Name == service);
        var otherSchemas = Services.Values.Where(s => s != ownSchema).ToList();
        var reference = new Regex($@"\b({string.Join("|", otherSchemas)})\.\w", RegexOptions.IgnoreCase);

        var scripts = assembly.GetManifestResourceNames().Where(n => n.EndsWith(".sql")).ToList();
        Assert.NotEmpty(scripts);

        foreach (var name in scripts)
        {
            using var reader = new StreamReader(assembly.GetManifestResourceStream(name)!);
            var code = StripComments(reader.ReadToEnd());
            var match = reference.Match(code);
            Assert.False(match.Success, $"{name} uses another service's schema: '{match.Value}'");
        }
    }

    private static string StripComments(string sql) =>
        Regex.Replace(Regex.Replace(sql, @"/\*.*?\*/", "", RegexOptions.Singleline), @"--[^\n]*", "");
}

public class NotificationsBoundaryTests
{
    // Nothing may depend on the Notifications service: emails are a side effect, never part of a business rule.
    [Fact]
    public void No_other_service_references_the_notifications_service()
    {
        var notifications = typeof(Insurance.Notifications.Services.NotificationDispatcher).Assembly.GetName().Name;
        var others = new[]
        {
            typeof(Insurance.Gateway.Controllers.AdminCustomersController).Assembly,
            typeof(Insurance.Identity.Services.AuthService).Assembly,
            typeof(Insurance.Catalog.Services.PlanCatalog).Assembly,
            typeof(Insurance.Quotes.Services.QuoteService).Assembly,
            typeof(Insurance.Enrollments.Services.EnrollmentService).Assembly,
            typeof(Insurance.Billing.Services.PaymentService).Assembly,
            typeof(Insurance.Claims.Services.ClaimService).Assembly,
        };

        foreach (var assembly in others)
            Assert.DoesNotContain(assembly.GetReferencedAssemblies(), r => r.Name == notifications);
    }
}
