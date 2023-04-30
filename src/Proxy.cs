using System.Net;
using System.Net.Sockets;

class Proxy
{
    class EventSocketListener: ISocketListener
    {
        public event EventHandler<int>? DataReceived;
        public event EventHandler<int>? DataSent;

        public ValueTask OnDataReceived(SocketGlue sender, int bytes)
        {
            DataReceived?.Invoke(sender, bytes);
            return ValueTask.CompletedTask;
        }

        public ValueTask OnDataSent(SocketGlue sender, int bytes)
        {
            DataSent?.Invoke(sender, bytes);
            return ValueTask.CompletedTask;
        }
    }

    private IPEndPoint _destAddress;
    private int _bufferSize;
    private TimeSpan _timeout;
    private int _prevConnectionId = 0;

    public Proxy(IPEndPoint destAddress, int bufferSize, TimeSpan timeout)
    {
        _destAddress = destAddress;
        _bufferSize = bufferSize;
        _timeout = timeout;
    }

    // Required for Native AOT to work
    [System.Diagnostics.CodeAnalysis.DynamicDependency(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All, typeof(Options))]
    public async Task StartAsync(TcpClient tcp, CancellationToken cancellationToken)
    {
        var connectionId = Interlocked.Increment(ref _prevConnectionId);
        try
        {
            Log.Info($"[#{connectionId}] Accepted connection from {tcp.Client.RemoteEndPoint}");

            using var dest = new TcpClient();
            dest.ReceiveBufferSize = tcp.ReceiveBufferSize = 8192;
            dest.SendBufferSize = tcp.SendBufferSize = 8192;
            tcp.Client.Blocking = dest.Client.Blocking = true;

            Log.Debug($"[#{connectionId}] Connecting to {_destAddress}...");
            await dest.ConnectAsync(_destAddress, cancellationToken);
            Log.Debug($"[#{connectionId}] Connected!");

            try
            {
                using var cts = _timeout > TimeSpan.Zero ? new CancellationTokenSource(_timeout) : new CancellationTokenSource();
                using var combinedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, cancellationToken);

                var listener = new EventSocketListener();
                listener.DataReceived += (sender, bytes) =>
                {
                    if (_timeout > TimeSpan.Zero)
                    {
                        cts.CancelAfter(_timeout);
                    }
                    var fromClient = (sender as SocketGlue)!.Source == tcp.Client;
                    if (fromClient)
                    {
                        Log.Debug($"[#{connectionId}] Client ***> Proxy ---> Server: {bytes} byte(s)");
                    }
                    else
                    {
                        Log.Debug($"[#{connectionId}] Client <--- Proxy <*** Server: {bytes} byte(s)");
                    }
                };
                listener.DataSent += (sender, bytes) =>
                {
                    var toClient = (sender as SocketGlue)!.Destination == tcp.Client;
                    if (toClient)
                    {
                        Log.Debug($"[#{connectionId}] Client <*** Proxy <--- Server: {bytes} byte(s)");
                    }
                    else
                    {
                        Log.Debug($"[#{connectionId}] Client ---> Proxy ***> Server: {bytes} byte(s)");
                    }
                };

                var tasks = new Task[] {
                    SocketGlue.StartAsync(tcp.Client, dest.Client, _bufferSize, listener, combinedCancellation.Token),
                    SocketGlue.StartAsync(dest.Client, tcp.Client, _bufferSize, listener, combinedCancellation.Token) };

                await Task.WhenAny(tasks);
                cts.Cancel();
                await Task.WhenAll(tasks);
            }
            finally
            {
                dest.Close();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Warning($"[#{connectionId}] Error in connection handler:\n{ex}");
        }
        finally
        {
            tcp.Close();
            tcp.Dispose();
            Log.Info($"[#{connectionId}] Closed connection");
        }
    }
}
