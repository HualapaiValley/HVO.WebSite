using System.Linq.Expressions;

namespace HVO.DataModels.Repositories
{
    /// <summary>
    /// Generic repository interface for data access operations
    /// </summary>
    /// <typeparam name="T">Entity type</typeparam>
    public interface IRepository<T> where T : class
    {
        /// <summary>
        /// Gets all entities
        /// </summary>
        /// <returns>Queryable collection of entities</returns>
        IQueryable<T> GetAll();

        /// <summary>
        /// Gets entities that match the specified filter
        /// </summary>
        /// <param name="filter">Filter expression</param>
        /// <returns>Queryable collection of filtered entities</returns>
        IQueryable<T> GetWhere(Expression<Func<T, bool>> filter);

        /// <summary>
        /// Gets an entity by its ID
        /// </summary>
        /// <param name="id">Entity ID</param>
        /// <returns>Entity or null if not found</returns>
        Task<T?> GetByIdAsync(int id);

        /// <summary>
        /// Gets the first entity that matches the specified filter
        /// </summary>
        /// <param name="filter">Filter expression</param>
        /// <returns>Entity or null if not found</returns>
        Task<T?> GetFirstOrDefaultAsync(Expression<Func<T, bool>> filter);

        /// <summary>
        /// Adds a new entity
        /// </summary>
        /// <param name="entity">Entity to add</param>
        /// <returns>Added entity</returns>
        Task<T> AddAsync(T entity);

        /// <summary>
        /// Adds multiple entities
        /// </summary>
        /// <param name="entities">Entities to add</param>
        /// <returns>Async task</returns>
        Task AddRangeAsync(IEnumerable<T> entities);

        /// <summary>
        /// Updates an existing entity
        /// </summary>
        /// <param name="entity">Entity to update</param>
        /// <returns>Updated entity</returns>
        T Update(T entity);

        /// <summary>
        /// Updates multiple entities
        /// </summary>
        /// <param name="entities">Entities to update</param>
        void UpdateRange(IEnumerable<T> entities);

        /// <summary>
        /// Removes an entity
        /// </summary>
        /// <param name="entity">Entity to remove</param>
        void Remove(T entity);

        /// <summary>
        /// Removes multiple entities
        /// </summary>
        /// <param name="entities">Entities to remove</param>
        void RemoveRange(IEnumerable<T> entities);

        /// <summary>
        /// Checks if any entity matches the specified filter
        /// </summary>
        /// <param name="filter">Filter expression</param>
        /// <returns>True if any entity matches, false otherwise</returns>
        Task<bool> AnyAsync(Expression<Func<T, bool>> filter);

        /// <summary>
        /// Counts entities that match the specified filter
        /// </summary>
        /// <param name="filter">Filter expression</param>
        /// <returns>Number of matching entities</returns>
        Task<int> CountAsync(Expression<Func<T, bool>> filter);

        /// <summary>
        /// Saves changes to the database
        /// </summary>
        /// <returns>Number of entities written to the database</returns>
        Task<int> SaveChangesAsync();
    }
}
