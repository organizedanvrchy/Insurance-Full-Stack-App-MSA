using Insurance.Billing.DTOs;
using Insurance.Billing.Gateway;
using Insurance.Billing.Repositories;

namespace Insurance.Billing.Services;

public interface IPaymentMethodService
{
    Task<List<SavedPaymentMethodDto>> GetMineAsync(int userId);
    Task<bool> DeleteAsync(int paymentMethodId, int userId);   // false => 404
}

// The customer's saved payment methods. Saving happens during checkout ("Save this card").
public class PaymentMethodService : IPaymentMethodService
{
    private readonly ISavedMethodRepository _methods;
    private readonly IPaymentGateway _gateway;

    public PaymentMethodService(ISavedMethodRepository methods, IPaymentGateway gateway)
    {
        _methods = methods;
        _gateway = gateway;
    }

    // Only methods the current processor can charge (after switching processors, old references are useless).
    public async Task<List<SavedPaymentMethodDto>> GetMineAsync(int userId) =>
        (await _methods.GetByUserAsync(userId))
            .Where(m => m.Provider == _gateway.Name)
            .Select(m => new SavedPaymentMethodDto
            {
                PaymentMethodId = m.PaymentMethodId,
                MethodType = m.MethodType,
                CardBrand = m.CardBrand,
                CardLast4 = m.CardLast4,
                ExpMonth = m.ExpMonth,
                ExpYear = m.ExpYear,
                HolderName = m.HolderName,
                CreatedAt = m.CreatedAt
            })
            .ToList();

    // Removed at the processor too, so the reference can never be charged again.
    public async Task<bool> DeleteAsync(int paymentMethodId, int userId)
    {
        var method = await _methods.GetByIdAsync(paymentMethodId);
        if (method is null || method.UserId != userId)
            return false;

        if (method.Provider == _gateway.Name)
            await _gateway.RemoveSavedAsync(method.ProviderMethodId);
        return await _methods.DeleteAsync(paymentMethodId, userId);
    }
}
