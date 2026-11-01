using Insurance.Billing.Models;

namespace Insurance.Billing.Repositories;

public interface ISavedMethodRepository
{
    Task<List<SavedPaymentMethod>> GetByUserAsync(int userId);
    Task<SavedPaymentMethod?> GetByIdAsync(int paymentMethodId);
    Task AddAsync(SavedPaymentMethod method);                    // keeps the existing entry for the same card
    Task<bool> DeleteAsync(int paymentMethodId, int userId);     // false => not found / not yours
}
