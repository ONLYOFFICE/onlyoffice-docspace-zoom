namespace ASC.ZoomService.Models
{
    public class ZoomIntegrationPayload
    {
        public string ConfirmLink { get; set; }
        public string Error { get; set; }
        public string Home { get; set; } = "zoomservice";
        public string DocSpaceUrl { get; set; }
        public string OwnAccountId { get; set; }

        public ZoomCollaborationRoom Collaboration { get; set; }
    }
}
