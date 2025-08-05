// File: Nom.Api/Factories/DefaultRepository.cs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nom.Api.Core;

namespace Nom.Api.Factories
{
    /// <summary>
    /// Default concrete implementation of BaseRepository for use when no specific repository is registered
    /// </summary>
    /// <typeparam name="TEntity">The entity type</typeparam>
    /// <typeparam name="TId">The ID type</typeparam>
    public class DefaultRepository<TEntity, TId> : BaseRepository<TEntity, TId>
        where TEntity : class
        where TId : struct
    {
        private readonly IServiceProvider _serviceProvider;
        private DbContext? _localDbContext;

        public DefaultRepository(IServiceProvider serviceProvider, ILogger<DefaultRepository<TEntity, TId>> logger)
            : base(null!, logger) // We'll set the DbContext later
        {
            _serviceProvider = serviceProvider;
        }

        public override DbSet<TEntity> EntitySet
        {
            get
            {
                if (_localDbContext == null)
                {
                    _localDbContext = _serviceProvider.GetRequiredService<DbContext>();
                }
                return _localDbContext.Set<TEntity>();
            }
        }

        protected override TId GetEntityId(TEntity entity)
        {
            // Default implementation - try to get ID using reflection
            // This is a fallback implementation and should be overridden in specific repositories
            var idProperty = typeof(TEntity).GetProperty("Id");
            if (idProperty != null && idProperty.PropertyType == typeof(TId))
            {
                return (TId)idProperty.GetValue(entity)!;
            }
            
            throw new NotImplementedException($"GetEntityId not implemented for {typeof(TEntity).Name}. Override this method in a specific repository implementation.");
        }
    }
} 