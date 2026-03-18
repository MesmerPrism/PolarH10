# Protocol Overview

The Polar H10 exposes two primary BLE services for data access:

1. **Standard Heart Rate Service** (`0x180D`) — broadcasts heart rate and RR intervals
   using the standard Bluetooth SIG Heart Rate Measurement characteristic (`0x2A37`).

2. **Polar Measurement Data (PMD) Service** (`FB005C80-02E7-F387-1CAD-8ACD2D8DF0C8`) —
   a vendor-specific service for high-resolution streaming of ECG, accelerometer, and
   other sensor data.

## Connection Lifecycle

```
Scan → Connect → Discover Services → Request MTU → Enable Notifications → Configure Stream → Receive Data → Stop → Disconnect
```

1. **Scan** for BLE advertisements matching the Polar H10 device name prefix.
2. **Connect** using the device's Bluetooth address.
3. **Discover Services** — enumerate GATT services and find HR and PMD service handles.
4. **Request MTU** — request a larger ATT MTU (e.g., 232 bytes) to receive full PMD
   frames without fragmentation. If the device responds with a smaller MTU, retry with
   ordered fallback candidates.
5. **Enable Notifications** — write to the Client Characteristic Configuration Descriptor
   (CCCD, `0x2902`) on each data characteristic.
6. **Configure Stream** — write PMD control point commands to query settings and start
   measurements.
7. **Receive Data** — process incoming notifications on the HR Measurement and PMD Data
   characteristics.
8. **Stop** — write stop commands and disable notifications.
9. **Disconnect** — close the GATT session and release the Bluetooth device.

## Reference Material

Protocol details in this documentation are derived from:

- The Polar BLE SDK open-source repository (MIT License), specifically the
  [technical documentation directory at tag 4.0.0](https://github.com/polarofficial/polar-ble-sdk/tree/4.0.0/technical_documentation/).
- Observations from the Sensors 2025 paper: Sieciński, S. et al., "The Newer, the More
  Secure? Comparing the Polar Verity Sense and H10 Heart Rate Sensors," *Sensors*, vol. 25,
  no. 7, 2025.
- Direct BLE traffic analysis against Polar H10 firmware.

All protocol descriptions are written in our own words. Consult the original sources for
authoritative definitions.
