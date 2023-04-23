using System.Diagnostics;
using System.Net.Sockets;

interface ISocketListener
{
    ValueTask OnDataReceived(SocketGlue sender, int bytes);
    ValueTask OnDataSent(SocketGlue sender, int bytes);
}

class SocketGlue
{
    public Socket Source { get; }
    public Socket Destination { get; }
    
    private readonly ISocketListener _listener;
    private readonly byte[] _buffer;
    private volatile TaskCompletionSource _bufferWritten = new();
    private volatile TaskCompletionSource _bufferRead = new();
    private volatile TaskCompletionSource _receiverStopped = new();
    private volatile int _bufferSenderPosition = 0;
    private volatile int _bufferReceiverPosition = 0;
    private volatile int _receiveGeneration = 0;
    private volatile int _sendGeneration = 0;
    private readonly object _lock = new();

    private SocketGlue(Socket src, Socket dest, int bufferSize, ISocketListener listener)
    {
        Source = src;
        Destination = dest;
        _listener = listener;
        _buffer = new byte[bufferSize];
    }

    public static async Task StartAsync(Socket src, Socket dest, int bufferSize, ISocketListener listener, CancellationToken cancellationToken)
    {
        var glue = new SocketGlue(src, dest, bufferSize, listener);
        using var cts = new CancellationTokenSource();
        using var combinedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, cancellationToken);

        var sender = glue.SenderAsync(combinedCancellation.Token);
        var receiver = glue.ReceiverAsync(combinedCancellation.Token);
        var tasks = new Task[] { sender, receiver };

        if (await Task.WhenAny(tasks) == sender)
        {
            cts.Cancel();
        }
        await Task.WhenAll(tasks);
    }

    private int GetFreeReceiveBufferBytes()
    {
        lock (_lock)
        {
            Debug.Assert(
                (_receiveGeneration == _sendGeneration && _bufferSenderPosition <= _bufferReceiverPosition) ||
                (_receiveGeneration - 1 == _sendGeneration && _bufferReceiverPosition <= _bufferSenderPosition));

            if (_receiveGeneration == _sendGeneration)
            {
                return _buffer.Length - _bufferReceiverPosition;
            }
            else
            {
                return _bufferSenderPosition - _bufferReceiverPosition;
            }
        }
    }

    private int GetAvailableToSendBytes()
    {
        lock (_lock)
        {
            Debug.Assert(
                (_receiveGeneration == _sendGeneration && _bufferSenderPosition <= _bufferReceiverPosition) ||
                (_receiveGeneration - 1 == _sendGeneration && _bufferReceiverPosition <= _bufferSenderPosition));

            if (_receiveGeneration == _sendGeneration)
            {
                return _bufferReceiverPosition - _bufferSenderPosition;
            }
            else
            {
                return _buffer.Length - _bufferSenderPosition;
            }
        }
    }

    private void AdvanceReceivePosition(int bytes)
    {
        lock (_lock)
        {
            Debug.Assert(
                (_bufferReceiverPosition + bytes <= _buffer.Length && _receiveGeneration == _sendGeneration) ||
                (_bufferReceiverPosition + bytes <= _bufferSenderPosition && _receiveGeneration -1 == _sendGeneration));

            _bufferReceiverPosition += bytes;
            if (_bufferReceiverPosition == _buffer.Length)
            {
                _bufferReceiverPosition = 0;
                _receiveGeneration++;
            }
        }

        _bufferWritten.TrySetResult();
    }

    private void AdvanceSendPosition(int bytes)
    {
        lock (_lock)
        {
            Debug.Assert(
                (_bufferSenderPosition + bytes <= _bufferReceiverPosition && _receiveGeneration == _sendGeneration) ||
                (_bufferSenderPosition + bytes <= _buffer.Length && _receiveGeneration -1 == _sendGeneration));

            _bufferSenderPosition += bytes;
            if (_bufferSenderPosition == _buffer.Length)
            {
                _bufferSenderPosition = 0;
                _sendGeneration++;
            }
        }

        _bufferRead.TrySetResult();
    }

    private async Task SenderAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            _bufferWritten = new();
            if (GetAvailableToSendBytes() == 0)
            {
                var cancelTask = Task.Delay(-1, cancellationToken);
                if (await Task.WhenAny(_bufferWritten.Task, _receiverStopped.Task, cancelTask) == cancelTask)
                {
                    return;
                }
            }
            var availableBytes = GetAvailableToSendBytes();
            if (_receiverStopped.Task.IsCompleted && availableBytes == 0)
            {
                return;
            }
            Debug.Assert(availableBytes > 0);

            var sent = await Destination.SendAsync(_buffer.AsMemory(_bufferSenderPosition, availableBytes), cancellationToken);
            if (sent == 0)
            {
                break;
            }
            else
            {
                await _listener.OnDataSent(this, sent);
            }
            AdvanceSendPosition(sent);
        }
    }

    private async Task ReceiverAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                _bufferRead = new();
                if (GetFreeReceiveBufferBytes() == 0)
                {
                    if (await Task.WhenAny(_bufferRead.Task, Task.Delay(-1, cancellationToken)) != _bufferRead.Task)
                    {
                        return;
                    }
                }
                var freeBytes = GetFreeReceiveBufferBytes();
                Debug.Assert(freeBytes > 0);

                var read = 0;
                if (Source.Available == 0)
                {
                    read = await Source.ReceiveAsync(_buffer.AsMemory(_bufferReceiverPosition, 1), cancellationToken);
                }

                if (Source.Available > 0)
                {
                    freeBytes = GetFreeReceiveBufferBytes();

                    if (Source.Available > 0 && freeBytes > read)
                    {
                        read += await Source.ReceiveAsync(_buffer.AsMemory(_bufferReceiverPosition + read, Math.Min(Source.Available, freeBytes - read)), cancellationToken);
                    }
                }
                
                if (read == 0)
                {
                    return;
                }
                else
                {
                    await _listener.OnDataReceived(this, read);
                }
                AdvanceReceivePosition(read);
            }
        }
        finally
        {
            _receiverStopped.SetResult();
        }
    }
}
