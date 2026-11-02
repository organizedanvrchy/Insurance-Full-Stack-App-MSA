using System.Security.Claims;
using Insurance.Billing.DTOs;
using Insurance.Billing.Services;
using Insurance.BuildingBlocks.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Insurance.Billing.Controllers;

// The customer's saved payment methods (brand, last 4 and expiry only).
[ApiController]
[Route("api/payment-methods")]
[Authorize(Roles = nameof(UserRole.Customer))]
public class PaymentMethodsController : ControllerBase
{
    private readonly IPaymentMethodService _methods;
    public PaymentMethodsController(IPaymentMethodService methods) => _methods = methods;

    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<ActionResult<List<SavedPaymentMethodDto>>> GetMine() =>
        Ok(await _methods.GetMineAsync(CurrentUserId));

    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id) =>
        await _methods.DeleteAsync(id, CurrentUserId) ? NoContent() : NotFound();
}
