#!/usr/bin/env python3
"""Non-deployable SolarAssistant read-interface discovery probe.

The probe prints sanitized metadata only. It does not publish MQTT messages,
write SolarAssistant settings, write website APIs, or create an outbox.
"""

from __future__ import annotations

import base64
import hashlib
import json
import os
import socket
import struct
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
from collections import Counter
from typing import Iterable


HOST = (os.environ.get("SOLARASSISTANT_HOST") or os.environ.get("SOLAR_ASSISTANT_IP") or "").strip()
USER = os.environ.get("SOLARASSISTANT_USER") or os.environ.get("SOLAR_ASSISTANT_REST_USERNAME", "admin")
PASSWORD = (
    os.environ.get("SOLARASSISTANT_PASSWORD")
    or os.environ.get("SOLAR_ASSISTANT_PASSWORD")
    or os.environ.get("SOLAR_ASSISTANT_REST_PASSWORD", "")
)
TOKEN = os.environ.get("SOLARASSISTANT_TOKEN") or os.environ.get("SOLAR_ASSISTANT_TOKEN", "")
MQTT_USER = (
    os.environ.get("SOLARASSISTANT_MQTT_USER")
    or os.environ.get("SOLAR_ASSISTANT_MQTT_USER")
    or os.environ.get("SOLAR_ASSISTANT_MQTT_USERNAME", "")
)
MQTT_PASSWORD = os.environ.get("SOLARASSISTANT_MQTT_PASSWORD") or os.environ.get("SOLAR_ASSISTANT_MQTT_PASSWORD", "")
MQTT_TOPIC = os.environ.get("SOLARASSISTANT_MQTT_TOPIC", "#")
WEBSOCKET_TOPICS = [
    item.strip()
    for item in os.environ.get("SOLARASSISTANT_WEBSOCKET_TOPICS", "total/*,inverter_1/*,battery_1/*").split(",")
    if item.strip()
]


def main() -> int:
    if not HOST:
        print("SOLARASSISTANT_HOST is required", file=sys.stderr)
        return 2

    print(f"host={HOST}")
    probe_ports(HOST)
    probe_rest(HOST)
    probe_mqtt(HOST)
    probe_websocket(HOST)
    return 0


def probe_ports(host: str) -> None:
    print("\n[ports]")
    for port in (80, 443, 1883):
        with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as sock:
            sock.settimeout(3)
            rc = sock.connect_ex((host, port))
        print(f"{port}: {'open' if rc == 0 else 'closed-or-filtered'}")


def probe_rest(host: str) -> None:
    print("\n[rest]")
    url = f"http://{host}/api/v1/metrics"
    headers: dict[str, str] = {}
    if TOKEN:
        headers["Authorization"] = f"Bearer {TOKEN}"
    elif PASSWORD:
        raw = f"{USER}:{PASSWORD}".encode("utf-8")
        headers["Authorization"] = "Basic " + base64.b64encode(raw).decode("ascii")

    req = urllib.request.Request(url, headers=headers)
    try:
        with urllib.request.urlopen(req, timeout=10) as resp:
            body = resp.read()
        data = json.loads(body.decode("utf-8"))
    except urllib.error.HTTPError as ex:
        print(f"status={ex.code} reason={ex.reason}")
        print("rest_metrics=unavailable")
        return
    except Exception as ex:  # noqa: BLE001 - diagnostic probe
        print(f"error={type(ex).__name__}: {ex}")
        print("rest_metrics=unavailable")
        return

    if not isinstance(data, list):
        print(f"unexpected_shape={type(data).__name__}")
        return

    topics = [str(item.get("topic")) for item in data if isinstance(item, dict) and item.get("topic")]
    groups = sorted({str(item.get("group")) for item in data if isinstance(item, dict) and item.get("group")})
    units = sorted({str(item.get("unit")) for item in data if isinstance(item, dict) and item.get("unit")})

    print(f"metric_count={len(data)}")
    print(f"topic_count={len(topics)}")
    print("topic_prefixes=" + format_counter(topic_prefixes(topics), 20))
    print("groups=" + ", ".join(groups[:30]))
    print("units=" + ", ".join(units[:30]))
    print("sample_topics=" + ", ".join(topics[:40]))


def probe_mqtt(host: str) -> None:
    print("\n[mqtt]")
    try:
        with socket.create_connection((host, 1883), timeout=5) as sock:
            sock.settimeout(10)
            connect_mqtt(sock)
            subscribe_mqtt(sock, MQTT_TOPIC)
            topics = collect_mqtt_topics(sock, seconds=10, max_packets=250)
    except MqttAuthError as ex:
        print(str(ex))
        print("mqtt_topics=unavailable")
        return
    except Exception as ex:  # noqa: BLE001 - diagnostic probe
        print(f"error={type(ex).__name__}: {ex}")
        print("mqtt_topics=unavailable")
        return

    print(f"publish_packets={sum(topics.values())}")
    print(f"unique_topics={len(topics)}")
    print("topic_prefixes=" + format_counter(topic_prefixes(topics.keys()), 20))
    for topic, count in sorted(topics.items())[:60]:
        print(f"topic={topic} count={count}")


def probe_websocket(host: str) -> None:
    print("\n[websocket]")
    if not PASSWORD:
        print("skipped=SOLARASSISTANT_PASSWORD is required for WebSocket probing")
        return

    try:
        with socket.create_connection((host, 80), timeout=5) as sock:
            sock.settimeout(10)
            websocket_handshake(sock, host)
            join_metrics_channel(sock)
            events, definitions, data_topics = collect_websocket_metrics(sock, seconds=10, max_messages=250)
    except Exception as ex:  # noqa: BLE001 - diagnostic probe
        print(f"error={type(ex).__name__}: {ex}")
        print("websocket_metrics=unavailable")
        return

    print("events=" + format_counter(events, 20))
    print(f"definition_topics={len(definitions)}")
    print(f"data_topics={len(data_topics)}")
    print("definition_prefixes=" + format_counter(topic_prefixes(definitions), 20))
    print("data_prefixes=" + format_counter(topic_prefixes(data_topics), 20))
    print("sample_definition_topics=" + ", ".join(sorted(definitions)[:40]))
    print("sample_data_topics=" + ", ".join(sorted(data_topics)[:40]))


def websocket_handshake(sock: socket.socket, host: str) -> None:
    key = base64.b64encode(os.urandom(16)).decode("ascii")
    query = urllib.parse.urlencode({"password": PASSWORD, "vsn": "2.0.0"})
    request = (
        f"GET /api/websocket?{query} HTTP/1.1\r\n"
        f"Host: {host}\r\n"
        "Upgrade: websocket\r\n"
        "Connection: Upgrade\r\n"
        f"Sec-WebSocket-Key: {key}\r\n"
        "Sec-WebSocket-Version: 13\r\n"
        "\r\n"
    )
    sock.sendall(request.encode("ascii"))
    response = read_until(sock, b"\r\n\r\n", limit=8192).decode("iso-8859-1", "replace")
    status_line = response.split("\r\n", 1)[0]
    if " 101 " not in status_line:
        raise WebSocketError(f"handshake_failed status='{status_line}'")

    accept = None
    for line in response.split("\r\n")[1:]:
        if line.lower().startswith("sec-websocket-accept:"):
            accept = line.split(":", 1)[1].strip()
            break
    expected = base64.b64encode(hashlib.sha1((key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11").encode("ascii")).digest()).decode("ascii")
    if accept != expected:
        raise WebSocketError("handshake_failed invalid_accept")
    print("handshake=ok")


def join_metrics_channel(sock: socket.socket) -> None:
    topics = [{"topic": topic} for topic in WEBSOCKET_TOPICS]
    message = ["1", "1", "metrics", "phx_join", {"topics": topics}]
    send_websocket_text(sock, json.dumps(message, separators=(",", ":")))
    print("subscribed_topics=" + ", ".join(WEBSOCKET_TOPICS))


def collect_websocket_metrics(sock: socket.socket, seconds: int, max_messages: int) -> tuple[Counter[str], set[str], set[str]]:
    events: Counter[str] = Counter()
    definitions: set[str] = set()
    data_topics: set[str] = set()
    start = time.monotonic()
    messages = 0
    while time.monotonic() - start < seconds and messages < max_messages:
        try:
            opcode, payload = read_websocket_frame(sock)
        except socket.timeout:
            break
        if opcode == 0x8:
            break
        if opcode == 0x9:
            send_websocket_frame(sock, 0xA, payload)
            continue
        if opcode != 0x1:
            continue
        messages += 1
        try:
            message = json.loads(payload.decode("utf-8"))
        except json.JSONDecodeError:
            events["unparseable"] += 1
            continue
        event, payload_obj = parse_phoenix_message(message)
        events[event] += 1
        if not isinstance(payload_obj, dict):
            continue
        for item in payload_obj.get("definitions") or []:
            if isinstance(item, dict) and item.get("topic"):
                definitions.add(str(item["topic"]))
        for item in payload_obj.get("metrics") or []:
            if isinstance(item, dict) and item.get("topic"):
                data_topics.add(str(item["topic"]))
    return events, definitions, data_topics


def parse_phoenix_message(message: object) -> tuple[str, object]:
    if isinstance(message, dict):
        return str(message.get("event", "unknown")), message.get("payload")
    if isinstance(message, list) and len(message) >= 5:
        return str(message[3]), message[4]
    return "unknown_shape", None


def read_websocket_frame(sock: socket.socket) -> tuple[int, bytes]:
    header = read_exact(sock, 2)
    first, second = header[0], header[1]
    opcode = first & 0x0F
    masked = bool(second & 0x80)
    length = second & 0x7F
    if length == 126:
        length = struct.unpack("!H", read_exact(sock, 2))[0]
    elif length == 127:
        length = struct.unpack("!Q", read_exact(sock, 8))[0]
    mask = read_exact(sock, 4) if masked else b""
    payload = bytearray(read_exact(sock, length))
    if masked:
        for index, value in enumerate(payload):
            payload[index] = value ^ mask[index % 4]
    return opcode, bytes(payload)


def send_websocket_text(sock: socket.socket, text: str) -> None:
    send_websocket_frame(sock, 0x1, text.encode("utf-8"))


def send_websocket_frame(sock: socket.socket, opcode: int, payload: bytes) -> None:
    mask = os.urandom(4)
    header = bytearray([0x80 | opcode])
    length = len(payload)
    if length < 126:
        header.append(0x80 | length)
    elif length <= 0xFFFF:
        header.append(0x80 | 126)
        header.extend(struct.pack("!H", length))
    else:
        header.append(0x80 | 127)
        header.extend(struct.pack("!Q", length))
    masked = bytes(value ^ mask[index % 4] for index, value in enumerate(payload))
    sock.sendall(bytes(header) + mask + masked)


def read_until(sock: socket.socket, marker: bytes, limit: int) -> bytes:
    data = bytearray()
    while marker not in data:
        chunk = sock.recv(1)
        if not chunk:
            break
        data.extend(chunk)
        if len(data) > limit:
            raise WebSocketError("response_too_large")
    return bytes(data)


def read_exact(sock: socket.socket, length: int) -> bytes:
    data = bytearray()
    while len(data) < length:
        chunk = sock.recv(length - len(data))
        if not chunk:
            raise WebSocketError("unexpected_eof")
        data.extend(chunk)
    return bytes(data)


def connect_mqtt(sock: socket.socket) -> None:
    variable_header = mqtt_string("MQTT") + bytes([4, 2]) + struct.pack("!H", 10)
    payload = mqtt_string(f"hvo-discovery-{os.getpid()}")

    if MQTT_USER:
        connect_flags = 0x80 | 0x02
        payload += mqtt_string(MQTT_USER)
        if MQTT_PASSWORD:
            connect_flags |= 0x40
            payload += mqtt_string(MQTT_PASSWORD)
        variable_header = mqtt_string("MQTT") + bytes([4, connect_flags]) + struct.pack("!H", 10)

    sock.sendall(bytes([0x10]) + encode_remaining_length(len(variable_header) + len(payload)) + variable_header + payload)
    packet_type, payload = read_mqtt_packet(sock)
    if packet_type != 0x20 or len(payload) < 2:
        raise MqttAuthError(f"connack_unexpected packet={packet_type} bytes={len(payload)}")
    if payload[1] != 0:
        raise MqttAuthError(f"connack_return_code={payload[1]}")
    print(f"connack_session_present={payload[0]} return_code={payload[1]}")


def subscribe_mqtt(sock: socket.socket, topic: str) -> None:
    payload = struct.pack("!H", 1) + mqtt_string(topic) + bytes([0])
    sock.sendall(bytes([0x82]) + encode_remaining_length(len(payload)) + payload)
    packet_type, payload = read_mqtt_packet(sock)
    if packet_type != 0x90:
        raise MqttAuthError(f"suback_unexpected packet={packet_type}")
    print(f"subscribed_topic={topic}")


def collect_mqtt_topics(sock: socket.socket, seconds: int, max_packets: int) -> Counter[str]:
    topics: Counter[str] = Counter()
    start = time.monotonic()
    while time.monotonic() - start < seconds and sum(topics.values()) < max_packets:
        try:
            packet_type, payload = read_mqtt_packet(sock)
        except socket.timeout:
            break
        if packet_type is None:
            break
        if packet_type >> 4 != 3 or len(payload) < 2:
            continue
        topic_len = struct.unpack("!H", payload[:2])[0]
        topic = payload[2 : 2 + topic_len].decode("utf-8", "replace")
        topics[topic] += 1
    return topics


def read_mqtt_packet(sock: socket.socket) -> tuple[int | None, bytes]:
    first = sock.recv(1)
    if not first:
        return None, b""
    multiplier = 1
    remaining = 0
    while True:
        b = sock.recv(1)[0]
        remaining += (b & 127) * multiplier
        if not (b & 128):
            break
        multiplier *= 128

    payload = bytearray()
    while len(payload) < remaining:
        chunk = sock.recv(remaining - len(payload))
        if not chunk:
            break
        payload.extend(chunk)
    return first[0], bytes(payload)


def mqtt_string(value: str) -> bytes:
    data = value.encode("utf-8")
    return struct.pack("!H", len(data)) + data


def encode_remaining_length(value: int) -> bytes:
    output = bytearray()
    while True:
        encoded = value % 128
        value //= 128
        if value:
            encoded |= 128
        output.append(encoded)
        if not value:
            return bytes(output)


def topic_prefixes(topics: Iterable[str]) -> Counter[str]:
    counts: Counter[str] = Counter()
    for topic in topics:
        prefix = topic.split("/", 1)[0]
        counts[prefix] += 1
    return counts


def format_counter(counter: Counter[str], limit: int) -> str:
    return ", ".join(f"{key}:{value}" for key, value in counter.most_common(limit))


class MqttAuthError(Exception):
    pass


class WebSocketError(Exception):
    pass


if __name__ == "__main__":
    raise SystemExit(main())
