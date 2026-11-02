/* Changing the sign-in email is not offered after all (the original application never had it), so the parts of
   002_email_verification.sql that supported it go: accounts.Users.PendingEmail, the token Purpose column, and
   usp_User_SetPendingEmail. Verification links are now only for confirming the address an account registered with.
   Error numbers: 50020 verification link invalid, expired, used or replaced. */

DROP PROCEDURE IF EXISTS accounts.usp_User_SetPendingEmail;
GO

-- Links that were sent for an email change can never be used now.
DELETE FROM accounts.EmailVerificationTokens WHERE Purpose <> 'Registration';
GO

ALTER TABLE accounts.EmailVerificationTokens DROP CONSTRAINT CK_EmailTokens_Purpose;
ALTER TABLE accounts.EmailVerificationTokens DROP COLUMN Purpose;
ALTER TABLE accounts.Users DROP COLUMN PendingEmail;
GO

CREATE OR ALTER PROCEDURE accounts.usp_User_GetByEmail
    @Email NVARCHAR(256)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT UserId, FullName, Email, PasswordHash, Phone, Address, Role, IsActive, CreatedAt, EmailVerifiedAt
    FROM accounts.Users
    WHERE Email = @Email;
END
GO

CREATE OR ALTER PROCEDURE accounts.usp_User_GetById
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT UserId, FullName, Email, PasswordHash, Phone, Address, Role, IsActive, CreatedAt, EmailVerifiedAt
    FROM accounts.Users
    WHERE UserId = @UserId;
END
GO

/* A new link for @Email. Older unused links stop working, so only the latest email counts. */
CREATE OR ALTER PROCEDURE accounts.usp_EmailToken_Create
    @UserId    INT,
    @Email     NVARCHAR(256),
    @TokenHash CHAR(64),
    @ExpiresAt DATETIME2
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;
        UPDATE accounts.EmailVerificationTokens
        SET UsedAt = SYSUTCDATETIME()
        WHERE UserId = @UserId AND UsedAt IS NULL;

        INSERT INTO accounts.EmailVerificationTokens (UserId, Email, TokenHash, ExpiresAt)
        VALUES (@UserId, @Email, @TokenHash, @ExpiresAt);
    COMMIT TRANSACTION;
END
GO

/* Use a link: marks the account's address verified. The token row is locked, so a link works exactly once.
   A link for an address the account no longer has (an admin edited it in the database) is refused. */
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
            THROW 50020, 'This verification link is invalid, has expired or has already been used.', 1;

        IF NOT EXISTS (SELECT 1 FROM accounts.Users WITH (UPDLOCK) WHERE UserId = @UserId AND Email = @Email)
            THROW 50020, 'This verification link is invalid, has expired or has already been used.', 1;

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
