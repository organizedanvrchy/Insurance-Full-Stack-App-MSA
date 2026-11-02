/* Billing service database (InsuranceBilling). Run automatically by the service at startup
   (DatabaseMigrator): each script runs once, in name order, and is recorded in dbo.SchemaVersions.
   Only the Billing service reads or writes this database. */

CREATE SCHEMA billing;
GO

-- One row per monthly installment, created when Billing hears that an enrollment was requested.
CREATE TABLE billing.PremiumPayments (
    PaymentId    INT IDENTITY(1,1) CONSTRAINT PK_PremiumPayments PRIMARY KEY,
    EnrollmentId INT NOT NULL,
    DueDate      DATE NOT NULL,
    Amount       DECIMAL(18,2) NOT NULL,
    PaidDate     DATE NULL,
    Status       NVARCHAR(20) NOT NULL CONSTRAINT DF_Payments_Status DEFAULT 'Pending',
    CONSTRAINT CK_Payments_Status CHECK (Status IN ('Pending','Paid','Overdue'))
);
CREATE INDEX IX_Payments_EnrollmentId ON billing.PremiumPayments(EnrollmentId);
GO

/* Overdue is derived on read, so no job ever has to update it. An installment is Overdue only once
   it is MORE than 5 days past its due date. (002_notifications.sql moves the 5 into billing.ufn_OverdueGraceDays.) */
CREATE OR ALTER VIEW billing.vw_PaymentDetails
AS
SELECT  PaymentId, EnrollmentId, DueDate, Amount, PaidDate,
        CASE WHEN Status = 'Pending' AND DueDate < DATEADD(DAY, -5, CAST(SYSUTCDATETIME() AS DATE))
             THEN 'Overdue' ELSE Status END AS Status
FROM billing.PremiumPayments;
GO

/* One premium row per month. Counting 0..Term-1 and adding N months to the START date
   (not to the previous due date) keeps end-of-month starts correct: Jan 31, Feb 28, Mar 31... */
CREATE OR ALTER PROCEDURE billing.usp_Premium_CreateSchedule
    @EnrollmentId   INT,
    @StartDate      DATE,
    @TermMonths     INT,
    @MonthlyPremium DECIMAL(18,2)
AS
BEGIN
    SET NOCOUNT ON;

    -- Messages can be delivered more than once: a second delivery finds the schedule and does nothing.
    IF EXISTS (SELECT 1 FROM billing.PremiumPayments WHERE EnrollmentId = @EnrollmentId)
        RETURN;

    WITH Months AS (
        SELECT 0 AS N
        UNION ALL
        SELECT N + 1 FROM Months WHERE N + 1 < @TermMonths
    )
    INSERT INTO billing.PremiumPayments (EnrollmentId, DueDate, Amount)
    SELECT @EnrollmentId, DATEADD(MONTH, N, @StartDate), @MonthlyPremium
    FROM Months
    OPTION (MAXRECURSION 600);      -- default limit is 100; our max term is 600 months
END
GO

/* Cancelling a policy (or undoing a failed enrollment): unpaid installments go, paid ones stay as history. */
CREATE OR ALTER PROCEDURE billing.usp_Premium_RemoveUnpaid
    @EnrollmentId INT
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM billing.PremiumPayments
    WHERE EnrollmentId = @EnrollmentId AND PaidDate IS NULL;
END
GO

/* Per-enrollment totals for the policy cards and the claim rules. Overdue comes from the view above,
   the single source of truth (including the grace period). @EnrollmentIds is a JSON array. */
CREATE OR ALTER PROCEDURE billing.usp_Premium_GetSummaries
    @EnrollmentIds NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT  ids.EnrollmentId,
            (SELECT COUNT(*) FROM billing.PremiumPayments pp WHERE pp.EnrollmentId = ids.EnrollmentId AND pp.Status = 'Paid') AS PaymentsPaid,
            (SELECT COUNT(*) FROM billing.PremiumPayments pp WHERE pp.EnrollmentId = ids.EnrollmentId)                       AS PaymentsTotal,
            (SELECT COUNT(*) FROM billing.vw_PaymentDetails pd WHERE pd.EnrollmentId = ids.EnrollmentId AND pd.Status = 'Overdue') AS OverdueCount,
            nxt.DueDate AS NextDueDate,        -- earliest unpaid installment (NULL if none left)
            nxt.Amount  AS NextDueAmount
    FROM (SELECT DISTINCT CAST(value AS INT) AS EnrollmentId FROM OPENJSON(@EnrollmentIds)) ids
    OUTER APPLY (
        SELECT TOP (1) pp.DueDate, pp.Amount
        FROM billing.PremiumPayments pp
        WHERE pp.EnrollmentId = ids.EnrollmentId AND pp.PaidDate IS NULL
        ORDER BY pp.DueDate
    ) nxt;
END
GO

CREATE OR ALTER PROCEDURE billing.usp_Payment_GetByEnrollment
    @EnrollmentId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT * FROM billing.vw_PaymentDetails
    WHERE EnrollmentId = @EnrollmentId
    ORDER BY DueDate;
END
GO

CREATE OR ALTER PROCEDURE billing.usp_Payment_GetById
    @PaymentId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT * FROM billing.vw_PaymentDetails
    WHERE PaymentId = @PaymentId;
END
GO

/* Pay one installment. The checks and the update happen in one transaction, with the row locked,
   so two simultaneous "Pay" clicks can't both succeed.

   Error numbers used by this proc:
     50004  payment not found
     50005  already paid
     50007  an earlier installment is still unpaid
   (Ownership, also reported as 50004's "not found", and 50006 "enrollment not active" are checked by the Billing service, which asks the Enrollments service.) */
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
        SET Status = 'Paid', PaidDate = CAST(SYSUTCDATETIME() AS DATE)
        WHERE PaymentId = @PaymentId;

    COMMIT TRANSACTION;

    SELECT * FROM billing.vw_PaymentDetails WHERE PaymentId = @PaymentId;   -- return the updated row
END
GO
