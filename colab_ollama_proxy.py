import os
import sys
import urllib.request
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

TARGET = os.environ["COLAB_OLLAMA_URL"].rstrip("/")
HOST = "127.0.0.1"
PORT = 11434

class ProxyHandler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def do_GET(self):
        self.forward()

    def do_POST(self):
        self.forward()

    def do_HEAD(self):
        self.forward()

    def forward(self):
        content_length = int(self.headers.get("Content-Length", "0") or "0")
        body = self.rfile.read(content_length) if content_length > 0 else None

        target_url = TARGET + self.path

        headers = {
            key: value
            for key, value in self.headers.items()
            if key.lower() not in {
                "host",
                "content-length",
                "connection",
                "accept-encoding",
            }
        }

        request = urllib.request.Request(
            target_url,
            data=body,
            headers=headers,
            method=self.command,
        )

        print(f"{self.command} {self.path} -> {target_url}", flush=True)

        try:
            with urllib.request.urlopen(request, timeout=600) as response:
                response_body = response.read()

                self.send_response(response.status)
                self.send_header(
                    "Content-Type",
                    response.headers.get("Content-Type", "application/json"),
                )
                self.send_header("Content-Length", str(len(response_body)))
                self.end_headers()

                if self.command != "HEAD":
                    self.wfile.write(response_body)

        except Exception as exc:
            error_body = str(exc).encode("utf-8")

            self.send_response(502)
            self.send_header("Content-Type", "text/plain; charset=utf-8")
            self.send_header("Content-Length", str(len(error_body)))
            self.end_headers()

            if self.command != "HEAD":
                self.wfile.write(error_body)

            print(f"ERROR {self.path}: {exc}", file=sys.stderr, flush=True)

    def log_message(self, format, *args):
        return

if __name__ == "__main__":
    print(f"Proxy listening on http://{HOST}:{PORT}")
    print(f"Forwarding to {TARGET}")
    server = ThreadingHTTPServer((HOST, PORT), ProxyHandler)
    server.serve_forever()
