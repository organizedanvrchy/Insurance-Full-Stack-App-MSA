/* Email verification (Identity service).
   - accounts.Users gains EmailVerifiedAt (NULL = not verified yet) and PendingEmail (a requested, unconfirmed change).
   - accounts.EmailVerificationTokens holds one row per emailed link. Only a SHA-256 HASH of the token is stored, so a
     copy of this table can't be used to verify anyone. Each token is single-use, expires, and is replaced by a newer one.
   Error numbers:
     50001  email already registered (reused from usp_User_Register)
     50020  verification link invalid, expired, already used, or superseded */

ALTER TABLE accounts.Users ADD
    EmailVerifiedAt DATETIME2     NULL,
    PendingEmail    NVARCHAR(256) NULL;
GO

-- Accounts that existed before verification was introduced are treated as verified,
-- so nobody already using the system is suddenly blocked from enrolling or filing claims.
UPDATE accounts.Users SET EmailVerifiedAt = CreatedAt WHERE EmailVerifiedAt IS NULL;
GO

CREATE TABLE accounts.EmailVerificationTokens (
    TokenId   INT IDENTITY(1,1) CONSTRAINT PK_EmailVerificationTokens PRIMARY KEY,
    UserId    INT           NOT NULL CONSTRAINT FK_EmailTokens_Users REFERENCES accounts.Users(UserId) ON DELETE CASCADE,
    Email     NVARCHAR(256) NOT NULL,          -- the address this link proves the user owns
    Purpose   NVARCHAR(20)  NOT NULL,
    TokenHash CHAR(64)      NOT NULL,          -- hex SHA-256 of the token in the link
    CreatedAt DATETIME2     NOT NULL CONSTRAINT DF_EmailTokens_CreatedAt DEFAULT SYSUTCDATETIME(),
    ExpiresAt DATETIME2     NOT NULL,
    UsedAt    DATETIME2     NULL,              -- set when used, or when a newer link replaces this one
    CONSTRAINT UQ_EmailTokens_Hash UNIQUE (TokenHash),
    CONSTRAINT CK_EmailTokens_Purpose CHECK (Purpose IN ('Registration','EmailChange'))
);
CREATE INDEX IX_EmailTokens_UserId ON accounts.EmailVerificationTokens(UserId, CreatedAt);
GO

/* The user procs now also return the verification state. */
CREATE OR ALTER PROCEDURE accounts.usp_User_GetByEmail
    @Email NVARCHAR(256)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT UserId, FullName, Email, PasswordHash, Phone, Address, Role, IsActive, CreatedAt, EmailVerifiedAt, PendingEmail
    FROM accounts.Users
    WHERE Email = @Email;
END
GO

CREATE OR ALTER PROCEDURE accounts.usp_User_GetById
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT UserId, FullName, Email, PasswordHash, Phone, Address, Role, IsActive, CreatedAt, EmailVerifiedAt, PendingEmail
    FROM accounts.Users
    WHERE UserId = @UserId;
END
GO

CREATE OR ALTER PROCEDURE accounts.usp_User_GetMany
    @UserIds NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT UserId, FullName, Email, Role, IsActive,
           CAST(CASE WHEN EmailVerifiedAt IS NULL THEN 0 ELSE 1 END AS BIT) AS EmailVerified
    FROM accounts.Users
    WHERE UserId IN (SELECT CAST(value AS INT) FROM OPENJSON(@UserIds));
END
GO

CREATE OR ALTER PROCEDURE accounts.usp_Customer_GetAll
    @Search   NVARCHAR(100) = NULL,
    @IsActive BIT           = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT UserId, FullName, Email, Phone, IsActive, CreatedAt,
           CAST(CASE WHEN EmailVerifiedAt IS NULL THEN 0 ELSE 1 END AS BIT) AS EmailVerified
    FROM accounts.Users
    WHERE Role = 'Customer'
      AND (@IsActive IS NULL OR IsActive = @IsActive)
      AND (@Search IS NULL OR FullName LIKE '%' + @Search + '%' OR Email LIKE '%' + @Search + '%')
    ORDER BY FullName;
END
GO

CREATE OR ALTER PROCEDURE accounts.usp_Customer_SetActive
    @UserId   INT,
    @IsActive BIT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE accounts.Users
    SET IsActive = @IsActive
    WHERE UserId = @UserId AND Role = 'Customer';

    SELECT UserId, FullName, Email, Phone, IsActive, CreatedAt,
           CAST(CASE WHEN EmailVerifiedAt IS NULL THEN 0 ELSE 1 END AS BIT) AS EmailVerified
    FROM accounts.Users
    WHERE UserId = @UserId AND Role = 'Customer';
END
GO

/* A new link for @Email. Older unused links of the same purpose stop working, so only the latest email counts. */
CREATE OR ALTER PROCEDURE accounts.usp_EmailToken_Create
    @UserId    INT,
    @Email     NVARCHAR(256),
    @Purpose   NVARCHAR(20),
    @TokenHash CHAR(64),
    @ExpiresAt DATETIME2
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;
        UPDATE accounts.EmailVerificationTokens
        SET UsedAt = SYSUTCDATETIME()
        WHERE UserId = @UserId AND Purpose = @Purpose AND UsedAt IS NULL;

        INSERT INTO accounts.EmailVerificationTokens (UserId, Email, Purpose, TokenHash, ExpiresAt)
        VALUES (@UserId, @Email, @Purpose, @TokenHash, @ExpiresAt);
    COMMIT TRANSACTION;
END
GO

/* For the resend limit: when the last link was sent, and how many in the last 24 hours. */
CREATE OR ALTER PROCEDURE accounts.usp_EmailToken_GetRecent
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT MAX(CreatedAt) AS LastCreatedAt,
           COUNT(*)       AS CountLastDay
    FROM accounts.EmailVerificationTokens
    WHERE UserId = @UserId AND CreatedAt > DATEADD(HOUR, -24, SYSUTCDATETIME());
END
GO

/* Use a link. Registration: marks the address verified. EmailChange: the pending address becomes the sign-in email.
   The checks and the update run in one transaction with the rows locked, so a link works exactly once.
   Returns the purpose, the address before the change, and the user row as it is now. */
CREATE OR ALTER PROCEDURE accounts.usp_EmailToken_Consume
    @TokenHash CHAR(64)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @TokenId INT, @UserId INT, @Email NVARCHAR(256), @Purpose NVARCHAR(20),
            @CurrentEmail NVARCHAR(256), @PendingEmail NVARCHAR(256);

    BEGIN TRANSACTION;

        SELECT @TokenId = TokenId, @UserId = UserId, @Email = Email, @Purpose = Purpose
        FROM accounts.EmailVerificationTokens WITH (UPDLOCK)
        WHERE TokenHash = @TokenHash AND UsedAt IS NULL AND ExpiresAt > SYSUTCDATETIME();

        IF @TokenId IS NULL
            THROW 50020, 'This verification link is invalid, has expired or has already been used.', 1;

        SELECT @CurrentEmail = Email, @PendingEmail = PendingEmail
        FROM accounts.Users WITH (UPDLOCK)
        WHERE UserId = @UserId;

        IF @Purpose = 'EmailChange'
        BEGIN
            -- The change may have been cancelled or replaced since this link was sent.
            IF @PendingEmail IS NULL OR @PendingEmail <> @Email
                THROW 50020, 'This verification link is invalid, has expired or has already been used.', 1;

            -- Someone may have registered with this address in the meantime.
            IF EXISTS (SELECT 1 FROM accounts.Users WHERE Email = @Email AND UserId <> @UserId)
                THROW 50001, 'Email is already registered.', 1;

            UPDATE accounts.Users
            SET Email = @Email, PendingEmail = NULL, EmailVerifiedAt = SYSUTCDATETIME()
            WHERE UserId = @UserId;
        END
        ELSE
        BEGIN
            IF @CurrentEmail <> @Email
                THROW 50020, 'This verification link is invalid, has expired or has already been used.', 1;

            UPDATE accounts.Users
            SET EmailVerifiedAt = COALESCE(EmailVerifiedAt, SYSUTCDATETIME())
            WHERE UserId = @UserId;
        END

        UPDATE accounts.EmailVerificationTokens SET UsedAt = SYSUTCDATETIME() WHERE TokenId = @TokenId;

    COMMIT TRANSACTION;

    SELECT @Purpose AS Purpose, @CurrentEmail AS PreviousEmail,
           UserId, FullName, Email, PasswordHash, Phone, Address, Role, IsActive, CreatedAt, EmailVerifiedAt, PendingEmail
    FROM accounts.Users
    WHERE UserId = @UserId;
END
GO

/* Start an email change: the new address waits in PendingEmail until its link is used. */
CREATE OR ALTER PROCEDURE accounts.usp_User_SetPendingEmail
    @UserId       INT,
    @PendingEmail NVARCHAR(256) = NULL      -- NULL cancels a pending change
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;

        IF @PendingEmail IS NOT NULL AND EXISTS (SELECT 1 FROM accounts.Users WHERE Email = @PendingEmail AND UserId <> @UserId)
            THROW 50001, 'Email is already registered.', 1;

        UPDATE accounts.Users SET PendingEmail = @PendingEmail WHERE UserId = @UserId;

        IF @PendingEmail IS NULL
            UPDATE accounts.EmailVerificationTokens
            SET UsedAt = SYSUTCDATETIME()
            WHERE UserId = @UserId AND Purpose = 'EmailChange' AND UsedAt IS NULL;

    COMMIT TRANSACTION;
END
GO
