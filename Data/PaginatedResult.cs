using System.Collections.Generic;

namespace RM_CMS.Data
{
    public class PaginatedResult<T>
    {
        public IEnumerable<T> Data { get; set; } = new List<T>();
        public int TotalCount { get; set; }
    }
}
