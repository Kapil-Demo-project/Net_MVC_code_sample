using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;

namespace DreamHouseOMS.Core.Abstractions
{
    public interface IOmsGenericRepository<TEntity>
        where TEntity : IEntity
    {
        #region Getters
        IQueryable<TEntity> GetAll();
        IQueryable<TEntity> Get(Expression<Func<TEntity, bool>> predicate);

        IQueryable<TEntity> FromSqlInterpolated(FormattableString query);
        IQueryable<TEntity> FromSqlRaw(string query);

        TEntity GetById(int id);
        Task<TEntity> GetByIdAsync(int id);
        #endregion

        #region Utils
        bool Any(Expression<Func<TEntity, bool>> predicate = null);
        Task<bool> AnyAsync(Expression<Func<TEntity, bool>> predicate = null);

        int Count(Expression<Func<TEntity, bool>> predicate = null) => predicate == null ? GetAll().Count() : Get(predicate).Count();
        async Task<int> CountAsync(Expression<Func<TEntity, bool>> predicate = null) => predicate == null ? await GetAll().CountAsync() : await Get(predicate).CountAsync();

        bool Exists(int id) => Any(e => e.Id.Equals(id));
        async Task<bool> ExistsAsync(int id) => await AnyAsync(e => e.Id.Equals(id));
        #endregion

        #region Modifiers
        void Create(TEntity entity);
        Task CreateAsync(TEntity entity);

        void Update(TEntity entity);
        Task UpdateAsync(TEntity entity);

        void Delete(int id);
        Task DeleteAsync(int id);
        #endregion

        #region Transaction
        IDbContextTransaction BeginTransaction();
        Task<IDbContextTransaction> BeginTransactionAsync();
        #endregion
    }
}