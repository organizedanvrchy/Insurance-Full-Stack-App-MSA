/* Admins manage customers from one pop-up: edit name, email, phone and address, reset the password, deactivate or
   reactivate. A reset replaces the password with a one-time password (only its hash is stored), emailed to the
   customer and valid for 30 minutes. The old password stops working at once. Signing in with the one-time password
   only allows choosing a new password (the gateway refuses everything else while MustChangePassword is set).
   Error numbers: 50001 email already registered. */

ALTER TABLE accounts.Users ADD
    MustChangePassword    BIT       NOT NULL CONSTRAINT DF_Users_MustChangePassword DEFAULT 0,
    PasswordResetExpiresAt DATETIME2 NULL;      -- when the one-time password stops working
GO

CREATE OR ALTER PROCEDURE accounts.usp_User_GetByEmail
    @Email NVARCHAR(256)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT UserId, FullName, Email, PasswordHash, Phone, Address, Role, IsActive, CreatedAt, EmailVerifiedAt,
           MustChangePassword, PasswordResetExpiresAt
    FROM accounts.Users
    WHERE Email = @Email;
END
GO

CREATE OR ALTER PROCEDURE accounts.usp_User_GetById
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT UserId, FullName, Email, PasswordHash, Phone, Address, Role, IsActive, CreatedAt, EmailVerifiedAt,
           MustChangePassword, PasswordResetExpiresAt
    FROM accounts.Users
    WHERE UserId = @UserId;
END
GO

/* Other services and the gateway (which uses MustChangePassword to refuse everything but choosing a new password). */
CREATE OR ALTER PROCEDURE accounts.usp_User_GetMany
    @UserIds NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT UserId, FullName, Email, Role, IsActive,
           CAST(CASE WHEN EmailVerifiedAt IS NULL THEN 0 ELSE 1 END AS BIT) AS EmailVerified,
           MustChangePassword
    FROM accounts.Users
    WHERE UserId IN (SELECT CAST(value AS INT) FROM OPENJSON(@UserIds));
END
GO

/* The admin customer list and pop-up now also show the address and whether a password reset is pending. */
CREATE OR ALTER PROCEDURE accounts.usp_Customer_GetAll
    @Search   NVARCHAR(100) = NULL,
    @IsActive BIT           = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT UserId, FullName, Email, Phone, Address, IsActive, CreatedAt,
           CAST(CASE WHEN EmailVerifiedAt IS NULL THEN 0 ELSE 1 END AS BIT) AS EmailVerified,
           MustChangePassword
    FROM accounts.Users
    WHERE Role = 'Customer'
      AND (@IsActive IS NULL OR IsActive = @IsActive)
      AND (@Search IS NULL OR FullName LIKE '%' + @Search + '%' OR Email LIKE '%' + @Search + '%')
    ORDER BY FullName;
END
GO

CREATE OR ALTER PROCEDURE accounts.usp_Customer_GetById
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT UserId, FullName, Email, Phone, Address, IsActive, CreatedAt,
           CAST(CASE WHEN EmailVerifiedAt IS NULL THEN 0 ELSE 1 END AS BIT) AS EmailVerified,
           MustChangePassword
    FROM accounts.Users
    WHERE UserId = @UserId AND Role = 'Customer';
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

    EXEC accounts.usp_Customer_GetById @UserId = @UserId;
END
GO

/* An admin edits a customer's contact details. Email is changed through usp_User_ChangeEmail (it must be verified
   again), so this proc never touches it. The Role filter keeps admins out of reach. */
CREATE OR ALTER PROCEDURE accounts.usp_Customer_UpdateDetails
    @UserId   INT,
    @FullName NVARCHAR(100),
    @Phone    NVARCHAR(20)  = NULL,
    @Address  NVARCHAR(250) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE accounts.Users
    SET FullName = @FullName, Phone = @Phone, Address = @Address
    WHERE UserId = @UserId AND Role = 'Customer';

    EXEC accounts.usp_Customer_GetById @UserId = @UserId;
END
GO

/* Admin reset: the one-time password's hash replaces the password, so the old one stops working immediately. */
CREATE OR ALTER PROCEDURE accounts.usp_Customer_ResetPassword
    @UserId       INT,
    @PasswordHash NVARCHAR(100),
    @ExpiresAt    DATETIME2
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE accounts.Users
    SET PasswordHash = @PasswordHash, MustChangePassword = 1, PasswordResetExpiresAt = @ExpiresAt
    WHERE UserId = @UserId AND Role = 'Customer';

    EXEC accounts.usp_Customer_GetById @UserId = @UserId;
END
GO

/* A new password, chosen by the customer: also ends a pending admin reset. */
CREATE OR ALTER PROCEDURE accounts.usp_User_ChangePassword
    @UserId       INT,
    @PasswordHash NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE accounts.Users
    SET PasswordHash = @PasswordHash, MustChangePassword = 0, PasswordResetExpiresAt = NULL
    WHERE UserId = @UserId;
END
GO
