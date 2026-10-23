using Microsoft.AspNetCore.Mvc;
namespace UniStay.Api;
// Numeric/boolean filters remain strings so invalid values use the established API errors.
public sealed class ListingQuery
{
    [FromQuery(Name = "search")] public string? Search { get; set; }
    [FromQuery(Name = "city")] public string? City { get; set; }
    [FromQuery(Name = "district")] public string? District { get; set; }
    [FromQuery(Name = "university")] public string? University { get; set; }
    [FromQuery(Name = "min_price")] public string? MinPrice { get; set; }
    [FromQuery(Name = "max_price")] public string? MaxPrice { get; set; }
    [FromQuery(Name = "available")] public string? Available { get; set; }
    [FromQuery(Name = "ordering")] public string? Ordering { get; set; }
    [FromQuery(Name = "page")] public string? Page { get; set; }
}
