"""Loopback-only test transport: deterministic delay/loss, no production networking changes."""
import heapq
import random
import select
import socket
import threading
import time


class MatrixUdpProxy:
    def __init__(self, server_port, delay_ms=0, loss=0, seed=1):
        self.server = ("127.0.0.1", server_port)
        self.socket = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self.socket.bind(("127.0.0.1", 0))
        # Keep the proxy alive after the intentionally killed player's socket closes.
        if hasattr(socket, "SIO_UDP_CONNRESET"):
            self.socket.ioctl(socket.SIO_UDP_CONNRESET, False)
        self.port = self.socket.getsockname()[1]
        self.delay = delay_ms / 1000
        self.loss = loss
        self.uplink_hold_until = 0
        self.random = random.Random(seed)
        self.stop = threading.Event()
        self.stats = dict(received=0, dropped=0, forwarded=0, errors=0)
        self.thread = threading.Thread(target=self.run, daemon=True)
        self.thread.start()

    def run(self):
        client = None
        pending = []
        serial = 0
        while not self.stop.is_set():
            readable, _, _ = select.select([self.socket], [], [], .005)
            if readable:
                try:
                    data, source = self.socket.recvfrom(65535)
                    self.stats["received"] += 1
                    if source == self.server:
                        destination = client
                    else:
                        client = source
                        destination = self.server
                    if destination:
                        if self.random.random() < self.loss:
                            self.stats["dropped"] += 1
                        else:
                            serial += 1
                            due = time.monotonic() + self.delay
                            if destination == self.server:
                                due = max(due, self.uplink_hold_until)
                            heapq.heappush(pending, (due, serial, destination, data))
                except OSError:
                    self.stats["errors"] += 1
            while pending and pending[0][0] <= time.monotonic():
                _, _, destination, data = heapq.heappop(pending)
                try:
                    self.socket.sendto(data, destination)
                    self.stats["forwarded"] += 1
                except OSError:
                    self.stats["errors"] += 1

    def close(self):
        self.stop.set()
        self.thread.join(timeout=2)
        self.socket.close()
