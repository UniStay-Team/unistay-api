using ImageMagick;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using UniStay.Core;
namespace UniStay.Application;

public sealed class PhotoService(UniStayDb db, IConfiguration config)
{
    private string Folder => config["MediaDirectory"] ?? throw new InvalidOperationException("MediaDirectory required.");
    public async Task<ListingPhoto> UploadAsync(Account actor, long listingId, byte[] data, string caption, CancellationToken ct)
    {
        Access.Role(actor, "landlord");
        if (!await db.Listings.AnyAsync(l => l.Id == listingId && l.OwnerId == actor.Id, ct)) throw new ApiFault(404, "Не найдено.");
        if (data.Length is 0 or > 5242880 || caption.Length > 160) throw new ApiFault(400, "Фото до 5 MiB, подпись до 160 символов.");
        // Restrict the decoder before processing untrusted bytes: never auto-detect SVG/PDF/delegates.
        var format = data.Length >= 3 && data[0] == 255 && data[1] == 216 && data[2] == 255 ? MagickFormat.Jpeg :
            data.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) ? MagickFormat.Png :
            data.Length >= 12 && System.Text.Encoding.ASCII.GetString(data, 0, 4) == "RIFF" && System.Text.Encoding.ASCII.GetString(data, 8, 4) == "WEBP" ? MagickFormat.WebP : throw new ApiFault(400, "Нужен JPEG, PNG или WebP.");
        byte[] encoded;
        try
        {
            var settings = new MagickReadSettings { Format = format, FrameCount = 1 };
            using var probe = new MagickImage(); probe.Ping(data, settings);
            if ((ulong)probe.Width * probe.Height > 20_000_000) throw new ApiFault(400, "Максимум 20 мегапикселей.");
            using var image = new MagickImage(data, settings);
            image.AutoOrient(); image.Resize(new MagickGeometry(2400, 2400) { Greater = true });
            image.Strip(); image.Format = MagickFormat.Jpeg; image.Quality = 85;
            encoded = image.ToByteArray();
        }
        catch (MagickException) { throw new ApiFault(400, "Повреждённое изображение."); }
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        _ = await db.Listings.FromSqlInterpolated($"SELECT * FROM listings WHERE \"Id\"={listingId} FOR UPDATE").SingleAsync(ct);
        if (await db.Photos.CountAsync(p => p.ListingId == listingId, ct) >= 10) throw new ApiFault(409, "Не больше 10 фотографий.");
        Directory.CreateDirectory(Folder);
        var file = Guid.NewGuid().ToString("N") + ".jpg";
        var path = Path.Combine(Folder, file);
        var photo = new ListingPhoto { ListingId = listingId, FileName = file, Caption = caption.Trim() };
        try
        {
            await File.WriteAllBytesAsync(path, encoded, ct);
            db.Photos.Add(photo); await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        }
        catch { File.Delete(path); throw; }
        return photo;
    }
    public async Task DeleteAsync(Account actor, long id, CancellationToken ct)
    {
        Access.Role(actor, "landlord");
        var info = await db.Photos.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, ct) ?? throw new ApiFault(404, "Не найдено.");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var listing = await db.Listings.FromSqlInterpolated($"SELECT * FROM listings WHERE \"Id\"={info.ListingId} FOR UPDATE").SingleAsync(ct);
        if (listing.OwnerId != actor.Id) throw new ApiFault(404, "Не найдено.");
        var photo = await db.Photos.SingleOrDefaultAsync(p => p.Id == id, ct) ?? throw new ApiFault(404, "Не найдено.");
        if (listing.Status == "published" && await db.Photos.CountAsync(p => p.ListingId == listing.Id, ct) <= 1) throw new ApiFault(409, "Нельзя удалить последнее фото опубликованного объявления.");
        db.Photos.Remove(photo); await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        File.Delete(Path.Combine(Folder, photo.FileName));
    }
}
