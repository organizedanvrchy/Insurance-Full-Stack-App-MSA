using Insurance.Billing.DTOs;
using Insurance.Billing.Exceptions;
using Insurance.Billing.Models;
using Insurance.Billing.Repositories;
using Insurance.Enrollments.Contracts;

namespace Insurance.Billing.Services;

public class PaymentService : IPaymentService
{
    private readonly IPaymentRepository _payments;
    private readonly IEnrollmentLookup _enrollments;

    public PaymentService(IPaymentRepository payments, IEnrollmentLookup enrollments)
    {
        _payments = payments;
        _enrollments = enrollments;
    }

    public async Task<List<PaymentDto>?> GetScheduleAsync(int enrollmentId, int userId, bool isAdmin)
    {
        var enrollment = await _enrollments.GetAsync(enrollmentId);

        // Same null for "doesn't exist" and "not yours".
        if (enrollment is null || (enrollment.UserId != userId && !isAdmin))
            return null;

        var payments = await _payments.GetByEnrollmentAsync(enrollmentId);
        return payments.Where(p => IsStillDue(p, enrollment)).Select(MapToDto).ToList();
    }

    // Ownership and the enrollment's status come from the Enrollments service (HTTP). "Already paid" and
    // "pay in order" are enforced again inside usp_Payment_Pay, with the row locked, against double clicks.
    public async Task<PaymentDto> PayAsync(int paymentId, int userId)
    {
        var payment = await _payments.GetByIdAsync(paymentId);
        var enrollment = payment is null ? null : await _enrollments.GetAsync(payment.EnrollmentId);

        // Same answer for "doesn't exist" and "not yours", so ids can't be probed.
        if (payment is null || enrollment is null || enrollment.UserId != userId || !IsStillDue(payment, enrollment))
            throw new PaymentNotFoundException(paymentId);

        if (payment.PaidDate is not null)
            throw new PaymentConflictException("This installment has already been paid.");

        // An Active policy past its end date reads as Expired; its last installments can still be paid.
        if (enrollment.Status is not (EnrollmentStatus.Active or EnrollmentStatus.Expired))
            throw new PaymentConflictException("Payments can only be made on an active enrollment.");

        // The repository saves the PaymentReceived event with the payment; the customer gets an emailed receipt.
        return MapToDto(await _payments.PayAsync(paymentId, enrollment.UserId, enrollment.PolicyId));
    }

    // Cancelling removes the unpaid installments, but that happens when this service receives EnrollmentCancelled,
    // a moment after the cancellation itself. Until then they are treated as already gone, so the customer sees
    // the same thing either way. Paid installments stay as history.
    private static bool IsStillDue(Payment p, EnrollmentInfo enrollment) =>
        p.PaidDate is not null || enrollment.Status != EnrollmentStatus.Cancelled;

    private static PaymentDto MapToDto(Payment p) => new()
    {
        PaymentId = p.PaymentId,
        EnrollmentId = p.EnrollmentId,
        DueDate = p.DueDate,
        Amount = p.Amount,
        PaidDate = p.PaidDate,
        Status = p.Status
    };
}