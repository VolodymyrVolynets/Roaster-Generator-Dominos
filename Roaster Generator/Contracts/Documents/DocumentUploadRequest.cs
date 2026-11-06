using Microsoft.AspNetCore.Http;

namespace Roaster_Generator.Contracts.Documents;

public sealed class DocumentUploadRequest
{
    public List<IFormFile> Files { get; set; } = [];
}
