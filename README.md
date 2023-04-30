# confor &mdash; CONnection FORwarder

Simple TCP connection forwarder.

## Why?

I am using this tool to investigate some connection issues on my side while streaming to Twitch. If you think you need it for some reason, there are probably better tools to do that, but feel free to use it anyway if you think it is exactly what you need.

## How?

```
$ docker run -it --rm slkoshka/confor --help
Copyright (c) Slkoshka

  -p, --listen-port       Port to listen to (use 0 to assign a random port).

  -s, --listen-address    Listen address (use 0.0.0.0 to listen on all interfaces).

  -v, --verbose           Show more debug output.

  -t, --timeout           Timeout (in seconds, use a non-positive number to disable timeout).

  -b, --buffer-size       Buffer size (in bytes, must be a positive number >= 1024).

  --help                  Display this help screen.

  --version               Display version information.

  [address] (pos. 0)      Destination address (e.g. 127.0.0.1:4321, [::1]:1234, or google.com:80).
```

### Examples

Redirect all incoming connections from the local port `80` on all network interfaces to `google.com:80`:
```
$ docker run -it --rm -p 80 slkoshka/confor -p 80 google.com:80
 ✨ Listening to 0.0.0.0:80
 ✨ Press Ctrl+C to exit
```

Redirect all incoming connections from a random local port on the loopback interface to `google.com:80` without using Docker:
```
$ dotnet run -c Release -- -s 127.0.0.1 google.com:80
 ✨ Listening to 127.0.0.1:37607
 ✨ Press Ctrl+C to exit
```

## Requirements

Either Docker or .NET SDK 7.0.
