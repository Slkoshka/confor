using CommandLine;
using Pastel;
using System.Net;
using System.Net.Sockets;

Console.OutputEncoding = System.Text.Encoding.UTF8;

await Parser.Default.ParseArguments<Options>(args)
    .WithParsedAsync(async o =>
    {
        if (o.IsVerbose)
        {
            Log.IsVerbose = true;
        }

        if (o.BufferSize < 1024)
        {
            Log.Warning("Invalid buffer size!");
            return;
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

        var proxy = new Proxy(destAddress, o.BufferSize, TimeSpan.FromSeconds(o.Timeout));

        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                _ = proxy.StartAsync(await listener.AcceptTcpClientAsync(cancellation.Token), cancellation.Token);
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
    [Option('p', "listen-port", HelpText = "Port to listen to (use 0 to assign a random port).", Required = false)]
    public short ListenPort { get; set; } = 0;

    [Option('s', "listen-address", HelpText = "Listen address (use 0.0.0.0 to listen on all interfaces).", Required = false)]
    public string ListenAddress { get; set; } = "0.0.0.0";

    [Option('v', "verbose", HelpText = "Show more debug output.", Required = false)]
    public bool IsVerbose { get; set; } = false;

    [Option('t', "timeout", Required = false, HelpText = "Timeout (in seconds, use a non-positive number to disable timeout).")]
    public float Timeout { get; set; } = 600;

    [Option('b', "buffer-size", Required = false, HelpText = "Buffer size (in bytes, must be a positive number >= 1024).")]
    public int BufferSize { get; set; } = 65536;

    [Value(0, MetaName = "[address]", HelpText = "Destination address (e.g. 127.0.0.1:4321, [::1]:1234, or google.com:80).")]
    public string Address { get; set; } = "";
}
