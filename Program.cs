namespace RM_CMS
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.

            builder.Services.AddControllers();
            // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            // Add CORS support
            builder.Services.AddCors(options =>
            {
                options.AddPolicy("AllowAll", policy =>
                {
                    policy.AllowAnyOrigin()
                          .AllowAnyMethod()
                          .AllowAnyHeader();
                });
            });

            // Register Dapper and Data Access Services
            builder.Services.AddScoped<RM_CMS.Data.IDbConnectionFactory, RM_CMS.Data.DbConnectionFactory>();

            // Register Common DAL Services
            builder.Services.AddScoped<RM_CMS.DAL.CommonDAL.ITelegram, RM_CMS.DAL.CommonDAL.Telegram>();

            // Register Peoples Module
            builder.Services.AddScoped<RM_CMS.BLL.Peoples.IPeoplesBLL, RM_CMS.BLL.Peoples.PeoplesBLL>();
            builder.Services.AddScoped<RM_CMS.DAL.Peoples.IPeoplesDAL, RM_CMS.DAL.Peoples.PeoplesDAL>();

            // Register Followups Module
            builder.Services.AddScoped<RM_CMS.BLL.Followups.IFollowupsBLL, RM_CMS.BLL.Followups.FollowupsBLL>();
            builder.Services.AddScoped<RM_CMS.DAL.Followups.IFollowupsDAL, RM_CMS.DAL.Followups.FollowupsDAL>();

            // Register Escalations Module
            builder.Services.AddScoped<RM_CMS.BLL.Followups.IEscalationsBLL, RM_CMS.BLL.Followups.EscalationsBLL>();
            builder.Services.AddScoped<RM_CMS.DAL.Followups.IEscalationsDAL, RM_CMS.DAL.Followups.EscalationsDAL>();    

            // Register Volunteers Module
            builder.Services.AddScoped<RM_CMS.BLL.Volunteers.IVolunteersBLL, RM_CMS.BLL.Volunteers.VolunteersBLL>();
            builder.Services.AddScoped<RM_CMS.DAL.Volunteers.IVolunteersDAL, RM_CMS.DAL.Volunteers.VolunteersDAL>();


            // Register Team Leads Module
            builder.Services.AddScoped<RM_CMS.DAL.TeamLeads.ITeamLeadDashBoardDAL, RM_CMS.DAL.TeamLeads.TeamLeadDashBoardDAL>();
            builder.Services.AddScoped<RM_CMS.BLL.TeamLeads.ITeamLeadDashBoardBLL, RM_CMS.BLL.TeamLeads.TeamLeadDashBoardBLL>();

           

            //register check-in  module
            builder.Services.AddScoped<RM_CMS.DAL.TeamLeads.ICheckInDAL, RM_CMS.DAL.TeamLeads.CheckInDAL>();
            builder.Services.AddScoped<RM_CMS.BLL.TeamLeads.ICheckInBLL, RM_CMS.BLL.TeamLeads.CheckInBLL>();           
           

            // Register Pastors Module
            builder.Services.AddScoped<RM_CMS.DAL.Pastors.IPastorDashboardDAL, RM_CMS.DAL.Pastors.PastorDashboardDAL>();
            builder.Services.AddScoped<RM_CMS.BLL.Pastors.IPastorDashboardBLL, RM_CMS.BLL.Pastors.PastorDashBoardBLL>();

            // Register Users Module
            builder.Services.AddScoped<RM_CMS.DAL.Users.IUsersDAL, RM_CMS.DAL.Users.UsersDAL>();
            builder.Services.AddScoped<RM_CMS.BLL.Users.IUsersBLL, RM_CMS.BLL.Users.UsersBLL>();

            // Register Events Module
            builder.Services.AddScoped<RM_CMS.DAL.Events.IEventsDAL, RM_CMS.DAL.Events.EventsDAL>();
            builder.Services.AddScoped<RM_CMS.BLL.Events.IEventsBLL, RM_CMS.BLL.Events.EventsBLL>();

            // Register Attendance Module
            builder.Services.AddScoped<RM_CMS.DAL.Attendance.IAttendanceDAL, RM_CMS.DAL.Attendance.AttendanceDAL>();
            builder.Services.AddScoped<RM_CMS.BLL.Attendance.IAttendanceBLL, RM_CMS.BLL.Attendance.AttendanceBLL>();

            // Register Nurture Module
            builder.Services.AddScoped<RM_CMS.DAL.Nurture.INurtureDAL, RM_CMS.DAL.Nurture.NurtureDAL>();
            builder.Services.AddScoped<RM_CMS.BLL.Nurture.INurtureBLL, RM_CMS.BLL.Nurture.NurtureBLL>();

            //Register CornJobs
            builder.Services.AddScoped<RM_CMS.BLL.Jobs.ICornJobsBLL,RM_CMS.BLL.Jobs.CornJobsBLL > ();

            // Register Admin Module for System Config
            builder.Services.AddScoped<RM_CMS.DAL.Admin.ISystemConfigRepository, RM_CMS.DAL.Admin.SystemConfigRepository>();
            builder.Services.AddScoped<RM_CMS.BLL.Admin.ISystemConfigService, RM_CMS.BLL.Admin.SystemConfigService>();
            builder.Services.AddScoped<RM_CMS.BLL.Admin.INotificationService, RM_CMS.BLL.Admin.NotificationService>();

            Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;



            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseHttpsRedirection();

            // Enable static file serving for wwwroot
            app.UseStaticFiles(new StaticFileOptions
            {
                ServeUnknownFileTypes = true
            });

            // Use CORS
            app.UseCors("AllowAll");

            app.UseAuthorization();

            app.MapControllers();

            // Map default document
            app.MapGet("/", async context =>
            {
                context.Response.ContentType = "text/html";
                await context.Response.SendFileAsync(Path.Combine(app.Environment.ContentRootPath, "wwwroot", "templates","Volunteers", "Login.html"));
            });          

            app.Run();
        }
    }
}
