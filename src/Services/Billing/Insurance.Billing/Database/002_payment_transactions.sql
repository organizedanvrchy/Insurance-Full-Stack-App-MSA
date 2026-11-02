/* Simulated payment processing: every attempt to pay an installment, and the payment methods customers saved.

   Never edit a script that has already run (DatabaseMigrator skips it on every database where it was applied);
   change the schema with a new numbered script like this one.

   No card numbers or security codes are stored anywhere: the browser exchanges card details for a token with the
   payment provider, and this service only ever sees that token, the card brand and the last 4 digits. That is
   what keeps a real system out of most of PCI DSS scope, and it is the same here with the mock provider. */

/* One row per attempt to pay an installment, successful or not: the payment log. */
CREATE TABLE billing.PaymentTransactions (
    TransactionId     INT IDENTITY(1,1) CONSTRAINT PK_PaymentTransactions PRIMARY KEY,
    PaymentId         INT            NOT NULL,      -- the installment
    EnrollmentId      INT            NOT NULL,
    UserId            INT            NOT NULL,      -- the customer who paid
    Amount            DECIMAL(18,2)  NOT NULL,
    Currency          CHAR(3)        NOT NULL CONSTRAINT DF_PaymentTransactions_Currency DEFAULT 'USD',
    MethodType        NVARCHAR(20)   NOT NULL,      -- Card, PayPal, ApplePay, GooglePay, Venmo
    CardBrand         NVARCHAR(20)   NULL,
    CardLast4         CHAR(4)        NULL,
    Provider          NVARCHAR(30)   NOT NULL,      -- which gateway handled it ("Mock")
    ProviderChargeId  NVARCHAR(100)  NULL,          -- the gateway's reference, needed for refunds and disputes
    Status            NVARCHAR(20)   NOT NULL CONSTRAINT DF_PaymentTransactions_Status DEFAULT 'Processing',
    DeclineCode       NVARCHAR(50)   NULL,          -- machine-readable reason (card_declined, insufficient_funds...)
    Message           NVARCHAR(300)  NULL,          -- what the customer was told
    IdempotencyKey    NVARCHAR(100)  NOT NULL,      -- one per checkout attempt, chosen by the browser
    BillingName       NVARCHAR(100)  NULL,
    BillingPostalCode NVARCHAR(10)   NULL,
    CreatedAt         DATETIME2      NOT NULL CONSTRAINT DF_PaymentTransactions_CreatedAt DEFAULT SYSUTCDATETIME(),
    CompletedAt       DATETIME2      NULL,
    CONSTRAINT CK_PaymentTransactions_Status CHECK (Status IN ('Processing','Succeeded','Declined','Failed','Refunded')),
    CONSTRAINT CK_PaymentTransactions_Method CHECK (MethodType IN ('Card','PayPal','ApplePay','GooglePay','Venmo')),
    CONSTRAINT CK_PaymentTransactions_Amount CHECK (Amount > 0),
    -- The same customer sending the same key twice is a retry of the same attempt, never a second charge.
    CONSTRAINT UQ_PaymentTransactions_Idempotency UNIQUE (UserId, IdempotencyKey)
);
CREATE INDEX IX_PaymentTransactions_EnrollmentId ON billing.PaymentTransactions(EnrollmentId);
CREATE INDEX IX_PaymentTransactions_UserId ON billing.PaymentTransactions(UserId);

/* Payment methods a customer chose to keep. ProviderMethodId is the gateway's reusable reference, not a card number. */
CREATE TABLE billing.SavedPaymentMethods (
    PaymentMethodId  INT IDENTITY(1,1) CONSTRAINT PK_SavedPaymentMethods PRIMARY KEY,
    UserId           INT            NOT NULL,
    MethodType       NVARCHAR(20)   NOT NULL,
    CardBrand        NVARCHAR(20)   NULL,
    CardLast4        CHAR(4)        NULL,
    ExpMonth         TINYINT        NULL,
    ExpYear          SMALLINT       NULL,
    HolderName       NVARCHAR(100)  NULL,
    Provider         NVARCHAR(30)   NOT NULL,
    ProviderMethodId NVARCHAR(100)  NOT NULL,
    CreatedAt        DATETIME2      NOT NULL CONSTRAINT DF_SavedPaymentMethods_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT UQ_SavedPaymentMethods_ProviderMethod UNIQUE (Provider, ProviderMethodId)
);
CREATE INDEX IX_SavedPaymentMethods_UserId ON billing.SavedPaymentMethods(UserId);
GO

/* ===================== Transactions ===================== */

/* Starts an attempt, or finds the earlier one with the same key (a retry). Existing = 1 means "replay this result,
   don't charge again". The lock makes two simultaneous requests with the same key line up instead of both inserting. */
CREATE OR ALTER PROCEDURE billing.usp_Transaction_Begin
    @PaymentId         INT,
    @EnrollmentId      INT,
    @UserId            INT,
    @Amount            DECIMAL(18,2),
    @MethodType        NVARCHAR(20),
    @CardBrand         NVARCHAR(20)  = NULL,
    @CardLast4         CHAR(4)       = NULL,
    @Provider          NVARCHAR(30),
    @IdempotencyKey    NVARCHAR(100),
    @BillingName       NVARCHAR(100) = NULL,
    @BillingPostalCode NVARCHAR(10)  = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @TransactionId INT, @Existing BIT = 0;

    BEGIN TRANSACTION;

        SELECT @TransactionId = TransactionId
        FROM billing.PaymentTransactions WITH (UPDLOCK, HOLDLOCK)
        WHERE UserId = @UserId AND IdempotencyKey = @IdempotencyKey;

        IF @TransactionId IS NOT NULL
            SET @Existing = 1;
        ELSE
        BEGIN
            INSERT INTO billing.PaymentTransactions
                (PaymentId, EnrollmentId, UserId, Amount, MethodType, CardBrand, CardLast4, Provider,
                 IdempotencyKey, BillingName, BillingPostalCode)
            VALUES
                (@PaymentId, @EnrollmentId, @UserId, @Amount, @MethodType, @CardBrand, @CardLast4, @Provider,
                 @IdempotencyKey, @BillingName, @BillingPostalCode);
            SET @TransactionId = SCOPE_IDENTITY();
        END

    COMMIT TRANSACTION;

    SELECT *, @Existing AS Existing FROM billing.PaymentTransactions WHERE TransactionId = @TransactionId;
END
GO

/* Records the gateway's answer (Declined, Failed) or a refund. Success goes through usp_Payment_Settle instead. */
CREATE OR ALTER PROCEDURE billing.usp_Transaction_Complete
    @TransactionId    INT,
    @Status           NVARCHAR(20),
    @ProviderChargeId NVARCHAR(100) = NULL,
    @DeclineCode      NVARCHAR(50)  = NULL,
    @Message          NVARCHAR(300) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE billing.PaymentTransactions
    SET Status           = @Status,
        ProviderChargeId = COALESCE(@ProviderChargeId, ProviderChargeId),
        DeclineCode      = @DeclineCode,
        Message          = @Message,
        CompletedAt      = SYSUTCDATETIME()
    WHERE TransactionId = @TransactionId;

    SELECT * FROM billing.PaymentTransactions WHERE TransactionId = @TransactionId;
END
GO

/* The money was taken: mark the installment paid and the transaction Succeeded in ONE transaction, with the
   installment locked. Same checks as usp_Payment_Pay; if one fails here (another payment won a race), nothing
   is changed and the service refunds the charge.
   Error numbers: 50004 payment not found, 50005 already paid, 50007 an earlier installment is unpaid. */
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
        SET Status = 'Paid', PaidDate = CAST(SYSUTCDATETIME() AS DATE)
        WHERE PaymentId = @PaymentId;

        UPDATE billing.PaymentTransactions
        SET Status = 'Succeeded', ProviderChargeId = @ProviderChargeId, Message = @Message,
            DeclineCode = NULL, CompletedAt = SYSUTCDATETIME()
        WHERE TransactionId = @TransactionId;

    COMMIT TRANSACTION;

    SELECT * FROM billing.vw_PaymentDetails WHERE PaymentId = @PaymentId;
END
GO

CREATE OR ALTER PROCEDURE billing.usp_Transaction_GetById
    @TransactionId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT * FROM billing.PaymentTransactions WHERE TransactionId = @TransactionId;
END
GO

/* Is this a retry of an attempt we already handled? Checked first, so a retry after success replays the receipt
   instead of failing the "already paid" check. */
CREATE OR ALTER PROCEDURE billing.usp_Transaction_GetByKey
    @UserId         INT,
    @IdempotencyKey NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT * FROM billing.PaymentTransactions WHERE UserId = @UserId AND IdempotencyKey = @IdempotencyKey;
END
GO

/* The payment log. Every filter is optional (NULL = all); newest first, capped so the admin screen stays fast. */
CREATE OR ALTER PROCEDURE billing.usp_Transaction_Search
    @EnrollmentId INT          = NULL,
    @UserId       INT          = NULL,
    @Status       NVARCHAR(20) = NULL,
    @Top          INT          = 200
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP (@Top) *
    FROM billing.PaymentTransactions
    WHERE (@EnrollmentId IS NULL OR EnrollmentId = @EnrollmentId)
      AND (@UserId IS NULL OR UserId = @UserId)
      AND (@Status IS NULL OR Status = @Status)
    ORDER BY TransactionId DESC;
END
GO

/* ===================== Saved payment methods ===================== */

CREATE OR ALTER PROCEDURE billing.usp_SavedMethod_GetByUser
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT * FROM billing.SavedPaymentMethods WHERE UserId = @UserId ORDER BY CreatedAt DESC;
END
GO

CREATE OR ALTER PROCEDURE billing.usp_SavedMethod_GetById
    @PaymentMethodId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT * FROM billing.SavedPaymentMethods WHERE PaymentMethodId = @PaymentMethodId;
END
GO

/* Saving the same card twice (same brand, last 4 and expiry) keeps the existing entry. */
CREATE OR ALTER PROCEDURE billing.usp_SavedMethod_Add
    @UserId           INT,
    @MethodType       NVARCHAR(20),
    @CardBrand        NVARCHAR(20)  = NULL,
    @CardLast4        CHAR(4)       = NULL,
    @ExpMonth         TINYINT       = NULL,
    @ExpYear          SMALLINT      = NULL,
    @HolderName       NVARCHAR(100) = NULL,
    @Provider         NVARCHAR(30),
    @ProviderMethodId NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;

        IF NOT EXISTS (SELECT 1 FROM billing.SavedPaymentMethods WITH (UPDLOCK, HOLDLOCK)
                       WHERE UserId = @UserId AND MethodType = @MethodType
                         AND ISNULL(CardBrand, '') = ISNULL(@CardBrand, '') AND ISNULL(CardLast4, '') = ISNULL(@CardLast4, '')
                         AND ISNULL(ExpMonth, 0) = ISNULL(@ExpMonth, 0) AND ISNULL(ExpYear, 0) = ISNULL(@ExpYear, 0))
            INSERT INTO billing.SavedPaymentMethods
                (UserId, MethodType, CardBrand, CardLast4, ExpMonth, ExpYear, HolderName, Provider, ProviderMethodId)
            VALUES
                (@UserId, @MethodType, @CardBrand, @CardLast4, @ExpMonth, @ExpYear, @HolderName, @Provider, @ProviderMethodId);

    COMMIT TRANSACTION;
END
GO

/* Only the owner can remove it. Returns 1 if removed, 0 if not found or not theirs. */
CREATE OR ALTER PROCEDURE billing.usp_SavedMethod_Delete
    @PaymentMethodId INT,
    @UserId          INT
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM billing.SavedPaymentMethods WHERE PaymentMethodId = @PaymentMethodId AND UserId = @UserId;
    SELECT @@ROWCOUNT AS RowsDeleted;
END
GO
