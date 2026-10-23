using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniStay.Application;
using UniStay.Core;
namespace UniStay.Api;

[ApiController]
public abstract class HousingControllerBase : ControllerBase
{
    protected Account? Viewer => (HttpContext.Items["session"] as Session)?.Account;
    protected Account Actor => Viewer ?? throw new ApiFault(401, "Требуется вход.");
    protected string Origin => $"{Request.Scheme}://{Request.Host}";
    protected async Task<PageResponse<TResponse>> PageAsync<T, TResponse>(IQueryable<T> query, Func<T, TResponse> map, CancellationToken ct)
    {
        var page = 1;
        if (Request.Query.ContainsKey("page") && (!int.TryParse(Request.Query["page"], out page) || page < 1 || page > 1000000)) throw new ApiFault(404, "Страница не найдена.");
        var count = await query.CountAsync(ct);
        if (page > 1 && (page - 1) * 20 >= count) throw new ApiFault(404, "Страница не найдена.");
        string Link(int number)
        {
            var values = Request.Query.ToDictionary(k => k.Key, k => k.Value.ToString()); values["page"] = number.ToString();
            return Origin + Request.Path + "?" + string.Join("&", values.Select(k => Uri.EscapeDataString(k.Key) + "=" + Uri.EscapeDataString(k.Value)));
        }
        var results = await query.Skip((page - 1) * 20).Take(20).ToListAsync(ct);
        return new(count, page * 20 < count ? Link(page + 1) : null, page > 1 ? Link(page - 1) : null, results.Select(map).ToArray());
    }
}
[Route("api/v1/listings")]
public sealed class ListingsController(HousingService service) : HousingControllerBase
{
    [HttpGet("")]
    public Task<PageResponse<ListingResponse>> List([FromQuery] ListingQuery filters, CancellationToken ct)
    {
        var query = service.Listings.Where(l => l.Status == "published");
        var p = Request.Query;
        string Val(string key) => p[key].ToString();
        if (p.ContainsKey("city")) { var value = Val("city").ToLowerInvariant(); query = query.Where(l => l.City.ToLower().Contains(value)); }
        if (p.ContainsKey("district")) { var value = Val("district").ToLowerInvariant(); query = query.Where(l => l.District.ToLower().Contains(value)); }
        if (p.ContainsKey("university")) { var value = Val("university").ToLowerInvariant(); query = query.Where(l => l.University.ToLower().Contains(value)); }
        if (p.ContainsKey("search")) { var value = Val("search").ToLowerInvariant(); query = query.Where(l => l.Title.ToLower().Contains(value) || l.Description.ToLower().Contains(value)); }
        foreach (var key in new[] { "min_price", "max_price" }) if (p.ContainsKey(key))
        {
            if (!decimal.TryParse(Val(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var amount) || amount < 0 || amount > 99999999) throw new ApiFault(400, "Некорректная цена фильтра.");
            query = key == "min_price" ? query.Where(l => l.Price >= amount) : query.Where(l => l.Price <= amount);
        }
        if (p.ContainsKey("available") && Val("available") != "")
        {
            if (Val("available") is not ("true" or "false")) throw new ApiFault(400, "Используйте true или false.");
            query = Val("available") == "true" ? query.Where(l => l.Applications.Count(a => a.Status == "accepted") < l.Capacity) : query.Where(l => l.Applications.Count(a => a.Status == "accepted") >= l.Capacity);
        }
        var ordering = p.ContainsKey("ordering") ? Val("ordering") : "-created_at";
        query = ordering switch
        {
            "price" => query.OrderBy(l => l.Price).ThenBy(l => l.Id),
            "-price" => query.OrderByDescending(l => l.Price).ThenBy(l => l.Id),
            "created_at" => query.OrderBy(l => l.CreatedAt).ThenBy(l => l.Id),
            "-created_at" => query.OrderByDescending(l => l.CreatedAt).ThenBy(l => l.Id),
            _ => throw new ApiFault(400, "Некорректная сортировка.")
        };
        return PageAsync(query, l => ListingResponse.From(l, Viewer?.Id, Origin), ct);
    }
    [Authorize, HttpGet("mine/")]
    public Task<PageResponse<ListingResponse>> Mine(CancellationToken ct)
    { Access.Role(Actor, "landlord"); return PageAsync(service.Listings.Where(l => l.OwnerId == Actor.Id).OrderByDescending(l => l.CreatedAt).ThenByDescending(l => l.Id), l => ListingResponse.From(l, Actor.Id, Origin), ct); }
    [HttpGet("{id:long}/")]
    public async Task<ListingResponse> Detail(long id, CancellationToken ct) => ListingResponse.From(await service.DetailAsync(id, Viewer, ct), Viewer?.Id, Origin);
    [Authorize, HttpPost("")]
    public async Task<ActionResult<ListingResponse>> Create(ListingRequest input, CancellationToken ct) => StatusCode(201, ListingResponse.From(await service.SaveListingAsync(Actor, null, input, ct), Actor.Id, Origin));
    [Authorize, HttpPatch("{id:long}/")]
    public async Task<ListingResponse> Patch(long id, ListingRequest input, CancellationToken ct) => ListingResponse.From(await service.SaveListingAsync(Actor, id, input, ct), Actor.Id, Origin);
    [Authorize, HttpDelete("{id:long}/")]
    public async Task<IActionResult> Delete(long id, CancellationToken ct) { await service.ArchiveAsync(Actor, id, ct); return NoContent(); }
    [Authorize, HttpGet("{id:long}/group/")]
    public Task<IReadOnlyList<GroupResponse>> Group(long id, CancellationToken ct) => service.GroupAsync(Actor, id, ct);
}
[Authorize, Route("api/v1/applications")]
public sealed class ApplicationsController(HousingService service) : HousingControllerBase
{
    [HttpGet("")]
    public Task<PageResponse<ApplicationResponse>> List(CancellationToken ct) => PageAsync(service.Applications.Where(a => a.StudentId == Actor.Id || a.Listing.OwnerId == Actor.Id).OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id), ApplicationResponse.From, ct);
    [HttpGet("{id:long}/")]
    public async Task<ApplicationResponse> Detail(long id, CancellationToken ct) => ApplicationResponse.From(await service.Applications.SingleOrDefaultAsync(a => a.Id == id && (a.StudentId == Actor.Id || a.Listing.OwnerId == Actor.Id), ct) ?? throw new ApiFault(404, "Не найдено."));
    [HttpPost("")]
    public async Task<ActionResult<ApplicationResponse>> Create(ApplyRequest input, CancellationToken ct) => StatusCode(201, ApplicationResponse.From(await service.ApplyAsync(Actor, input, ct)));
    [HttpPost("{id:long}/accept/")]
    public async Task<ApplicationResponse> Accept(long id, CancellationToken ct) => ApplicationResponse.From(await service.TransitionAsync(Actor, id, "accepted", ct));
    [HttpPost("{id:long}/reject/")]
    public async Task<ApplicationResponse> Reject(long id, CancellationToken ct) => ApplicationResponse.From(await service.TransitionAsync(Actor, id, "rejected", ct));
    [HttpPost("{id:long}/cancel/")]
    public async Task<ApplicationResponse> Cancel(long id, CancellationToken ct) => ApplicationResponse.From(await service.TransitionAsync(Actor, id, "cancelled", ct));
}
