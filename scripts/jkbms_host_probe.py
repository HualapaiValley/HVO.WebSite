#!/usr/bin/env python3
import argparse
import json
import sys
import time

import dbus
import dbus.mainloop.glib

try:
    from gi.repository import GLib
except ImportError:
    GLib = None

BLUEZ = "org.bluez"
PROPS = "org.freedesktop.DBus.Properties"
OBJMGR = "org.freedesktop.DBus.ObjectManager"
ADAPTER = "org.bluez.Adapter1"
DEVICE = "org.bluez.Device1"
GATT_SERVICE = "org.bluez.GattService1"
GATT_CHAR = "org.bluez.GattCharacteristic1"

JK_SERVICE_UUID = "0000ffe0-0000-1000-8000-00805f9b34fb"
JK_NOTIFY_UUID = "0000ffe1-0000-1000-8000-00805f9b34fb"
JK_WRITE_UUID = "0000ffe2-0000-1000-8000-00805f9b34fb"

JK_COMMANDS = {
    "activate": bytes.fromhex("AA 55 90 EB 95 00 00 00 00 00 00 00 00 00 00 00 00 00 00 0F"),
    "cell": bytes.fromhex("AA 55 90 EB 96 00 00 00 00 00 00 00 00 00 00 00 00 00 00 10"),
    "device": bytes.fromhex("AA 55 90 EB 97 00 00 00 00 00 00 00 00 00 00 00 00 00 00 11"),
}

DEFAULT_BANKS = [
    "C8:47:8C:E4:58:37",
    "C8:47:8C:E4:56:B0",
    "C8:47:8C:E4:55:71",
    "C8:47:8C:E4:54:B1",
    "C8:47:8C:EC:1B:0F",
    "C8:47:8C:EC:1E:B5",
    "C8:47:8C:EA:FF:2F",
]


def device_path(adapter_name: str, address: str) -> str:
    return f"/org/bluez/{adapter_name}/dev_" + address.replace(":", "_").upper()


def get_managed_objects(bus):
    obj = dbus.Interface(bus.get_object(BLUEZ, "/"), OBJMGR)
    return obj.GetManagedObjects()


def get_prop(bus, path: str, iface: str, name: str):
    props = dbus.Interface(bus.get_object(BLUEZ, path), PROPS)
    return props.Get(iface, name)


def path_exists(bus, path: str) -> bool:
    return path in get_managed_objects(bus)


def wait_for_device_path(bus, adapter, path: str, timeout_seconds: int) -> bool:
    if path_exists(bus, path):
        return True

    print(f"scanning for {path}", flush=True)
    adapter.StartDiscovery()
    deadline = time.time() + timeout_seconds
    try:
        while time.time() < deadline:
            if path_exists(bus, path):
                return True
            time.sleep(0.5)
        return False
    finally:
        try:
            adapter.StopDiscovery()
        except Exception as ex:
            print(f"warning: StopDiscovery failed: {ex}", flush=True)


def wait_for_property(bus, path: str, iface: str, name: str, expected, timeout_seconds: int) -> bool:
    deadline = time.time() + timeout_seconds
    while time.time() < deadline:
        if get_prop(bus, path, iface, name) == expected:
            return True
        time.sleep(0.5)
    return False


def wait_for_disconnect(bus, path: str, timeout_seconds: int) -> bool:
    deadline = time.time() + timeout_seconds
    while time.time() < deadline:
        try:
            if not bool(get_prop(bus, path, DEVICE, "Connected")):
                return True
        except Exception:
            return True
        time.sleep(0.5)
    return False


def normalize_uuid(value: str) -> str:
    value = value.lower()
    if len(value) == 4:
        return f"0000{value}-0000-1000-8000-00805f9b34fb"
    return value


def find_gatt_objects(bus, device_obj_path: str):
    objects = get_managed_objects(bus)
    service_path = None
    notify_path = None
    write_path = None

    for path, ifaces in objects.items():
        path = str(path)
        if not path.startswith(device_obj_path + "/"):
            continue

        service = ifaces.get(GATT_SERVICE)
        if service and str(service.get("UUID", "")).lower() == JK_SERVICE_UUID:
            service_path = path

    if service_path is None:
        return None, None, None

    for path, ifaces in objects.items():
        path = str(path)
        if not path.startswith(service_path + "/"):
            continue

        characteristic = ifaces.get(GATT_CHAR)
        if not characteristic:
            continue

        uuid = str(characteristic.get("UUID", "")).lower()
        if uuid == JK_NOTIFY_UUID:
            notify_path = path
        if uuid == JK_WRITE_UUID:
            write_path = path

    return service_path, notify_path, write_path


def try_accumulate_frame(buffer: bytearray, chunk: bytes):
    if len(chunk) >= 4 and chunk[:4] == b"\x55\xAA\xEB\x90":
        buffer.clear()

    buffer.extend(chunk)

    if len(buffer) < 4:
        return None

    sof = buffer.find(b"\x55\xAA\xEB\x90")
    if sof < 0:
        if len(buffer) > 3:
            del buffer[:-3]
        return None

    if sof > 0:
        del buffer[:sof]

    if len(buffer) < 300:
        return None

    frame = bytes(buffer[:300])
    del buffer[:300]
    return frame


def make_byte_array(data: bytes):
    return dbus.Array([dbus.Byte(b) for b in data], signature="y")


def listen_with_loop(bus, listen_seconds: int):
    if GLib is not None:
        loop = GLib.MainLoop()
        GLib.timeout_add_seconds(listen_seconds, lambda: (loop.quit(), False)[1])
        loop.run()
        return

    connection = bus.get_connection()
    deadline = time.time() + listen_seconds
    while time.time() < deadline:
        connection.read_write_dispatch(100)


def probe_once(bus, adapter_name: str, address: str, scan_timeout: int, connect_timeout: int,
               listen_seconds: int, command_name: str, write_char_name: str):
    adapter_path = f"/org/bluez/{adapter_name}"
    target_path = device_path(adapter_name, address)
    adapter = dbus.Interface(bus.get_object(BLUEZ, adapter_path), ADAPTER)

    try:
        adapter.StopDiscovery()
    except Exception:
        pass

    print(f"adapter={adapter_name}", flush=True)
    print(f"address={address}", flush=True)
    print(f"device_path={target_path}", flush=True)

    if not wait_for_device_path(bus, adapter, target_path, scan_timeout):
        print("result=not-found", flush=True)
        return {"status": "not-found", "chunks": 0, "frames": 0}

    device = dbus.Interface(bus.get_object(BLUEZ, target_path), DEVICE)
    print(f"connected_before={bool(get_prop(bus, target_path, DEVICE, 'Connected'))}", flush=True)
    print(f"services_before={bool(get_prop(bus, target_path, DEVICE, 'ServicesResolved'))}", flush=True)

    print("connect=starting", flush=True)
    try:
        device.Connect(timeout=connect_timeout)
    except Exception as ex:
        print(f"result=connect-failed error={type(ex).__name__}:{ex}", flush=True)
        return {"status": "connect-failed", "error": f"{type(ex).__name__}:{ex}", "chunks": 0, "frames": 0}

    if not wait_for_property(bus, target_path, DEVICE, "Connected", dbus.Boolean(True), connect_timeout):
        print("result=connected-timeout", flush=True)
        return {"status": "connected-timeout", "chunks": 0, "frames": 0}

    if not wait_for_property(bus, target_path, DEVICE, "ServicesResolved", dbus.Boolean(True), connect_timeout):
        print("result=services-timeout", flush=True)
        return {"status": "services-timeout", "chunks": 0, "frames": 0}

    print("connect=ready", flush=True)

    service_path, notify_path, write_path = find_gatt_objects(bus, target_path)
    print(f"service_path={service_path}", flush=True)
    print(f"notify_path={notify_path}", flush=True)
    print(f"write_path={write_path}", flush=True)

    if notify_path is None:
        print("result=notify-char-not-found", flush=True)
        return {"status": "notify-char-not-found", "chunks": 0, "frames": 0}

    selected_write_path = notify_path if write_char_name == "ffe1" else write_path
    if selected_write_path is None:
        print("result=write-char-not-found", flush=True)
        return {"status": "write-char-not-found", "chunks": 0, "frames": 0}

    notify_char = dbus.Interface(bus.get_object(BLUEZ, notify_path), GATT_CHAR)
    write_char = dbus.Interface(bus.get_object(BLUEZ, selected_write_path), GATT_CHAR)

    chunks = []
    frame_buffer = bytearray()
    full_frames = []

    def on_properties_changed(interface, changed, invalidated, path=None):
        if interface != GATT_CHAR or "Value" not in changed:
            return
        value = bytes(changed["Value"])
        chunks.append(value)
        print(f"notify len={len(value)} hex={value.hex()}", flush=True)
        frame = try_accumulate_frame(frame_buffer, value)
        if frame is not None:
            full_frames.append(frame)
            print(f"frame len={len(frame)} type=0x{frame[4]:02x} crc=0x{frame[-1]:02x}", flush=True)

    bus.add_signal_receiver(
        on_properties_changed,
        dbus_interface=PROPS,
        signal_name="PropertiesChanged",
        path=notify_path,
        path_keyword="path")

    status = "ok"
    try:
        notify_char.StartNotify()
        command = JK_COMMANDS[command_name]
        print(f"notify=started command={command_name} write_char={write_char_name}", flush=True)
        write_char.WriteValue(make_byte_array(command), dbus.Dictionary({}, signature="sv"))
        listen_with_loop(bus, listen_seconds)
        if not full_frames:
            status = "no-frame"
    finally:
        bus.remove_signal_receiver(
            on_properties_changed,
            dbus_interface=PROPS,
            signal_name="PropertiesChanged",
            path=notify_path,
            path_keyword="path")
        try:
            notify_char.StopNotify()
        except Exception:
            pass
        try:
            device.Disconnect()
        except Exception:
            pass
        wait_for_disconnect(bus, target_path, 10)

    print(f"result={status} chunks={len(chunks)} frames={len(full_frames)}", flush=True)
    return {"status": status, "chunks": len(chunks), "frames": len(full_frames)}


def soak(bus, adapter_name: str, addresses, cycles: int, pause_seconds: float,
         scan_timeout: int, connect_timeout: int, listen_seconds: int,
         command_name: str, write_char_name: str):
    stats = {
        address: {
            "ok": 0,
            "not-found": 0,
            "connect-failed": 0,
            "connected-timeout": 0,
            "services-timeout": 0,
            "notify-char-not-found": 0,
            "write-char-not-found": 0,
            "no-frame": 0,
            "other": 0,
        }
        for address in addresses
    }

    for cycle in range(1, cycles + 1):
        print(f"=== cycle {cycle}/{cycles} adapter={adapter_name} ===", flush=True)
        for address in addresses:
            print(f"--- begin {address} ---", flush=True)
            try:
                result = probe_once(
                    bus,
                    adapter_name,
                    address,
                    scan_timeout,
                    connect_timeout,
                    listen_seconds,
                    command_name,
                    write_char_name)
            except Exception as ex:
                print(f"result=exception error={type(ex).__name__}:{ex}", flush=True)
                result = {"status": "other", "error": f"{type(ex).__name__}:{ex}"}

            status = result.get("status", "other")
            if status not in stats[address]:
                status = "other"
            stats[address][status] += 1
            print(f"--- end {address} status={status} ---", flush=True)
            if pause_seconds > 0:
                time.sleep(pause_seconds)

    print("summary=" + json.dumps({"adapter": adapter_name, "cycles": cycles, "stats": stats}, sort_keys=True), flush=True)
    return 0


def main():
    parser = argparse.ArgumentParser(description="Minimal bare-host JK BMS BlueZ probe")
    parser.add_argument("address", help="BLE MAC address, e.g. C8:47:8C:EA:FF:2F")
    parser.add_argument("--adapter", default="hci2")
    parser.add_argument("--scan-timeout", type=int, default=20)
    parser.add_argument("--connect-timeout", type=int, default=20)
    parser.add_argument("--listen-seconds", type=int, default=12)
    parser.add_argument("--command", choices=sorted(JK_COMMANDS.keys()), default="cell")
    parser.add_argument("--write-char", choices=["ffe1", "ffe2"], default="ffe1")
    parser.add_argument("--all-banks", action="store_true")
    parser.add_argument("--cycles", type=int, default=1)
    parser.add_argument("--pause-seconds", type=float, default=1.0)
    args = parser.parse_args()

    dbus.mainloop.glib.DBusGMainLoop(set_as_default=True)
    bus = dbus.SystemBus()

    if args.all_banks:
        return soak(
            bus,
            args.adapter,
            DEFAULT_BANKS,
            args.cycles,
            args.pause_seconds,
            args.scan_timeout,
            args.connect_timeout,
            args.listen_seconds,
            args.command,
            args.write_char)

    result = probe_once(
        bus,
        args.adapter,
        args.address,
        args.scan_timeout,
        args.connect_timeout,
        args.listen_seconds,
        args.command,
        args.write_char)
    return 0 if result["status"] == "ok" else 1


if __name__ == "__main__":
    sys.exit(main())
