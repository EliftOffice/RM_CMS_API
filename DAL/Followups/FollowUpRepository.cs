using RM_CMS.Data.Models;
using RM_CMS.Data;
using Dapper;
using System.Data;

namespace RM_CMS.DAL.Followups
{
    public interface IFollowUpRepository
    {
    }

    public class FollowUpRepository : IFollowUpRepository
    {
        private readonly IDbConnectionFactory _dbConnectionFactory;

        public FollowUpRepository(IDbConnectionFactory dbConnectionFactory)
        {
            _dbConnectionFactory = dbConnectionFactory;
        }
    }
}
