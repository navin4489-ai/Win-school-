using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SchoolErp.Application.Common;
using SchoolErp.Domain.Common;
using SchoolErp.Domain.Entities;
using SchoolErp.Domain.Enums;

namespace SchoolErp.Application.Operations;

public sealed class OperationsService
{
    private readonly IAppDbContext _db;
    private readonly IUnitOfWork _uow;
    private readonly ICurrentUser _user;
    private readonly IDateTimeProvider _clock;
    private readonly LibraryOptions _library;

    public OperationsService(IAppDbContext db, IUnitOfWork uow, ICurrentUser user, IDateTimeProvider clock,
        IOptions<LibraryOptions> libraryOptions)
    {
        _db = db;
        _uow = uow;
        _user = user;
        _clock = clock;
        _library = libraryOptions.Value;
    }

    private int SchoolId => _user.SchoolId ?? throw DomainException.Unauthorized();

    // ---------- HR ----------

    public async Task<List<EmployeeDto>> GetEmployeesAsync(string? search, CancellationToken ct = default)
    {
        var query =
            from e in _db.Employees
            join d in _db.Departments on e.DepartmentId equals d.Id into departments
            from d in departments.DefaultIfEmpty()
            join des in _db.Designations on e.DesignationId equals des.Id into designations
            from des in designations.DefaultIfEmpty()
            where e.SchoolId == SchoolId && !e.IsDeleted
            select new { e, Department = d == null ? null : d.Name, Designation = des == null ? null : des.Name };

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.e.FirstName.Contains(term) || x.e.LastName.Contains(term) ||
                                     x.e.EmployeeNo.Contains(term) || x.e.Email.Contains(term));
        }

        return await query.OrderBy(x => x.e.FirstName)
            .Select(x => new EmployeeDto
            {
                Id = x.e.Id, EmployeeNo = x.e.EmployeeNo,
                FullName = x.e.FirstName + " " + x.e.LastName, Email = x.e.Email, Phone = x.e.Phone,
                Department = x.Department, Designation = x.Designation,
                JoiningDate = x.e.JoiningDate, Status = x.e.Status
            }).ToListAsync(ct);
    }

    public async Task<EmployeeDto> CreateEmployeeAsync(SaveEmployeeRequest request, CancellationToken ct = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var duplicate = await _db.Employees.AnyAsync(e => e.SchoolId == SchoolId && e.Email == email && !e.IsDeleted, ct);
        if (duplicate) throw DomainException.Conflict("An employee with this email already exists.");
        if (request.DepartmentId is > 0 &&
            !await _db.Departments.AnyAsync(d => d.Id == request.DepartmentId && d.SchoolId == SchoolId && !d.IsDeleted, ct))
            throw DomainException.NotFound("Department");
        if (request.DesignationId is > 0 &&
            !await _db.Designations.AnyAsync(d => d.Id == request.DesignationId && d.SchoolId == SchoolId && !d.IsDeleted, ct))
            throw DomainException.NotFound("Designation");

        var employee = new Employee
        {
            SchoolId = SchoolId,
            BranchId = _user.BranchId,
            EmployeeNo = await NextEmployeeNoAsync(ct),
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            Email = email,
            Phone = request.Phone,
            DepartmentId = request.DepartmentId,
            DesignationId = request.DesignationId,
            JoiningDate = request.JoiningDate == default ? _clock.Today : request.JoiningDate,
            Qualification = request.Qualification,
            ExperienceYears = request.ExperienceYears,
            BasicSalary = request.BasicSalary,
            Status = EmploymentStatus.Active
        };
        _db.Add(employee);
        await _uow.SaveChangesAsync(ct);

        var department = request.DepartmentId is null ? null : await _db.Departments
            .Where(d => d.Id == request.DepartmentId).Select(d => d.Name).FirstOrDefaultAsync(ct);
        var designation = request.DesignationId is null ? null : await _db.Designations
            .Where(d => d.Id == request.DesignationId).Select(d => d.Name).FirstOrDefaultAsync(ct);

        return new EmployeeDto
        {
            Id = employee.Id, EmployeeNo = employee.EmployeeNo, FullName = employee.FullName,
            Email = employee.Email, Phone = employee.Phone, Department = department,
            Designation = designation, JoiningDate = employee.JoiningDate, Status = employee.Status
        };
    }

    public async Task<List<LookupDto>> GetDepartmentsAsync(CancellationToken ct = default) =>
        await _db.Departments.Where(d => d.SchoolId == SchoolId && !d.IsDeleted).OrderBy(d => d.Name)
            .Select(d => new LookupDto { Id = d.Id, Name = d.Name }).ToListAsync(ct);

    public async Task<List<LookupDto>> GetDesignationsAsync(CancellationToken ct = default) =>
        await _db.Designations.Where(d => d.SchoolId == SchoolId && !d.IsDeleted).OrderBy(d => d.Name)
            .Select(d => new LookupDto { Id = d.Id, Name = d.Name }).ToListAsync(ct);

    public async Task<List<LeaveDto>> GetLeavesAsync(LeaveStatus? status, CancellationToken ct = default)
    {
        var query =
            from l in _db.LeaveApplications
            join e in _db.Employees on l.EmployeeId equals e.Id
            where l.SchoolId == SchoolId && !l.IsDeleted
            select new { l, e };

        if (status is not null) query = query.Where(x => x.l.Status == status);

        return await query.OrderByDescending(x => x.l.CreatedAtUtc)
            .Select(x => new LeaveDto
            {
                Id = x.l.Id, EmployeeId = x.l.EmployeeId,
                EmployeeName = x.e.FirstName + " " + x.e.LastName, LeaveType = x.l.LeaveType,
                FromDate = x.l.FromDate, ToDate = x.l.ToDate, Reason = x.l.Reason,
                Status = x.l.Status, CreatedAtUtc = x.l.CreatedAtUtc
            }).ToListAsync(ct);
    }

    public async Task<LeaveDto> ApplyLeaveAsync(ApplyLeaveRequest request, CancellationToken ct = default)
    {
        var employeeId = request.EmployeeId ?? _user.EmployeeId
            ?? throw DomainException.Validation("Employee id is required.");

        if (request.EmployeeId is not null && request.EmployeeId != _user.EmployeeId &&
            !_user.HasPermission(Permissions.HrManage))
            throw DomainException.Forbidden("You can only apply for your own leave.");

        if (request.ToDate < request.FromDate)
            throw DomainException.Validation("Leave end date must be on or after the start date.");

        var employee = await _db.Employees.FirstOrDefaultAsync(e => e.Id == employeeId && e.SchoolId == SchoolId && !e.IsDeleted, ct)
            ?? throw DomainException.NotFound("Employee");

        var overlaps = await _db.LeaveApplications.AnyAsync(l =>
            l.SchoolId == SchoolId && l.EmployeeId == employee.Id && !l.IsDeleted &&
            (l.Status == LeaveStatus.Pending || l.Status == LeaveStatus.Approved) &&
            l.FromDate <= request.ToDate && l.ToDate >= request.FromDate, ct);
        if (overlaps)
            throw DomainException.Conflict("A pending or approved leave already overlaps these dates.");

        var leave = new LeaveApplication
        {
            SchoolId = SchoolId,
            EmployeeId = employee.Id,
            LeaveType = request.LeaveType.Trim(),
            FromDate = request.FromDate,
            ToDate = request.ToDate,
            Reason = request.Reason.Trim(),
            Status = LeaveStatus.Pending
        };
        _db.Add(leave);
        await _uow.SaveChangesAsync(ct);

        return new LeaveDto
        {
            Id = leave.Id, EmployeeId = leave.EmployeeId, EmployeeName = employee.FullName,
            LeaveType = leave.LeaveType, FromDate = leave.FromDate, ToDate = leave.ToDate,
            Reason = leave.Reason, Status = leave.Status, CreatedAtUtc = leave.CreatedAtUtc
        };
    }

    public async Task ApproveLeaveAsync(int id, bool approve, string? remarks, CancellationToken ct = default)
    {
        var leave = await _db.LeaveApplications.FirstOrDefaultAsync(l => l.Id == id && l.SchoolId == SchoolId && !l.IsDeleted, ct)
            ?? throw DomainException.NotFound("Leave application");

        if (leave.Status != LeaveStatus.Pending) throw DomainException.Conflict("Leave is not pending approval.");
        if (_user.EmployeeId is not null && leave.EmployeeId == _user.EmployeeId)
            throw DomainException.Forbidden("You cannot approve or reject your own leave.");

        leave.Status = approve ? LeaveStatus.Approved : LeaveStatus.Rejected;
        leave.ApprovedBy = _user.UserId;
        leave.ApprovedAtUtc = _clock.UtcNow;
        leave.Remarks = remarks;
        await _uow.SaveChangesAsync(ct);
    }

    public async Task<List<PayrollRunDto>> GetPayrollRunsAsync(CancellationToken ct = default)
    {
        var runs = await _db.PayrollRuns.Where(p => p.SchoolId == SchoolId && !p.IsDeleted)
            .OrderByDescending(p => p.Year).ThenByDescending(p => p.Month).ToListAsync(ct);

        var ids = runs.Select(r => r.Id).ToList();
        var counts = await _db.Payslips.Where(p => ids.Contains(p.PayrollRunId) && !p.IsDeleted)
            .GroupBy(p => p.PayrollRunId)
            .Select(g => new { PayrollRunId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.PayrollRunId, x => x.Count, ct);

        return runs.Select(r => new PayrollRunDto
        {
            Id = r.Id, Month = r.Month, Year = r.Year, Status = r.Status,
            TotalGross = r.TotalGross, TotalDeductions = r.TotalDeductions, TotalNet = r.TotalNet,
            EmployeeCount = counts.GetValueOrDefault(r.Id), ProcessedAtUtc = r.ProcessedAtUtc
        }).ToList();
    }

    public async Task<PayrollRunDto> RunPayrollAsync(RunPayrollRequest request, CancellationToken ct = default)
    {
        return await _uow.ExecuteInTransactionAsync(async token =>
        {
            var duplicate = await _db.PayrollRuns.AnyAsync(p =>
                p.SchoolId == SchoolId && p.Month == request.Month && p.Year == request.Year && !p.IsDeleted, token);
            if (duplicate) throw DomainException.Conflict("Payroll has already been processed for this month.");

            var monthStart = new DateOnly(request.Year, request.Month, 1);
            var monthEnd = monthStart.AddMonths(1).AddDays(-1);

            var employees = await _db.Employees
                .Where(e => e.SchoolId == SchoolId && e.Status == EmploymentStatus.Active && !e.IsDeleted &&
                            e.JoiningDate <= monthEnd)
                .ToListAsync(token);

            if (employees.Count == 0) throw DomainException.Validation("No active employees to process.");

            var components = await (
                from ec in _db.EmployeeSalaryComponents
                join sc in _db.SalaryComponents on ec.SalaryComponentId equals sc.Id
                where ec.SchoolId == SchoolId && !ec.IsDeleted && !sc.IsDeleted
                select new { ec.EmployeeId, sc.Type, ec.Amount, ec.EffectiveFrom, ec.EffectiveTo }).ToListAsync(token);

            var attendance = await _db.EmployeeAttendances
                .Where(a => a.SchoolId == SchoolId && a.AttendanceDate >= monthStart && a.AttendanceDate <= monthEnd && !a.IsDeleted)
                .Select(a => new { a.EmployeeId, a.Status }).ToListAsync(token);

            var run = new PayrollRun
            {
                SchoolId = SchoolId, Month = request.Month, Year = request.Year,
                Status = PayrollStatus.Processed, ProcessedAtUtc = _clock.UtcNow,
                ProcessedBy = _user.UserId
            };
            _db.Add(run);
            await _uow.SaveChangesAsync(token);

            decimal totalGross = 0, totalDeductions = 0, totalNet = 0;
            var workingDays = DateTime.DaysInMonth(request.Year, request.Month);

            foreach (var employee in employees)
            {
                var employeeComponents = components.Where(c => c.EmployeeId == employee.Id &&
                    c.EffectiveFrom <= monthEnd && (c.EffectiveTo == null || c.EffectiveTo >= monthStart)).ToList();

                var basic = employee.BasicSalary ?? 0;
                if (employee.JoiningDate > monthStart)
                {
                    var daysEmployed = monthEnd.DayNumber - employee.JoiningDate.DayNumber + 1;
                    basic = basic * daysEmployed / workingDays;
                }
                var gross = basic + employeeComponents.Where(c => c.Type == SalaryComponentType.Earning).Sum(c => c.Amount);
                var deductions = employeeComponents.Where(c => c.Type == SalaryComponentType.Deduction).Sum(c => c.Amount);
                var net = gross - deductions;
                var present = attendance.Count(a => a.EmployeeId == employee.Id &&
                    a.Status is AttendanceStatus.Present or AttendanceStatus.Late or AttendanceStatus.HalfDay);

                _db.Add(new Payslip
                {
                    SchoolId = SchoolId, PayrollRunId = run.Id, EmployeeId = employee.Id,
                    Basic = Math.Round(basic, 2), Gross = Math.Round(gross, 2),
                    Deductions = Math.Round(deductions, 2), Net = Math.Round(net, 2),
                    DaysPresent = present, DaysAbsent = Math.Max(0, workingDays - present),
                    GeneratedAtUtc = _clock.UtcNow
                });

                totalGross += gross;
                totalDeductions += deductions;
                totalNet += net;
            }

            run.TotalGross = Math.Round(totalGross, 2);
            run.TotalDeductions = Math.Round(totalDeductions, 2);
            run.TotalNet = Math.Round(totalNet, 2);

            await _uow.SaveChangesAsync(token);

            return new PayrollRunDto
            {
                Id = run.Id, Month = run.Month, Year = run.Year, Status = run.Status,
                TotalGross = run.TotalGross, TotalDeductions = run.TotalDeductions, TotalNet = run.TotalNet,
                EmployeeCount = employees.Count, ProcessedAtUtc = run.ProcessedAtUtc
            };
        }, ct);
    }

    public async Task<List<PayslipDto>> GetPayslipsAsync(int runId, CancellationToken ct = default)
    {
        var run = await _db.PayrollRuns.FirstOrDefaultAsync(p => p.Id == runId && p.SchoolId == SchoolId && !p.IsDeleted, ct)
            ?? throw DomainException.NotFound("Payroll run");

        return await (
            from p in _db.Payslips
            join e in _db.Employees on p.EmployeeId equals e.Id
            where p.PayrollRunId == run.Id && !p.IsDeleted
            orderby e.FirstName
            select new PayslipDto
            {
                Id = p.Id, EmployeeId = p.EmployeeId,
                EmployeeName = e.FirstName + " " + e.LastName, Basic = p.Basic, Gross = p.Gross,
                Deductions = p.Deductions, Net = p.Net, DaysPresent = p.DaysPresent, DaysAbsent = p.DaysAbsent
            }).ToListAsync(ct);
    }

    // ---------- Transport ----------

    public async Task<List<VehicleDto>> GetVehiclesAsync(CancellationToken ct = default) =>
        await _db.Vehicles.Where(v => v.SchoolId == SchoolId && !v.IsDeleted).OrderBy(v => v.RegistrationNo)
            .Select(v => new VehicleDto
            {
                Id = v.Id, RegistrationNo = v.RegistrationNo, Model = v.Model, Capacity = v.Capacity,
                DriverName = v.DriverName, DriverPhone = v.DriverPhone,
                InsuranceExpiry = v.InsuranceExpiry, FitnessExpiry = v.FitnessExpiry
            }).ToListAsync(ct);

    public async Task<VehicleDto> CreateVehicleAsync(SaveVehicleRequest request, CancellationToken ct = default)
    {
        var duplicate = await _db.Vehicles.AnyAsync(v =>
            v.SchoolId == SchoolId && v.RegistrationNo == request.RegistrationNo && !v.IsDeleted, ct);
        if (duplicate) throw DomainException.Conflict("A vehicle with this registration already exists.");

        var vehicle = new Vehicle
        {
            SchoolId = SchoolId, RegistrationNo = request.RegistrationNo.Trim(), Model = request.Model,
            Capacity = request.Capacity, DriverName = request.DriverName, DriverPhone = request.DriverPhone,
            DriverLicenceNo = request.DriverLicenceNo, InsuranceExpiry = request.InsuranceExpiry,
            FitnessExpiry = request.FitnessExpiry, IsActive = true
        };
        _db.Add(vehicle);
        await _uow.SaveChangesAsync(ct);

        return new VehicleDto
        {
            Id = vehicle.Id, RegistrationNo = vehicle.RegistrationNo, Model = vehicle.Model,
            Capacity = vehicle.Capacity, DriverName = vehicle.DriverName, DriverPhone = vehicle.DriverPhone,
            InsuranceExpiry = vehicle.InsuranceExpiry, FitnessExpiry = vehicle.FitnessExpiry
        };
    }

    public async Task<List<TransportRouteDto>> GetRoutesAsync(CancellationToken ct = default)
    {
        var routes = await _db.TransportRoutes.Where(r => r.SchoolId == SchoolId && !r.IsDeleted)
            .OrderBy(r => r.Name).ToListAsync(ct);
        var ids = routes.Select(r => r.Id).ToList();
        var stops = await _db.TransportStops.Where(s => ids.Contains(s.TransportRouteId) && !s.IsDeleted)
            .GroupBy(s => s.TransportRouteId).Select(g => new { RouteId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.RouteId, x => x.Count, ct);

        return routes.Select(r => new TransportRouteDto
        {
            Id = r.Id, Name = r.Name, Code = r.Code, VehicleId = r.VehicleId,
            StartPoint = r.StartPoint, EndPoint = r.EndPoint, MonthlyFee = r.MonthlyFee,
            StopCount = stops.GetValueOrDefault(r.Id)
        }).ToList();
    }

    public async Task<TransportRouteDto> CreateRouteAsync(SaveTransportRouteRequest request, CancellationToken ct = default)
    {
        var duplicate = await _db.TransportRoutes.AnyAsync(r =>
            r.SchoolId == SchoolId && r.Code == request.Code && !r.IsDeleted, ct);
        if (duplicate) throw DomainException.Conflict("A route with this code already exists.");
        if (request.VehicleId is not null &&
            !await _db.Vehicles.AnyAsync(v => v.Id == request.VehicleId && v.SchoolId == SchoolId && !v.IsDeleted, ct))
            throw DomainException.NotFound("Vehicle");

        var route = new TransportRoute
        {
            SchoolId = SchoolId, Name = request.Name.Trim(), Code = request.Code.Trim(),
            VehicleId = request.VehicleId, StartPoint = request.StartPoint, EndPoint = request.EndPoint,
            MonthlyFee = request.MonthlyFee, IsActive = true
        };
        _db.Add(route);
        await _uow.SaveChangesAsync(ct);

        return new TransportRouteDto
        {
            Id = route.Id, Name = route.Name, Code = route.Code, VehicleId = route.VehicleId,
            StartPoint = route.StartPoint, EndPoint = route.EndPoint, MonthlyFee = route.MonthlyFee
        };
    }

    public async Task<List<TransportAllocationDto>> GetAllocationsAsync(CancellationToken ct = default) =>
        await (
            from a in _db.TransportAllocations
            join s in _db.Students on a.StudentId equals s.Id
            join r in _db.TransportRoutes on a.TransportRouteId equals r.Id
            where a.SchoolId == SchoolId && !a.IsDeleted
            orderby s.FirstName
            select new TransportAllocationDto
            {
                Id = a.Id, StudentId = a.StudentId, StudentName = s.FirstName + " " + s.LastName,
                TransportRouteId = a.TransportRouteId, RouteName = r.Name,
                TransportStopId = a.TransportStopId, StartDate = a.StartDate, IsActive = a.IsActive
            }).ToListAsync(ct);

    public async Task<TransportAllocationDto> AllocateTransportAsync(AllocateTransportRequest request, CancellationToken ct = default)
    {
        var student = await _db.Students.FirstOrDefaultAsync(s => s.Id == request.StudentId && s.SchoolId == SchoolId && !s.IsDeleted, ct)
            ?? throw DomainException.NotFound("Student");
        var route = await _db.TransportRoutes.FirstOrDefaultAsync(r => r.Id == request.TransportRouteId && r.SchoolId == SchoolId && !r.IsDeleted, ct)
            ?? throw DomainException.NotFound("Route");

        var duplicate = await _db.TransportAllocations.AnyAsync(a =>
            a.SchoolId == SchoolId && a.StudentId == student.Id && a.IsActive && !a.IsDeleted, ct);
        if (duplicate) throw DomainException.Conflict("The student already has an active transport allocation.");
        if (request.TransportStopId is > 0 && !await _db.TransportStops.AnyAsync(s =>
                s.Id == request.TransportStopId && s.TransportRouteId == route.Id && s.SchoolId == SchoolId && !s.IsDeleted, ct))
            throw DomainException.NotFound("Stop on this route");

        var allocation = new TransportAllocation
        {
            SchoolId = SchoolId, StudentId = student.Id, TransportRouteId = route.Id,
            TransportStopId = request.TransportStopId,
            StartDate = request.StartDate ?? _clock.Today, IsActive = true
        };
        _db.Add(allocation);
        await _uow.SaveChangesAsync(ct);

        return new TransportAllocationDto
        {
            Id = allocation.Id, StudentId = student.Id, StudentName = student.FullName,
            TransportRouteId = route.Id, RouteName = route.Name, TransportStopId = allocation.TransportStopId,
            StartDate = allocation.StartDate, IsActive = true
        };
    }

    // ---------- Library ----------

    public async Task<List<BookDto>> GetBooksAsync(string? search, CancellationToken ct = default)
    {
        var query = _db.Books.Where(b => b.SchoolId == SchoolId && !b.IsDeleted);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(b => b.Title.Contains(term) || (b.Isbn != null && b.Isbn.Contains(term)) ||
                                     (b.Author != null && b.Author.Contains(term)));
        }

        return await query.OrderBy(b => b.Title)
            .Select(b => new BookDto
            {
                Id = b.Id, Title = b.Title, Isbn = b.Isbn, Category = b.Category, Author = b.Author,
                TotalCopies = b.TotalCopies, AvailableCopies = b.AvailableCopies, Shelf = b.Shelf
            }).ToListAsync(ct);
    }

    public async Task<BookDto> CreateBookAsync(SaveBookRequest request, CancellationToken ct = default)
    {
        var book = new Book
        {
            SchoolId = SchoolId, Title = request.Title.Trim(), Isbn = request.Isbn,
            Category = request.Category, Author = request.Author, Publisher = request.Publisher,
            TotalCopies = request.TotalCopies, AvailableCopies = request.TotalCopies,
            Shelf = request.Shelf, IsActive = true
        };
        _db.Add(book);
        await _uow.SaveChangesAsync(ct);

        return new BookDto
        {
            Id = book.Id, Title = book.Title, Isbn = book.Isbn, Category = book.Category,
            Author = book.Author, TotalCopies = book.TotalCopies, AvailableCopies = book.AvailableCopies,
            Shelf = book.Shelf
        };
    }

    public async Task<BookTransactionDto> IssueBookAsync(IssueBookRequest request, CancellationToken ct = default)
    {
        if (request.StudentId is null && request.EmployeeId is null)
            throw DomainException.Validation("A student or employee must be selected.");

        var book = await _db.Books.FirstOrDefaultAsync(b => b.Id == request.BookId && b.SchoolId == SchoolId && !b.IsDeleted, ct)
            ?? throw DomainException.NotFound("Book");
        if (book.AvailableCopies <= 0) throw DomainException.Conflict("No copies are available.");

        var borrower = request.StudentId is not null
            ? await _db.Students.Where(s => s.Id == request.StudentId && s.SchoolId == SchoolId && !s.IsDeleted)
                .Select(s => s.FirstName + " " + s.LastName).FirstOrDefaultAsync(ct)
            : await _db.Employees.Where(e => e.Id == request.EmployeeId && e.SchoolId == SchoolId && !e.IsDeleted)
                .Select(e => e.FirstName + " " + e.LastName).FirstOrDefaultAsync(ct);

        if (borrower is null) throw DomainException.NotFound("Borrower");

        var days = request.LoanDays is < 1 or > 365 ? 14 : request.LoanDays;

        var transaction = new BookTransaction
        {
            SchoolId = SchoolId, BookId = book.Id, StudentId = request.StudentId,
            EmployeeId = request.EmployeeId, Type = BookTransactionType.Issue,
            IssuedAtUtc = _clock.UtcNow, DueAtUtc = _clock.UtcNow.AddDays(days)
        };
        _db.Add(transaction);
        book.AvailableCopies--;

        await _uow.SaveChangesAsync(ct);

        return new BookTransactionDto
        {
            Id = transaction.Id, BookId = book.Id, BookTitle = book.Title, Borrower = borrower,
            Type = transaction.Type, IssuedAtUtc = transaction.IssuedAtUtc,
            DueAtUtc = transaction.DueAtUtc, FineAmount = 0
        };
    }

    public async Task<BookTransactionDto> ReturnBookAsync(ReturnBookRequest request, CancellationToken ct = default)
    {
        var issue = await _db.BookTransactions.FirstOrDefaultAsync(t =>
            t.Id == request.BookTransactionId && t.SchoolId == SchoolId && !t.IsDeleted, ct)
            ?? throw DomainException.NotFound("Book issue");

        if (issue.ReturnedAtUtc is not null) throw DomainException.Conflict("This book has already been returned.");

        var book = await _db.Books.FirstAsync(b => b.Id == issue.BookId, ct);

        var fine = request.FineAmount ?? 0;
        if (request.FineAmount is null && issue.DueAtUtc is not null && _clock.UtcNow > issue.DueAtUtc)
        {
            var overdueDays = (int)Math.Ceiling((_clock.UtcNow - issue.DueAtUtc.Value).TotalDays);
            fine = overdueDays * Math.Max(0, _library.FinePerDay);
        }
        if (fine < 0) throw DomainException.Validation("Fine amount cannot be negative.");

        issue.ReturnedAtUtc = _clock.UtcNow;
        issue.FineAmount = fine;
        issue.Type = request.IsLost ? BookTransactionType.Lost : BookTransactionType.Return;
        if (request.IsLost)
        {
            book.TotalCopies = Math.Max(0, book.TotalCopies - 1);
            book.AvailableCopies = Math.Clamp(book.AvailableCopies, 0, book.TotalCopies);
        }
        else book.AvailableCopies = Math.Min(book.TotalCopies, book.AvailableCopies + 1);

        var borrower = issue.StudentId is not null
            ? await _db.Students.Where(s => s.Id == issue.StudentId).Select(s => s.FirstName + " " + s.LastName).FirstOrDefaultAsync(ct) ?? string.Empty
            : await _db.Employees.Where(e => e.Id == issue.EmployeeId).Select(e => e.FirstName + " " + e.LastName).FirstOrDefaultAsync(ct) ?? string.Empty;

        await _uow.SaveChangesAsync(ct);

        return new BookTransactionDto
        {
            Id = issue.Id, BookId = book.Id, BookTitle = book.Title, Borrower = borrower,
            Type = issue.Type, IssuedAtUtc = issue.IssuedAtUtc, DueAtUtc = issue.DueAtUtc,
            ReturnedAtUtc = issue.ReturnedAtUtc, FineAmount = issue.FineAmount
        };
    }

    public async Task<List<BookTransactionDto>> GetBookTransactionsAsync(CancellationToken ct = default) =>
        await (
            from t in _db.BookTransactions
            join b in _db.Books on t.BookId equals b.Id
            where t.SchoolId == SchoolId && !t.IsDeleted
            orderby t.IssuedAtUtc descending
            select new BookTransactionDto
            {
                Id = t.Id, BookId = b.Id, BookTitle = b.Title,
                Borrower = t.StudentId != null ? "Student #" + t.StudentId : "Employee #" + t.EmployeeId,
                Type = t.Type, IssuedAtUtc = t.IssuedAtUtc, DueAtUtc = t.DueAtUtc,
                ReturnedAtUtc = t.ReturnedAtUtc, FineAmount = t.FineAmount
            }).Take(200).ToListAsync(ct);

    // ---------- Inventory ----------

    public async Task<List<InventoryItemDto>> GetInventoryAsync(string? search, CancellationToken ct = default)
    {
        var query = _db.InventoryItems.Where(i => i.SchoolId == SchoolId && !i.IsDeleted);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(i => i.Name.Contains(term) || i.Code.Contains(term));
        }

        return await query.OrderBy(i => i.Name)
            .Select(i => new InventoryItemDto
            {
                Id = i.Id, Name = i.Name, Code = i.Code, Category = i.Category, Unit = i.Unit,
                Quantity = i.Quantity, MinimumStock = i.MinimumStock, UnitPrice = i.UnitPrice,
                IsBelowMinimum = i.Quantity < i.MinimumStock
            }).ToListAsync(ct);
    }

    public async Task<InventoryItemDto> CreateInventoryItemAsync(SaveInventoryItemRequest request, CancellationToken ct = default)
    {
        var duplicate = await _db.InventoryItems.AnyAsync(i =>
            i.SchoolId == SchoolId && i.Code == request.Code && !i.IsDeleted, ct);
        if (duplicate) throw DomainException.Conflict("An inventory item with this code already exists.");

        var item = new InventoryItem
        {
            SchoolId = SchoolId, Name = request.Name.Trim(), Code = request.Code.Trim(),
            Category = request.Category, Unit = request.Unit, Quantity = request.Quantity,
            MinimumStock = request.MinimumStock, UnitPrice = request.UnitPrice,
            Location = request.Location, IsActive = true
        };
        _db.Add(item);
        await _uow.SaveChangesAsync(ct);

        return new InventoryItemDto
        {
            Id = item.Id, Name = item.Name, Code = item.Code, Category = item.Category,
            Unit = item.Unit, Quantity = item.Quantity, MinimumStock = item.MinimumStock,
            UnitPrice = item.UnitPrice, IsBelowMinimum = item.Quantity < item.MinimumStock
        };
    }

    public async Task<InventoryItemDto> RecordStockAsync(StockEntryRequest request, CancellationToken ct = default)
    {
        var item = await _db.InventoryItems.FirstOrDefaultAsync(i =>
            i.Id == request.InventoryItemId && i.SchoolId == SchoolId && !i.IsDeleted, ct)
            ?? throw DomainException.NotFound("Inventory item");

        if (request.Type != StockTransactionType.Adjustment && request.Quantity <= 0)
            throw DomainException.Validation("Quantity must be greater than zero.");
        if (request.Quantity < 0)
            throw DomainException.Validation("Stock cannot go below zero.");

        var transactionQuantity = request.Quantity;
        switch (request.Type)
        {
            case StockTransactionType.Inward:
            case StockTransactionType.Return:
                item.Quantity += request.Quantity;
                break;
            case StockTransactionType.Outward:
                if (request.Quantity > item.Quantity)
                    throw DomainException.Validation("Cannot issue more stock than is available.");
                item.Quantity -= request.Quantity;
                break;
            case StockTransactionType.Adjustment:
                transactionQuantity = request.Quantity - item.Quantity;
                item.Quantity = request.Quantity;
                break;
            default:
                throw DomainException.Validation("Invalid stock transaction type.");
        }

        _db.Add(new StockTransaction
        {
            SchoolId = SchoolId, InventoryItemId = item.Id, Type = request.Type,
            Quantity = transactionQuantity, ReferenceNo = request.ReferenceNo,
            Remarks = request.Remarks, TransactionDate = _clock.Today, RecordedBy = _user.UserId
        });

        await _uow.SaveChangesAsync(ct);

        return new InventoryItemDto
        {
            Id = item.Id, Name = item.Name, Code = item.Code, Category = item.Category,
            Unit = item.Unit, Quantity = item.Quantity, MinimumStock = item.MinimumStock,
            UnitPrice = item.UnitPrice, IsBelowMinimum = item.Quantity < item.MinimumStock
        };
    }

    // ---------- LMS ----------

    public async Task<List<StudyMaterialDto>> GetMaterialsAsync(int? classId, int? subjectId, CancellationToken ct = default)
    {
        var query =
            from m in _db.StudyMaterials
            join subject in _db.Subjects on m.SubjectId equals subject.Id
            where m.SchoolId == SchoolId && !m.IsDeleted && m.IsPublished
            select new { m, SubjectName = subject.Name };

        if (classId is > 0) query = query.Where(x => x.m.ClassId == classId);
        if (subjectId is > 0) query = query.Where(x => x.m.SubjectId == subjectId);

        return await query.OrderByDescending(x => x.m.CreatedAtUtc).Take(200)
            .Select(x => new StudyMaterialDto
            {
                Id = x.m.Id, ClassId = x.m.ClassId, SectionId = x.m.SectionId, SubjectId = x.m.SubjectId,
                SubjectName = x.SubjectName, Title = x.m.Title, Description = x.m.Description,
                FileUrl = x.m.FileUrl, VideoUrl = x.m.VideoUrl, Chapter = x.m.Chapter,
                CreatedAtUtc = x.m.CreatedAtUtc
            }).ToListAsync(ct);
    }

    public async Task<StudyMaterialDto> CreateMaterialAsync(SaveStudyMaterialRequest request, CancellationToken ct = default)
    {
        var subject = await _db.Subjects.FirstOrDefaultAsync(s => s.Id == request.SubjectId && s.SchoolId == SchoolId && !s.IsDeleted, ct)
            ?? throw DomainException.NotFound("Subject");
        if (request.ClassId is > 0 &&
            !await _db.Classes.AnyAsync(c => c.Id == request.ClassId && c.SchoolId == SchoolId && !c.IsDeleted, ct))
            throw DomainException.NotFound("Class");
        if (request.SectionId is > 0 && !await _db.Sections.AnyAsync(s =>
                s.Id == request.SectionId && s.ClassId == request.ClassId && s.SchoolId == SchoolId && !s.IsDeleted, ct))
            throw DomainException.NotFound("Section of this class");

        var material = new StudyMaterial
        {
            SchoolId = SchoolId, ClassId = request.ClassId, SectionId = request.SectionId,
            SubjectId = subject.Id, Title = request.Title.Trim(), Description = request.Description,
            FileUrl = request.FileUrl, VideoUrl = request.VideoUrl, Chapter = request.Chapter,
            IsPublished = true
        };
        _db.Add(material);
        await _uow.SaveChangesAsync(ct);

        return new StudyMaterialDto
        {
            Id = material.Id, ClassId = material.ClassId, SectionId = material.SectionId,
            SubjectId = material.SubjectId, SubjectName = subject.Name, Title = material.Title,
            Description = material.Description, FileUrl = material.FileUrl, VideoUrl = material.VideoUrl,
            Chapter = material.Chapter, CreatedAtUtc = material.CreatedAtUtc
        };
    }

    private async Task<string> NextEmployeeNoAsync(CancellationToken ct)
    {
        var year = _clock.Today.Year;
        for (var i = 1; i < 100000; i++)
        {
            var candidate = $"EMP-{year}-{i:0000}";
            var exists = await _db.Employees.IgnoreQueryFilters()
                .AnyAsync(e => e.SchoolId == SchoolId && e.EmployeeNo == candidate, ct);
            if (!exists) return candidate;
        }

        throw DomainException.Conflict("Employee number sequence exhausted.");
    }
}
