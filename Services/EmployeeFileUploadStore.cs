using System.Text.Json;

namespace ProjectTracking.Services;

public static class EmployeeFileUploadStore
{
    public const int ChunkSize = 8 * 1024 * 1024;
    public sealed record Metadata(int EmpId, string DocumentType, string FileName, string? Description, long FileSize);

    private static string Folder(string root, int userId, string uploadId)
    {
        if (userId <= 0 || !Guid.TryParseExact(uploadId, "N", out _))
            throw new InvalidDataException("รหัสอัปโหลดไม่ถูกต้อง");
        return Path.Combine(root, userId.ToString(System.Globalization.CultureInfo.InvariantCulture), uploadId);
    }

    public static async Task<string> BeginAsync(string root, int userId, Metadata metadata)
    {
        if (metadata.FileSize <= 0) throw new InvalidDataException("กรุณาเลือกไฟล์ที่มีข้อมูล");
        var id = Guid.NewGuid().ToString("N");
        var folder = Folder(root, userId, id);
        Directory.CreateDirectory(folder);
        await System.IO.File.WriteAllTextAsync(Path.Combine(folder, "metadata.json"), JsonSerializer.Serialize(metadata));
        return id;
    }

    public static async Task<Metadata> ReadAsync(string root, int userId, string uploadId)
    {
        var json = await System.IO.File.ReadAllTextAsync(Path.Combine(Folder(root, userId, uploadId), "metadata.json"));
        return JsonSerializer.Deserialize<Metadata>(json) ?? throw new InvalidDataException("ไม่พบข้อมูลอัปโหลด");
    }

    public static async Task<long> AppendAsync(string root, int userId, string uploadId, long offset, Stream input, long length)
    {
        var metadata = await ReadAsync(root, userId, uploadId);
        if (length <= 0 || length > ChunkSize || offset < 0 || offset > metadata.FileSize - length)
            throw new InvalidDataException("ขนาดชิ้นส่วนไฟล์ไม่ถูกต้อง");
        var path = Path.Combine(Folder(root, userId, uploadId), "content.bin");
        await using var output = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None, 65536, useAsync: true);
        if (output.Length != offset) throw new InvalidDataException("ลำดับอัปโหลดไม่ตรงกัน กรุณาลองใหม่");
        output.Position = offset;
        try
        {
            await input.CopyToAsync(output);
            if (output.Position - offset != length) throw new InvalidDataException("ชิ้นส่วนไฟล์ส่งมาไม่ครบ");
            return output.Position;
        }
        catch
        {
            output.SetLength(offset);
            throw;
        }
    }

    public static async Task MoveCompletedAsync(string root, int userId, string uploadId, string destination)
    {
        var metadata = await ReadAsync(root, userId, uploadId);
        var path = Path.Combine(Folder(root, userId, uploadId), "content.bin");
        await using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            if (stream.Length != metadata.FileSize) throw new InvalidDataException("ไฟล์ยังอัปโหลดไม่ครบ");
        }
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        System.IO.File.Move(path, destination);
    }

    public static void Delete(string root, int userId, string uploadId)
    {
        var folder = Folder(root, userId, uploadId);
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }

    public static void RemoveExpired(string root, int userId)
    {
        var ownerFolder = Path.Combine(root, userId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (!Directory.Exists(ownerFolder)) return;
        foreach (var folder in Directory.EnumerateDirectories(ownerFolder))
        {
            var lastWrite = Directory.EnumerateFiles(folder).Select(System.IO.File.GetLastWriteTimeUtc)
                .DefaultIfEmpty(Directory.GetLastWriteTimeUtc(folder)).Max();
            if (lastWrite < DateTime.UtcNow.AddDays(-2))
            {
                try { Directory.Delete(folder, recursive: true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
