# confor &mdash; CONnection FORwarder

Simple TCP connection forwarder.

## Usage

```bash
$ docker run -it --rm slkoshka/confor --help
confor 1.0.0
Copyright (c) Slkoshka

  -p, --listen-port       Port to listen to.

  -s, --listen-address    Listen address.

  -v, --verbose           Show more debug output.

  -t, --timeout           Timeout (in seconds).

  -b, --buffer-size       Buffer size.

  --help                  Display this help screen.

  --version               Display version information.

  [address] (pos. 0)      Destination address.
```

### Examples

Redirect all incoming connections from the local port `80` on all network interfaces to `google.com:80`:
```bash
$ docker run -it --rm -p 80 slkoshka/confor -p 80 google.com:80
 ✨ Listening to 0.0.0.0:80
 ✨ Press Ctrl+C to exit
```

Redirect all incoming connections from a random local port on the loopback interface to `google.com:80` without using Docker:
```bash
$ dotnet run -c Release -- -s 127.0.0.1 google.com:80
 ✨ Listening to 127.0.0.1:37607
 ✨ Press Ctrl+C to exit
```
