using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniStay.Application;
namespace UniStay.Api;

[Authorize]
public sealed class PhotosController(PhotoService service) : HousingControllerBase
{
    [HttpPost("api/v1/listings/{id:long}/photos/"), RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<ActionResult<PhotoResponse>> Upload(long id, [FromForm] IFormFile image, [FromForm] string caption = "", CancellationToken ct = default)
    {
        if (image.Length > 5 * 1024 * 1024) throw new ApiFault(400, "Максимум 5 MiB.");
        using var buffer = new MemoryStream(); await image.CopyToAsync(buffer, ct);
        var p = await service.UploadAsync(Actor, id, buffer.ToArray(), caption, ct);
        return StatusCode(201, new PhotoResponse(p.Id, Origin + "/media/" + p.FileName, p.Caption));
    }
    [HttpDelete("api/v1/photos/{id:long}/")]
    public async Task<IActionResult> Delete(long id, CancellationToken ct) { await service.DeleteAsync(Actor, id, ct); return NoContent(); }
}
