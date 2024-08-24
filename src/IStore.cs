using System.Threading.Tasks;
using System.Threading;
using System.Collections.Generic;

namespace Ipfs.Engine
{
    public interface IStore<TName, TValue> where TValue : class
    {
        /// <summary>
        ///   Try to get the value with the specified name.
        /// </summary>
        /// <param name="name">
        ///   The unique name of the entity.
        /// </param>
        /// <param name="cancel">
        ///   Is used to stop the task.  When cancelled, the <see cref="TaskCanceledException"/> is raised.
        /// </param>
        /// <returns>
        ///   A task that represents the asynchronous operation. The task's result is
        ///   a <typeparamref name="TValue"/> or <b>null</b> if the <paramref name="name"/>
        ///   does not exist.
        /// </returns>
        Task<TValue> TryGetAsync(TName name, CancellationToken cancel = default);

        /// <summary>
        ///   Get the value with the specified name.
        /// </summary>
        /// <param name="name">
        ///   The unique name of the entity.
        /// </param>
        /// <param name="cancel">
        ///   Is used to stop the task.  When cancelled, the <see cref="TaskCanceledException"/> is raised.
        /// </param>
        /// <returns>
        ///   A task that represents the asynchronous operation. The task's result is
        ///   a <typeparamref name="TValue"/>
        /// </returns>
        /// <exception cref="KeyNotFoundException">
        ///   When the <paramref name="name"/> does not exist.
        /// </exception>
        Task<TValue> GetAsync(TName name, CancellationToken cancel = default);

        /// <summary>
        ///   Put the value with the specified name.
        /// </summary>
        /// <param name="name">
        ///   The unique name of the entity.
        /// </param>
        /// <param name="value">
        ///   The entity.
        /// </param>
        /// <param name="cancel">
        ///   Is used to stop the task.  When cancelled, the <see cref="TaskCanceledException"/> is raised.
        /// </param>
        /// <returns>
        ///   A task that represents the asynchronous operation.
        /// </returns>
        /// <remarks>
        ///   If <paramref name="name"/> already exists, it's value is overwriten.
        ///   <para>
        ///   The file is deleted if an exception is encountered.
        ///   </para>
        /// </remarks>
        Task PutAsync(TName name, TValue value, CancellationToken cancel = default);

        /// <summary>
        ///   Remove the value with the specified name.
        /// </summary>
        /// <param name="name">
        ///   The unique name of the entity.
        /// </param>
        /// <param name="cancel">
        ///   Is used to stop the task.  When cancelled, the <see cref="TaskCanceledException"/> is raised.
        /// </param>
        /// <returns>
        ///   A task that represents the asynchronous operation.
        /// </returns>
        /// <remarks>
        ///   A non-existent <paramref name="name"/> does nothing.
        /// </remarks>
        Task RemoveAsync(TName name, CancellationToken cancel = default);

        /// <summary>
        ///   Gets the values in the file store.
        /// </summary>
        /// <value>
        ///   A sequence of <typeparamref name="TValue"/>.
        /// </value>
        IEnumerable<TValue> Values { get; }
    }
}