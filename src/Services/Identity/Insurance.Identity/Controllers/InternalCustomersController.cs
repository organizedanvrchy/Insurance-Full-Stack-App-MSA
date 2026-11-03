using Insurance.BuildingBlocks.Security;
using Insurance.Identity.Contracts;
using Insurance.Identity.DTOs;
using Insurance.Identity.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Insurance.Identity.Controllers;

// For the gateway's BFF, which serves /api/admin/customers and adds the policy and claim counts.
// The gateway never routes /internal; the admin's token is forwarded, so the role check still applies here.
[ApiController]
[Route("internal/customers")]
[Authorize(Roles = nameof(UserRole.Admin))]
public class InternalCustomersController : ControllerBase
{
    private readonly ICustomerService _customers;
    public InternalCustomersController(ICustomerService customers) => _customers = customers;

    // e.g. GET /internal/customers?search=smith&isActive=true
    [HttpGet]
    public async Task<ActionResult<List<CustomerInfo>>> GetAll([FromQuery] string? search, [FromQuery] bool? isActive) =>
        Ok(await _customers.GetAllAsync(search, isActive));

    // One customer, for the admin's customer pop-up.
    [HttpGet("{id:int}")]
    public async Task<ActionResult<CustomerInfo>> Get(int id) => Ok(await _customers.GetAsync(id));

    // Edit name, email, phone and address. 400 invalid, 404 not a customer, 409 email taken.
    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(CustomerInfo), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CustomerInfo>> Update(int id, AdminUpdateCustomerRequest request) =>
        Ok(await _customers.UpdateDetailsAsync(id, request));

    // Emails the customer a one-time password; the old password stops working at once.
    [HttpPost("{id:int}/reset-password")]
    [ProducesResponseType(typeof(CustomerInfo), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CustomerInfo>> ResetPassword(int id) => Ok(await _customers.ResetPasswordAsync(id));

    // Deactivate or reactivate. 404 if the id isn't a customer.
    [HttpPut("{id:int}/active")]
    [ProducesResponseType(typeof(CustomerInfo), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CustomerInfo>> SetActive(int id, SetCustomerActiveRequest request) =>
        Ok(await _customers.SetActiveAsync(id, request.IsActive!.Value));
}
