using MedicalCollege.Infrastructure.Persistence;

namespace MedicalCollege.Infrastructure.Repositories;

public abstract class JsonRepositoryBase<T> where T : class
{
    protected readonly JsonFileStore Store;
    protected readonly string FileName;
    private readonly Func<T, string> _idSelector;

    protected JsonRepositoryBase(JsonFileStore store, string fileName, Func<T, string> idSelector)
    {
        Store = store;
        FileName = fileName;
        _idSelector = idSelector;
    }

    public virtual async Task<IReadOnlyList<T>> GetAllAsync()
        => await Store.ReadAsync<T>(FileName);

    public virtual async Task<T?> GetByIdAsync(string id)
    {
        var all = await GetAllAsync();
        return all.FirstOrDefault(x => _idSelector(x) == id);
    }

    public virtual async Task AddAsync(T entity)
    {
        var all = (await GetAllAsync()).ToList();
        all.Add(entity);
        await Store.WriteAsync(FileName, all);
    }

    public virtual async Task UpdateAsync(T entity)
    {
        var all = (await GetAllAsync()).ToList();
        var id = _idSelector(entity);
        var index = all.FindIndex(x => _idSelector(x) == id);
        if (index < 0) throw new InvalidOperationException($"{typeof(T).Name} '{id}' not found.");
        all[index] = entity;
        await Store.WriteAsync(FileName, all);
    }

    public virtual async Task DeleteAsync(string id)
    {
        var all = (await GetAllAsync()).Where(x => _idSelector(x) != id).ToList();
        await Store.WriteAsync(FileName, all);
    }

    public virtual Task SaveAllAsync(IEnumerable<T> entities)
        => Store.WriteAsync(FileName, entities);
}
