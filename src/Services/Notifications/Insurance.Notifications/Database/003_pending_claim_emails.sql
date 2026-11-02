/* The "We received your claim" email is the customer's copy of their claim, including copies of its documents.
   The documents are uploaded one by one AFTER the claim is saved (and ClaimSubmitted published), so the email waits
   in this table for a short while (Notifications:ClaimEmailDelay) and ClaimReceivedEmailJob sends it then. */

CREATE TABLE notifications.PendingClaimEmails (
    ClaimId   INT           NOT NULL CONSTRAINT PK_PendingClaimEmails PRIMARY KEY,
    Payload   NVARCHAR(MAX) NOT NULL,          -- the ClaimSubmitted event, as JSON
    SendAfter DATETIME2     NOT NULL,
    Attempts  INT           NOT NULL CONSTRAINT DF_PendingClaimEmails_Attempts DEFAULT 0,
    CreatedAt DATETIME2     NOT NULL CONSTRAINT DF_PendingClaimEmails_CreatedAt DEFAULT SYSUTCDATETIME()
);
GO

/* Queue one claim's email. A repeated event finds it queued (or already sent, see notifications.Emails) and does nothing. */
CREATE OR ALTER PROCEDURE notifications.usp_PendingClaimEmail_Add
    @ClaimId   INT,
    @Payload   NVARCHAR(MAX),
    @SendAfter DATETIME2
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO notifications.PendingClaimEmails (ClaimId, Payload, SendAfter)
    SELECT @ClaimId, @Payload, @SendAfter
    WHERE NOT EXISTS (SELECT 1 FROM notifications.PendingClaimEmails WITH (UPDLOCK, HOLDLOCK) WHERE ClaimId = @ClaimId);
END
GO

/* Takes the emails that are due, and pushes their SendAfter back by @LeaseSeconds so another instance (or this one,
   if it crashes mid-send) only retries them after that. READPAST skips rows another instance is taking right now. */
CREATE OR ALTER PROCEDURE notifications.usp_PendingClaimEmail_TakeDue
    @Take         INT = 20,
    @LeaseSeconds INT = 120
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE TOP (@Take) p
    SET SendAfter = DATEADD(SECOND, @LeaseSeconds, SYSUTCDATETIME()), Attempts = Attempts + 1
    OUTPUT inserted.ClaimId, inserted.Payload, inserted.Attempts
    FROM notifications.PendingClaimEmails p WITH (UPDLOCK, READPAST, ROWLOCK)
    WHERE p.SendAfter <= SYSUTCDATETIME();
END
GO

CREATE OR ALTER PROCEDURE notifications.usp_PendingClaimEmail_Remove
    @ClaimId INT
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM notifications.PendingClaimEmails WHERE ClaimId = @ClaimId;
END
GO
