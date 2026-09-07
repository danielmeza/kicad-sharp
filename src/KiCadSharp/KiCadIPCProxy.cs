using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

using Kiapi.Common;
using Kiapi.Common.Commands;
using Kiapi.Common.Types;

namespace KiCadSharp
{
    /// <summary>
    /// Base class for all KiCad IPC proxy classes
    /// </summary>
    public abstract class KiCadIPCProxy
    {
        /// <summary>
        /// Creates a new KiCad IPC proxy with the given client
        /// </summary>
        /// <param name="client">KiCad IPC client for communication with KiCad</param>
        protected KiCadIPCProxy(KiCadIPCClient client)
        {
            Client = client;
        }

        /// <summary>
        /// The KiCad IPC client used for communication
        /// </summary>
        protected KiCadIPCClient Client { get; }
        
        /// <summary>
        /// Sends a command to KiCad and returns a result of the specified type
        /// </summary>
        /// <typeparam name="TResult">Type of result to return</typeparam>
        /// <param name="command">Command to send</param>
        /// <param name="cancellationToken">Cancels the round trip.</param>
        /// <returns>Result of the command</returns>
        /// <remarks>
        /// The token is passed through. It used to be dropped here, which meant
        /// <see cref="KiCadIPCClient.Send{TResult}"/> took a <see cref="CancellationToken"/> that
        /// nothing on <see cref="KiCad"/>, <see cref="Board"/> or <see cref="Project"/> could supply.
        /// </remarks>
        protected async ValueTask<TResult> Send<TResult>(IMessage command, CancellationToken cancellationToken = default)
            where TResult : IMessage, new()
        {
            return await Client.Send<TResult>(command, cancellationToken);
        }

        /// <summary>
        /// Sends a command to KiCad with no result
        /// </summary>
        /// <param name="command">Command to send</param>
        /// <param name="cancellationToken">Cancels the round trip.</param>
        protected async ValueTask Send(IMessage command, CancellationToken cancellationToken = default)
        {
            await Client.Send(command, cancellationToken);
        }
    }
}