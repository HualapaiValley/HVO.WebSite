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
import ssl
import socket
import struct
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
from collections import Counter
from dataclasses import dataclass
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
MQTT_TOPIC = os.environ.get("SOLARASSISTANT_MQTT_TOPIC", "solar_assistant/#")
MQTT_SECONDS = int(os.environ.get("SOLARASSISTANT_MQTT_SECONDS", "15"))
MQTT_MAX_PACKETS = int(os.environ.get("SOLARASSISTANT_MQTT_MAX_PACKETS", "1000"))
WEBSOCKET_SCHEME = os.environ.get("SOLARASSISTANT_WEBSOCKET_SCHEME", "ws").strip().lower()
_WEBSOCKET_PORT = os.environ.get("SOLARASSISTANT_WEBSOCKET_PORT")
WEBSOCKET_PORT = int(_WEBSOCKET_PORT or ("443" if WEBSOCKET_SCHEME == "wss" else "80"))
WEBSOCKET_TOPICS = [
    item.strip()
    for item in os.environ.get("SOLARASSISTANT_WEBSOCKET_TOPICS", "total/*,inverter_1/*,battery_1/*").split(",")
    if item.strip()
]


@dataclass(frozen=True)
class MqttMessage:
    topic: str
    payload: bytes
    retain: bool
    qos: int


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
    print("topic_groups=" + format_counter(topic_groups(topics), 30))
    print("groups=" + ", ".join(groups[:30]))
    print("units=" + ", ".join(units[:30]))
    print("sample_topics=" + ", ".join(topics[:40]))
    print_rest_classification(data)


def probe_mqtt(host: str) -> None:
    print("\n[mqtt]")
    try:
        with socket.create_connection((host, 1883), timeout=5) as sock:
            sock.settimeout(10)
            connect_mqtt(sock)
            subscribe_mqtt(sock, MQTT_TOPIC)
            messages = collect_mqtt_messages(sock, seconds=MQTT_SECONDS, max_packets=MQTT_MAX_PACKETS)
    except MqttAuthError as ex:
        print(str(ex))
        print("mqtt_topics=unavailable")
        return
    except Exception as ex:  # noqa: BLE001 - diagnostic probe
        print(f"error={type(ex).__name__}: {ex}")
        print("mqtt_topics=unavailable")
        return

    topics = Counter(message.topic for message in messages)
    print(f"publish_packets={len(messages)}")
    print(f"unique_topics={len(topics)}")
    print("topic_prefixes=" + format_counter(topic_prefixes(topics.keys()), 20))
    print("topic_groups=" + format_counter(topic_groups(topics.keys()), 30))
    print(f"retained_packets={sum(1 for message in messages if message.retain)}")
    for topic, count in sorted(topics.items())[:60]:
        print(f"topic={topic} count={count}")
    print_mqtt_inventory(messages)


def probe_websocket(host: str) -> None:
    print("\n[websocket]")
    if not PASSWORD:
        print("skipped=SOLARASSISTANT_PASSWORD is required for WebSocket probing")
        return
    if WEBSOCKET_SCHEME not in {"ws", "wss"}:
        print(f"skipped=unsupported WebSocket scheme {WEBSOCKET_SCHEME!r}")
        return
    print("warning=WebSocket authentication sends the local password in the URL query string; run only on trusted local networks.")
    try:
        with open_websocket_socket(host) as sock:
            websocket_handshake(sock, websocket_host_header(host))
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


def open_websocket_socket(host: str) -> socket.socket:
    raw_sock = socket.create_connection((host, WEBSOCKET_PORT), timeout=5)
    raw_sock.settimeout(10)
    if WEBSOCKET_SCHEME == "wss":
        context = ssl.create_default_context()
        return context.wrap_socket(raw_sock, server_hostname=host)
    return raw_sock


def websocket_host_header(host: str) -> str:
    default_port = 443 if WEBSOCKET_SCHEME == "wss" else 80
    return host if WEBSOCKET_PORT == default_port else f"{host}:{WEBSOCKET_PORT}"


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


def collect_mqtt_messages(sock: socket.socket, seconds: int, max_packets: int) -> list[MqttMessage]:
    messages: list[MqttMessage] = []
    start = time.monotonic()
    while time.monotonic() - start < seconds and len(messages) < max_packets:
        try:
            packet_type, payload = read_mqtt_packet(sock)
        except socket.timeout:
            break
        if packet_type is None:
            break
        if packet_type >> 4 != 3 or len(payload) < 2:
            continue
        flags = packet_type & 0x0F
        qos = (flags & 0x06) >> 1
        retain = bool(flags & 0x01)
        topic_len = struct.unpack("!H", payload[:2])[0]
        payload_index = 2 + topic_len
        topic = payload[2 : 2 + topic_len].decode("utf-8", "replace")
        if qos:
            payload_index += 2
        messages.append(MqttMessage(topic=topic, payload=payload[payload_index:], retain=retain, qos=qos))
    return messages


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


def print_rest_classification(data: list[object]) -> None:
    metrics = [item for item in data if isinstance(item, dict)]
    db_candidates: list[str] = []
    local_only: list[str] = []
    review: list[str] = []

    for metric in metrics:
        topic = str(metric.get("topic") or "").strip("/")
        if not topic:
            continue
        classification = classify_topic(topic, str(metric.get("unit") or ""), str(metric.get("name") or ""))
        if classification == "db_candidate":
            db_candidates.append(topic)
        elif classification == "local_only":
            local_only.append(topic)
        else:
            review.append(topic)

    print("classification_db_candidates=" + ", ".join(sorted(db_candidates)[:80]))
    print("classification_review=" + ", ".join(sorted(review)[:80]))
    print("classification_local_only=" + ", ".join(sorted(local_only)[:80]))


def print_mqtt_inventory(messages: list[MqttMessage]) -> None:
    discovery = parse_homeassistant_discovery(messages)
    solar_topics = sorted({message.topic for message in messages if message.topic.startswith("solar_assistant/")})

    print("\n[mqtt_inventory]")
    print(f"homeassistant_entities={len(discovery)}")
    print(f"solarassistant_state_topics={len(solar_topics)}")
    print("ha_components=" + format_counter(Counter(item["component"] for item in discovery), 30))
    print("device_classes=" + format_counter(Counter(item["device_class"] for item in discovery if item["device_class"]), 30))
    print("state_classes=" + format_counter(Counter(item["state_class"] for item in discovery if item["state_class"]), 30))
    print("units=" + format_counter(Counter(item["unit"] for item in discovery if item["unit"]), 30))
    print("devices=" + ", ".join(format_device(item) for item in unique_devices(discovery)[:30]))

    db_candidates = [item for item in discovery if classify_discovery_entity(item) == "db_candidate"]
    review = [item for item in discovery if classify_discovery_entity(item) == "review"]
    local_only = [item for item in discovery if classify_discovery_entity(item) == "local_only"]

    print("\n[mqtt_db_candidates]")
    for item in db_candidates[:80]:
        print(format_entity(item))

    print("\n[mqtt_review]")
    for item in review[:80]:
        print(format_entity(item))

    print("\n[mqtt_local_only]")
    for item in local_only[:80]:
        print(format_entity(item))


def parse_homeassistant_discovery(messages: list[MqttMessage]) -> list[dict[str, str]]:
    entities: list[dict[str, str]] = []
    latest: dict[str, MqttMessage] = {}
    for message in messages:
        if not is_homeassistant_config_topic(message.topic):
            continue
        latest[message.topic] = message

    for topic, message in sorted(latest.items()):
        try:
            payload = json.loads(message.payload.decode("utf-8"))
        except (UnicodeDecodeError, json.JSONDecodeError):
            continue
        if not isinstance(payload, dict):
            continue

        parts = topic.split("/")
        component = parts[1] if len(parts) > 1 else ""
        state_topic = str(payload.get("stat_t") or payload.get("state_topic") or "")
        command_topic = str(payload.get("cmd_t") or payload.get("command_topic") or "")
        availability_topic = str(payload.get("avty_t") or payload.get("availability_topic") or "")
        unique_id = str(payload.get("uniq_id") or payload.get("unique_id") or "")
        name = str(payload.get("name") or payload.get("object_id") or "")
        device = payload.get("dev") or payload.get("device") or {}
        if not isinstance(device, dict):
            device = {}

        entities.append(
            {
                "topic": topic,
                "component": component,
                "name": name,
                "unique_id": unique_id,
                "device_class": str(payload.get("dev_cla") or payload.get("device_class") or ""),
                "state_class": str(payload.get("stat_cla") or payload.get("state_class") or ""),
                "unit": str(payload.get("unit_of_meas") or payload.get("unit_of_measurement") or ""),
                "icon": str(payload.get("icon") or ""),
                "entity_category": str(payload.get("ent_cat") or payload.get("entity_category") or ""),
                "state_topic": state_topic,
                "command_topic": command_topic,
                "availability_topic": availability_topic,
                "device_name": str(device.get("name") or ""),
                "manufacturer": str(device.get("mf") or device.get("manufacturer") or ""),
                "model": str(device.get("mdl") or device.get("model") or ""),
                "software_version": str(device.get("sw") or device.get("sw_version") or ""),
                "identifiers": format_identifiers(device.get("ids") or device.get("identifiers") or ""),
            }
        )
    return entities


def is_homeassistant_config_topic(topic: str) -> bool:
    return topic.startswith("homeassistant/") and topic.endswith("/config")


def format_identifiers(value: object) -> str:
    if isinstance(value, list):
        return ",".join(str(item) for item in value[:5])
    return str(value or "")


def unique_devices(entities: list[dict[str, str]]) -> list[dict[str, str]]:
    devices: dict[tuple[str, str, str, str], dict[str, str]] = {}
    for item in entities:
        key = (item["device_name"], item["manufacturer"], item["model"], item["identifiers"])
        if any(key):
            devices.setdefault(key, item)
    return list(devices.values())


def format_device(item: dict[str, str]) -> str:
    parts = [item["device_name"] or "unnamed"]
    if item["manufacturer"]:
        parts.append(f"manufacturer={item['manufacturer']}")
    if item["model"]:
        parts.append(f"model={item['model']}")
    if item["software_version"]:
        parts.append(f"sw={item['software_version']}")
    return " [" + "; ".join(parts) + "]"


def format_entity(item: dict[str, str]) -> str:
    parts = [
        f"name={safe_text(item['name'])}",
        f"component={item['component']}",
    ]
    if item["device_class"]:
        parts.append(f"device_class={item['device_class']}")
    if item["state_class"]:
        parts.append(f"state_class={item['state_class']}")
    if item["unit"]:
        parts.append(f"unit={item['unit']}")
    if item["state_topic"]:
        parts.append(f"state_topic={item['state_topic']}")
    if item["command_topic"]:
        parts.append(f"command_topic={item['command_topic']}")
    if item["device_name"]:
        parts.append(f"device={safe_text(item['device_name'])}")
    return "entity " + " ".join(parts)


def safe_text(value: str) -> str:
    return value.replace("\n", " ").replace("\r", " ").strip()[:120] or "unnamed"


def classify_discovery_entity(item: dict[str, str]) -> str:
    state_topic = item["state_topic"]
    return classify_topic(state_topic, item["unit"], item["name"])


def classify_topic(topic: str, unit: str, name: str) -> str:
    normalized = topic.lower().strip("/")
    if normalized.startswith("solar_assistant/"):
        normalized = normalized[len("solar_assistant/") :]
    if normalized.endswith("/state"):
        normalized = normalized[: -len("/state")]

    db_exact = {
        "total/pv_power",
        "total/load_power",
        "total/grid_power",
        "total/battery_power",
        "total/system_power",
        "total/power",
        "total/battery_state_of_charge",
        "total/battery_voltage",
        "total/battery_current",
        "total/battery_capacity",
        "total/grid_voltage",
        "total/grid_frequency",
        "total/ac_output_voltage",
        "total/ac_output_frequency",
        "total/load_percentage",
        "total/inverter_mode",
        "total/output_source_priority",
        "battery_1/voltage",
        "battery_1/current",
        "battery_1/capacity",
        "inverter_1/grid_voltage",
        "inverter_1/grid_frequency",
        "inverter_1/ac_output_voltage",
        "inverter_1/ac_output_frequency",
        "inverter_1/output_voltage",
        "inverter_1/output_frequency",
        "inverter_1/load_percentage",
        "inverter_1/device_mode",
        "inverter_1/output_source_priority",
        "inverter_1/charger_source_priority",
    }
    local_fragments = (
        "wifi",
        "rssi",
        "uptime",
        "availability",
        "firmware",
        "version",
        "serial",
        "status",
        "temperature",
        "warnings",
        "error",
    )
    review_fragments = (
        "energy",
        "charge",
        "capacity",
        "battery",
        "inverter",
        "pv",
        "grid",
        "load",
        "frequency",
        "voltage",
        "current",
        "power",
        "priority",
        "mode",
    )

    if normalized in db_exact:
        return "db_candidate"
    if any(fragment in normalized for fragment in local_fragments):
        return "local_only"
    if unit in {"W", "Wh", "kWh", "V", "A", "Hz", "%"}:
        return "review"
    if any(fragment in normalized for fragment in review_fragments) or any(fragment in name.lower() for fragment in review_fragments):
        return "review"
    return "local_only"


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


def topic_groups(topics: Iterable[str]) -> Counter[str]:
    counts: Counter[str] = Counter()
    for topic in topics:
        parts = topic.split("/")
        if topic.startswith("homeassistant/") and len(parts) >= 2:
            key = "/".join(parts[:2])
        elif topic.startswith("solar_assistant/") and len(parts) >= 2:
            key = "/".join(parts[:2])
        elif len(parts) >= 1:
            key = parts[0]
        else:
            key = topic
        counts[key] += 1
    return counts


def format_counter(counter: Counter[str], limit: int) -> str:
    return ", ".join(f"{key}:{value}" for key, value in counter.most_common(limit))


class MqttAuthError(Exception):
    pass


class WebSocketError(Exception):
    pass


if __name__ == "__main__":
    raise SystemExit(main())
