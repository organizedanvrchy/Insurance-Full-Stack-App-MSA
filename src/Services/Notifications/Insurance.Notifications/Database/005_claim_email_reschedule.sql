/* A queued "We received your claim" email whose documents are still uploading is checked again shortly instead of
   waiting the whole Notifications:ClaimEmailDelay (ClaimReceivedEmailJob). */
CREATE OR ALTER PROCEDURE notifications.usp_PendingClaimEmail_Delay
    @ClaimId   INT,
    @SendAfter DATETIME2
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE notifications.PendingClaimEmails SET SendAfter = @SendAfter WHERE ClaimId = @ClaimId;
END
GO
