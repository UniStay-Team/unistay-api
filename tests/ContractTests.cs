using System.Text.Json;
using System.Text.Json.Serialization;
using UniStay.Application;
using UniStay.Core;
using Xunit;
public sealed class ContractTests
{
    private static JsonSerializerOptions Options => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        Converters = { new OptionalConverterFactory() }
    };
    [Fact]
    public void PatchDistinguishesOmittedAndExplicitNull()
    {
        var empty = JsonSerializer.Deserialize<ListingRequest>("{}", Options)!;
        var clear = JsonSerializer.Deserialize<ListingRequest>("{\"move_in_date\":null}", Options)!;
        Assert.False(empty.MoveInDate.IsSet); Assert.True(clear.MoveInDate.IsSet); Assert.Null(clear.MoveInDate.Value);
    }
    [Fact]
    public void PricesAcceptDjangoDecimalStrings()
    { var request = JsonSerializer.Deserialize<ListingRequest>("{\"price\":\"12345.67\"}", Options)!; Assert.Equal(12345.67m, request.Price.Value); }
    [Fact]
    public void SmokingFalseIsAnExplicitAnswer()
    { var request = JsonSerializer.Deserialize<ProfilePatch>("{\"smoking\":false}", Options)!; Assert.True(request.HasSmoking); Assert.False(request.Smoking); Assert.False(request.HasUniversity); }
    [Fact]
    public void AddressIsHiddenUntilAcceptedOrOwner()
    {
        var listing = new Listing { OwnerId = 10, Owner = new Account { Name = "Owner" }, Address = "private", Capacity = 2, Price = 100 };
        Assert.Null(ListingResponse.From(listing, null, "http://localhost").Address);
        Assert.Null(ListingResponse.From(listing, 20, "http://localhost").Address);
        Assert.Equal("private", ListingResponse.From(listing, 10, "http://localhost").Address);
        listing.Applications.Add(new HousingApplication { StudentId = 20, Status = "pending" });
        Assert.Null(ListingResponse.From(listing, 20, "http://localhost").Address);
        listing.Applications[0].Status = "accepted";
        Assert.Equal("private", ListingResponse.From(listing, 20, "http://localhost").Address);
        var json = JsonSerializer.Serialize(ListingResponse.From(listing, 20, "http://localhost"), Options);
        Assert.Contains("\"price\":\"100.00\"", json); Assert.Contains("\"owner_name\"", json);
    }
    [Fact]
    public void LandlordIsNotAStudentAndUnverifiedCannotMutate()
    {
        Assert.Throws<ApiFault>(() => Access.Role(new Account { Role = "landlord", EmailVerified = true }, "student"));
        Assert.Throws<ApiFault>(() => Access.Role(new Account { Role = "landlord" }, "landlord"));
    }
    [Theory]
    [InlineData("123456789012")]
    [InlineData("password123")]
    [InlineData("short")]
    [InlineData("example.test")]
    public void WeakPasswordsAreRejected(string value) => Assert.Throws<ApiFault>(() => PasswordPolicy.Validate(value, new Account { Email = "student@example.test" }));
    [Fact] public void StrongPasswordIsAccepted() => PasswordPolicy.Validate("Unique-Between!84Lake", new Account { Email = "student@example.test" });
}
