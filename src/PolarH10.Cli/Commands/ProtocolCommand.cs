using System.CommandLine;
using PolarH10.Protocol;

namespace PolarH10.Cli.Commands;

internal static class ProtocolCommand
{
    public static Command Create()
    {
        var cmd = new Command("protocol", "Print a concise developer protocol reference");

        var markdownCmd = new Command("markdown", "Print protocol reference as Markdown");
        markdownCmd.SetHandler(() =>
        {
            Console.WriteLine("""
                # Polar H10 PMD Protocol Reference

                > Unofficial reference. Not affiliated with or endorsed by Polar.
                > Derived from independent analysis and public research.

                ## GATT Services

                | Service          | UUID                                   |
                |------------------|----------------------------------------|
                | Heart Rate       | 0000180d-0000-1000-8000-00805f9b34fb   |
                | PMD              | fb005c80-02e7-f387-1cad-8acd2d8df0c8   |
                | Battery          | 0000180f-0000-1000-8000-00805f9b34fb   |
                | Device Info      | 0000180a-0000-1000-8000-00805f9b34fb   |

                ## PMD Characteristics

                | Characteristic   | UUID                                   | Properties       |
                |------------------|----------------------------------------|------------------|
                | PMD Control Pt   | fb005c81-02e7-f387-1cad-8acd2d8df0c8   | Write, Notify    |
                | PMD Data         | fb005c82-02e7-f387-1cad-8acd2d8df0c8   | Notify           |

                ## Notification Enable Sequence

                1. Connect to the device
                2. Discover services and characteristics
                3. Write CCCD descriptor (0x2902) = 0x0100 on HR Measurement (0x2A37)
                4. Write CCCD descriptor (0x2902) = 0x0100 on PMD Control Point
                5. Write CCCD descriptor (0x2902) = 0x0100 on PMD Data
                6. Send GET_SETTINGS command to PMD Control Point
                7. Parse settings response for available sample rates, resolutions, ranges
                8. Send START command with desired settings

                ## PMD Control Point Commands

                All commands are written to the PMD Control Point characteristic.

                ### Get Settings (opcode 0x01)

                ```
                [0x01] [measurement_type]
                ```

                Measurement types: ECG=0x00, PPG=0x01, ACC=0x02, PPI=0x03, Gyro=0x05, Mag=0x06

                ### Start Stream (opcode 0x02)

                ```
                [0x02] [measurement_type] [setting_type count value_le16]...
                ```

                Setting types: SampleRate=0x00, Resolution=0x01, Range=0x02, Channels=0x04

                ### Stop Stream (opcode 0x03)

                ```
                [0x03] [measurement_type]
                ```

                ## PMD Data Frame Layout

                ```
                Byte 0:     Measurement type (ECG=0x00, ACC=0x02)
                Bytes 1-8:  Sensor timestamp (64-bit LE, nanoseconds)
                Byte 9:     Frame type (bit 7 = compressed flag)
                Bytes 10+:  Payload (samples)
                ```

                ### ECG Payload (uncompressed, frame type 0x00)

                Each sample is 3 bytes, little-endian, signed 24-bit integer (microvolts).
                Typical: 130 Hz sample rate, 14-bit resolution.

                ### ACC Payload (uncompressed, frame type 0x01)

                Each sample is 6 bytes: X, Y, Z as 16-bit signed LE integers (milli-g).
                Typical: 200 Hz sample rate, 16-bit resolution, 8g range.

                ### ACC Payload (compressed)

                Reference sample (6 bytes) followed by bit-packed signed deltas.
                Delta bit width matches the resolution setting.

                ## Heart Rate Measurement (standard BLE 0x2A37)

                ```
                Byte 0: Flags
                  Bit 0: 0=8-bit HR, 1=16-bit HR
                  Bit 3: Energy Expended present
                  Bit 4: RR-Interval present
                Byte 1 (or 1-2): Heart rate value
                Following: Optional energy expended (2 bytes), then RR intervals (2 bytes each, 1/1024 s)
                ```

                ## References

                - Polar BLE SDK technical documentation (for protocol context):
                  https://github.com/polarofficial/polar-ble-sdk/tree/master/technical_documentation
                - PMD specification versioned reference (cited in academic literature):
                  https://github.com/polarofficial/polar-ble-sdk/blob/4.0.0/technical_documentation/Polar_Measurement_Data_Specification.pdf
                - Bluetooth SIG Heart Rate Profile specification
                - "The Newer, the More Secure? Standards-Compliant Bluetooth Low Energy
                  Man-in-the-Middle Attacks on Fitness Trackers" (Sensors, 2025)
                  — references the Polar PMD spec as a secondary source
                """);
        });

        cmd.Add(markdownCmd);
        return cmd;
    }
}
