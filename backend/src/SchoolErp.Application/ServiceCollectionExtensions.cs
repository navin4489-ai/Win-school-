using Microsoft.Extensions.DependencyInjection;
using SchoolErp.Application.Administration;
using SchoolErp.Application.Admissions;
using SchoolErp.Application.Attendance;
using SchoolErp.Application.Auth;
using SchoolErp.Application.Dashboard;
using SchoolErp.Application.Exams;
using SchoolErp.Application.Fees;
using SchoolErp.Application.Homework;
using SchoolErp.Application.Notifications;
using SchoolErp.Application.Operations;
using SchoolErp.Application.Parents;
using SchoolErp.Application.Reports;
using SchoolErp.Application.Students;

namespace SchoolErp.Application;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<AuthService>();
        services.AddScoped<AdministrationService>();
        services.AddScoped<AdmissionService>();
        services.AddScoped<StudentService>();
        services.AddScoped<ParentService>();
        services.AddScoped<FeeService>();
        services.AddScoped<AttendanceService>();
        services.AddScoped<ExamService>();
        services.AddScoped<HomeworkService>();
        services.AddScoped<DashboardService>();
        services.AddScoped<ReportService>();
        services.AddScoped<NotificationService>();
        services.AddScoped<OperationsService>();
        services.AddScoped<Platform.PlatformService>();
        return services;
    }
}
