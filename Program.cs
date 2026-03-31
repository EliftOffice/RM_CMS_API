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

            app.UseAuthorization();


            app.MapControllers();

            app.Run();
        }
    }
}
