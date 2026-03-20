---
title: GATT Service & Characteristic Map
description: UUID and measurement-type reference for the standard Heart Rate Service and Polar PMD service.
summary: Use this page when you need the service, characteristic, CCCD, and measurement identifiers in one place.
nav_label: GATT Map
nav_group: Internals
nav_order: 20
---

# GATT Service & Characteristic Map

## Heart Rate Service

| Item                  | UUID / Handle            | Notes                                    |
|-----------------------|--------------------------|------------------------------------------|
| Service               | `0x180D`                 | Bluetooth SIG standard                   |
| HR Measurement        | `0x2A37`                 | Notify — heart rate + optional RR values |

## Polar Measurement Data (PMD) Service

| Item                  | UUID                                           | Notes                                      |
|-----------------------|------------------------------------------------|--------------------------------------------|
| Service               | `FB005C80-02E7-F387-1CAD-8ACD2D8DF0C8`        | Vendor-specific                            |
| PMD Control Point     | `FB005C81-02E7-F387-1CAD-8ACD2D8DF0C8`        | Write + Indicate — commands & responses    |
| PMD Data              | `FB005C82-02E7-F387-1CAD-8ACD2D8DF0C8`        | Notify — streaming measurement frames      |

## Client Characteristic Configuration Descriptor (CCCD)

UUID `0x2902`. Must be written with value `0x01 0x00` (notifications) or `0x02 0x00`
(indications) on each characteristic before data will flow.

- HR Measurement → enable notifications
- PMD Control Point → enable indications
- PMD Data → enable notifications

## Measurement Type Identifiers

Used in PMD commands and data frames:

| Type          | Byte Value |
|---------------|------------|
| ECG           | `0x00`     |
| PPG           | `0x01`     |
| Accelerometer | `0x02`     |
| PPI           | `0x03`     |
| Gyroscope     | `0x05`     |
| Magnetometer  | `0x06`     |

> These identifiers appear in the Polar BLE SDK source under MIT License.
> Values here are documented from observed BLE traffic and the SDK's open-source code.
