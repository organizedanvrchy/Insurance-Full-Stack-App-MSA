/* Emails the mail server said to try again later (its sending limit, a temporary error, or no connection).
   The dispatcher puts the finished email here instead of failing it, and EmailRetryJob resends it on a growing
   schedule (EmailRetrySchedule). Meanwhile its notifications.Emails row says 'Waiting', so a redelivered event
   doesn't send it a second time. */

ALTER TABLE notifications.Emails DROP CONSTRAINT CK_Emails_Status;
ALTER TABLE notifications.Emails ADD CONSTRAINT CK_Emails_Status
    CHECK (Status IN ('Sending','Sent','Skipped','Failed','Waiting'));
GO

CREATE TABLE notifications.EmailRetries (
    NotificationKey NVARCHAR(200)  NOT NULL CONSTRAINT PK_EmailRetries PRIMARY KEY,
    Payload         NVARCHAR(MAX)  NOT NULL,      -- the finished email (EmailMessage) as JSON, attachments included
    SendAfter       DATETIME2      NOT NULL,
    Attempts        INT            NOT NULL CONSTRAINT DF_EmailRetries_Attempts DEFAULT 0,   -- retries so far
    CreatedAt       DATETIME2      NOT NULL CONSTRAINT DF_EmailRetries_CreatedAt DEFAULT SYSUTCDATETIME()
);
CREATE INDEX IX_EmailRetries_SendAfter ON notifications.EmailRetries(SendAfter);
GO

/* Queue an email for a later attempt and mark it Waiting (with what the server said). */
CREATE OR ALTER PROCEDURE notifications.usp_EmailRetry_Add
    @NotificationKey NVARCHAR(200),
    @Payload         NVARCHAR(MAX),
    @SendAfter       DATETIME2,
    @ToEmail         NVARCHAR(256),
    @Subject         NVARCHAR(300),
    @Detail          NVARCHAR(1000)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;
        INSERT INTO notifications.EmailRetries (NotificationKey, Payload, SendAfter)
        SELECT @NotificationKey, @Payload, @SendAfter
        WHERE NOT EXISTS (SELECT 1 FROM notifications.EmailRetries WITH (UPDLOCK, HOLDLOCK)
                          WHERE NotificationKey = @NotificationKey);

        UPDATE notifications.Emails
        SET Status = 'Waiting', ToEmail = @ToEmail, Subject = @Subject, Detail = @Detail
        WHERE NotificationKey = @NotificationKey;
    COMMIT TRANSACTION;
END
GO

/* Takes the retries that are due and pushes their SendAfter back by @LeaseSeconds, so another instance (or this one,
   after a crash mid-send) only picks them up again after that. Attempts counts the retries made. */
CREATE OR ALTER PROCEDURE notifications.usp_EmailRetry_TakeDue
    @Take         INT = 10,
    @LeaseSeconds INT = 300
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE TOP (@Take) r
    SET SendAfter = DATEADD(SECOND, @LeaseSeconds, SYSUTCDATETIME()), Attempts = Attempts + 1
    OUTPUT inserted.NotificationKey, inserted.Payload, inserted.Attempts
    FROM notifications.EmailRetries r WITH (UPDLOCK, READPAST, ROWLOCK)
    WHERE r.SendAfter <= SYSUTCDATETIME();
END
GO

/* Not yet: try again at @SendAfter, keeping the server's latest answer on the email. */
CREATE OR ALTER PROCEDURE notifications.usp_EmailRetry_Reschedule
    @NotificationKey NVARCHAR(200),
    @SendAfter       DATETIME2,
    @Detail          NVARCHAR(1000)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE notifications.EmailRetries SET SendAfter = @SendAfter WHERE NotificationKey = @NotificationKey;
    UPDATE notifications.Emails SET Detail = @Detail WHERE NotificationKey = @NotificationKey;
END
GO

CREATE OR ALTER PROCEDURE notifications.usp_EmailRetry_Remove
    @NotificationKey NVARCHAR(200)
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM notifications.EmailRetries WHERE NotificationKey = @NotificationKey;
END
GO
