namespace LostAndFoundApp.Models
{
    public class ErrorViewModel
    {
        public string? RequestId { get; set; }
        public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);
        public int StatusCode { get; set; } = 500;

        public string Title => StatusCode switch
        {
            400 => "Bad Request",
            403 => "Access Denied",
            404 => "Page Not Found",
            405 => "Method Not Allowed",
            408 => "Request Timeout",
            500 => "Something Went Wrong",
            503 => "Service Unavailable",
            _ => "Error"
        };

        public string Message => StatusCode switch
        {
            400 => "The request could not be understood by the server. Please check the URL and try again.",
            403 => "You don't have permission to access this resource. Contact your administrator if you believe this is an error.",
            404 => "The page or record you're looking for doesn't exist or may have been removed.",
            405 => "This action is not supported. Please go back and try a different approach.",
            408 => "The server took too long to respond. Please try again.",
            500 => "An unexpected error occurred on the server. Please try again later.",
            503 => "The service is temporarily unavailable. Please try again in a few minutes.",
            _ => "An error occurred. Please try again."
        };

        public string Icon => StatusCode switch
        {
            403 => "bi-shield-x",
            404 => "bi-search",
            408 => "bi-hourglass-split",
            503 => "bi-cloud-slash",
            _ => "bi-exclamation-triangle"
        };

        public string IconColor => StatusCode switch
        {
            403 => "hsl(0, 72%, 51%)",
            404 => "hsl(217, 91%, 60%)",
            _ => "#E6A817"
        };
    }
}
