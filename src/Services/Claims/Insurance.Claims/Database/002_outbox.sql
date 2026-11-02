/* Claims service: transactional outbox, so ClaimSubmitted and ClaimStatusChanged are published exactly when
   the claim change commits (see BuildingBlocks/Messaging/Outbox.cs). Same table as in the other services. */

CREATE TABLE dbo.OutboxMessages (
    Id          BIGINT IDENTITY(1,1) CONSTRAINT PK_OutboxMessages PRIMARY KEY,
    MessageType NVARCHAR(300) NOT NULL,
    Payload     NVARCHAR(MAX) NOT NULL,
    OccurredAt  DATETIME2 NOT NULL CONSTRAINT DF_OutboxMessages_OccurredAt DEFAULT SYSUTCDATETIME(),
    PublishedAt DATETIME2 NULL
);
CREATE INDEX IX_OutboxMessages_Unpublished ON dbo.OutboxMessages(Id) WHERE PublishedAt IS NULL;
GO
