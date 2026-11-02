using System.ComponentModel.DataAnnotations;
using Insurance.BuildingBlocks.Security;
using Insurance.Claims.Contracts;
using Insurance.Enrollments.Contracts;
using Insurance.Identity.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Insurance.Gateway.Controllers;

// BFF composition: the admin customer screen shows data owned by three services.
// Customers come from Identity; active-policy counts from Enrollments; claim counts from Claims.
// One call to each per request, whatever the number of customers.
[ApiController]
[Route("api/admin/customers")]
[Authorize(Roles = nameof(UserRole.Admin))]     // Admins only
public class AdminCustomersController : ControllerBase
{
    private readonly ICustomerDirectory _customers;
    private readonly IEnrollmentLookup _enrollments;
    private readonly IClaimStatistics _claims;

    public AdminCustomersController(ICustomerDirectory customers, IEnrollmentLookup enrollments, IClaimStatistics claims)
    {
        _customers = customers;
        _enrollments = enrollments;
        _claims = claims;
    }

    // e.g. GET /api/admin/customers?search=smith&isActive=true
    [HttpGet]
    public async Task<ActionResult<List<CustomerDto>>> GetAll([FromQuery] string? search, [FromQuery] bool? isActive)
    {
        var customers = await _customers.GetAllAsync(search, isActive);
        return Ok(await ComposeAsync(customers));
    }

    // Deactivate or reactivate. Returns the updated customer so the UI can refresh the row.
    [HttpPut("{id:int}/active")]
    [ProducesResponseType(typeof(CustomerDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CustomerDto>> SetActive(int id, SetCustomerActiveRequest request)
    {
        var customer = await _customers.SetActiveAsync(id, request.IsActive!.Value);
        return Ok((await ComposeAsync(new List<CustomerInfo> { customer })).Single());
    }

    private async Task<List<CustomerDto>> ComposeAsync(List<CustomerInfo> customers)
    {
        if (customers.Count == 0) return new List<CustomerDto>();

        var ids = customers.Select(c => c.UserId).ToList();
        var policies = _enrollments.CountActiveByUserAsync(ids);
        var claims = _claims.GetCountsByUserAsync(ids);
        await Task.WhenAll(policies, claims);   // the two services are asked in parallel

        return customers.Select(c =>
        {
            var counts = claims.Result.GetValueOrDefault(c.UserId);
            return new CustomerDto
            {
                UserId = c.UserId,
                FullName = c.FullName,
                Email = c.Email,
                Phone = c.Phone,
                IsActive = c.IsActive,
                EmailVerified = c.EmailVerified,
                CreatedAt = c.CreatedAt,
                ActivePolicies = policies.Result.GetValueOrDefault(c.UserId),
                TotalClaims = counts?.TotalClaims ?? 0,
                OpenClaims = counts?.OpenClaims ?? 0
            };
        }).ToList();
    }
}

public class CustomerDto
{
    public int UserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public bool IsActive { get; set; }
    public bool EmailVerified { get; set; }
    public DateTime CreatedAt { get; set; }
    public int ActivePolicies { get; set; }
    public int TotalClaims { get; set; }
    public int OpenClaims { get; set; }
}

public class SetCustomerActiveRequest
{
    [Required]      // nullable so a missing field is a 400
    public bool? IsActive { get; set; }
}
