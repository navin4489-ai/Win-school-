/*
  Reference data seed for SQL Server deployments.

  Demo tenants, users, classes and sample records are created by the API's
  DbInitializer in Development. This script only inserts the permission catalogue,
  because permissions are not tenant-scoped and are safe to create everywhere.

  Default demo password (created by the API seeder): Passw0rd!
  Change or remove demo accounts before production use.
*/
SET NOCOUNT ON;
GO

IF OBJECT_ID(N'dbo.Permission', N'U') IS NULL
BEGIN
  RAISERROR('Run database/schema.sql before seed.sql.', 16, 1);
  RETURN;
END;
GO

DECLARE @permissions TABLE ([Code] NVARCHAR(32) NOT NULL, [Module] NVARCHAR(256) NOT NULL,
                            [Action] NVARCHAR(256) NOT NULL, [Description] NVARCHAR(256) NULL);

INSERT INTO @permissions ([Code], [Module], [Action], [Description]) VALUES
  (N'students.read',       N'Students',     N'Read',    N'View student records'),
  (N'students.write',      N'Students',     N'Write',   N'Create, update, promote and withdraw students'),
  (N'admissions.manage',   N'Admissions',   N'Manage',  N'Process enquiries, applications and enrolment'),
  (N'parents.read',        N'Parents',      N'Read',    N'View parent and guardian records'),
  (N'fees.read',           N'Fees',         N'Read',    N'View fee structures, ledgers and reports'),
  (N'fees.collect',        N'Fees',         N'Collect', N'Record payments and issue receipts'),
  (N'fees.refund',         N'Fees',         N'Refund',  N'Approve and process refunds'),
  (N'attendance.read',     N'Attendance',   N'Read',    N'View attendance records'),
  (N'attendance.mark',     N'Attendance',   N'Mark',    N'Mark and correct attendance'),
  (N'exams.read',          N'Exams',        N'Read',    N'View examinations, marks and report cards'),
  (N'exams.manage',        N'Exams',        N'Manage',  N'Configure exams, enter/approve marks and publish results'),
  (N'homework.manage',     N'Homework',     N'Manage',  N'Create, grade and manage homework'),
  (N'hr.read',             N'HR',           N'Read',    N'View employees, leave and payroll'),
  (N'hr.manage',           N'HR',           N'Manage',  N'Manage employees, leave approvals and payroll'),
  (N'transport.manage',    N'Transport',    N'Manage',  N'Manage vehicles, routes and allocations'),
  (N'library.manage',      N'Library',      N'Manage',  N'Manage catalogue, issues and returns'),
  (N'inventory.manage',    N'Inventory',    N'Manage',  N'Manage stock, vendors and purchase orders'),
  (N'reports.read',        N'Reports',      N'Read',    N'Generate and export reports'),
  (N'settings.manage',     N'Settings',     N'Manage',  N'Configure school, sessions, classes and permissions'),
  (N'users.manage',        N'Users',        N'Manage',  N'Manage user accounts and role assignments'),
  (N'audit.read',          N'Audit',        N'Read',    N'View audit logs'),
  (N'lms.manage',          N'LMS',          N'Manage',  N'Publish study material and assessments'),
  (N'communication.manage',N'Communication',N'Manage',  N'Send announcements and notifications');

INSERT INTO [dbo].[Permission] ([Code], [Module], [Action], [Description])
SELECT p.[Code], p.[Module], p.[Action], p.[Description]
FROM @permissions p
WHERE NOT EXISTS (SELECT 1 FROM [dbo].[Permission] x WHERE x.[Code] = p.[Code]);

SELECT COUNT(*) AS [Permissions] FROM [dbo].[Permission];
GO
