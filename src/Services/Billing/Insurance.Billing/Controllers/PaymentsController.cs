using System.Security.Claims;
using Insurance.Billing.DTOs;
using Insurance.Billing.Enums;
using Insurance.Billing.Services;
using Insurance.BuildingBlocks.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Insurance.Billing.Controllers;

[ApiController]
[Route("api")]
[Authorize]
public class PaymentsController : ControllerBase
{
    private readonly IPaymentService _payments;
    private readonly ICheckoutService _checkout;

    public PaymentsController(IPaymentService payments, ICheckoutService checkout)
    {
        _payments = payments;
        _checkout = checkout;
    }

    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private bool IsAdmin => User.IsInRole(nameof(UserRole.Admin));

    [HttpGet("enrollments/{enrollmentId:int}/payments")]
    [ProducesResponseType(typeof(List<PaymentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<PaymentDto>>> GetSchedule(int enrollmentId)
    {
        var schedule = await _payments.GetScheduleAsync(enrollmentId, CurrentUserId, IsAdmin);

        if (schedule is null) return NotFound();
        return Ok(schedule);
    }

    // The checkout page's "Pay" button. 200 with Outcome = Succeeded (a receipt) or Declined (why).
    // Errors (400 bad request/unreadable method, 404 not found/not yours,
    // 409 already paid/out of order/inactive) are translated by ApiExceptionHandler.
    [HttpPost("payments/{id:int}/pay")]
    [Authorize(Roles = nameof(UserRole.Customer))]
    [ProducesResponseType(typeof(CheckoutResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CheckoutResultDto>> Pay(int id, CheckoutRequest request) =>
        Ok(await _checkout.PayAsync(id, CurrentUserId, request));

    // The payment log, e.g. GET /api/payments/transactions?enrollmentId=12 (one policy, owner or admin),
    // or GET /api/payments/transactions?status=Declined (a customer's own attempts; an admin sees everyone's).
    [HttpGet("payments/transactions")]
    [ProducesResponseType(typeof(List<TransactionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<TransactionDto>>> GetTransactions([FromQuery] int? enrollmentId, [FromQuery] TransactionStatus? status)
    {
        var list = await _payments.GetTransactionsAsync(CurrentUserId, IsAdmin, enrollmentId, status);
        if (list is null) return NotFound();
        return Ok(list);
    }

    // A receipt.
    [HttpGet("payments/transactions/{transactionId:int}")]
    [ProducesResponseType(typeof(TransactionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TransactionDto>> GetTransaction(int transactionId)
    {
        var dto = await _payments.GetTransactionAsync(transactionId, CurrentUserId, IsAdmin);
        if (dto is null) return NotFound();
        return Ok(dto);
    }
}
