/* Claims service: "today" now means today in the business time zone (BusinessTimeZone, default America/New_York), not
   in UTC. dbo.ufn_Today() is created by DatabaseMigrator from configuration at every start. These are the same
   definitions as before with CAST(SYSUTCDATETIME() AS DATE) replaced; timestamps (CreatedAt, SubmittedAt...) stay UTC. */

CREATE OR ALTER PROCEDURE claims.usp_Claim_Submit
    @UserId         INT,
    @EnrollmentId   INT,
    @IncidentDate   DATE,
    @Description    NVARCHAR(2000),
    @ClaimAmount    DECIMAL(18,2),
    @CoverageAmount DECIMAL(18,2)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Today DATE = dbo.ufn_Today();
    DECLARE @Used DECIMAL(18,2), @Seq BIGINT, @ClaimNumber NVARCHAR(20), @ClaimId INT, @Msg NVARCHAR(200);

    BEGIN TRANSACTION;

        -- Lock this enrollment's claims so a concurrent approval can't change "used" under us.
        SELECT @Used = ISNULL(SUM(ClaimAmount), 0)
        FROM claims.Claims WITH (UPDLOCK, HOLDLOCK)
        WHERE EnrollmentId = @EnrollmentId AND Status IN ('Approved', 'Paid');

        IF @ClaimAmount > @CoverageAmount - @Used
        BEGIN
            SET @Msg = CONCAT('The claim amount exceeds your remaining coverage of ',
                              FORMAT(@CoverageAmount - @Used, 'C', 'en-US'), '.');
            THROW 50012, @Msg, 1;
        END

        SET @Seq = NEXT VALUE FOR claims.ClaimNumberSeq;
        SET @ClaimNumber = CONCAT('CLM-', YEAR(@Today), '-', RIGHT(CONCAT('000000', @Seq), 6));

        INSERT INTO claims.Claims (ClaimNumber, EnrollmentId, UserId, IncidentDate, Description, ClaimAmount)
        VALUES (@ClaimNumber, @EnrollmentId, @UserId, @IncidentDate, @Description, @ClaimAmount);

        SET @ClaimId = SCOPE_IDENTITY();

    COMMIT TRANSACTION;

    SELECT @ClaimId AS ClaimId;
END
GO
