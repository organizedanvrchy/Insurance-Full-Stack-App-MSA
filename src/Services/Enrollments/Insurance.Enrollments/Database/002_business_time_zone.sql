/* Enrollments service: "today" now means today in the business time zone (BusinessTimeZone, default America/New_York), not
   in UTC. dbo.ufn_Today() is created by DatabaseMigrator from configuration at every start. These are the same
   definitions as before with CAST(SYSUTCDATETIME() AS DATE) replaced; timestamps (CreatedAt, SubmittedAt...) stay UTC. */

CREATE OR ALTER VIEW enrollments.vw_EnrollmentDetails
AS
SELECT  EnrollmentId, UserId, PolicyId, QuoteId, StartDate, EndDate, MonthlyPremium,
        CASE WHEN Status = 'Active' AND EndDate < dbo.ufn_Today()
             THEN 'Expired' ELSE Status END AS Status,
        CreatedAt
FROM enrollments.Enrollments;
GO

CREATE OR ALTER PROCEDURE enrollments.usp_Enrollment_CreatePending
    @UserId         INT,
    @PolicyId       INT,
    @QuoteId        INT,
    @StartDate      DATE,
    @TermMonths     INT,
    @MonthlyPremium DECIMAL(18,2)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @EnrollmentId INT;

    BEGIN TRANSACTION;

        -- UPDLOCK + HOLDLOCK: two simultaneous requests can't both pass this check.
        IF EXISTS (SELECT 1 FROM enrollments.Enrollments WITH (UPDLOCK, HOLDLOCK)
                   WHERE UserId = @UserId AND PolicyId = @PolicyId
                     AND Status IN ('Active', 'Pending') AND EndDate >= dbo.ufn_Today())
            THROW 50002, 'You already have an active enrollment in this policy.', 1;

        INSERT INTO enrollments.Enrollments (UserId, PolicyId, QuoteId, StartDate, EndDate, MonthlyPremium, Status)
        VALUES (@UserId, @PolicyId, @QuoteId, @StartDate, DATEADD(MONTH, @TermMonths, @StartDate), @MonthlyPremium, 'Pending');

        SET @EnrollmentId = SCOPE_IDENTITY();

    COMMIT TRANSACTION;

    SELECT @EnrollmentId AS EnrollmentId;
END
GO

CREATE OR ALTER PROCEDURE enrollments.usp_Enrollment_CountActiveByUser
    @UserIds NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT UserId, COUNT(*) AS ActivePolicies
    FROM enrollments.Enrollments
    WHERE UserId IN (SELECT CAST(value AS INT) FROM OPENJSON(@UserIds))
      AND Status = 'Active' AND EndDate >= dbo.ufn_Today()
    GROUP BY UserId;
END
GO

