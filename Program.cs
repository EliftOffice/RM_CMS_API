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
            builder.Services.AddScoped<RM_CMS.DAL.Visitors.IPeopleRepository, RM_CMS.DAL.Visitors.PeopleRepository>();
            builder.Services.AddScoped<RM_CMS.BLL.Visitors.IPeopleService, RM_CMS.BLL.Visitors.PeopleService>();

            // Register Volunteers Module
            builder.Services.AddScoped<RM_CMS.DAL.Volunteers.IVolunteerRepository, RM_CMS.DAL.Volunteers.VolunteerRepository>();
            builder.Services.AddScoped<RM_CMS.BLL.Volunteers.IVolunteerService, RM_CMS.BLL.Volunteers.VolunteerService>();

            // Register Followups Module
            builder.Services.AddScoped<RM_CMS.DAL.Followups.IFollowUpRepository, RM_CMS.DAL.Followups.FollowUpRepository>();
            builder.Services.AddScoped<RM_CMS.BLL.Followups.IFollowUpService, RM_CMS.BLL.Followups.FollowUpService>();

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseHttpsRedirection();

            // Enable static file serving for templates
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
