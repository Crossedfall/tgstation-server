﻿using Microsoft.EntityFrameworkCore;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace Tgstation.Server.Host.Database
{
	/// <inheritdoc />
	sealed class DatabaseCollection<TModel> : IDatabaseCollection<TModel> where TModel : class
	{
		/// <summary>
		/// The backing <see cref="DbSet{TEntity}"/>.
		/// </summary>
		readonly DbSet<TModel> dbSet;

		/// <summary>
		/// Initializes a new instance of the <see cref="DatabaseCollection{TModel}"/> <see langword="class"/>.
		/// </summary>
		/// <param name="dbSet">The value of <see cref="dbSet"/>.</param>
		public DatabaseCollection(DbSet<TModel> dbSet)
		{
			this.dbSet = dbSet ?? throw new ArgumentNullException(nameof(dbSet));
		}

		/// <inheritdoc />
		public IEnumerable<TModel> Local => dbSet.Local;

		/// <inheritdoc />
		public Type ElementType => ((IQueryable<TModel>)dbSet).ElementType;

		/// <inheritdoc />
		public Expression Expression => ((IQueryable<TModel>)dbSet).Expression;

		/// <inheritdoc />
		public IQueryProvider Provider => ((IQueryable<TModel>)dbSet).Provider;

		/// <inheritdoc />
		public void Add(TModel model) => dbSet.Add(model);

		/// <inheritdoc />
		public void AddRange(IEnumerable<TModel> models) => dbSet.AddRange(models);

		/// <inheritdoc />
		public void Attach(TModel model) => dbSet.Attach(model);

		/// <inheritdoc />
		public IEnumerator<TModel> GetEnumerator() => ((IQueryable<TModel>)dbSet).GetEnumerator();

		/// <inheritdoc />
		public void Remove(TModel model) => dbSet.Remove(model);

		/// <inheritdoc />
		public void RemoveRange(IEnumerable<TModel> models) => dbSet.RemoveRange(models);

		/// <inheritdoc />
		IEnumerator IEnumerable.GetEnumerator() => ((IQueryable<TModel>)dbSet).GetEnumerator();
	}
}