using Microsoft.AspNetCore.StaticFiles;

namespace LostAndFoundApp.Services
{
    /// <summary>
    /// Handles secure file upload with server-side validation for type, size, and storage
    /// outside of web root. Files are never served directly — only through authenticated controller actions.
    /// </summary>
    public class FileService
    {
        private readonly IConfiguration _config;
        private readonly ILogger<FileService> _logger;

        public FileService(IConfiguration config, ILogger<FileService> logger)
        {
            _config = config;
            _logger = logger;
        }

        /// <summary>
        /// Saves an uploaded photo to the configured secure storage path.
        /// Returns the stored file name on success, or null on failure.
        /// </summary>
        public async Task<string?> SavePhotoAsync(IFormFile file)
        {
            var allowedExtensions = _config.GetSection("FileUpload:AllowedPhotoExtensions").Get<string[]>()
                ?? new[] { ".jpg", ".jpeg", ".png", ".gif" };
            var storagePath = _config["FileUpload:PhotoStoragePath"] ?? "./SecureStorage/Photos";
            return await SaveFileAsync(file, storagePath, allowedExtensions);
        }

        /// <summary>
        /// Saves an uploaded attachment to the configured secure storage path.
        /// Returns the stored file name on success, or null on failure.
        /// </summary>
        public async Task<string?> SaveAttachmentAsync(IFormFile file)
        {
            var allowedExtensions = _config.GetSection("FileUpload:AllowedAttachmentExtensions").Get<string[]>()
                ?? new[] { ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".txt", ".jpg", ".jpeg", ".png" };
            var storagePath = _config["FileUpload:AttachmentStoragePath"] ?? "./SecureStorage/Attachments";
            return await SaveFileAsync(file, storagePath, allowedExtensions);
        }

        private async Task<string?> SaveFileAsync(IFormFile file, string storagePath, string[] allowedExtensions)
        {
            if (file == null || file.Length == 0)
                return null;

            // Validate file size
            var maxSize = _config.GetValue<long>("FileUpload:MaxFileSizeBytes", 10485760); // 10MB default
            if (file.Length > maxSize)
            {
                _logger.LogWarning("File upload rejected: size {Size} exceeds max {Max}", file.Length, maxSize);
                return null;
            }

            // Validate file extension
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (string.IsNullOrEmpty(extension) || !allowedExtensions.Contains(extension))
            {
                _logger.LogWarning("File upload rejected: extension '{Ext}' is not allowed", extension);
                return null;
            }

            // Reject files with multiple extensions (e.g., 'malware.exe.jpg') as a security measure
            var nameWithoutExt = Path.GetFileNameWithoutExtension(file.FileName);
            if (nameWithoutExt.Contains('.'))
            {
                var innerExt = Path.GetExtension(nameWithoutExt).ToLowerInvariant();
                if (!string.IsNullOrEmpty(innerExt) && !allowedExtensions.Contains(innerExt))
                {
                    _logger.LogWarning("File upload rejected: suspicious double extension in '{FileName}'", file.FileName);
                    return null;
                }
            }

            // Validate MIME type matches the extension (defense in depth)
            var expectedMimeTypes = GetExpectedMimeTypes(extension);
            if (expectedMimeTypes.Length > 0 && !string.IsNullOrEmpty(file.ContentType))
            {
                var contentType = file.ContentType.ToLowerInvariant();
                if (!expectedMimeTypes.Contains(contentType))
                {
                    _logger.LogWarning("File upload rejected: MIME type '{ContentType}' does not match extension '{Ext}'",
                        file.ContentType, extension);
                    return null;
                }
            }

            // Ensure storage directory exists
            Directory.CreateDirectory(storagePath);

            // Generate unique file name to prevent overwriting and path traversal
            var uniqueFileName = $"{Guid.NewGuid()}{extension}";
            var filePath = Path.Combine(storagePath, uniqueFileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            _logger.LogInformation("File saved successfully: {FileName}", uniqueFileName);
            return uniqueFileName;
        }

        /// <summary>
        /// Returns expected MIME types for a given file extension.
        /// Used to verify the browser-reported Content-Type matches the extension.
        /// </summary>
        private static string[] GetExpectedMimeTypes(string extension) => extension switch
        {
            ".jpg" or ".jpeg" => new[] { "image/jpeg" },
            ".png" => new[] { "image/png" },
            ".gif" => new[] { "image/gif" },
            ".pdf" => new[] { "application/pdf" },
            ".doc" => new[] { "application/msword" },
            ".docx" => new[] { "application/vnd.openxmlformats-officedocument.wordprocessingml.document" },
            ".xls" => new[] { "application/vnd.ms-excel" },
            ".xlsx" => new[] { "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" },
            ".txt" => new[] { "text/plain" },
            _ => Array.Empty<string>()
        };

        /// <summary>
        /// Retrieves file bytes for authenticated download. Returns null if file not found.
        /// FIX: Changed from returning FileStream (which caused memory leaks) to returning byte array.
        /// </summary>
        public (byte[]? Bytes, string ContentType)? GetPhoto(string fileName)
        {
            var storagePath = _config["FileUpload:PhotoStoragePath"] ?? "./SecureStorage/Photos";
            return GetFile(fileName, storagePath);
        }

        /// <summary>
        /// Retrieves file bytes for authenticated download. Returns null if file not found.
        /// FIX: Changed from returning FileStream (which caused memory leaks) to returning byte array.
        /// </summary>
        public (byte[]? Bytes, string ContentType)? GetAttachment(string fileName)
        {
            var storagePath = _config["FileUpload:AttachmentStoragePath"] ?? "./SecureStorage/Attachments";
            return GetFile(fileName, storagePath);
        }

        private (byte[]? Bytes, string ContentType)? GetFile(string fileName, string storagePath)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return null;

            // Prevent path traversal attacks by ensuring only the file name is used
            var sanitizedFileName = Path.GetFileName(fileName);
            var filePath = Path.GetFullPath(Path.Combine(storagePath, sanitizedFileName));

            // Defense-in-depth: ensure resolved path stays within the storage directory
            var fullStoragePath = Path.GetFullPath(storagePath);
            if (!filePath.StartsWith(fullStoragePath, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Path traversal attempt detected: {Path}", fileName);
                return null;
            }

            if (!File.Exists(filePath))
            {
                _logger.LogWarning("File not found: {Path}", filePath);
                return null;
            }

            var provider = new FileExtensionContentTypeProvider();
            if (!provider.TryGetContentType(sanitizedFileName, out var contentType))
            {
                contentType = "application/octet-stream";
            }

            // FIX: Read file into byte array and properly dispose the FileStream
            byte[] fileBytes;
            try
            {
                fileBytes = File.ReadAllBytes(filePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to read file: {Path}", filePath);
                return null;
            }

            return (fileBytes, contentType);
        }

        /// <summary>
        /// Deletes a file from secure storage. Used when replacing uploads.
        /// </summary>
        public void DeletePhoto(string? fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return;
            var storagePath = _config["FileUpload:PhotoStoragePath"] ?? "./SecureStorage/Photos";
            DeleteFile(fileName, storagePath);
        }

        public void DeleteAttachment(string? fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return;
            var storagePath = _config["FileUpload:AttachmentStoragePath"] ?? "./SecureStorage/Attachments";
            DeleteFile(fileName, storagePath);
        }

        private void DeleteFile(string fileName, string storagePath)
        {
            try
            {
                var sanitizedFileName = Path.GetFileName(fileName);
                var filePath = Path.Combine(storagePath, sanitizedFileName);
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    _logger.LogInformation("File deleted: {FileName}", sanitizedFileName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete file '{FileName}'. It may be locked or inaccessible.", fileName);
                // Don't rethrow — file deletion failure should not crash the parent operation
            }
        }
    }
}
