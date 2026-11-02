/* Billing service: "today" now means today in the business time zone (BusinessTimeZone, default America/New_York), not
   in UTC. dbo.ufn_Today() is created by DatabaseMigrator from configuration at every start. These are the same
   definitions as before with CAST(SYSUTCDATETIME() AS DATE) replaced; timestamps (CreatedAt, SubmittedAt...) stay UTC. */

CREATE OR ALTER VIEW billing.vw_PaymentDetails
AS
SELECT  PaymentId, EnrollmentId, DueDate, Amount, PaidDate,
        CASE WHEN Status = 'Pending' AND DueDate < DATEADD(DAY, -billing.ufn_OverdueGraceDays(), dbo.ufn_Today())
             THEN 'Overdue' ELSE Status END AS Status
FROM billing.PremiumPayments;
GO

CREATE OR ALTER PROCEDURE billing.usp_Payment_Settle
    @PaymentId        INT,
    @TransactionId    INT,
    @ProviderChargeId NVARCHAR(100),
    @Message          NVARCHAR(300)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @EnrollmentId INT, @DueDate DATE, @PaidDate DATE;

    BEGIN TRANSACTION;

        SELECT @EnrollmentId = EnrollmentId, @DueDate = DueDate, @PaidDate = PaidDate
        FROM billing.PremiumPayments WITH (UPDLOCK)
        WHERE PaymentId = @PaymentId;

        IF @EnrollmentId IS NULL
            THROW 50004, 'Payment not found.', 1;

        IF @PaidDate IS NOT NULL
            THROW 50005, 'This installment has already been paid.', 1;

        IF EXISTS (SELECT 1 FROM billing.PremiumPayments
                   WHERE EnrollmentId = @EnrollmentId AND PaidDate IS NULL AND DueDate < @DueDate)
            THROW 50007, 'Please pay your earlier installments first.', 1;

        UPDATE billing.PremiumPayments
        SET Status = 'Paid', PaidDate = dbo.ufn_Today()
        WHERE PaymentId = @PaymentId;

        UPDATE billing.PaymentTransactions
        SET Status = 'Succeeded', ProviderChargeId = @ProviderChargeId, Message = @Message,
            DeclineCode = NULL, CompletedAt = SYSUTCDATETIME()
        WHERE TransactionId = @TransactionId;

    COMMIT TRANSACTION;

    SELECT * FROM billing.vw_PaymentDetails WHERE PaymentId = @PaymentId;
END
GO

CREATE OR ALTER PROCEDURE billing.usp_Payment_Pay
    @PaymentId INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @EnrollmentId INT, @DueDate DATE, @PaidDate DATE;

    BEGIN TRANSACTION;

        SELECT @EnrollmentId = EnrollmentId, @DueDate = DueDate, @PaidDate = PaidDate
        FROM billing.PremiumPayments WITH (UPDLOCK)
        WHERE PaymentId = @PaymentId;

        IF @EnrollmentId IS NULL
            THROW 50004, 'Payment not found.', 1;

        IF @PaidDate IS NOT NULL
            THROW 50005, 'This installment has already been paid.', 1;

        IF EXISTS (SELECT 1 FROM billing.PremiumPayments
                   WHERE EnrollmentId = @EnrollmentId AND PaidDate IS NULL AND DueDate < @DueDate)
            THROW 50007, 'Please pay your earlier installments first.', 1;

        UPDATE billing.PremiumPayments
        SET Status = 'Paid', PaidDate = dbo.ufn_Today()
        WHERE PaymentId = @PaymentId;

    COMMIT TRANSACTION;

    SELECT * FROM billing.vw_PaymentDetails WHERE PaymentId = @PaymentId;   -- return the updated row
END
GO
