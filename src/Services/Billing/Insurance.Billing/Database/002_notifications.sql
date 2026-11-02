/* Billing service: receipts and payment reminders.
   - dbo.OutboxMessages: PaymentReceived and PaymentReminderDue are published exactly when their change commits.
   - billing.ufn_OverdueGraceDays: the grace period, now in ONE place, used by the view and the reminder query.
   - billing.PaymentReminders: which reminder stages were already sent for which installment, so the reminder job
     (Services/Reminders) never sends the same reminder twice, even when several Billing instances run it. */

CREATE TABLE dbo.OutboxMessages (
    Id          BIGINT IDENTITY(1,1) CONSTRAINT PK_OutboxMessages PRIMARY KEY,
    MessageType NVARCHAR(300) NOT NULL,
    Payload     NVARCHAR(MAX) NOT NULL,
    OccurredAt  DATETIME2 NOT NULL CONSTRAINT DF_OutboxMessages_OccurredAt DEFAULT SYSUTCDATETIME(),
    PublishedAt DATETIME2 NULL
);
CREATE INDEX IX_OutboxMessages_Unpublished ON dbo.OutboxMessages(Id) WHERE PublishedAt IS NULL;
GO

/* An installment is Overdue once it is MORE than this many days past its due date.
   To change the grace period, change the 5 here and nowhere else. */
CREATE OR ALTER FUNCTION billing.ufn_OverdueGraceDays()
RETURNS INT
WITH SCHEMABINDING
AS
BEGIN
    RETURN 5;
END
GO

CREATE OR ALTER VIEW billing.vw_PaymentDetails
AS
SELECT  PaymentId, EnrollmentId, DueDate, Amount, PaidDate,
        CASE WHEN Status = 'Pending' AND DueDate < DATEADD(DAY, -billing.ufn_OverdueGraceDays(), CAST(SYSUTCDATETIME() AS DATE))
             THEN 'Overdue' ELSE Status END AS Status
FROM billing.PremiumPayments;
GO

CREATE TABLE billing.PaymentReminders (
    PaymentId INT          NOT NULL CONSTRAINT FK_PaymentReminders_Payments
                           REFERENCES billing.PremiumPayments(PaymentId) ON DELETE CASCADE,
    Stage     NVARCHAR(30) NOT NULL,
    SentAt    DATETIME2    NOT NULL CONSTRAINT DF_PaymentReminders_SentAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_PaymentReminders PRIMARY KEY (PaymentId, Stage),
    CONSTRAINT CK_PaymentReminders_Stage CHECK (Stage IN ('DueSoon','DueToday','Overdue','OverdueSecondNotice','OverdueFinalNotice'))
);
GO

CREATE INDEX IX_Payments_Unpaid_DueDate ON billing.PremiumPayments(DueDate) INCLUDE (EnrollmentId, Amount) WHERE PaidDate IS NULL;
GO

/* Unpaid installments due between @FromDate and @ToDate, with the first day each one counts as Overdue
   and the reminder stages already sent for it (comma-separated). The job decides what to send. */
CREATE OR ALTER PROCEDURE billing.usp_Reminder_GetCandidates
    @FromDate DATE,
    @ToDate   DATE
AS
BEGIN
    SET NOCOUNT ON;
    SELECT  pp.PaymentId, pp.EnrollmentId, pp.DueDate, pp.Amount,
            DATEADD(DAY, billing.ufn_OverdueGraceDays() + 1, pp.DueDate) AS OverdueFrom,
            (SELECT STRING_AGG(r.Stage, ',') FROM billing.PaymentReminders r WHERE r.PaymentId = pp.PaymentId) AS SentStages
    FROM billing.PremiumPayments pp
    WHERE pp.PaidDate IS NULL
      AND pp.DueDate BETWEEN @FromDate AND @ToDate
    ORDER BY pp.DueDate, pp.PaymentId;
END
GO

/* Claims a reminder: 1 if this call recorded it (so the caller publishes it), 0 if it was already recorded
   or the installment was paid in the meantime. The caller writes the event to the outbox in the same transaction. */
CREATE OR ALTER PROCEDURE billing.usp_Reminder_Record
    @PaymentId INT,
    @Stage     NVARCHAR(30)
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO billing.PaymentReminders (PaymentId, Stage)
    SELECT @PaymentId, @Stage
    FROM billing.PremiumPayments WITH (UPDLOCK)
    WHERE PaymentId = @PaymentId AND PaidDate IS NULL
      AND NOT EXISTS (SELECT 1 FROM billing.PaymentReminders WITH (UPDLOCK, HOLDLOCK)
                      WHERE PaymentId = @PaymentId AND Stage = @Stage);

    SELECT @@ROWCOUNT AS Recorded;
END
GO

/* The facts for a payment receipt: where this installment sits in its schedule and what is due next. */
CREATE OR ALTER PROCEDURE billing.usp_Payment_GetReceiptInfo
    @PaymentId INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @EnrollmentId INT, @DueDate DATE;
    SELECT @EnrollmentId = EnrollmentId, @DueDate = DueDate FROM billing.PremiumPayments WHERE PaymentId = @PaymentId;

    SELECT  (SELECT COUNT(*) FROM billing.PremiumPayments
             WHERE EnrollmentId = @EnrollmentId AND (DueDate < @DueDate OR (DueDate = @DueDate AND PaymentId <= @PaymentId))) AS InstallmentNumber,
            (SELECT COUNT(*) FROM billing.PremiumPayments WHERE EnrollmentId = @EnrollmentId) AS InstallmentCount,
            nxt.DueDate AS NextDueDate,
            nxt.Amount  AS NextDueAmount
    FROM (SELECT 1 AS One) x
    OUTER APPLY (
        SELECT TOP (1) DueDate, Amount
        FROM billing.PremiumPayments
        WHERE EnrollmentId = @EnrollmentId AND PaidDate IS NULL
        ORDER BY DueDate
    ) nxt;
END
GO
