#!/usr/bin/env python3
"""Small development-only HTTPS-to-HTTP relay for the Google logo ARAM example.

Do not expose this unauthenticated HTTP endpoint to the public internet.
"""
from http.server import BaseHTTPRequestHandler, HTTPServer
from urllib.request import urlopen

GOOGLE_LOGO = "https://www.google.com/images/branding/googlelogo/2x/googlelogo_color_272x92dp.png"
MAX_BYTES = 256 * 1024

class Handler(BaseHTTPRequestHandler):
    def do_GET(self):
        if self.path != "/google-logo.png":
            self.send_error(404)
            return
        try:
            with urlopen(GOOGLE_LOGO, timeout=15) as upstream:
                data = upstream.read(MAX_BYTES + 1)
            if len(data) > MAX_BYTES:
                self.send_error(502, "Logo larger than sample budget")
                return
        except Exception as exc:
            self.send_error(502, str(exc))
            return
        self.send_response(200)
        self.send_header("Content-Type", "image/png")
        self.send_header("Content-Length", str(len(data)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(data)

if __name__ == "__main__":
    print("Serving Google logo at http://0.0.0.0:18081/google-logo.png")
    HTTPServer(("0.0.0.0", 18081), Handler).serve_forever()
