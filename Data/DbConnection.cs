using System.Data;
using MySqlConnector;

namespace RM_CMS.Data
{
    public interface IDbConnectionFactory
    {
        IDbConnection GetConnection();
    }

    public class DbConnectionFactory : IDbConnectionFactory
    {
        private readonly IConfiguration _configuration;

        public DbConnectionFactory(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public IDbConnection GetConnection()
        {
            var connectionString = _configuration.GetConnectionString("DefaultConnection");
            return new MySqlConnection(connectionString);
        }
    }
}
