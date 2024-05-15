namespace ASC.ZoomService.Models
{
    public class ZoomLinkResponse
    {
        public string Login { get; set; }
        public List<ZoomTenantInfo> TenantInfo { get; set; } = new List<ZoomTenantInfo>();
    }

    public class ZoomTenantInfo
    {
        public string Name { get; set; }
        public string Domain { get; set; }
        public int Id { get; set; }
    }
}
