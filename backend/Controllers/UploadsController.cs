using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using CopilotEvalApi.Models;

namespace CopilotEvalApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UploadsController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<UploadsController> _logger;

    public UploadsController(IConfiguration configuration, ILogger<UploadsController> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    [HttpPost]
    [ProducesResponseType(typeof(BlobReference), 201)]
    [ProducesResponseType(typeof(ErrorResponse), 400)]
    [ProducesResponseType(typeof(ErrorResponse), 500)]
    public async Task<IActionResult> Upload([Required] IFormFile file)
    {
        var requestId = Guid.NewGuid().ToString("N")[..8];
        _logger.LogInformation("📤 [Uploads {RequestId}] Upload request received: {FileName} ({Size} bytes)", requestId, file?.FileName, file?.Length ?? 0);

        if (file == null || file.Length == 0)
        {
            return BadRequest(new ErrorResponse(new ErrorDetails("VALIDATION_ERROR", "No file was provided")));
        }

        // Determine blob storage connection
        var storageConnectionString = _configuration.GetConnectionString("BlobStorage");
        if (string.IsNullOrEmpty(storageConnectionString) || storageConnectionString == "InMemory")
        {
            return BadRequest(new ErrorResponse(new ErrorDetails("STORAGE_NOT_CONFIGURED", "Blob storage is not configured on the server")));
        }

        try
        {
            var containerName = "uploads";
            var blobName = $"{Guid.NewGuid()}/{Path.GetFileName(file.FileName)}";

            var blobServiceClient = new BlobServiceClient(storageConnectionString);
            var containerClient = blobServiceClient.GetBlobContainerClient(containerName);
            await containerClient.CreateIfNotExistsAsync(PublicAccessType.None);

            var blobClient = containerClient.GetBlobClient(blobName);

            // Upload stream
            using (var stream = file.OpenReadStream())
            {
                await blobClient.UploadAsync(stream, overwrite: true);
            }

            var blobReference = new BlobReference
            {
                BlobId = Guid.NewGuid(),
                StorageAccount = blobServiceClient.AccountName,
                Container = containerName,
                BlobName = blobName,
                ContentType = file.ContentType ?? "text/csv",
                SizeBytes = file.Length,
                CreatedAt = DateTimeOffset.UtcNow,
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(30),
                AccessUrl = blobClient.Uri.ToString()
            };

            _logger.LogInformation("✅ [Uploads {RequestId}] File uploaded to blob storage: {BlobName}", requestId, blobName);

            return Created(string.Empty, blobReference);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "💥 [Uploads {RequestId}] Failed to upload file to blob storage: {Error}", requestId, ex.Message);
            return StatusCode(500, new ErrorResponse(new ErrorDetails("INTERNAL_ERROR", "Failed to upload file to storage")));
        }
    }
}
