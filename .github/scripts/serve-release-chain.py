#!/usr/bin/env python3
"""Serves releases the way the product's address and GitHub serve them, on this machine alone, for CI's Linux legs.

    serve-release-chain.py --root <folder> --cert <pem> --key <pem> --log <file> [--port 443]

<folder> holds one folder per release, v<version>/, with that release's files, and a file named latest whose one line
is the version that is the latest release. Three host names reach this server, which the leg maps to this machine in
/etc/hosts and whose certificate it trusts: the product's address, get.jasonharness.ai, and two stand-ins for GitHub's
hosts. Each request is answered as the real chain answers it, hop by hop:

    get.jasonharness.ai/apt/<file>                     302 to github.example.test/releases/latest/download/<file>
    get.jasonharness.ai/releases/<rest>                302 to github.example.test/releases/<rest>
    github.example.test/releases/latest/download/<f>   302 to github.example.test/releases/download/v<latest>/<f>
    github.example.test/releases/download/v<v>/<f>     302 to assets.example.test/<an opaque name for that file>
    assets.example.test/<opaque name>                  200 with the file

So apt and the install script read the address the package and the script name, and each file reaches them from one
release through redirects whose last hop names no file, as GitHub's asset addresses do. apt asks for <path>/./<file> on
a flat repository; the first hop resolves the dot, as the address's redirect does. A file the release does not hold is
404 at GitHub's hop. Connections are HTTP/1.1 and kept alive, which apt 3.2 relies on. Each request is one JSON line in
the log: the host, the path, the status, and the file it was served from.
"""

import argparse
import http.server
import json
import os
import posixpath
import ssl
import threading
import uuid
from urllib.parse import urlsplit

ADDRESS = "get.jasonharness.ai"
GITHUB = "github.example.test"
ASSETS = "assets.example.test"


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", required=True)
    parser.add_argument("--cert", required=True)
    parser.add_argument("--key", required=True)
    parser.add_argument("--log", required=True)
    parser.add_argument("--port", type=int, default=443)
    arguments = parser.parse_args()
    root = os.path.realpath(arguments.root)
    opaque = {}
    names = {}
    lock = threading.Lock()

    def asset_name(path):
        with lock:
            if path not in names:
                name = f"github-production-release-asset/{uuid.uuid4()}"
                names[path] = name
                opaque[name] = path
            return names[path]

    class Chain(http.server.BaseHTTPRequestHandler):
        protocol_version = "HTTP/1.1"

        def log_message(self, *args):
            pass

        def said(self, status, served=None):
            with lock, open(arguments.log, "a", encoding="utf-8") as log:
                log.write(json.dumps({"host": self.host(), "path": self.path, "status": status, "served": served}) + "\n")

        def host(self):
            return (self.headers.get("Host") or "").split(":")[0].lower()

        def answer(self, status, location=None, body=b"", served=None):
            self.said(status, served)
            self.send_response(status)
            if location is not None:
                self.send_header("Location", location)
            self.send_header("Content-Length", str(len(body)))
            if body:
                self.send_header("Content-Type", "application/octet-stream")
            self.end_headers()
            if self.command != "HEAD" and body:
                self.wfile.write(body)

        def latest(self):
            with open(os.path.join(root, "latest"), encoding="utf-8") as file:
                return file.read().strip()

        def do_HEAD(self):
            self.do_GET()

        def do_GET(self):
            path = posixpath.normpath(urlsplit(self.path).path)
            host = self.host()
            if host == ADDRESS and path.startswith("/apt/") and path.count("/") == 2:
                return self.answer(302, f"https://{GITHUB}/releases/latest/download/{path[len('/apt/'):]}")
            if host == ADDRESS and path.startswith("/releases/"):
                return self.answer(302, f"https://{GITHUB}{path}")
            if host == GITHUB and path.startswith("/releases/latest/download/") and path.count("/") == 4:
                return self.answer(302, f"https://{GITHUB}/releases/download/v{self.latest()}/{path.rsplit('/', 1)[1]}")
            if host == GITHUB and path.startswith("/releases/download/v") and path.count("/") == 4:
                _, _, _, tag, name = path.split("/")
                file = os.path.join(root, tag, name)
                if os.path.realpath(file).startswith(root + os.sep) and os.path.isfile(file):
                    return self.answer(302, f"https://{ASSETS}/{asset_name(file)}")
                return self.answer(404)
            if host == ASSETS and path[1:] in opaque:
                file = opaque[path[1:]]
                with open(file, "rb") as content:
                    return self.answer(200, body=content.read(), served=os.path.relpath(file, root))
            return self.answer(404)

    server = http.server.ThreadingHTTPServer(("127.0.0.1", arguments.port), Chain)
    context = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
    context.load_cert_chain(arguments.cert, arguments.key)
    server.socket = context.wrap_socket(server.socket, server_side=True)
    server.serve_forever()


if __name__ == "__main__":
    main()
