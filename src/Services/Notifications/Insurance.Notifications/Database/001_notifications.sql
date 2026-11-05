/* Notifications service database (InsuranceNotifications). Run automatically by the service at startup
   (DatabaseMigrator): each script runs once, in name order, and is recorded in dbo.SchemaVersions.
   Only the Notifications service reads or writes this database. */

CREATE SCHEMA notifications;
GO

-- A customer's email choices. No row = the defaults (everything on).
-- Only reminders can be turned off: receipts, claim decisions and security emails always go out.
CREATE TABLE notifications.Preferences (
    UserId           INT       NOT NULL CONSTRAINT PK_Preferences PRIMARY KEY,
    PaymentReminders BIT       NOT NULL CONSTRAINT DF_Preferences_PaymentReminders DEFAULT 1,
    UpdatedAt        DATETIME2 NOT NULL CONSTRAINT DF_Preferences_UpdatedAt DEFAULT SYSUTCDATETIME()
);
GO

-- One row per email this service decided about. NotificationKey makes consumers idempotent: an event delivered
-- twice finds its row already 'Sent' (or 'Skipped') and sends nothing. 'Failed' rows are retried.
CREATE TABLE notifications.Emails (
    EmailId         BIGINT IDENTITY(1,1) CONSTRAINT PK_Emails PRIMARY KEY,
    NotificationKey NVARCHAR(200)  NOT NULL,
    UserId          INT            NOT NULL,
    Kind            NVARCHAR(40)   NOT NULL,
    ToEmail         NVARCHAR(256)  NULL,
    Subject         NVARCHAR(300)  NULL,
    Status          NVARCHAR(20)   NOT NULL,
    Detail          NVARCHAR(1000) NULL,       -- why it was skipped, or the last error
    Attempts        INT            NOT NULL CONSTRAINT DF_Emails_Attempts DEFAULT 0,
    CreatedAt       DATETIME2      NOT NULL CONSTRAINT DF_Emails_CreatedAt DEFAULT SYSUTCDATETIME(),
    SentAt          DATETIME2      NULL,
    CONSTRAINT UQ_Emails_Key UNIQUE (NotificationKey),
    CONSTRAINT CK_Emails_Status CHECK (Status IN ('Sending','Sent','Skipped','Failed'))
);
CREATE INDEX IX_Emails_UserId ON notifications.Emails(UserId, CreatedAt DESC);
GO

CREATE OR ALTER PROCEDURE notifications.usp_Preferences_Get
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT @UserId AS UserId,
           ISNULL(p.PaymentReminders, CAST(1 AS BIT)) AS PaymentReminders,
           p.UpdatedAt
    FROM (SELECT 1 AS One) x
    LEFT JOIN notifications.Preferences p ON p.UserId = @UserId;
END
GO

CREATE OR ALTER PROCEDURE notifications.usp_Preferences_Set
    @UserId           INT,
    @PaymentReminders BIT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;
        UPDATE notifications.Preferences WITH (UPDLOCK, SERIALIZABLE)
        SET PaymentReminders = @PaymentReminders, UpdatedAt = SYSUTCDATETIME()
        WHERE UserId = @UserId;

        IF @@ROWCOUNT = 0
            INSERT INTO notifications.Preferences (UserId, PaymentReminders) VALUES (@UserId, @PaymentReminders);
    COMMIT TRANSACTION;

    EXEC notifications.usp_Preferences_Get @UserId = @UserId;
END
GO

/* Claims the right to send one email. Returns 1 if the caller should go ahead (a new key, or an earlier attempt
   that failed or never finished), 0 if this email was already sent or deliberately skipped. */
CREATE OR ALTER PROCEDURE notifications.usp_Email_Begin
    @NotificationKey NVARCHAR(200),
    @UserId          INT,
    @Kind            NVARCHAR(40)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Status NVARCHAR(20);

    BEGIN TRANSACTION;
        SELECT @Status = Status
        FROM notifications.Emails WITH (UPDLOCK, HOLDLOCK)
        WHERE NotificationKey = @NotificationKey;

        IF @Status IS NULL
            INSERT INTO notifications.Emails (NotificationKey, UserId, Kind, Status, Attempts)
            VALUES (@NotificationKey, @UserId, @Kind, 'Sending', 1);
        ELSE IF @Status IN ('Failed', 'Sending')
            UPDATE notifications.Emails
            SET Status = 'Sending', Attempts = Attempts + 1
            WHERE NotificationKey = @NotificationKey;
    COMMIT TRANSACTION;

    SELECT CAST(CASE WHEN @Status IS NULL OR @Status IN ('Failed', 'Sending') THEN 1 ELSE 0 END AS BIT) AS ShouldSend;
END
GO

/* Records how it went: Sent, Skipped (with the reason) or Failed (with the error). */
CREATE OR ALTER PROCEDURE notifications.usp_Email_Complete
    @NotificationKey NVARCHAR(200),
    @Status          NVARCHAR(20),
    @ToEmail         NVARCHAR(256)  = NULL,
    @Subject         NVARCHAR(300)  = NULL,
    @Detail          NVARCHAR(1000) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE notifications.Emails
    SET Status  = @Status,
        ToEmail = COALESCE(@ToEmail, ToEmail),
        Subject = COALESCE(@Subject, Subject),
        Detail  = @Detail,
        SentAt  = CASE WHEN @Status = 'Sent' THEN SYSUTCDATETIME() ELSE SentAt END
    WHERE NotificationKey = @NotificationKey;
END
GO

/* The customer's own recent emails, newest first (only those actually sent). */
CREATE OR ALTER PROCEDURE notifications.usp_Email_GetRecentByUser
    @UserId INT,
    @Take   INT = 20
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP (@Take) EmailId, Kind, ToEmail, Subject, SentAt
    FROM notifications.Emails
    WHERE UserId = @UserId AND Status = 'Sent'
    ORDER BY SentAt DESC;
END
GO
