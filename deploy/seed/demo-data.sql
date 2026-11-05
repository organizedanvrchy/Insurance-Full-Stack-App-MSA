/* DEMO DATA (development only): 6 customers with realistic histories, written straight into all six
   service databases with three-part names (InsuranceIdentity.accounts.Users ...). That only works when
   the databases share one SQL Server instance, as they do in docker compose. In production, data reaches
   a service only through its API or its events.

   Run it after the services have started once (each creates its own database) and after you have
   registered and promoted an admin. With docker compose:
     docker compose -f deploy/docker-compose.yml exec -T sqlserver /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "$SA_PASSWORD" -i /seed/demo-data.sql

   Password for every demo customer:  Demo#2026pass     
   (emails all end in @demo.example)

   Requires: the plans (the Catalog service seeds them on first start)
   and an existing admin account (used as the claim reviewer).
   Safe to re-run: skipped if demo customers already exist. 
   Remove everything with remove-demo-data.sql.
   All dates are relative to today. The ages in the quote 
   breakdown labels assume an October 2026 run. 
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF EXISTS (SELECT 1 FROM InsuranceIdentity.accounts.Users WHERE Email LIKE N'%@demo.example')
BEGIN
    PRINT 'Demo data already exists. Nothing was changed. Run remove-demo-data.sql first to reseed.';
    RETURN;
END

DECLARE @Today   DATE = CAST(SYSUTCDATETIME() AS DATE);
DECLARE @AdminId INT  = (SELECT TOP (1) UserId FROM InsuranceIdentity.accounts.Users WHERE Role = 'Admin' ORDER BY UserId);
DECLARE @Missing NVARCHAR(MAX), @Msg NVARCHAR(500);

SELECT @Missing = STRING_AGG(n.PolicyName, N', ')
FROM (VALUES (N'Auto Full Coverage'), (N'Auto Standard Coverage'), (N'Auto Basic Liability'),
             (N'Silver Health Plan'), (N'Gold Health Plan'), (N'Term Life 20-Year'),
             (N'Homeowners Preferred'), (N'Renters Insurance'),
             (N'Single-Trip Comprehensive'), (N'Single-Trip Basic')) AS n (PolicyName)
WHERE NOT EXISTS (SELECT 1 FROM InsuranceCatalog.catalog.Policies p WHERE p.PolicyName = n.PolicyName);

IF @Missing IS NOT NULL
BEGIN
    SET @Msg = CONCAT(N'These plans are missing: ', @Missing, N'. Start the Catalog service once first: it seeds the plans.');
    THROW 50100, @Msg, 1;
END

IF @AdminId IS NULL
    PRINT 'Warning: no admin account found. Reviewed claims will show no reviewer name.';

BEGIN TRANSACTION;

/* 
1. CUSTOMERS 
*/
INSERT INTO InsuranceIdentity.accounts.Users (FullName, Email, PasswordHash, Phone, Address, Role, IsActive, CreatedAt)
VALUES
 (N'Emma Carter',     N'emma.carter@demo.example',     N'$2a$11$DeKme8rKt71zDTH/0vm.0.SO0VNtq/tAN1/YT8zDh5vauglwgwh62', N'(203) 555-0141', N'48 Maple Avenue, Stamford, CT 06902',     'Customer', 1, DATEADD(DAY, -140, SYSUTCDATETIME())),
 (N'Marcus Johnson',  N'marcus.johnson@demo.example',  N'$2a$11$2M4YMR5gRWdSnsKiyHJ6YeRa2ti5e9LKMVbA8je3cRcpddgzxA3zG', N'(203) 555-0172', N'112 Harbor View Drive, Norwalk, CT 06854', 'Customer', 1, DATEADD(DAY, -170, SYSUTCDATETIME())),
 (N'Priya Patel',     N'priya.patel@demo.example',     N'$2a$11$SYs8vgcnvVuGGaW7wobFcuaWmqgMDrKfMUdr5tm9bIyqRMNXsTZYq', N'(914) 555-0133', N'27 Birchwood Lane, White Plains, NY 10601', 'Customer', 1, DATEADD(DAY, -230, SYSUTCDATETIME())),
 (N'Daniel Kim',      N'daniel.kim@demo.example',      N'$2a$11$6aOHC8Rk1xH9kYwtucTEKe8coSuNTJKPTR828XsABQct6TH7rpiVq', N'(203) 555-0188', N'905 Orchard Street, New Haven, CT 06511',  'Customer', 1, DATEADD(DAY, -75,  SYSUTCDATETIME())),
 (N'Sofia Rodriguez', N'sofia.rodriguez@demo.example', N'$2a$11$fLptEy9RudVQ53M/5gUKYudojXwDuknuXdTvICUC5sEH29VgtaL6u', N'(860) 555-0126', N'14 Elm Court, Apt 3B, Hartford, CT 06103',  'Customer', 1, DATEADD(DAY, -2,   SYSUTCDATETIME())),
 (N'Tyler Brooks',    N'tyler.brooks@demo.example',    N'$2a$11$cS0CMCNU8xHtOk5YTfml2u4pOEPQIAASd7WnLGzECeh9vvgMcgPbi', N'(203) 555-0159', N'360 Ridge Road, Danbury, CT 06810',        'Customer', 0, DATEADD(DAY, -100, SYSUTCDATETIME()));

-- Every demo customer has already confirmed their email address (enrolling and filing claims require it).
UPDATE InsuranceIdentity.accounts.Users SET EmailVerifiedAt = CreatedAt WHERE Email LIKE N'%@demo.example';

/* 
2. ENROLLMENT PLAN
One row per policy purchase. PaidCount = how many installments (from the first) are paid.
Answers / Breakdown / premiums match what the rating engine produces for those answers. 
*/
CREATE TABLE #Spec (
    Email          NVARCHAR(256),
    PolicyName     NVARCHAR(100),
    StartMonthsAgo INT,
    StartDaysAgo   INT,
    PaidCount      INT,
    EnrollStatus   NVARCHAR(20),
    Answers        NVARCHAR(MAX),
    Breakdown      NVARCHAR(MAX),
    BasePremium    DECIMAL(18,2),
    MonthlyPremium DECIMAL(18,2)
);

INSERT INTO #Spec (Email, PolicyName, StartMonthsAgo, StartDaysAgo, PaidCount, EnrollStatus, Answers, Breakdown, BasePremium, MonthlyPremium)
VALUES
 -- Emma: fully paid up
 (N'emma.carter@demo.example', N'Auto Full Coverage', 4, 0, 5, 'Active',
  N'{"dateOfBirth":"1988-03-14","vehicleYear":2022,"atFaultAccidents":0,"annualMileage":"Under 7,500"}',
  N'[{"Label":"Driver age 38","Multiplier":1.00},{"Label":"Vehicle age 4 yr","Multiplier":1.00},{"Label":"At-fault accidents: 0","Multiplier":1.00},{"Label":"Mileage: Under 7,500","Multiplier":0.90}]',
  215.00, 193.50),
 (N'emma.carter@demo.example', N'Silver Health Plan', 2, 0, 3, 'Active',
  N'{"dateOfBirth":"1988-03-14","tobaccoUse":false,"conditions":"Minor"}',
  N'[{"Label":"Age 38","Multiplier":1.00},{"Label":"Non-tobacco user","Multiplier":1.00},{"Label":"Medical conditions: Minor","Multiplier":1.15}]',
  450.00, 517.50),

 -- Marcus: home + long-term life
 (N'marcus.johnson@demo.example', N'Homeowners Preferred', 5, 0, 6, 'Active',
  N'{"yearBuilt":1998,"securitySystem":true,"priorClaims":0}',
  N'[{"Label":"Home age 28 yr","Multiplier":1.00},{"Label":"Monitored security system","Multiplier":0.90},{"Label":"Claims in last 5 years: 0","Multiplier":1.00}]',
  210.00, 189.00),
 (N'marcus.johnson@demo.example', N'Term Life 20-Year', 3, 0, 4, 'Active',
  N'{"dateOfBirth":"1979-08-02","tobaccoUse":false,"conditions":"None"}',
  N'[{"Label":"Age 47","Multiplier":2.00},{"Label":"Non-tobacco user","Multiplier":1.00},{"Label":"Medical conditions: None","Multiplier":1.00}]',
  29.00, 58.00),

 -- Priya: Health Gold has 2 OVERDUE installments (months 6 and 7 of the schedule are unpaid and past the grace period)
 (N'priya.patel@demo.example', N'Gold Health Plan', 7, 0, 5, 'Active',
  N'{"dateOfBirth":"1994-06-09","tobaccoUse":true,"conditions":"None"}',
  N'[{"Label":"Age 32","Multiplier":1.00},{"Label":"Tobacco use","Multiplier":1.40},{"Label":"Medical conditions: None","Multiplier":1.00}]',
  620.00, 868.00),
 (N'priya.patel@demo.example', N'Auto Standard Coverage', 1, 0, 2, 'Active',
  N'{"dateOfBirth":"1994-06-09","vehicleYear":2019,"atFaultAccidents":1,"annualMileage":"Over 15,000"}',
  N'[{"Label":"Driver age 32","Multiplier":1.00},{"Label":"Vehicle age 7 yr","Multiplier":1.00},{"Label":"At-fault accidents: 1","Multiplier":1.30},{"Label":"Mileage: Over 15,000","Multiplier":1.20}]',
  140.00, 218.40),

 -- Daniel: one active trip policy, one that has already expired
 (N'daniel.kim@demo.example', N'Single-Trip Comprehensive', 0, 10, 1, 'Active',
  N'{"dateOfBirth":"1971-02-27","tripLengthDays":10,"destination":"International"}',
  N'[{"Label":"Traveler age 55","Multiplier":1.00},{"Label":"Trip length 10 days","Multiplier":1.00},{"Label":"Destination: International","Multiplier":1.45}]',
  120.00, 174.00),
 (N'daniel.kim@demo.example', N'Single-Trip Basic', 2, 0, 1, 'Active',
  N'{"dateOfBirth":"1971-02-27","tripLengthDays":7,"destination":"Domestic"}',
  N'[{"Label":"Traveler age 55","Multiplier":1.00},{"Label":"Trip length 7 days","Multiplier":1.00},{"Label":"Destination: Domestic","Multiplier":1.00}]',
  55.00, 55.00),

 -- Sofia: starts today, nothing paid yet -> "payment due" alert
 (N'sofia.rodriguez@demo.example', N'Renters Insurance', 0, 0, 0, 'Active',
  N'{"yearBuilt":2008,"securitySystem":false,"priorClaims":0}',
  N'[{"Label":"Home age 18 yr","Multiplier":1.00},{"Label":"No security system","Multiplier":1.00},{"Label":"Claims in last 5 years: 0","Multiplier":1.00}]',
  14.00, 14.00),

 -- Tyler: cancelled after 2 payments (unpaid installments are removed, as cancelling does)
 (N'tyler.brooks@demo.example', N'Auto Basic Liability', 3, 0, 2, 'Cancelled',
  N'{"dateOfBirth":"1999-12-05","vehicleYear":2012,"atFaultAccidents":2,"annualMileage":"7,500 to 15,000"}',
  N'[{"Label":"Driver age 26","Multiplier":1.00},{"Label":"Vehicle age 14 yr","Multiplier":0.90},{"Label":"At-fault accidents: 2","Multiplier":1.70},{"Label":"Mileage: 7,500 to 15,000","Multiplier":1.00}]',
  75.00, 114.75);

/*
3. ENROLLMENTS 
*/
INSERT INTO InsuranceEnrollments.enrollments.Enrollments (UserId, PolicyId, QuoteId, StartDate, EndDate, MonthlyPremium, Status, CreatedAt)
SELECT u.UserId, p.PolicyId, 0, st.StartDate, DATEADD(MONTH, p.TermMonths, st.StartDate), s.MonthlyPremium, s.EnrollStatus,
       CASE WHEN st.StartDate < @Today
            THEN DATEADD(HOUR, 10, CAST(st.StartDate AS DATETIME2))
            ELSE SYSUTCDATETIME() END
FROM #Spec s
JOIN InsuranceIdentity.accounts.Users u    ON u.Email = s.Email
JOIN InsuranceCatalog.catalog.Policies p ON p.PolicyName = s.PolicyName
CROSS APPLY (SELECT DATEADD(DAY, -s.StartDaysAgo, DATEADD(MONTH, -s.StartMonthsAgo, @Today)) AS StartDate) st;

/*
4. QUOTES (each enrollment came from one) 
*/
INSERT INTO InsuranceQuotes.quotes.Quotes (UserId, PolicyId, Answers, Breakdown, BasePremium, MonthlyPremium, CreatedAt, ExpiresAt, EnrollmentId)
SELECT e.UserId, e.PolicyId, s.Answers, s.Breakdown, s.BasePremium, s.MonthlyPremium,
       DATEADD(DAY, -1, e.CreatedAt), DATEADD(DAY, 29, e.CreatedAt), e.EnrollmentId
FROM #Spec s
JOIN InsuranceIdentity.accounts.Users u       ON u.Email = s.Email
JOIN InsuranceCatalog.catalog.Policies p    ON p.PolicyName = s.PolicyName
JOIN InsuranceEnrollments.enrollments.Enrollments e ON e.UserId = u.UserId AND e.PolicyId = p.PolicyId;

-- ...and each enrollment points back at its quote.
UPDATE e SET QuoteId = q.QuoteId
FROM InsuranceEnrollments.enrollments.Enrollments e
JOIN InsuranceQuotes.quotes.Quotes q ON q.EnrollmentId = e.EnrollmentId
WHERE e.QuoteId = 0;

/* 
5. PREMIUM PAYMENTS
One row per month of the term. The first PaidCount are paid on their due date; the rest are pending
(the payments view shows Pending rows more than 5 days past due as Overdue). 
*/
;WITH Nums AS (
    SELECT 0 AS N
    UNION ALL
    SELECT N + 1 FROM Nums WHERE N < 599
)
INSERT INTO InsuranceBilling.billing.PremiumPayments (EnrollmentId, DueDate, Amount, PaidDate, Status)
SELECT e.EnrollmentId, d.DueDate, e.MonthlyPremium,
       CASE WHEN n.N < s.PaidCount THEN d.DueDate END,
       CASE WHEN n.N < s.PaidCount THEN 'Paid' ELSE 'Pending' END
FROM #Spec s
JOIN InsuranceIdentity.accounts.Users u       ON u.Email = s.Email
JOIN InsuranceCatalog.catalog.Policies p    ON p.PolicyName = s.PolicyName
JOIN InsuranceEnrollments.enrollments.Enrollments e ON e.UserId = u.UserId AND e.PolicyId = p.PolicyId
JOIN Nums n            ON n.N < p.TermMonths
CROSS APPLY (SELECT DATEADD(MONTH, n.N, e.StartDate) AS DueDate) d
WHERE NOT (s.EnrollStatus = 'Cancelled' AND n.N >= s.PaidCount)   -- cancelled: unpaid rows were removed
OPTION (MAXRECURSION 600);

/*
6. CLAIMS (one in every status)
Listed oldest first so claim numbers increase over time. 
Incident dates fall inside each coverage period. 
*/
CREATE TABLE #ClaimSpec (
    Id               INT IDENTITY(1,1) PRIMARY KEY,
    Email            NVARCHAR(256),
    PolicyName       NVARCHAR(100),
    IncidentDaysAgo  INT,
    SubmittedDaysAgo INT,
    ReviewedDaysAgo  INT NULL,
    ClaimAmount      DECIMAL(18,2),
    Status           NVARCHAR(20),
    Description      NVARCHAR(2000),
    AdminNotes       NVARCHAR(1000) NULL
);

INSERT INTO #ClaimSpec (Email, PolicyName, IncidentDaysAgo, SubmittedDaysAgo, ReviewedDaysAgo, ClaimAmount, Status, Description, AdminNotes)
VALUES
 (N'marcus.johnson@demo.example', N'Homeowners Preferred', 100, 98, 60, 4200.00, 'Paid',
  N'A washing machine supply hose burst while we were away and flooded the laundry room and part of the hallway. Plumber and water extraction invoices are attached.',
  N'Approved after photo review and the contractor estimate. Payment issued.'),
 (N'emma.carter@demo.example', N'Auto Full Coverage', 40, 36, 30, 1850.00, 'Approved',
  N'Rear-ended at a red light on Route 1. The bumper, trunk lid and left taillight need replacement. A police report was filed at the scene.',
  N'Collision damage confirmed by the repair estimate. Approved, payment to follow.'),
 (N'priya.patel@demo.example', N'Auto Standard Coverage', 18, 16, NULL, 1200.00, 'Submitted',
  N'Backed into a parked car in a garage, damaging my rear bumper and the other vehicle door. Both drivers exchanged insurance information.',
  NULL),
 (N'emma.carter@demo.example', N'Silver Health Plan', 12, 10, 8, 620.00, 'UnderReview',
  N'Emergency room visit for a severe asthma attack. I was treated and released the same evening.',
  N'Waiting on the itemized hospital invoice.'),
 (N'daniel.kim@demo.example', N'Single-Trip Comprehensive', 6, 5, 2, 1500.00, 'Rejected',
  N'My checked suitcase did not arrive at the destination airport. It contained clothing and a camera.',
  N'The airline was not notified within 24 hours of arrival, which the policy requires for baggage claims. If you have a delay report from the airline, please submit a new claim with a copy attached.'),
 (N'marcus.johnson@demo.example', N'Homeowners Preferred', 6, 4, NULL, 780.00, 'Submitted',
  N'A branch fell during the storm on Saturday and broke a double-pane window in the living room. The window has been boarded up temporarily.',
  NULL);

DECLARE @i INT = 1, @n INT = (SELECT COUNT(*) FROM #ClaimSpec), @Seq BIGINT;

WHILE @i <= @n
BEGIN
    SET @Seq = NEXT VALUE FOR InsuranceClaims.claims.ClaimNumberSeq;

    INSERT INTO InsuranceClaims.claims.Claims (ClaimNumber, EnrollmentId, UserId, IncidentDate, Description, ClaimAmount, Status, AdminNotes,
                               SubmittedAt, ReviewedAt, ReviewedByUserId)
    SELECT CONCAT(N'CLM-', YEAR(DATEADD(DAY, -c.SubmittedDaysAgo, @Today)), N'-', RIGHT(CONCAT(N'000000', @Seq), 6)),
           e.EnrollmentId, u.UserId,
           DATEADD(DAY, -c.IncidentDaysAgo, @Today),
           c.Description, c.ClaimAmount, c.Status, c.AdminNotes,
           DATEADD(HOUR, 14, CAST(DATEADD(DAY, -c.SubmittedDaysAgo, @Today) AS DATETIME2)),
           CASE WHEN c.ReviewedDaysAgo IS NULL THEN NULL
                ELSE DATEADD(HOUR, 16, CAST(DATEADD(DAY, -c.ReviewedDaysAgo, @Today) AS DATETIME2)) END,
           CASE WHEN c.ReviewedDaysAgo IS NULL THEN NULL ELSE @AdminId END
    FROM #ClaimSpec c
    JOIN InsuranceIdentity.accounts.Users u       ON u.Email = c.Email
    JOIN InsuranceCatalog.catalog.Policies p    ON p.PolicyName = c.PolicyName
    JOIN InsuranceEnrollments.enrollments.Enrollments e ON e.UserId = u.UserId AND e.PolicyId = p.PolicyId
    WHERE c.Id = @i;

    SET @i += 1;
END

DROP TABLE #ClaimSpec;
DROP TABLE #Spec;

COMMIT TRANSACTION;

/* Check results */
SELECT u.FullName, p.PolicyName, e.Status, e.StartDate, e.EndDate, e.MonthlyPremium,
       (SELECT COUNT(*) FROM InsuranceBilling.billing.PremiumPayments pp WHERE pp.EnrollmentId = e.EnrollmentId AND pp.Status = 'Paid') AS PaymentsPaid,
       (SELECT COUNT(*) FROM InsuranceBilling.billing.PremiumPayments pp WHERE pp.EnrollmentId = e.EnrollmentId)                       AS PaymentsTotal
FROM InsuranceEnrollments.enrollments.vw_EnrollmentDetails e
JOIN InsuranceIdentity.accounts.Users u   ON u.UserId = e.UserId
JOIN InsuranceCatalog.catalog.Policies p ON p.PolicyId = e.PolicyId
WHERE u.Email LIKE N'%@demo.example'
ORDER BY u.FullName, e.StartDate;

SELECT c.ClaimNumber, u.FullName AS CustomerName, p.PolicyName, c.ClaimAmount, c.Status
FROM InsuranceClaims.claims.Claims c
JOIN InsuranceIdentity.accounts.Users u          ON u.UserId = c.UserId
JOIN InsuranceEnrollments.enrollments.Enrollments e ON e.EnrollmentId = c.EnrollmentId
JOIN InsuranceCatalog.catalog.Policies p        ON p.PolicyId = e.PolicyId
WHERE u.Email LIKE N'%@demo.example'
ORDER BY c.ClaimNumber;

PRINT 'Demo data created. Log in as any @demo.example customer with the password Demo#2026pass.';
GO