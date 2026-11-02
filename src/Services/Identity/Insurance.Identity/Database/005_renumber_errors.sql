/* Error numbers are unique across all services (docs/ARCHITECTURE.md, section 9.3). 50020 and 50021 went to the
   Claims service's document upload and "send to reviewer" (main), so the verification-link error moves from 50020 to
   50022. Same procedure as in 003_remove_email_change.sql otherwise.
   Error numbers: 50022 verification link invalid, expired, used or replaced. */

CREATE OR ALTER PROCEDURE accounts.usp_EmailToken_Consume
    @TokenHash CHAR(64)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @TokenId INT, @UserId INT, @Email NVARCHAR(256);

    BEGIN TRANSACTION;

        SELECT @TokenId = TokenId, @UserId = UserId, @Email = Email
        FROM accounts.EmailVerificationTokens WITH (UPDLOCK)
        WHERE TokenHash = @TokenHash AND UsedAt IS NULL AND ExpiresAt > SYSUTCDATETIME();

        IF @TokenId IS NULL
            THROW 50022, 'This verification link is invalid, has expired or has already been used.', 1;

        IF NOT EXISTS (SELECT 1 FROM accounts.Users WITH (UPDLOCK) WHERE UserId = @UserId AND Email = @Email)
            THROW 50022, 'This verification link is invalid, has expired or has already been used.', 1;

        UPDATE accounts.Users
        SET EmailVerifiedAt = COALESCE(EmailVerifiedAt, SYSUTCDATETIME())
        WHERE UserId = @UserId;

        UPDATE accounts.EmailVerificationTokens SET UsedAt = SYSUTCDATETIME() WHERE TokenId = @TokenId;

    COMMIT TRANSACTION;

    SELECT UserId, FullName, Email, PasswordHash, Phone, Address, Role, IsActive, CreatedAt, EmailVerifiedAt
    FROM accounts.Users
    WHERE UserId = @UserId;
END
GO
