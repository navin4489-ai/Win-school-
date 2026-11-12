using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SchoolErp.Application.Common;
using SchoolErp.Domain.Entities;
using SchoolErp.Domain.Enums;
using SchoolErp.Infrastructure.Identity;

namespace SchoolErp.Infrastructure.Persistence;

/// <summary>
/// Idempotent demo data. Runs only in Development and only when the database is empty.
/// All demo accounts use the password below; rotate or delete them before any real use.
/// </summary>
public sealed class DbInitializer
{
    public const string DemoPassword = "Passw0rd!";

    private readonly AppDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<DbInitializer> _logger;

    public DbInitializer(AppDbContext db, IPasswordHasher hasher, IDateTimeProvider clock,
        ILogger<DbInitializer> logger)
    {
        _db = db;
        _hasher = hasher;
        _clock = clock;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (await _db.Schools.AnyAsync(ct))
        {
            _logger.LogInformation("Demo data already present; skipping seed.");
            return;
        }

        _logger.LogInformation("Seeding demo school data...");
        var today = _clock.Today;
        var sessionStartYear = today.Month >= 4 ? today.Year : today.Year - 1;
        var passwordHash = _hasher.Hash(DemoPassword);

        var school = new School
        {
            Code = "DEMO",
            Name = "Wintech Demo Public School",
            LegalName = "Wintech Demo Education Trust",
            Email = "office@demo.edu",
            Phone = "+91-80-4000-1000",
            AddressLine1 = "12 Knowledge Park",
            AddressLine2 = "Indiranagar",
            City = "Bengaluru",
            State = "Karnataka",
            PostalCode = "560038",
            Country = "India",
            TimeZone = "Asia/Kolkata",
            CurrencyCode = "INR",
            IsActive = true
        };
        _db.Add(school);
        await _db.SaveChangesAsync(ct);

        var branch = new Branch
        {
            SchoolId = school.Id,
            Name = "Main Campus",
            Code = "MAIN",
            Address = "12 Knowledge Park, Indiranagar, Bengaluru",
            Phone = "+91-80-4000-1000",
            Email = "office@demo.edu",
            IsMainBranch = true
        };
        _db.Add(branch);
        await _db.SaveChangesAsync(ct);

        AcademicSession session = new()
        {
            SchoolId = school.Id,
            BranchId = branch.Id,
            Name = $"{sessionStartYear}-{sessionStartYear + 1}",
            StartDate = new DateOnly(sessionStartYear, 4, 1),
            EndDate = new DateOnly(sessionStartYear + 1, 3, 31),
            IsCurrent = true
        };
        _db.Add(session);

        foreach (var (key, value, description) in new[]
                 {
                     ("school.motto", "Knowledge with character", "Shown on report cards and receipts"),
                     ("fees.late_fee_percent", "2", "Late fee applied after the due date"),
                     ("attendance.lock_hours", "24", "Hours after which staff cannot edit attendance")
                 })
        {
            _db.Add(new SystemConfiguration
            {
                SchoolId = school.Id, BranchId = branch.Id, Key = key, Value = value, Description = description
            });
        }

        await _db.SaveChangesAsync(ct);

        // --- Permissions and roles -------------------------------------------------
        var existingPermissions = await _db.Permissions.ToListAsync(ct);
        foreach (var permission in Permissions.All)
        {
            if (existingPermissions.Any(p => p.Code == permission.Code)) continue;

            var entity = new Permission
            {
                Code = permission.Code,
                Module = permission.Module,
                Action = permission.Action,
                Description = permission.Description
            };
            _db.Add(entity);
            existingPermissions.Add(entity);
        }

        await _db.SaveChangesAsync(ct);
        var permissionByCode = existingPermissions.ToDictionary(p => p.Code, p => p);

        static string[] Codes(params string[] values) => values;
        var rolePermissions = new Dictionary<string, string[]>
        {
            [RoleCodes.SuperAdmin] = permissionByCode.Keys.ToArray(),
            [RoleCodes.SchoolOwner] = permissionByCode.Keys.ToArray(),
            [RoleCodes.Principal] = Codes(Permissions.StudentsRead, Permissions.StudentsWrite,
                Permissions.AdmissionsManage, Permissions.ParentsRead, Permissions.FeesRead,
                Permissions.FeesRefund, Permissions.AttendanceRead, Permissions.ExamsRead,
                Permissions.ExamsManage, Permissions.HomeworkManage, Permissions.HrRead,
                Permissions.HrManage, Permissions.TransportManage, Permissions.LibraryManage,
                Permissions.InventoryManage, Permissions.ReportsRead, Permissions.SettingsManage,
                Permissions.UsersManage, Permissions.AuditRead, Permissions.LmsManage,
                Permissions.CommunicationManage),
            [RoleCodes.Administrator] = Codes(Permissions.StudentsRead, Permissions.StudentsWrite,
                Permissions.AdmissionsManage, Permissions.ParentsRead, Permissions.FeesRead,
                Permissions.FeesCollect, Permissions.AttendanceRead, Permissions.AttendanceMark,
                Permissions.ExamsRead, Permissions.ExamsManage, Permissions.HrRead,
                Permissions.TransportManage, Permissions.LibraryManage, Permissions.InventoryManage,
                Permissions.ReportsRead, Permissions.SettingsManage, Permissions.UsersManage,
                Permissions.CommunicationManage),
            [RoleCodes.Accountant] = Codes(Permissions.StudentsRead, Permissions.ParentsRead,
                Permissions.FeesRead, Permissions.FeesCollect, Permissions.FeesRefund,
                Permissions.HrRead, Permissions.ReportsRead),
            [RoleCodes.AdmissionManager] = Codes(Permissions.StudentsRead, Permissions.AdmissionsManage,
                Permissions.ParentsRead, Permissions.FeesRead, Permissions.ReportsRead,
                Permissions.CommunicationManage),
            [RoleCodes.Teacher] = Codes(Permissions.StudentsRead, Permissions.ParentsRead,
                Permissions.AttendanceRead, Permissions.AttendanceMark, Permissions.ExamsRead,
                Permissions.ExamsManage, Permissions.HomeworkManage, Permissions.LmsManage),
            [RoleCodes.ClassCoordinator] = Codes(Permissions.StudentsRead, Permissions.ParentsRead,
                Permissions.AttendanceRead, Permissions.AttendanceMark, Permissions.ExamsRead,
                Permissions.ExamsManage, Permissions.HomeworkManage, Permissions.LmsManage,
                Permissions.ReportsRead, Permissions.CommunicationManage),
            [RoleCodes.Student] = Codes(Permissions.AttendanceRead, Permissions.ExamsRead,
                Permissions.FeesRead),
            [RoleCodes.Parent] = Codes(Permissions.ParentsRead, Permissions.AttendanceRead,
                Permissions.ExamsRead, Permissions.FeesRead),
            [RoleCodes.HrManager] = Codes(Permissions.HrRead, Permissions.HrManage,
                Permissions.ReportsRead, Permissions.UsersManage),
            [RoleCodes.Librarian] = Codes(Permissions.LibraryManage, Permissions.StudentsRead),
            [RoleCodes.TransportManager] = Codes(Permissions.TransportManage, Permissions.StudentsRead,
                Permissions.ReportsRead),
            [RoleCodes.TransportStaff] = Codes(Permissions.TransportManage, Permissions.StudentsRead)
        };

        var roles = new Dictionary<string, Role>();
        foreach (var (code, name) in new[]
                 {
                     (RoleCodes.SuperAdmin, "Super Admin"),
                     (RoleCodes.SchoolOwner, "School Owner"),
                     (RoleCodes.Principal, "Principal"),
                     (RoleCodes.Administrator, "Administrator"),
                     (RoleCodes.Accountant, "Accountant"),
                     (RoleCodes.AdmissionManager, "Admission Manager"),
                     (RoleCodes.Teacher, "Teacher"),
                     (RoleCodes.ClassCoordinator, "Class Coordinator"),
                     (RoleCodes.Student, "Student"),
                     (RoleCodes.Parent, "Parent"),
                     (RoleCodes.HrManager, "HR Manager"),
                     (RoleCodes.Librarian, "Librarian"),
                     (RoleCodes.TransportManager, "Transport Manager"),
                     (RoleCodes.TransportStaff, "Transport Staff")
                 })
        {
            var role = new Role
            {
                SchoolId = school.Id,
                BranchId = branch.Id,
                Name = name,
                Code = code,
                Description = $"{name} role",
                IsSystem = true
            };
            _db.Add(role);
            roles[code] = role;
        }

        await _db.SaveChangesAsync(ct);

        foreach (var (roleCode, codes) in rolePermissions)
        {
            var role = roles[roleCode];
            foreach (var code in codes.Distinct())
            {
                _db.Add(new RolePermission { RoleId = role.Id, PermissionId = permissionByCode[code].Id });
            }
        }

        await _db.SaveChangesAsync(ct);

        // --- Departments, designations, employees ---------------------------------
        var academicDept = new Department { SchoolId = school.Id, BranchId = branch.Id, Name = "Academics", Code = "ACAD" };
        var financeDept = new Department { SchoolId = school.Id, BranchId = branch.Id, Name = "Finance", Code = "FIN" };
        var opsDept = new Department { SchoolId = school.Id, BranchId = branch.Id, Name = "Operations", Code = "OPS" };
        _db.AddRange(academicDept, financeDept, opsDept);

        var principalDesignation = new Designation { SchoolId = school.Id, BranchId = branch.Id, Name = "Principal", Code = "PRIN", Level = 1 };
        var teacherDesignation = new Designation { SchoolId = school.Id, BranchId = branch.Id, Name = "Teacher", Code = "TCHR", Level = 3 };
        var accountantDesignation = new Designation { SchoolId = school.Id, BranchId = branch.Id, Name = "Accountant", Code = "ACCT", Level = 3 };
        var admissionDesignation = new Designation { SchoolId = school.Id, BranchId = branch.Id, Name = "Admission Officer", Code = "ADMO", Level = 3 };
        var hrDesignation = new Designation { SchoolId = school.Id, BranchId = branch.Id, Name = "HR Manager", Code = "HRM", Level = 2 };
        var librarianDesignation = new Designation { SchoolId = school.Id, BranchId = branch.Id, Name = "Librarian", Code = "LIB", Level = 4 };
        var transportDesignation = new Designation { SchoolId = school.Id, BranchId = branch.Id, Name = "Transport Manager", Code = "TRN", Level = 3 };
        _db.AddRange(principalDesignation, teacherDesignation, accountantDesignation, admissionDesignation,
            hrDesignation, librarianDesignation, transportDesignation);
        await _db.SaveChangesAsync(ct);

        Employee Staff(string no, string first, string last, string email, Department dept, Designation designation,
            decimal salary, Gender gender = Gender.Female)
        {
            var employee = new Employee
            {
                SchoolId = school.Id,
                BranchId = branch.Id,
                EmployeeNo = no,
                FirstName = first,
                LastName = last,
                Email = email,
                Phone = "+91-98765-1000" + no[^1],
                Gender = gender,
                DepartmentId = dept.Id,
                DesignationId = designation.Id,
                JoiningDate = today.AddYears(-4),
                Status = EmploymentStatus.Active,
                Qualification = "M.Ed.",
                ExperienceYears = 6,
                Address = "Bengaluru",
                BasicSalary = salary
            };
            _db.Add(employee);
            return employee;
        }

        var principal = Staff("EMP001", "Meera", "Rao", "principal@demo.edu", academicDept, principalDesignation, 120000m);
        var teacher = Staff("EMP002", "Rahul", "Menon", "teacher@demo.edu", academicDept, teacherDesignation, 62000m, Gender.Male);
        var accountant = Staff("EMP003", "Nisha", "Kulkarni", "accounts@demo.edu", financeDept, accountantDesignation, 58000m);
        var admissionOfficer = Staff("EMP004", "Priya", "Nair", "admissions@demo.edu", academicDept, admissionDesignation, 54000m);
        var hrManager = Staff("EMP005", "Sandeep", "Bose", "hr@demo.edu", opsDept, hrDesignation, 70000m, Gender.Male);
        var librarian = Staff("EMP006", "Kavya", "Rao", "librarian@demo.edu", academicDept, librarianDesignation, 42000m);
        var transportManager = Staff("EMP007", "Imran", "Sheikh", "transport@demo.edu", opsDept, transportDesignation, 48000m, Gender.Male);
        await _db.SaveChangesAsync(ct);

        // --- Classes, sections, subjects, timetable scope --------------------------
        SchoolClass Grade(string name, string code, int order)
        {
            var entity = new SchoolClass
            {
                SchoolId = school.Id, BranchId = branch.Id, Name = name, Code = code, DisplayOrder = order, IsActive = true
            };
            _db.Add(entity);
            return entity;
        }

        var class1 = Grade("Class 1", "C1", 1);
        var class2 = Grade("Class 2", "C2", 2);
        var class3 = Grade("Class 3", "C3", 3);
        var class4 = Grade("Class 4", "C4", 4);
        var class5 = Grade("Class 5", "C5", 5);
        await _db.SaveChangesAsync(ct);

        Section SectionFor(SchoolClass klass, string name, int capacity)
        {
            var entity = new Section
            {
                SchoolId = school.Id, BranchId = branch.Id, ClassId = klass.Id, Name = name,
                Capacity = capacity, ClassTeacherId = teacher.Id, IsActive = true
            };
            _db.Add(entity);
            return entity;
        }

        var class1A = SectionFor(class1, "A", 40);
        var class1B = SectionFor(class1, "B", 40);
        var class2A = SectionFor(class2, "A", 40);
        var class3A = SectionFor(class3, "A", 40);
        var class4A = SectionFor(class4, "A", 40);
        var class5A = SectionFor(class5, "A", 40);
        await _db.SaveChangesAsync(ct);

        Subject SubjectFor(string name, string code, bool elective = false)
        {
            var entity = new Subject
            {
                SchoolId = school.Id, BranchId = branch.Id, Name = name, Code = code, IsElective = elective
            };
            _db.Add(entity);
            return entity;
        }

        var english = SubjectFor("English", "ENG");
        var maths = SubjectFor("Mathematics", "MAT");
        var science = SubjectFor("Science", "SCI");
        var social = SubjectFor("Social Studies", "SOC");
        var hindi = SubjectFor("Hindi", "HIN");
        var computer = SubjectFor("Computer Science", "CSC");
        var art = SubjectFor("Art", "ART", elective: true);
        await _db.SaveChangesAsync(ct);

        var subjects = new[] { english, maths, science, social, hindi, computer };
        foreach (var klass in new[] { class1, class2, class3, class4, class5 })
        {
            foreach (var subject in subjects)
            {
                _db.Add(new ClassSubject
                {
                    SchoolId = school.Id, BranchId = branch.Id, ClassId = klass.Id,
                    SubjectId = subject.Id, TeacherId = teacher.Id, IsActive = true
                });
            }
        }

        _db.Add(new TeacherAssignment
        {
            SchoolId = school.Id, BranchId = branch.Id, EmployeeId = teacher.Id,
            ClassId = class1.Id, SectionId = class1A.Id, IsClassTeacher = true
        });
        _db.Add(new TeacherAssignment
        {
            SchoolId = school.Id, BranchId = branch.Id, EmployeeId = teacher.Id,
            ClassId = class2.Id, SectionId = class2A.Id, IsClassTeacher = true
        });
        await _db.SaveChangesAsync(ct);

        // --- Guardians, students, enrolments --------------------------------------
        Guardian GuardianFor(string first, string last, string relation, string phone, string? email)
        {
            var entity = new Guardian
            {
                SchoolId = school.Id, BranchId = branch.Id, FirstName = first, LastName = last,
                Relation = relation, Phone = phone, Email = email, Occupation = "Professional"
            };
            _db.Add(entity);
            return entity;
        }

        var primaryGuardian = GuardianFor("Rohit", "Sharma", "Father", "9876500001", "parent@demo.edu");
        var secondGuardian = GuardianFor("Neha", "Verma", "Mother", "9876500002", "neha.verma@example.com");
        var thirdGuardian = GuardianFor("Kiran", "Patel", "Father", "9876500003", "kiran.patel@example.com");
        var fourthGuardian = GuardianFor("Asha", "Gupta", "Mother", "9876500004", "asha.gupta@example.com");
        var fifthGuardian = GuardianFor("Farhan", "Khan", "Father", "9876500005", "farhan.khan@example.com");
        await _db.SaveChangesAsync(ct);

        Student StudentFor(string first, string last, Gender gender, DateOnly dob, SchoolClass klass, Section section,
            string rollNo, string admissionNo, string? medical = null)
        {
            var entity = new Student
            {
                SchoolId = school.Id, BranchId = branch.Id, AdmissionNo = admissionNo,
                AcademicSessionId = session.Id, ClassId = klass.Id, SectionId = section.Id,
                RollNo = rollNo, FirstName = first, LastName = last, DateOfBirth = dob,
                Gender = gender, BloodGroup = "O+", Email = $"{first.ToLowerInvariant()}.{last.ToLowerInvariant()}@student.demo.edu",
                Phone = null, Address = "Indiranagar, Bengaluru", City = "Bengaluru", State = "Karnataka",
                PostalCode = "560038", AdmissionDate = session.StartDate, Status = StudentStatus.Active,
                MedicalNotes = medical
            };
            _db.Add(entity);
            return entity;
        }

        var aarav = StudentFor("Aarav", "Sharma", Gender.Male, new DateOnly(sessionStartYear - 7, 6, 15),
            class1, class1A, "1", $"ADM-{sessionStartYear}-001");
        var diya = StudentFor("Diya", "Sharma", Gender.Female, new DateOnly(sessionStartYear - 8, 2, 8),
            class2, class2A, "1", $"ADM-{sessionStartYear}-002");
        var ananya = StudentFor("Ananya", "Verma", Gender.Female, new DateOnly(sessionStartYear - 7, 9, 21),
            class1, class1A, "2", $"ADM-{sessionStartYear}-003");
        var vihaan = StudentFor("Vihaan", "Patel", Gender.Male, new DateOnly(sessionStartYear - 7, 4, 2),
            class1, class1A, "3", $"ADM-{sessionStartYear}-004");
        var ishaan = StudentFor("Ishaan", "Gupta", Gender.Male, new DateOnly(sessionStartYear - 7, 12, 11),
            class1, class1A, "4", $"ADM-{sessionStartYear}-005");
        var sara = StudentFor("Sara", "Khan", Gender.Female, new DateOnly(sessionStartYear - 7, 7, 30),
            class1, class1A, "5", $"ADM-{sessionStartYear}-006", "Mild asthma - inhaler in school nurse office");
        var advait = StudentFor("Advait", "Joshi", Gender.Male, new DateOnly(sessionStartYear - 7, 1, 19),
            class1, class1A, "6", $"ADM-{sessionStartYear}-007");
        var myra = StudentFor("Myra", "Nair", Gender.Female, new DateOnly(sessionStartYear - 7, 5, 5),
            class1, class1A, "7", $"ADM-{sessionStartYear}-008");
        var kabir = StudentFor("Kabir", "Singh", Gender.Male, new DateOnly(sessionStartYear - 7, 8, 14),
            class1, class1A, "8", $"ADM-{sessionStartYear}-009");
        var riya = StudentFor("Riya", "Mehta", Gender.Female, new DateOnly(sessionStartYear - 8, 3, 27),
            class2, class2A, "2", $"ADM-{sessionStartYear}-010");
        var arjun = StudentFor("Arjun", "Reddy", Gender.Male, new DateOnly(sessionStartYear - 8, 11, 9),
            class2, class2A, "3", $"ADM-{sessionStartYear}-011");
        var tara = StudentFor("Tara", "Iyer", Gender.Female, new DateOnly(sessionStartYear - 8, 6, 18),
            class2, class2A, "4", $"ADM-{sessionStartYear}-012");
        await _db.SaveChangesAsync(ct);

        void Link(Student student, Guardian guardian, bool primary = false)
        {
            _db.Add(new StudentParent
            {
                SchoolId = school.Id, BranchId = branch.Id, StudentId = student.Id,
                GuardianId = guardian.Id, IsPrimary = primary, IsEmergencyContact = primary
            });
        }

        Link(aarav, primaryGuardian, true);
        Link(diya, primaryGuardian, true);
        Link(ananya, secondGuardian, true);
        Link(vihaan, thirdGuardian, true);
        Link(ishaan, fourthGuardian, true);
        Link(sara, fifthGuardian, true);
        Link(advait, secondGuardian);
        Link(myra, thirdGuardian);
        Link(kabir, fourthGuardian);
        Link(riya, fifthGuardian, true);
        Link(arjun, secondGuardian, true);
        Link(tara, thirdGuardian, true);
        await _db.SaveChangesAsync(ct);

        foreach (var student in new[] { aarav, diya, ananya, vihaan, ishaan, sara, advait, myra, kabir, riya, arjun, tara })
        {
            _db.Add(new StudentEnrollment
            {
                SchoolId = school.Id, BranchId = branch.Id, StudentId = student.Id,
                AcademicSessionId = session.Id, ClassId = student.ClassId, SectionId = student.SectionId,
                RollNo = student.RollNo, StartDate = session.StartDate, Status = "Active"
            });
        }

        await _db.SaveChangesAsync(ct);

        // --- User accounts --------------------------------------------------------
        UserAccount Login(string email, string fullName, string roleCode, Employee? employee = null,
            Guardian? guardian = null, Student? student = null)
        {
            var normalized = email.Trim().ToLowerInvariant();
            var user = new UserAccount
            {
                SchoolId = school.Id,
                BranchId = branch.Id,
                Email = normalized,
                NormalizedEmail = normalized,
                PasswordHash = passwordHash,
                FullName = fullName,
                Phone = employee is null ? null : "+91-98765-2000",
                RoleId = roles[roleCode].Id,
                EmployeeId = employee?.Id,
                GuardianId = guardian?.Id,
                StudentId = student?.Id,
                IsActive = true,
                EmailConfirmed = true,
                TokenVersion = 1
            };
            _db.Add(user);
            return user;
        }

        Login("superadmin@demo.edu", "Wintech Super Admin", RoleCodes.SuperAdmin);
        Login("principal@demo.edu", principal.FullName, RoleCodes.Principal, principal);
        Login("teacher@demo.edu", teacher.FullName, RoleCodes.Teacher, teacher);
        Login("accounts@demo.edu", accountant.FullName, RoleCodes.Accountant, accountant);
        Login("admissions@demo.edu", admissionOfficer.FullName, RoleCodes.AdmissionManager, admissionOfficer);
        Login("hr@demo.edu", hrManager.FullName, RoleCodes.HrManager, hrManager);
        Login("librarian@demo.edu", librarian.FullName, RoleCodes.Librarian, librarian);
        Login("transport@demo.edu", transportManager.FullName, RoleCodes.TransportManager, transportManager);
        var parentUser = Login("parent@demo.edu", "Rohit Sharma", RoleCodes.Parent, guardian: primaryGuardian);
        Login("student@demo.edu", "Aarav Sharma", RoleCodes.Student, student: aarav);
        await _db.SaveChangesAsync(ct);

        // --- Fees ------------------------------------------------------------------
        FeeCategory Category(string name, string code, bool recurring = true)
        {
            var entity = new FeeCategory
            {
                SchoolId = school.Id, BranchId = branch.Id, Name = name, Code = code, IsRecurring = recurring
            };
            _db.Add(entity);
            return entity;
        }

        var tuition = Category("Tuition Fee", "TUITION");
        var examFee = Category("Examination Fee", "EXAM");
        var libraryFee = Category("Library Fee", "LIBRARY");
        var transportFee = Category("Transport Fee", "TRANSPORT");
        var admissionFee = Category("Admission Fee", "ADMISSION", recurring: false);
        await _db.SaveChangesAsync(ct);

        FeeStructure Structure(SchoolClass klass, string name, decimal tuitionAmount, decimal examAmount,
            decimal libraryAmount)
        {
            var total = tuitionAmount + examAmount + libraryAmount;
            var entity = new FeeStructure
            {
                SchoolId = school.Id, BranchId = branch.Id, AcademicSessionId = session.Id,
                ClassId = klass.Id, Name = name, TotalAmount = total, IsActive = true
            };
            _db.Add(entity);
            return entity;
        }

        var class1Structure = Structure(class1, "Class 1 Annual 2026", 24000m, 2000m, 1000m);
        var class2Structure = Structure(class2, "Class 2 Annual 2026", 27000m, 2000m, 1000m);
        await _db.SaveChangesAsync(ct);

        void StructureItems(FeeStructure structure, decimal tuitionAmount, decimal examAmount, decimal libraryAmount)
        {
            _db.AddRange(
                new FeeStructureItem
                {
                    SchoolId = school.Id, BranchId = branch.Id, FeeStructureId = structure.Id,
                    FeeCategoryId = tuition.Id, Amount = tuitionAmount, Frequency = FeeFrequency.Quarterly, DueDay = 10
                },
                new FeeStructureItem
                {
                    SchoolId = school.Id, BranchId = branch.Id, FeeStructureId = structure.Id,
                    FeeCategoryId = examFee.Id, Amount = examAmount, Frequency = FeeFrequency.Annual, DueDay = 10
                },
                new FeeStructureItem
                {
                    SchoolId = school.Id, BranchId = branch.Id, FeeStructureId = structure.Id,
                    FeeCategoryId = libraryFee.Id, Amount = libraryAmount, Frequency = FeeFrequency.Annual, DueDay = 10
                });
        }

        StructureItems(class1Structure, 24000m, 2000m, 1000m);
        StructureItems(class2Structure, 27000m, 2000m, 1000m);
        await _db.SaveChangesAsync(ct);

        var installmentCounter = 0;
        var receiptCounter = 0;
        StudentFeeAssignment Assign(Student student, FeeStructure structure)
        {
            var assignment = new StudentFeeAssignment
            {
                SchoolId = school.Id, BranchId = branch.Id, StudentId = student.Id,
                FeeStructureId = structure.Id, AcademicSessionId = session.Id,
                TotalAmount = structure.TotalAmount, DiscountAmount = 0,
                NetAmount = structure.TotalAmount, IsActive = true
            };
            _db.Add(assignment);
            return assignment;
        }

        var assignments = new List<(Student Student, StudentFeeAssignment Assignment)>
        {
            (aarav, Assign(aarav, class1Structure)),
            (diya, Assign(diya, class2Structure)),
            (ananya, Assign(ananya, class1Structure)),
            (vihaan, Assign(vihaan, class1Structure)),
            (ishaan, Assign(ishaan, class1Structure)),
            (sara, Assign(sara, class1Structure)),
            (advait, Assign(advait, class1Structure)),
            (myra, Assign(myra, class1Structure)),
            (kabir, Assign(kabir, class1Structure)),
            (riya, Assign(riya, class2Structure)),
            (arjun, Assign(arjun, class2Structure)),
            (tara, Assign(tara, class2Structure))
        };
        await _db.SaveChangesAsync(ct);

        foreach (var (student, assignment) in assignments)
        {
            var amounts = new[]
            {
                Math.Round(assignment.NetAmount * 0.4m, 2),
                Math.Round(assignment.NetAmount * 0.3m, 2),
                Math.Round(assignment.NetAmount * 0.3m, 2)
            };
            var names = new[] { "Term 1", "Term 2", "Term 3" };
            var dueMonths = new[] { 4, 7, 10 };
            for (var i = 0; i < 3; i++)
            {
                installmentCounter++;
                var installment = new FeeInstallment
                {
                    SchoolId = school.Id, BranchId = branch.Id,
                    StudentFeeAssignmentId = assignment.Id, Name = names[i],
                    DueDate = new DateOnly(sessionStartYear, dueMonths[i], 10),
                    Amount = amounts[i], PaidAmount = 0, Status = InstallmentStatus.Pending
                };
                _db.Add(installment);
                await _db.SaveChangesAsync(ct);
                if (student.Id == aarav.Id && i < 2)
                {
                    receiptCounter++;
                    var receiptNo = $"RCPT-{sessionStartYear}-{receiptCounter:0000}";
                    _db.Add(new Payment
                    {
                        SchoolId = school.Id, BranchId = branch.Id, ReceiptNo = receiptNo,
                        StudentId = aarav.Id, StudentFeeAssignmentId = assignment.Id,
                        FeeInstallmentId = installment.Id, Amount = installment.Amount,
                        Mode = PaymentMode.Upi, Status = PaymentStatus.Success,
                        PaidAtUtc = today.AddDays(-(10 - i)).ToDateTime(new TimeOnly(10, 0), DateTimeKind.Utc), ReferenceNo = $"UPI-{receiptCounter:000000}",
                        CollectedBy = accountant.Id, Remarks = "Demo seed payment"
                    });
                    installment.PaidAmount = installment.Amount;
                    installment.Status = InstallmentStatus.Paid;
                }

                if (student.Id == diya.Id && i == 0)
                {
                    receiptCounter++;
                    _db.Add(new Payment
                    {
                        SchoolId = school.Id, BranchId = branch.Id, ReceiptNo = $"RCPT-{sessionStartYear}-{receiptCounter:0000}",
                        StudentId = diya.Id, StudentFeeAssignmentId = assignment.Id,
                        FeeInstallmentId = installment.Id, Amount = 6000m,
                        Mode = PaymentMode.BankTransfer, Status = PaymentStatus.Success,
                        PaidAtUtc = today.AddDays(-3).ToDateTime(new TimeOnly(10, 0), DateTimeKind.Utc), ReferenceNo = "NEFT-778812",
                        CollectedBy = accountant.Id, Remarks = "Part payment"
                    });
                    installment.PaidAmount = 6000m;
                    installment.Status = InstallmentStatus.PartiallyPaid;
                }

                if (student.Id == ananya.Id && i == 0)
                {
                    receiptCounter++;
                    _db.Add(new Payment
                    {
                        SchoolId = school.Id, BranchId = branch.Id, ReceiptNo = $"RCPT-{sessionStartYear}-{receiptCounter:0000}",
                        StudentId = ananya.Id, StudentFeeAssignmentId = assignment.Id,
                        FeeInstallmentId = installment.Id, Amount = installment.Amount,
                        Mode = PaymentMode.Cash, Status = PaymentStatus.Success,
                        PaidAtUtc = today.AddDays(-5).ToDateTime(new TimeOnly(10, 0), DateTimeKind.Utc), CollectedBy = accountant.Id
                    });
                    installment.PaidAmount = installment.Amount;
                    installment.Status = InstallmentStatus.Paid;
                }

                await _db.SaveChangesAsync(ct);
            }
        }

        // --- Academics: exam, marks, report cards ---------------------------------
        var examination = new Examination
        {
            SchoolId = school.Id, BranchId = branch.Id, AcademicSessionId = session.Id,
            Name = "Term 1 Assessment", ExamType = ExamType.UnitTest,
            StartDate = today.AddDays(-20), EndDate = today.AddDays(-18),
            Status = ExamStatus.Published, IsRankEnabled = true,
            PublishedAtUtc = today.AddDays(-10).ToDateTime(new TimeOnly(10, 0), DateTimeKind.Utc), ApprovedBy = principal.Id, ApprovedAtUtc = today.AddDays(-12).ToDateTime(new TimeOnly(10, 0), DateTimeKind.Utc)
        };
        _db.Add(examination);
        await _db.SaveChangesAsync(ct);

        var examSubjects = new List<ExamSubject>();
        foreach (var subject in new[] { english, maths, science })
        {
            var examSubject = new ExamSubject
            {
                SchoolId = school.Id, BranchId = branch.Id, ExaminationId = examination.Id,
                ClassId = class1.Id, SubjectId = subject.Id, MaxTheoryMarks = 100,
                MaxPracticalMarks = 0, PassMarks = 33, ExamDate = today.AddDays(-20)
            };
            _db.Add(examSubject);
            examSubjects.Add(examSubject);
        }

        await _db.SaveChangesAsync(ct);

        var class1Students = new[] { aarav, ananya, vihaan, ishaan, sara, advait, myra, kabir };
        var marksByStudent = new Dictionary<int, decimal>();
        for (var index = 0; index < class1Students.Length; index++)
        {
            var student = class1Students[index];
            var total = 0m;
            foreach (var examSubject in examSubjects)
            {
                var theory = 62m + index * 3 + examSubject.SubjectId % 7;
                theory = Math.Min(97m, theory);
                total += theory;
                _db.Add(new StudentMark
                {
                    SchoolId = school.Id, BranchId = branch.Id, ExaminationId = examination.Id,
                    ExamSubjectId = examSubject.Id, StudentId = student.Id,
                    TheoryMarks = theory, IsAbsent = false, EnteredByEmployeeId = teacher.Id
                });
            }

            marksByStudent[student.Id] = total;
        }

        await _db.SaveChangesAsync(ct);

        var ranked = marksByStudent.OrderByDescending(x => x.Value).ToList();
        for (var i = 0; i < ranked.Count; i++)
        {
            var studentId = ranked[i].Key;
            var obtained = ranked[i].Value;
            var percentage = Math.Round(obtained / 300m * 100m, 2);
            _db.Add(new ReportCard
            {
                SchoolId = school.Id, BranchId = branch.Id, ExaminationId = examination.Id,
                StudentId = studentId, ClassId = class1.Id, SectionId = class1A.Id,
                TotalMarks = 300, ObtainedMarks = obtained, Percentage = percentage,
                Grade = percentage >= 90 ? "A+" : percentage >= 80 ? "A" : percentage >= 70 ? "B" : "C",
                Rank = i + 1, ResultStatus = "Pass", PublishedAtUtc = examination.PublishedAtUtc
            });
        }

        await _db.SaveChangesAsync(ct);

        // --- Attendance -----------------------------------------------------------
        var attendanceDays = new List<DateOnly>();
        var cursor = today;
        while (attendanceDays.Count < 10)
        {
            if (cursor.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
                attendanceDays.Add(cursor);
            cursor = cursor.AddDays(-1);
        }

        var attendanceStudents = new[] { aarav, diya, ananya, vihaan, ishaan, sara, advait, myra, kabir, riya, arjun, tara };
        for (var index = 0; index < attendanceStudents.Length; index++)
        {
            var student = attendanceStudents[index];
            for (var dayIndex = 0; dayIndex < attendanceDays.Count; dayIndex++)
            {
                var status = (index + dayIndex) % 9 == 0 ? AttendanceStatus.Absent :
                    (index + dayIndex) % 13 == 0 ? AttendanceStatus.Late : AttendanceStatus.Present;
                _db.Add(new StudentAttendance
                {
                    SchoolId = school.Id, BranchId = branch.Id, StudentId = student.Id,
                    ClassId = student.ClassId, SectionId = student.SectionId,
                    AttendanceDate = attendanceDays[dayIndex], Status = status,
                    MarkedByEmployeeId = teacher.Id, Source = AttendanceSource.Manual
                });
            }
        }

        var staffAttendance = new[] { principal, teacher, accountant, admissionOfficer, hrManager, librarian, transportManager };
        for (var index = 0; index < staffAttendance.Length; index++)
        {
            _db.Add(new EmployeeAttendance
            {
                SchoolId = school.Id, BranchId = branch.Id, EmployeeId = staffAttendance[index].Id,
                AttendanceDate = attendanceDays[1],
                CheckInUtc = today.ToDateTime(new TimeOnly(8, 45), DateTimeKind.Utc),
                Status = index == 5 ? AttendanceStatus.Leave : AttendanceStatus.Present,
                Source = AttendanceSource.Biometric
            });
        }

        await _db.SaveChangesAsync(ct);

        // --- Timetable, homework --------------------------------------------------
        for (var day = 1; day <= 5; day++)
        {
            for (var period = 1; period <= 4; period++)
            {
                var subject = subjects[(day + period) % subjects.Length];
                _db.Add(new TimetableEntry
                {
                    SchoolId = school.Id, BranchId = branch.Id, ClassId = class1.Id,
                    SectionId = class1A.Id, SubjectId = subject.Id, EmployeeId = teacher.Id,
                    DayOfWeek = day, PeriodNo = period,
                    StartTime = TimeSpan.FromHours(8 + period),
                    EndTime = TimeSpan.FromHours(9 + period),
                    Room = "Room 101"
                });
            }
        }

        _db.Add(new Homework
        {
            SchoolId = school.Id, BranchId = branch.Id, ClassId = class1.Id,
            SectionId = class1A.Id, SubjectId = english.Id, EmployeeId = teacher.Id,
            Title = "Read chapter 2 and write a summary",
            Description = "Write 5 to 8 sentences in your own words. Bring the notebook tomorrow.",
            DueAtUtc = _clock.UtcNow.AddDays(2), MaxMarks = 10, IsPublished = true
        });
        _db.Add(new StudyMaterial
        {
            SchoolId = school.Id, BranchId = branch.Id, ClassId = class1.Id,
            SectionId = class1A.Id, SubjectId = maths.Id, Title = "Number bonds worksheet",
            Description = "Practice worksheet for numbers up to 20.", Chapter = "Chapter 1",
            IsPublished = true
        });

        _db.Add(new Announcement
        {
            SchoolId = school.Id, BranchId = branch.Id, Title = "Annual Sports Day",
            Body = "The annual sports day will be held next month. Please ensure students wear their house colours.",
            Audience = "All", PublishedAtUtc = _clock.UtcNow.AddDays(-1),
            ExpiresAtUtc = _clock.UtcNow.AddDays(30), CreatedByEmployeeId = principal.Id
        });

        _db.Add(new LeaveApplication
        {
            SchoolId = school.Id, BranchId = branch.Id, EmployeeId = teacher.Id,
            LeaveType = "Casual", FromDate = today.AddDays(10), ToDate = today.AddDays(11),
            Reason = "Family function", Status = LeaveStatus.Pending
        });
        await _db.SaveChangesAsync(ct);

        // --- Transport, library, inventory, expenses ------------------------------
        var vehicle = new Vehicle
        {
            SchoolId = school.Id, BranchId = branch.Id, RegistrationNo = "KA01AB1234",
            Model = "Tata Starbus", Capacity = 42, DriverName = "Suresh Kumar",
            DriverPhone = "+91-98765-3000", DriverLicenceNo = "KA0120180001234",
            InsuranceExpiry = today.AddMonths(6), FitnessExpiry = today.AddMonths(8), IsActive = true
        };
        _db.Add(vehicle);
        await _db.SaveChangesAsync(ct);

        var route = new TransportRoute
        {
            SchoolId = school.Id, BranchId = branch.Id, Name = "Indiranagar Route",
            Code = "R-01", VehicleId = vehicle.Id, StartPoint = "Indiranagar 100 ft Road",
            EndPoint = "School", MonthlyFee = 2500m, IsActive = true
        };
        _db.Add(route);
        await _db.SaveChangesAsync(ct);

        var stop1 = new TransportStop
        {
            SchoolId = school.Id, BranchId = branch.Id, TransportRouteId = route.Id,
            Name = "100 ft Road", PickupTime = new TimeSpan(7, 15, 0), DropTime = new TimeSpan(15, 45, 0),
            Latitude = 12.9719m, Longitude = 77.6412m, Order = 1
        };
        var stop2 = new TransportStop
        {
            SchoolId = school.Id, BranchId = branch.Id, TransportRouteId = route.Id,
            Name = "Domlur", PickupTime = new TimeSpan(7, 30, 0), DropTime = new TimeSpan(15, 30, 0),
            Latitude = 12.9609m, Longitude = 77.6387m, Order = 2
        };
        _db.AddRange(stop1, stop2);
        _db.Add(new TransportAllocation
        {
            SchoolId = school.Id, BranchId = branch.Id, StudentId = aarav.Id,
            TransportRouteId = route.Id, TransportStopId = stop1.Id,
            StartDate = session.StartDate, IsActive = true
        });

        var book = new Book
        {
            SchoolId = school.Id, BranchId = branch.Id, Title = "Panchatantra Tales",
            Isbn = "978-81-7000-001-1", Category = "Story", Author = "Vishnu Sharma",
            Publisher = "Scholastic", Edition = "Illustrated", TotalCopies = 5,
            AvailableCopies = 4, Shelf = "A-02", IsActive = true
        };
        _db.Add(book);
        await _db.SaveChangesAsync(ct);
        _db.Add(new BookTransaction
        {
            SchoolId = school.Id, BranchId = branch.Id, BookId = book.Id,
            StudentId = aarav.Id, Type = BookTransactionType.Issue,
            IssuedAtUtc = _clock.UtcNow.AddDays(-2), DueAtUtc = _clock.UtcNow.AddDays(5)
        });

        var vendor = new Vendor
        {
            SchoolId = school.Id, BranchId = branch.Id, Name = "Stationery Mart",
            ContactPerson = "Ramesh", Phone = "+91-98765-4000", Email = "sales@stationerymart.example",
            Address = "SP Road, Bengaluru", GstNumber = "29ABCDE1234F1Z5", IsActive = true
        };
        _db.Add(vendor);
        await _db.SaveChangesAsync(ct);

        var paper = new InventoryItem
        {
            SchoolId = school.Id, BranchId = branch.Id, Name = "A4 Paper Ream",
            Code = "INV-001", Category = "Stationery", Unit = "Ream", Quantity = 120,
            MinimumStock = 20, UnitPrice = 250m, Location = "Store Room 1", IsActive = true
        };
        _db.Add(paper);
        await _db.SaveChangesAsync(ct);

        var purchaseOrder = new PurchaseOrder
        {
            SchoolId = school.Id, BranchId = branch.Id, VendorId = vendor.Id,
            OrderNo = $"PO-{sessionStartYear}-001", OrderDate = today.AddDays(-15),
            Status = "Approved", TotalAmount = 12500m, Notes = "Term 1 stationery",
            ApprovedBy = principal.Id
        };
        _db.Add(purchaseOrder);
        await _db.SaveChangesAsync(ct);

        _db.Add(new PurchaseOrderItem
        {
            SchoolId = school.Id, BranchId = branch.Id, PurchaseOrderId = purchaseOrder.Id,
            InventoryItemId = paper.Id, Quantity = 50, UnitPrice = 250m, ReceivedQuantity = 50
        });
        _db.Add(new StockTransaction
        {
            SchoolId = school.Id, BranchId = branch.Id, InventoryItemId = paper.Id,
            Type = StockTransactionType.Inward, Quantity = 50, ReferenceNo = purchaseOrder.OrderNo,
            Remarks = "Received full order", TransactionDate = today.AddDays(-10),
            RecordedBy = accountant.Id
        });

        var utilities = new ExpenseCategory
        {
            SchoolId = school.Id, BranchId = branch.Id, Name = "Utilities", Code = "UTIL"
        };
        _db.Add(utilities);
        await _db.SaveChangesAsync(ct);
        _db.Add(new Expense
        {
            SchoolId = school.Id, BranchId = branch.Id, ExpenseCategoryId = utilities.Id,
            ExpenseDate = today.AddDays(-8), Amount = 15400m, Mode = PaymentMode.BankTransfer,
            VendorName = "BESCOM", Description = "Electricity bill", VoucherNo = "VCH-001",
            RecordedBy = accountant.Id
        });

        // --- Admissions -----------------------------------------------------------
        _db.AddRange(
            new Admission
            {
                SchoolId = school.Id, BranchId = branch.Id, ApplicationNo = $"APP-{sessionStartYear}-001",
                AcademicSessionId = session.Id, ClassId = class1.Id,
                FirstName = "Neel", LastName = "Kapoor", DateOfBirth = new DateOnly(sessionStartYear - 7, 5, 12),
                Gender = Gender.Male, GuardianName = "Vikram Kapoor", GuardianPhone = "9876500011",
                GuardianEmail = "vikram.kapoor@example.com", Status = AdmissionStatus.InterviewScheduled,
                AppliedAtUtc = _clock.UtcNow.AddDays(-6), InterviewAtUtc = _clock.UtcNow.AddDays(2),
                Source = "Website", Notes = "Sibling of a current student"
            },
            new Admission
            {
                SchoolId = school.Id, BranchId = branch.Id, ApplicationNo = $"APP-{sessionStartYear}-002",
                AcademicSessionId = session.Id, ClassId = class2.Id,
                FirstName = "Aadhya", LastName = "Menon", DateOfBirth = new DateOnly(sessionStartYear - 8, 8, 22),
                Gender = Gender.Female, GuardianName = "Lakshmi Menon", GuardianPhone = "9876500012",
                GuardianEmail = "lakshmi.menon@example.com", Status = AdmissionStatus.Application,
                AppliedAtUtc = _clock.UtcNow.AddDays(-2), Source = "Walk-in"
            });

        _db.Add(new Notification
        {
            SchoolId = school.Id,
            BranchId = branch.Id,
            UserAccountId = parentUser.Id,
            GuardianId = primaryGuardian.Id,
            Title = "Welcome to the parent portal",
            Message = "You can view attendance, fee dues and results for both children from this portal.",
            Channel = NotificationChannel.InApp,
            Status = NotificationStatus.Pending,
            Category = "General"
        });

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Demo seed complete. Sign in with superadmin@demo.edu / {Password} (and other demo accounts).",
            DemoPassword);
    }
}
