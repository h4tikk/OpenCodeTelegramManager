namespace OpenCodeManager.Application;
public interface IManagerDbFactory
{
    Task<IManagerDb> CreateAsync(CancellationToken ct = default);
}
