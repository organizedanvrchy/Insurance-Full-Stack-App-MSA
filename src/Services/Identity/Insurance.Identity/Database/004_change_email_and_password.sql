/* Customers can change their sign-in email and their password from the profile page.
   A new email takes effect at once but is UNVERIFIED (EmailVerifiedAt = NULL) until the customer uses the link sent
   to it; any link sent to the old address stops working. The service checks the current password first.
   Error numbers: 50001 email already registered. */

CREATE OR ALTER PROCEDURE accounts.usp_User_ChangeEmail
    @UserId   INT,
    @NewEmail NVARCHAR(256)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @OldEmail NVARCHAR(256);

    BEGIN TRANSACTION;

        SELECT @OldEmail = Email FROM accounts.Users WITH (UPDLOCK) WHERE UserId = @UserId;

        IF EXISTS (SELECT 1 FROM accounts.Users WITH (UPDLOCK, HOLDLOCK) WHERE Email = @NewEmail AND UserId <> @UserId)
            THROW 50001, 'Email is already registered.', 1;

        UPDATE accounts.Users
        SET Email = @NewEmail, EmailVerifiedAt = NULL
        WHERE UserId = @UserId;

        UPDATE accounts.EmailVerificationTokens
        SET UsedAt = SYSUTCDATETIME()
        WHERE UserId = @UserId AND UsedAt IS NULL;

    COMMIT TRANSACTION;

    SELECT @OldEmail AS OldEmail;
END
GO

CREATE OR ALTER PROCEDURE accounts.usp_User_ChangePassword
    @UserId       INT,
    @PasswordHash NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE accounts.Users SET PasswordHash = @PasswordHash WHERE UserId = @UserId;
END
GO
