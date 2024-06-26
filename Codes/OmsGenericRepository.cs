using DreamHouseOMS.Core.Abstractions;
using DreamHouseOMS.Core.Extensions;
using DreamHouseOMS.Data.Helpers;
using DreamHouseOMS.Data.Models.Contexts;
using DreamHouseOMS.Data.Models.Oms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;

namespace DreamHouseOMS.Data.Repositories.Oms
{
    public abstract class OmsGenericRepository<TEntity> : IOmsGenericRepository<TEntity>
        where TEntity : Entity, IEntity
    {
        protected internal OmsDbContext DbContext;
        protected internal ILogger Logger;

        protected OmsGenericRepository(OmsDbContext dbContext, ILogger logger)
        {
            DbContext = dbContext;
            Logger = logger;
        }

        #region Getters
        public virtual IQueryable<TEntity> GetAll() =>
            DbContext.Set<TEntity>().AsNoTracking();

        public virtual IQueryable<TEntity> Get(Expression<Func<TEntity, bool>> predicate)
            => GetAll().Where(predicate);

        public virtual IQueryable<TEntity> FromSqlInterpolated(FormattableString query) =>
            DbContext.Set<TEntity>().FromSqlInterpolated(query).AsNoTracking();

        public virtual IQueryable<TEntity> FromSqlRaw(string query) =>
            DbContext.Set<TEntity>().FromSqlRaw(query).AsNoTracking();

        public virtual TEntity GetById(int id)
            => GetAll().FirstOrDefault(x => x.Id.Equals(id));

        public virtual async Task<TEntity> GetByIdAsync(int id)
            => await GetAll().FirstOrDefaultAsyncSafe(x => x.Id.Equals(id));
        #endregion

        #region Utils
        public virtual bool Any(Expression<Func<TEntity, bool>> predicate = null)
            => predicate == null ? GetAll().Any() : Get(predicate).Any();

        public virtual async Task<bool> AnyAsync(Expression<Func<TEntity, bool>> predicate = null)
            => predicate == null ? await GetAll().AnyAsync() : await Get(predicate).AnyAsync();
        #endregion

        #region Modifiers
        public virtual void Create(TEntity entity)
        {
            SetNavigationPropertiesToNull(entity);
            DbContext.Set<TEntity>().Add(entity);
            DbContext.SaveChanges();
        }

        public virtual async Task CreateAsync(TEntity entity)
        {
            SetNavigationPropertiesToNull(entity);
            await DbContext.Set<TEntity>().AddAsync(entity);
            await DbContext.SaveChangesAsync();
        }

        public virtual void Update(TEntity entity)
        {
            SetNavigationPropertiesToNull(entity);
            if (DbContext.Entry(entity).State != EntityState.Modified) // ensure the overriden methods have not already marked as updated
            {
                DbContext.Set<TEntity>().Update(entity);
            }
            if (SupportsAudit)
            {
                DbContext.Entry(entity as IAudit).Property(x => x.Created).IsModified = false; // ignore changes to Created
                DbContext.Entry(entity as IAudit).Property(x => x.CreatedById).IsModified = false; // ignore changes to Created
            }
            try
            {
                DbContext.SaveChanges();
            }
            catch (DbUpdateConcurrencyException ex)
            {
                Logger.LogError($"Update Database Error: {ex.Message}\n{ex}");
            }
        }

        public virtual async Task UpdateAsync(TEntity entity)
        {
            SetNavigationPropertiesToNull(entity);
            if (DbContext.Entry(entity).State != EntityState.Modified) // ensure the overridden methods have not already marked as updated
            {
                DbContext.Set<TEntity>().Update(entity);
            }
            if (SupportsAudit)
            {
                DbContext.Entry(entity as IAudit).Property(x => x.Created).IsModified = false; // ignore changes to Created
                DbContext.Entry(entity as IAudit).Property(x => x.CreatedById).IsModified = false; // ignore changes to Created
            }
            try
            {
                await DbContext.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException ex)
            {
                Logger.LogError($"Update Database Error: {ex.Message}\n{ex}");
            }
        }

        public virtual void Delete(int id)
        {
            var entity = GetById(id);
            DbContext.Set<TEntity>().Remove(entity);
            DbContext.SaveChanges();
        }

        public virtual async Task DeleteAsync(int id)
        {
            var entity = await GetByIdAsync(id);
            DbContext.Set<TEntity>().Remove(entity);
            await DbContext.SaveChangesAsync();
        }
        #endregion

        #region Transaction
        public IDbContextTransaction BeginTransaction()
        {
            return DbContext.UseSqlServerForOms && DbContext.Database.CurrentTransaction == null
                ? DbContext.Database.BeginTransaction()
                : new NoOpDbTransaction();
        }

        public async Task<IDbContextTransaction> BeginTransactionAsync()
        {
            return DbContext.UseSqlServerForOms && DbContext.Database.CurrentTransaction == null
                ? await DbContext.Database.BeginTransactionAsync()
                : new NoOpDbTransaction();
        }
        #endregion

        #region Helper methods
        private static bool SupportsAudit => typeof(TEntity).GetInterfaces().Contains(typeof(IAudit));

        protected void SetNavigationPropertiesToNull(TEntity entity)
        {
            foreach (var np in DbContext.Model.FindEntityType(typeof(TEntity)).GetNavigations())
            {
                entity.GetType().GetProperty(np.Name).SetValue(entity, null);
            }
        }
        #endregion
    }
}