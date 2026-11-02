/* Removes the demo customers from every service's database (development only, like demo-data.sql). */
SET XACT_ABORT ON;
BEGIN TRANSACTION;

-- Emails and preferences first, while the users still exist to match on (if the Notifications service has run).
IF DB_ID(N'InsuranceNotifications') IS NOT NULL
BEGIN
    EXEC (N'DELETE n FROM InsuranceNotifications.notifications.Emails n
                JOIN InsuranceIdentity.accounts.Users u ON u.UserId = n.UserId
                WHERE u.Email LIKE N''%@demo.example'';
            DELETE p FROM InsuranceNotifications.notifications.Preferences p
                JOIN InsuranceIdentity.accounts.Users u ON u.UserId = p.UserId
                WHERE u.Email LIKE N''%@demo.example'';');
END

DELETE d  FROM InsuranceClaims.claims.ClaimDocuments d
  JOIN InsuranceClaims.claims.Claims c ON c.ClaimId = d.ClaimId
  JOIN InsuranceIdentity.accounts.Users u ON u.UserId = c.UserId WHERE u.Email LIKE N'%@demo.example';
DELETE c  FROM InsuranceClaims.claims.Claims c
          JOIN InsuranceIdentity.accounts.Users u  ON u.UserId = c.UserId
          WHERE u.Email LIKE N'%@demo.example';

-- (Their payment reminders go with them: ON DELETE CASCADE. So do verification links, with the users.)
DELETE pp FROM InsuranceBilling.billing.PremiumPayments pp
          JOIN InsuranceEnrollments.enrollments.Enrollments e ON e.EnrollmentId = pp.EnrollmentId
          JOIN InsuranceIdentity.accounts.Users u          ON u.UserId = e.UserId
          WHERE u.Email LIKE N'%@demo.example';

DELETE q  FROM InsuranceQuotes.quotes.Quotes q
          JOIN InsuranceIdentity.accounts.Users u ON u.UserId = q.UserId
          WHERE u.Email LIKE N'%@demo.example';

DELETE e  FROM InsuranceEnrollments.enrollments.Enrollments e
          JOIN InsuranceIdentity.accounts.Users u ON u.UserId = e.UserId
          WHERE u.Email LIKE N'%@demo.example';

DELETE FROM InsuranceIdentity.accounts.Users WHERE Email LIKE N'%@demo.example';

COMMIT TRANSACTION;
PRINT 'Demo data removed.';
GO