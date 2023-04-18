﻿using CommandLine;
using Pastel;
using System.Net;
using System.Net.Sockets;
using System.Text;

Console.OutputEncoding = Encoding.UTF8;

// Required for Native AOT to work
[System.Diagnostics.CodeAnalysis.DynamicDependency(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All, typeof(Options))]
static async Task ConnectionHandler(int connectionId, TcpClient tcp, IPEndPoint destAddress, int bufferSize, TimeSpan timeout, CancellationToken cancellationToken)
{
    try
    {
        Log.Info($"[#{connectionId}] Accepted connection from {tcp.Client.RemoteEndPoint}");

        using var dest = new TcpClient();
        dest.ReceiveBufferSize = tcp.ReceiveBufferSize = bufferSize;
        dest.SendBufferSize = tcp.SendBufferSize = bufferSize;

        Log.Debug($"[#{connectionId}] Connecting to {destAddress}...");
        await dest.ConnectAsync(destAddress, cancellationToken);
        Log.Debug($"[#{connectionId}] Connected!");

        try
        {
            static async Task Sender(string logPrefix, int bufferSize, TimeSpan timeout, Socket src, Socket dest, CancellationToken cancellationToken)
            {
                var buffer = new byte[bufferSize];

                try
                {
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        using var timeoutCts = new CancellationTokenSource(timeout);
                        using var combinedCancellation = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);

                        var read = await src.ReceiveAsync(buffer.AsMemory(0, 1), combinedCancellation.Token);
                        if (read > 0 && src.Available > 0)
                        {
                            read += await src.ReceiveAsync(buffer.AsMemory(1, Math.Min(bufferSize - 1, src.Available)), combinedCancellation.Token);
                        }

                        if (read == 0)
                        {
                            break;
                        }

                        Log.Debug($"{logPrefix}Received {read} bytes");
                        if (read > 0)
                        {
                            var totalSent = 0;
                            var sendFailed = false;
                            while (totalSent < read)
                            {
                                timeoutCts.CancelAfter(timeout);
                                var sent = await dest.SendAsync(buffer.AsMemory(0, read), combinedCancellation.Token);
                                Log.Debug($"{logPrefix}Sent {sent} bytes");

                                if (sent == 0)
                                {
                                    sendFailed = true;
                                    break;
                                }

                                totalSent += sent;
                            }
                            if (sendFailed)
                            {
                                break;
                            }
                        }
                    }
                }
                catch
                {
                    src.Close();
                    throw;
                }
            }

            await Task.WhenAll(
                Sender($"[#{connectionId} S->D] ", bufferSize, timeout, tcp.Client, dest.Client, cancellationToken),
                Sender($"[#{connectionId} D->S] ", bufferSize, timeout, dest.Client, tcp.Client, cancellationToken));
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

await Parser.Default.ParseArguments<Options>(args)
    .WithParsedAsync(async o =>
    {
        if (o.IsVerbose)
        {
            Log.IsVerbose = true;
        }

        if (!IPAddress.TryParse(o.ListenAddress, out var listenAddr))
        {
            Log.Warning("Invalid listen address!");
            return;
        }
        if (!IPEndPoint.TryParse(o.Address, out var destAddress))
        {
            var parts = o.Address.Split(':', 2);
            if (o.Address.Contains('[') || o.Address.Contains(']') || parts.Length != 2 || !short.TryParse(parts[1], out var port))
            {
                Log.Warning("Invalid destination address!");
                return;
            }
            Log.Debug("Destination address doesn't look like a valid IP address, let's try to resolve it...");
            var addresses = Dns.GetHostAddresses(parts[0]);
            if (addresses.Length == 0)
            {
                Log.Warning("Invalid destination address!");
                return;
            }
            Log.Debug($"Resolved to {addresses[0]}");
            destAddress = new IPEndPoint(addresses[0], port);
        }
        var listenEndpoint = new IPEndPoint(listenAddr, o.ListenPort);
        using var cancellation = new CancellationTokenSource();

        Log.Debug("Creating socket");
        var listener = new TcpListener(listenEndpoint);

        void OnCtrlC(object? sender, ConsoleCancelEventArgs e)
        {
            if (!cancellation.IsCancellationRequested)
            {
                cancellation!.Cancel();
                Log.Info("Shutting down...");
            }
            e.Cancel = true;
        }

        Log.Debug("Starting listening");
        listener.Start();
        Log.Info($"Listening to {listener.LocalEndpoint}");
        Log.Info("Press Ctrl+C to exit");
        Console.CancelKeyPress += OnCtrlC;

        try
        {
            var connectionId = 1;
            while (!cancellation.IsCancellationRequested)
            {
                _ = ConnectionHandler(connectionId++, await listener.AcceptTcpClientAsync(cancellation.Token), destAddress, o.BufferSize, TimeSpan.FromSeconds(o.Timeout), cancellation.Token);
            }
        }
        catch (OperationCanceledException) { }
        Console.CancelKeyPress -= OnCtrlC;
        Log.Debug("Stopping listening");
        listener.Stop();
        Log.Info("Bye!");
    });

class Options
{
    [Option('p', "listen-port", HelpText = "Port to listen to.", Required = false)]
    public short ListenPort { get; set; } = 0;

    [Option('s', "listen-address", HelpText = "Listen address.", Required = false)]
    public string ListenAddress { get; set; } = "0.0.0.0";

    [Option('v', "verbose", HelpText = "Show more debug output.", Required = false)]
    public bool IsVerbose { get; set; } = false;

    [Option('t', "timeout", Required = false, HelpText = "Timeout (in seconds).")]
    public float Timeout { get; set; } = 600;

    [Option('b', "buffer-size", Required = false, HelpText = "Buffer size.")]
    public int BufferSize { get; set; } = 65536;

    [Value(0, MetaName = "[address]", HelpText = "Destination address.")]
    public string Address { get; set; } = "";
}

class Log
{
    public static bool IsVerbose { get; set; } =
#if DEBUG
        true;
#else
        false;
#endif

    public static void Warning(string text) => Console.WriteLine($" ⚠️ {text}".Pastel(ConsoleColor.Yellow));
    public static void Debug(string text) { if (IsVerbose) { Console.WriteLine($" ℹ️ {text}".Pastel(ConsoleColor.Gray)); } }
    public static void Info(string text) => Console.WriteLine($" ✨ {text}".Pastel(ConsoleColor.White));
}
