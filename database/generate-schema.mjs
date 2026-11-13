#!/usr/bin/env node
/**
 * Generates database/schema.sql from the EF Core domain entities.
 *
 * EF Core's EnsureCreated() remains the source of truth for local development.
 * This script exists so DBAs can review and deploy an explicit script, and so the
 * schema stays close to the entities without a dotnet SDK on the build machine.
 *
 * Usage: node database/generate-schema.mjs
 */
import { readFileSync, writeFileSync, mkdirSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const here = dirname(fileURLToPath(import.meta.url));
const root = resolve(here, "..");
const entityDir = resolve(root, "backend/src/SchoolErp.Domain/Entities");
const files = ["Organisation.cs", "Academics.cs", "People.cs", "Finance.cs", "Operations.cs"];

const fkTargets = {
  School: "School", Branch: "Branch", AcademicSession: "AcademicSession", Class: "SchoolClass",
  Section: "Section", Subject: "Subject", Teacher: "Employee", Employee: "Employee",
  HeadEmployee: "Employee", ClassTeacher: "Employee", Student: "Student", Guardian: "Guardian",
  Admission: "Admission", Examination: "Examination", ExamSubject: "ExamSubject", Homework: "Homework",
  FeeCategory: "FeeCategory", FeeStructure: "FeeStructure", StudentFeeAssignment: "StudentFeeAssignment",
  FeeInstallment: "FeeInstallment", Payment: "Payment", Discount: "Discount", Refund: "Refund",
  ExpenseCategory: "ExpenseCategory", SalaryComponent: "SalaryComponent", PayrollRun: "PayrollRun",
  Department: "Department", Designation: "Designation", TransportRoute: "TransportRoute",
  TransportStop: "TransportStop", Vehicle: "Vehicle", Book: "Book", InventoryItem: "InventoryItem",
  Vendor: "Vendor", PurchaseOrder: "PurchaseOrder", Document: "Document", UserAccount: "UserAccount",
  Role: "Role", Permission: "Permission", Notification: "Notification", FeePenalty: "FeePenalty"
};

const entities = new Map();
const skipped = new Set(["FullName", "Balance", "IsOverdue", "IsActive"]);

for (const file of files) {
  const source = readFileSync(resolve(entityDir, file), "utf8");
  const classRe = /public class (\w+)\s*:\s*(AuditableEntity|Entity)/g;
  let match;
  while ((match = classRe.exec(source))) {
    const name = match[1];
    const kind = match[2];
    const open = source.indexOf("{", match.index);
    let depth = 0;
    let close = open;
    for (let i = open; i < source.length; i++) {
      if (source[i] === "{") depth++;
      else if (source[i] === "}") {
        depth--;
        if (depth === 0) { close = i; break; }
      }
    }
    const body = source.slice(open + 1, close);
    const props = [];
    for (const line of body.split(/\r?\n/)) {
      if (line.includes("[NotMapped]") || line.includes("=>")) continue;
      const p = line.match(/^\s*public\s+([\w.<>\[\]?]+)\s+(\w+)\s*\{\s*get;\s*set;\s*\}/);
      if (!p) continue;
      const [, type, propName] = p;
      if (skipped.has(propName)) continue;
      props.push({ type, name: propName });
    }
    entities.set(name, { name, kind, props });
  }
}

const stringLengths = {
  Email: 256, NormalizedEmail: 256, Phone: 32, GuardianPhone: 32, DriverPhone: 32, AlternatePhone: 32,
  Code: 32, CurrencyCode: 32, Country: 32, Name: 200, FullName: 200, LegalName: 200, Title: 200,
  GuardianName: 200, DriverName: 200, VendorName: 200, ContactPerson: 200, FatherName: 200, MotherName: 200,
  AdmissionNo: 64, EmployeeNo: 64, ApplicationNo: 64, OrderNo: 64, ReceiptNo: 64, VoucherNo: 64,
  RegistrationNo: 64, ReferenceNo: 64, GstNumber: 64, IdempotencyKey: 64, DriverLicenceNo: 64, Isbn: 64,
  NormalizedUserName: 256, TokenHash: 128, ReplacedByTokenHash: 128, PasswordHash: 512,
  PasswordResetTokenHash: 512, RefreshTokenHash: 512, Checksum: 512, ContentType: 512, StoragePath: 512,
  PublicUrl: 512, FileUrl: 512, PhotoUrl: 512, LogoUrl: 512, AttachmentUrl: 512, VideoUrl: 512,
  Address: 2000, AddressLine1: 2000, AddressLine2: 2000, Remarks: 2000, Description: 2000, Reason: 2000,
  Notes: 2000, Feedback: 2000, Error: 2000, FailureReason: 2000, RawResponse: 2000, BeforeJson: 2000,
  AfterJson: 2000, UserAgent: 2000, IpAddress: 128, CorrelationId: 128, Grade: 16, ResultStatus: 16
};

const enumNames = new Set([
  "Gender", "StudentStatus", "AdmissionStatus", "AttendanceStatus", "AttendanceSource", "EmploymentStatus",
  "PaymentMode", "PaymentStatus", "InstallmentStatus", "PaymentTransactionStatus", "RefundStatus",
  "DiscountType", "FeeFrequency", "NotificationChannel", "NotificationStatus", "LeaveStatus", "ExamType",
  "ExamStatus", "DocumentType", "BookTransactionType", "StockTransactionType", "SalaryComponentType",
  "PayrollStatus"
]);

function mapType(type, propName) {
  const nullable = type.endsWith("?");
  const bare = type.replace(/\?$/, "");
  let sql;
  if (bare === "int") sql = "INT";
  else if (bare === "long") sql = "BIGINT";
  else if (bare === "double") sql = "FLOAT";
  else if (bare === "string") sql = `NVARCHAR(${stringLengths[propName] ?? 256})`;
  else if (bare === "bool") sql = "BIT";
  else if (bare === "decimal") sql = propName === "Latitude" || propName === "Longitude" ? "DECIMAL(9,6)" : "DECIMAL(18,2)";
  else if (bare === "DateTime") sql = "DATETIME2";
  else if (bare === "DateOnly") sql = "DATE";
  else if (bare === "TimeSpan") sql = "TIME";
  else if (bare === "byte[]") sql = propName === "RowVersion" ? "ROWVERSION" : "VARBINARY(MAX)";
  else if (enumNames.has(bare)) sql = "INT";
  else throw new Error(`Unmapped type ${type} for ${propName}`);
  return nullable ? `${sql} NULL` : `${sql} NOT NULL`;
}

const auditableBase = [
  { name: "SchoolId", sql: "INT NOT NULL" },
  { name: "BranchId", sql: "INT NULL" },
  { name: "CreatedAtUtc", sql: "DATETIME2 NOT NULL CONSTRAINT [DF_${table}_CreatedAtUtc] DEFAULT SYSUTCDATETIME()" },
  { name: "CreatedBy", sql: "NVARCHAR(256) NULL" },
  { name: "ModifiedAtUtc", sql: "DATETIME2 NULL" },
  { name: "ModifiedBy", sql: "NVARCHAR(256) NULL" },
  { name: "IsDeleted", sql: "BIT NOT NULL CONSTRAINT [DF_${table}_IsDeleted] DEFAULT 0" },
  { name: "RowVersion", sql: "ROWVERSION NOT NULL" }
];

const tables = [...entities.values()].sort((a, b) => a.name.localeCompare(b.name));
const out = [];
out.push("/* Generated by database/generate-schema.mjs. Do not edit by hand. */");
out.push("/* SQL Server 2019+. EF Core EnsureCreated() is used for local development. */");
out.push("SET ANSI_NULLS ON;");
out.push("SET QUOTED_IDENTIFIER ON;");
out.push("GO");
out.push("");
out.push("IF SCHEMA_ID(N'dbo') IS NULL EXEC(N'CREATE SCHEMA dbo');");
out.push("GO");
out.push("");

for (const table of tables) {
  const base = table.kind === "AuditableEntity" ? auditableBase : [{ name: "Id", sql: "INT IDENTITY(1,1) NOT NULL PRIMARY KEY" }];
  const columns = [];
  if (table.kind === "AuditableEntity") {
    columns.push("[Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY");
  }
  for (const col of base) {
    if (table.kind === "AuditableEntity") {
      columns.push(`[${col.name}] ${col.sql.replaceAll("${table}", table.name)}`);
    } else if (col.name !== "Id") {
      columns.push(`[${col.name}] ${col.sql.replaceAll("${table}", table.name)}`);
    }
  }
  if (table.kind === "Entity") columns.push("[Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY");

  for (const prop of table.props) {
    if (prop.name === "Id") continue;
    columns.push(`[${prop.name}] ${mapType(prop.type, prop.name)}`);
  }
  out.push(`IF OBJECT_ID(N'dbo.${table.name}', N'U') IS NULL`);
  out.push("BEGIN");
  out.push(`  CREATE TABLE [dbo].[${table.name}] (`);
  out.push(columns.map(c => `    ${c}`).join(",\n"));
  out.push("  );");
  out.push("END;");
  out.push("GO");
  out.push("");
}

out.push("/* Foreign keys (added after all tables exist). */");
for (const table of tables) {
  for (const prop of table.props) {
    if (!prop.name.endsWith("Id") || prop.name === "Id") continue;
    const prefix = prop.name.slice(0, -2);
    const target = fkTargets[prefix];
    if (!target || target === table.name) continue;
    if (table.name === "AuditLog" && prop.name === "SchoolId") continue;
    if (!entities.has(target)) continue;
    const fk = `FK_${table.name}_${target}_${prop.name}`;
    out.push(`IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'${fk}')`);
    out.push(`  ALTER TABLE [dbo].[${table.name}] ADD CONSTRAINT [${fk}] FOREIGN KEY ([${prop.name}]) REFERENCES [dbo].[${target}] ([Id]) ON DELETE NO ACTION;`);
    out.push("GO");
  }
}
out.push("");
out.push("/* Unique indexes mirrored from AppDbContext.OnModelCreating. */");
const unique = [
  ["School", "Code", null],
  ["Permission", "Code", null],
  ["RefreshToken", "TokenHash", null],
  ["Branch", "SchoolId, Code", "[IsDeleted] = 0"],
  ["AcademicSession", "SchoolId, Name", "[IsDeleted] = 0"],
  ["SchoolClass", "SchoolId, Code", "[IsDeleted] = 0"],
  ["Section", "SchoolId, ClassId, Name", "[IsDeleted] = 0"],
  ["Subject", "SchoolId, Code", "[IsDeleted] = 0"],
  ["ClassSubject", "SchoolId, ClassId, SubjectId", "[IsDeleted] = 0"],
  ["TimetableEntry", "SchoolId, SectionId, DayOfWeek, PeriodNo", "[IsDeleted] = 0"],
  ["Student", "SchoolId, AdmissionNo", "[IsDeleted] = 0"],
  ["StudentEnrollment", "SchoolId, StudentId, AcademicSessionId", "[IsDeleted] = 0"],
  ["StudentParent", "StudentId, GuardianId", "[IsDeleted] = 0"],
  ["Admission", "SchoolId, ApplicationNo", "[IsDeleted] = 0"],
  ["Employee", "SchoolId, EmployeeNo", "[IsDeleted] = 0"],
  ["Department", "SchoolId, Code", "[IsDeleted] = 0"],
  ["Designation", "SchoolId, Code", "[IsDeleted] = 0"],
  ["UserAccount", "SchoolId, NormalizedEmail", "[IsDeleted] = 0"],
  ["Role", "SchoolId, Code", "[IsDeleted] = 0"],
  ["RolePermission", "RoleId, PermissionId", null],
  ["FeeCategory", "SchoolId, Code", "[IsDeleted] = 0"],
  ["FeeStructure", "SchoolId, AcademicSessionId, ClassId, Name", "[IsDeleted] = 0"],
  ["FeeStructureItem", "FeeStructureId, FeeCategoryId", "[IsDeleted] = 0"],
  ["FeeInstallment", "StudentFeeAssignmentId, Name", "[IsDeleted] = 0"],
  ["FeePenalty", "FeeInstallmentId, PenaltyDate", "[IsDeleted] = 0"],
  ["Payment", "SchoolId, ReceiptNo", "[IsDeleted] = 0"],
  ["Payment", "SchoolId, IdempotencyKey", "[IdempotencyKey] IS NOT NULL"],
  ["Discount", "SchoolId, Code", "[IsDeleted] = 0"],
  ["StudentDiscount", "SchoolId, StudentId, DiscountId, AcademicSessionId", "[IsDeleted] = 0"],
  ["ExpenseCategory", "SchoolId, Code", "[IsDeleted] = 0"],
  ["SalaryComponent", "SchoolId, Code", "[IsDeleted] = 0"],
  ["PayrollRun", "SchoolId, Year, Month", "[IsDeleted] = 0"],
  ["StudentAttendance", "SchoolId, StudentId, AttendanceDate", "[IsDeleted] = 0"],
  ["EmployeeAttendance", "SchoolId, EmployeeId, AttendanceDate", "[IsDeleted] = 0"],
  ["Vehicle", "SchoolId, RegistrationNo", "[IsDeleted] = 0"],
  ["TransportRoute", "SchoolId, Code", "[IsDeleted] = 0"],
  ["Book", "SchoolId, Isbn", "[Isbn] IS NOT NULL AND [IsDeleted] = 0"],
  ["InventoryItem", "SchoolId, Code", "[IsDeleted] = 0"],
  ["PurchaseOrder", "SchoolId, OrderNo", "[IsDeleted] = 0"],
  ["Examination", "SchoolId, AcademicSessionId, Name", "[IsDeleted] = 0"],
  ["ExamSubject", "ExaminationId, ClassId, SubjectId", "[IsDeleted] = 0"],
  ["StudentMark", "ExaminationId, ExamSubjectId, StudentId", "[IsDeleted] = 0"],
  ["ReportCard", "ExaminationId, StudentId", "[IsDeleted] = 0"]
];

let indexNo = 0;
for (const [table, cols, filter] of unique) {
  if (!entities.has(table)) continue;
  indexNo++;
  const name = `UX_${table}_${indexNo}`;
  const columnList = cols.split(",").map(c => `[${c.trim()}]`).join(", ");
  out.push(`IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'${name}' AND object_id = OBJECT_ID(N'dbo.${table}'))`);
  out.push(`  CREATE UNIQUE INDEX [${name}] ON [dbo].[${table}] (${columnList})${filter ? ` WHERE ${filter}` : ""};`);
  out.push("GO");
}

mkdirSync(here, { recursive: true });
writeFileSync(resolve(here, "schema.sql"), out.join("\n") + "\n", "utf8");
console.log(`Generated ${tables.length} tables with ${indexNo} unique indexes -> database/schema.sql`);
