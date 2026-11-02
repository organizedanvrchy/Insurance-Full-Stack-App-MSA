using Insurance.Billing.DTOs;
using Insurance.Billing.Enums;

namespace Insurance.Billing.Services;

public interface IPaymentService
{
    Task<List<PaymentDto>?> GetScheduleAsync(int enrollmentId, int userId, bool isAdmin);   // null => 404
    Task<List<TransactionDto>?> GetTransactionsAsync(int userId, bool isAdmin, int? enrollmentId, TransactionStatus? status);   // null => 404
    Task<TransactionDto?> GetTransactionAsync(int transactionId, int userId, bool isAdmin);   // null => 404
}
