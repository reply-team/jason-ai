"""Serve a folder on 127.0.0.1 for the installation proofs, and say what was done when.

    python3 .github/scripts/serve-feed.py <folder> <port> <log file>      (python on Windows)

Serves <folder> over HTTP on 127.0.0.1:<port> until it is stopped. Every line goes to <log file>, which is appended to and
flushed line by line, so a server that never answered still leaves a log: a line when the socket is bound and a line when
it listens, each with the moment, then one line per request in the form http.server writes it, for example

    127.0.0.1 - - [18/Nov/2026 17:02:36] "GET /manifest.json HTTP/1.1" 200 -

It exits on its own, with a line saying why, only when the folder is not one, the port is not a number, or the socket
cannot be bound or listen.

Nothing runs between bind and listen. http.server's own server looks the bound address's name up there (socket.getfqdn),
which on a runner can take longer than a proof waits for the feed to answer; this one names itself by the address alone.
"""

import datetime
import functools
import http.server
import os
import socket
import sys


def main(arguments):
    if len(arguments) != 3:
        sys.stderr.write("usage: serve-feed.py <folder> <port> <log file>\n")
        return 2

    folder, port, log_path = arguments
    log = open(log_path, "a", encoding="utf-8", buffering=1)

    def say(line):
        log.write(line + "\n")
        log.flush()

    def said(what):
        moment = datetime.datetime.now(datetime.timezone.utc).isoformat(timespec="milliseconds").replace("+00:00", "Z")
        say(f"{moment} {what}")

    if not os.path.isdir(folder):
        said(f"exits: {folder} is not a folder")
        return 1
    try:
        port = int(port)
    except ValueError:
        said(f"exits: the port '{port}' is not a number")
        return 1

    address = "127.0.0.1"

    class Handler(http.server.SimpleHTTPRequestHandler):
        def log_message(self, format, *args):
            say("%s - - [%s] %s" % (self.address_string(), self.log_date_time_string(), format % args))

    class Server(http.server.ThreadingHTTPServer):
        daemon_threads = True
        # On Windows SO_REUSEADDR lets a second socket take a port another one holds; elsewhere it only lets a port in
        # TIME_WAIT from an earlier leg's server be bound again.
        allow_reuse_address = os.name != "nt"

        def server_bind(self):
            if self.allow_reuse_address:
                self.socket.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
            self.socket.bind(self.server_address)
            self.server_address = self.socket.getsockname()
            self.server_name = address
            self.server_port = self.server_address[1]
            said(f"bound {address}:{self.server_port}, serving {os.path.abspath(folder)}, process {os.getpid()}")

        def server_activate(self):
            self.socket.listen(self.request_queue_size)
            said(f"listening on {address}:{self.server_port}")

    try:
        server = Server((address, port), functools.partial(Handler, directory=folder))
    except OSError as error:
        said(f"exits: {address}:{port} could not be bound or listen: {error}")
        return 1

    try:
        server.serve_forever()
    except KeyboardInterrupt:
        said("stopped")
    finally:
        server.server_close()
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
