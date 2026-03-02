using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using HVO.DataModels.Data;

namespace HVO.DataModels.Repositories
{
    /// <summary>
    /// Generic repository implementation for data access operations
    /// </summary>
    /// <typeparam name="T">Entity type</typeparam>
    public class Repository<T> : IRepository<T> where T : class
    {
        protected readonly HvoDbContext _context;
        protected readonly DbSet<T> _dbSet;

        /// <summary>
        /// Initializes a new instance of the Repository class
        /// </summary>
        /// <param name="context">Database context</param>
        public Repository(HvoDbContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _dbSet = _context.Set<T>();
        }

        /// <summary>
        /// Gets all entities
        /// </summary>
        /// <returns>Queryable collection of entities</returns>
        public virtual IQueryable<T> GetAll()
        {
            return _dbSet;
        }

        /// <summary>
        /// Gets entities that match the specified filter
        /// </summary>
        /// <param name="filter">Filter expression</param>
        /// <returns>Queryable collection of filtered entities</returns>
        public virtual IQueryable<T> GetWhere(Expression<Func<T, bool>> filter)
        {
            return _dbSet.Where(filter);
        }

        /// <summary>
        /// Gets an entity by its ID
        /// </summary>
        /// <param name="id">Entity ID</param>
        /// <returns>Entity or null if not found</returns>
        public virtual async Task<T?> GetByIdAsync(int id)
        {
            return await _dbSet.FindAsync(id);
        }

        /// <summary>
        /// Gets the first entity that matches the specified filter
        /// </summary>
        /// <param name="filter">Filter expression</param>
        /// <returns>Entity or null if not found</returns>
        public virtual async Task<T?> GetFirstOrDefaultAsync(Expression<Func<T, bool>> filter)
        {
            return await _dbSet.FirstOrDefaultAsync(filter);
        }

        /// <summary>
        /// Adds a new entity
        /// </summary>
        /// <param name="entity">Entity to add</param>
        /// <returns>Added entity</returns>
        public virtual async Task<T> AddAsync(T entity)
        {
            var result = await _dbSet.AddAsync(entity);
            return result.Entity;
        }

        /// <summary>
        /// Adds multiple entities
        /// </summary>
        /// <param name="entities">Entities to add</param>
        /// <returns>Async task</returns>
        public virtual async Task AddRangeAsync(IEnumerable<T> entities)
        {
            await _dbSet.AddRangeAsync(entities);
        }

        /// <summary>
        /// Updates an existing entity
        /// </summary>
        /// <param name="entity">Entity to update</param>
        /// <returns>Updated entity</returns>
        public virtual T Update(T entity)
        {
            var result = _dbSet.Update(entity);
            return result.Entity;
        }

        /// <summary>
        /// Updates multiple entities
        /// </summary>
        /// <param name="entities">Entities to update</param>
        public virtual void UpdateRange(IEnumerable<T> entities)
        {
            _dbSet.UpdateRange(entities);
        }

        /// <summary>
        /// Removes an entity
        /// </summary>
        /// <param name="entity">Entity to remove</param>
        public virtual void Remove(T entity)
        {
            _dbSet.Remove(entity);
        }

        /// <summary>
        /// Removes multiple entities
        /// </summary>
        /// <param name="entities">Entities to remove</param>
        public virtual void RemoveRange(IEnumerable<T> entities)
        {
            _dbSet.RemoveRange(entities);
        }

        /// <summary>
        /// Checks if any entity matches the specified filter
        /// </summary>
        /// <param name="filter">Filter expression</param>
        /// <returns>True if any entity matches, false otherwise</returns>
        public virtual async Task<bool> AnyAsync(Expression<Func<T, bool>> filter)
        {
            return await _dbSet.AnyAsync(filter);
        }

        /// <summary>
        /// Counts entities that match the specified filter
        /// </summary>
        /// <param name="filter">Filter expression</param>
        /// <returns>Number of matching entities</returns>
        public virtual async Task<int> CountAsync(Expression<Func<T, bool>> filter)
        {
            return await _dbSet.CountAsync(filter);
        }

        /// <summary>
        /// Saves changes to the database
        /// </summary>
        /// <returns>Number of entities written to the database</returns>
        public virtual async Task<int> SaveChangesAsync()
        {
            return await _context.SaveChangesAsync();
        }
    }
}
