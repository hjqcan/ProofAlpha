using NetDevPack.Data;
using NetDevPack.Domain;

namespace Autotrade.Domain.Abstractions.Repositories;

public interface IBaseRepository<T> : IRepository<T>
    where T : Entity, IAggregateRoot
{
    Task<T?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<T>> GetAllAsync(CancellationToken cancellationToken = default);

    void Add(T model);

    void Update(T model);

    void Remove(T model);

    Task RemoveByIdAsync(Guid id, CancellationToken cancellationToken = default);
}
