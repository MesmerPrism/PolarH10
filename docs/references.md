# References

## Primary Sources

1. **Polar BLE SDK** (MIT License)
   Repository: <https://github.com/polarofficial/polar-ble-sdk>
   Technical documentation (tag 4.0.0):
   <https://github.com/polarofficial/polar-ble-sdk/tree/4.0.0/technical_documentation/>

   The SDK's open-source code and technical documentation provide the authoritative
   reference for the Polar Measurement Data (PMD) service protocol, including
   measurement type identifiers, control point command structures, and data frame
   encoding formats.

2. **Polar Measurement Data Specification for 3rd Party** (PDF)
   Published by Polar Electro. This document describes the PMD service interface
   for third-party developers. It is referenced here for protocol comprehension;
   content in this repository's documentation is written independently.

3. **Sieciński, S.; Kostka, P.S.; Piaseczna, N.J.; Janik, S.; Delgado-Prieto, M.;
   Boczar, T.** "The Newer, the More Secure? Comparing the Polar Verity Sense and
   H10 Heart Rate Sensors." *Sensors* 2025, 25, 2005.
   <https://doi.org/10.3390/s25072005>

   This paper evaluates the Polar H10 and Verity Sense in terms of data quality and
   measurement reliability, providing independent validation of ECG and accelerometer
   data characteristics.

## Bluetooth SIG Specifications

- **Heart Rate Service** (Service UUID `0x180D`):
  Bluetooth SIG Assigned Numbers and GATT Specification Supplement.
- **Heart Rate Measurement Characteristic** (`0x2A37`):
  Format defined in the GATT Specification Supplement, Section 3.106.
- **Client Characteristic Configuration Descriptor** (`0x2902`):
  Defined in the Bluetooth Core Specification, Vol 3, Part G, Section 3.3.3.3.

## License Note

This repository is an independent implementation. Protocol knowledge was derived from
the sources above, the Polar BLE SDK's MIT-licensed code, and direct BLE traffic
analysis. No proprietary Polar documentation is reproduced verbatim.
