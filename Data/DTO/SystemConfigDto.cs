namespace RM_CMS.Data.DTO
{
    public class SystemConfigDto
    {
        public string ConfigKey { get; set; }
        public string ConfigValue { get; set; }
        public string ConfigType { get; set; }
        public string Description { get; set; }
    }

    public class UpdateSystemConfigDto
    {
        public string ConfigKey { get; set; }
        public string ConfigValue { get; set; }
    }
}