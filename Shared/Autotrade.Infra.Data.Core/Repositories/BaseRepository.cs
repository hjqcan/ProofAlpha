using Autotrade.Domain.Abstractions.Repositories;
using Microsoft.EntityFrameworkCore;
using NetDevPack.Data;
using NetDevPack.Domain;

namespace Autotrade.Infra.Data.Core.Repositories;

public abstract class BaseRepository<TContext, T> : IBaseRepository<T>, IDisposable
    where TContext : DbContext, IUnitOfWork
    where T : Entity, IAggregateRoot
{
    protected BaseRepository(TContext context)
    {
        Db = context ?? throw new ArgumentNullException(nameof(context));
        DbSet = Db.Set<T>();
    }

    protected TContext Db { get; }

    protected DbSet<T> DbSet { get; }

    public IUnitOfWork UnitOfWork => Db;

    public virtual async Task<T?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await DbSet.AsNoTracking().FirstOrDefaultAsync(entity => entity.Id == id, cancellationToken);
    }

    public virtual async Task<IReadOnlyCollection<T>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var items = await DbSet.AsNoTracking().ToListAsync(cancellationToken);
        return items;
    }

    protected virtual IQueryable<T> GetQueryable()
    {
        return DbSet.AsNoTracking();
    }

    public virtual void Add(T model)
    {
        DbSet.Add(model);
    }

    public virtual void Update(T model)
    {
        DbSet.Update(model);
    }

    public virtual void Remove(T model)
    {
        DbSet.Remove(model);
    }

    public virtual async Task RemoveByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await DbSet.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (entity is not null)
        {
            DbSet.Remove(entity);
        }
    }

    public virtual void Dispose()
    {
        Db.Dispose();
    }
}
