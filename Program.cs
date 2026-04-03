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
                await context.Response.SendFileAsync(Path.Combine(app.Environment.ContentRootPath, "wwwroot", "templates", "index.html"));
            });

            app.MapGet("/templates/index.html", async context =>
            {
                context.Response.ContentType = "text/html";
                await context.Response.SendFileAsync(Path.Combine(app.Environment.ContentRootPath, "wwwroot", "templates", "index.html"));
            });

            app.MapGet("/diagnostics", async context =>
            {
                context.Response.ContentType = "text/html";
                await context.Response.SendFileAsync(Path.Combine(app.Environment.ContentRootPath, "wwwroot", "templates", "diagnostics.html"));
            });

            app.MapGet("/templates/diagnostics.html", async context =>
            {
                context.Response.ContentType = "text/html";
                await context.Response.SendFileAsync(Path.Combine(app.Environment.ContentRootPath, "wwwroot", "templates", "diagnostics.html"));
            });

            app.Run();
        }
    }
}
