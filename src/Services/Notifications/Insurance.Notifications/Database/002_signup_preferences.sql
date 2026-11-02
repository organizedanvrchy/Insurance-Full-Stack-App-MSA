/* The "email me payment reminders" choice made on the sign-up form (Identity's CustomerRegistered event).
   Only applied if the customer has no saved preference yet, so a late or repeated event can never overwrite a
   choice made later on the profile page. */
CREATE OR ALTER PROCEDURE notifications.usp_Preferences_SetInitial
    @UserId           INT,
    @PaymentReminders BIT
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO notifications.Preferences (UserId, PaymentReminders)
    SELECT @UserId, @PaymentReminders
    WHERE NOT EXISTS (SELECT 1 FROM notifications.Preferences WITH (UPDLOCK, HOLDLOCK) WHERE UserId = @UserId);
END
GO
