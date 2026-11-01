"""Sends an mDNS PTR query for _airplay._tcp.local and _raop._tcp.local and prints the
instance names that answer. Used to verify that the receiver is discoverable."""
import socket, struct, sys, time

def qname(name):
    return b"".join(bytes([len(p)]) + p.encode() for p in name.split(".")) + b"\0"

def read_name(buf, off):
    labels, jumped, end = [], False, off
    while True:
        n = buf[off]
        if n == 0:
            off += 1
            break
        if n & 0xC0 == 0xC0:
            if not jumped:
                end = off + 2
            off = ((n & 0x3F) << 8) | buf[off + 1]
            jumped = True
            continue
        labels.append(buf[off + 1: off + 1 + n].decode(errors="replace"))
        off += n + 1
    return ".".join(labels), (end if jumped else off)

services = ["_airplay._tcp.local", "_raop._tcp.local"]
pkt = struct.pack(">HHHHHH", 0, 0, len(services), 0, 0, 0)
for s in services:
    pkt += qname(s) + struct.pack(">HH", 12, 1)   # PTR, IN

sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM, socket.IPPROTO_UDP)
sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
sock.setsockopt(socket.IPPROTO_IP, socket.IP_MULTICAST_TTL, 255)
if len(sys.argv) > 2:   # optional: outgoing interface IPv4 (avoid VirtualBox/VPN adapters)
    sock.setsockopt(socket.IPPROTO_IP, socket.IP_MULTICAST_IF, socket.inet_aton(sys.argv[2]))
sock.bind(("", 0))
sock.settimeout(0.5)
found = set()
a_records = set()
deadline = time.time() + float(sys.argv[1] if len(sys.argv) > 1 else 4)
for _ in range(3):
    sock.sendto(pkt, ("224.0.0.251", 5353))
while time.time() < deadline:
    try:
        data, addr = sock.recvfrom(9000)
    except socket.timeout:
        continue
    _, flags, qd, an, ns, ar = struct.unpack(">HHHHHH", data[:12])
    off = 12
    for _ in range(qd):
        _, off = read_name(data, off); off += 4
    for _ in range(an + ns + ar):
        name, off = read_name(data, off)
        rtype, _, _, rdlen = struct.unpack(">HHIH", data[off:off + 10]); off += 10
        if rtype == 12 and name in services:
            target, _ = read_name(data, off)
            found.add((addr[0], target))
        elif rtype == 1 and rdlen == 4:
            a_records.add((addr[0], name, socket.inet_ntoa(data[off:off + 4])))
        off += rdlen
for ip, t in sorted(found):
    print(f"{ip}  {t}")
for ip, host, a in sorted(a_records):
    print(f"{ip}  A {host} -> {a}")
print(f"{len(found)} service(s) found")
