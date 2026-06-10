using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RM_CMS.BLL.Worship;
using RM_CMS.DAL.Worship;

namespace RM_CMS.Extensions
{
    public static class WorshipModuleExtensions
    {
        public static IServiceCollection AddWorshipModule(this IServiceCollection services, IConfiguration configuration)
        {
            var worshipConnectionString = configuration.GetConnectionString("WorshipDatabase") 
                ?? throw new InvalidOperationException("WorshipDatabase connection string is missing.");

            services.AddScoped<IWorshipRepository>(provider => new WorshipRepository(worshipConnectionString));
            services.AddScoped<IWorshipService, WorshipService>();

            return services;
        }
    }
}