using System.Text.Json;

namespace MedicalCollege.Infrastructure.Persistence;

public class JsonFileStore
{
    private readonly string _dataPath;
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public JsonFileStore(string dataPath)
    {
        _dataPath = dataPath;
        Directory.CreateDirectory(_dataPath);
    }

    public string GetFilePath(string fileName) => Path.Combine(_dataPath, fileName);

    public async Task<List<T>> ReadAsync<T>(string fileName)
    {
        await Gate.WaitAsync();
        try
        {
            var path = GetFilePath(fileName);
            if (!File.Exists(path))
            {
                await File.WriteAllTextAsync(path, "[]");
                return new List<T>();
            }

            await using var stream = File.OpenRead(path);
            var data = await JsonSerializer.DeserializeAsync<List<T>>(stream, Options);
            return data ?? new List<T>();
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task WriteAsync<T>(string fileName, IEnumerable<T> items)
    {
        await Gate.WaitAsync();
        try
        {
            var path = GetFilePath(fileName);
            var json = JsonSerializer.Serialize(items.ToList(), Options);
            var temp = path + ".tmp";
            await File.WriteAllTextAsync(temp, json);
            File.Copy(temp, path, true);
            File.Delete(temp);
        }
        finally
        {
            Gate.Release();
        }
    }
}
