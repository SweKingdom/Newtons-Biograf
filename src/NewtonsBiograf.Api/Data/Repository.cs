using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace NewtonsBiograf.Api.Data;

public class Repository<T>(AppDbContext db) : IRepository<T> where T : class
{
    public async Task<T?> GetByIdAsync(int id, CancellationToken ct = default) =>
        await db.Set<T>().FindAsync([id], ct);

    public Task<List<T>> ListAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken ct = default)
    {
        var query = db.Set<T>().AsQueryable();
        if (predicate is not null)
        {
            query = query.Where(predicate);
        }
        return query.ToListAsync(ct);
    }

    public IQueryable<T> Query() => db.Set<T>();

    public async Task AddAsync(T entity, CancellationToken ct = default) =>
        await db.Set<T>().AddAsync(entity, ct);

    public void Update(T entity) => db.Set<T>().Update(entity);

    public void Remove(T entity) => db.Set<T>().Remove(entity);

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
