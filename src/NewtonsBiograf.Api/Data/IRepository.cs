using System.Linq.Expressions;

namespace NewtonsBiograf.Api.Data;

public interface IRepository<T> where T : class
{
    Task<T?> GetByIdAsync(int id, CancellationToken ct = default);

    Task<List<T>> ListAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken ct = default);

    /// <summary>Tracked query; callers apply AsNoTracking when they want it.</summary>
    IQueryable<T> Query();

    Task AddAsync(T entity, CancellationToken ct = default);

    void Update(T entity);

    void Remove(T entity);

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
