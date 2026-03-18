namespace PolarH10.Protocol;

/// <summary>
/// Standard BLE GATT UUIDs and Polar-specific service/characteristic UUIDs for the H10.
/// </summary>
public static class PolarGattIds
{
    // ── Standard BLE services ──────────────────────────────────────────
    /// <summary>Standard Heart Rate Service UUID.</summary>
    public const string HeartRateService = "0000180d-0000-1000-8000-00805f9b34fb";

    /// <summary>Standard Heart Rate Measurement Characteristic UUID (0x2A37).</summary>
    public const string HeartRateMeasurement = "00002a37-0000-1000-8000-00805f9b34fb";

    /// <summary>Standard Battery Service UUID.</summary>
    public const string BatteryService = "0000180f-0000-1000-8000-00805f9b34fb";

    /// <summary>Standard Battery Level Characteristic UUID (0x2A19).</summary>
    public const string BatteryLevel = "00002a19-0000-1000-8000-00805f9b34fb";

    /// <summary>Device Information Service UUID.</summary>
    public const string DeviceInfoService = "0000180a-0000-1000-8000-00805f9b34fb";

    /// <summary>Manufacturer Name String Characteristic UUID (0x2A29).</summary>
    public const string ManufacturerName = "00002a29-0000-1000-8000-00805f9b34fb";

    /// <summary>Model Number String Characteristic UUID (0x2A24).</summary>
    public const string ModelNumber = "00002a24-0000-1000-8000-00805f9b34fb";

    /// <summary>Firmware Revision String Characteristic UUID (0x2A26).</summary>
    public const string FirmwareRevision = "00002a26-0000-1000-8000-00805f9b34fb";

    // ── Polar PMD (Measurement Data) service ──────────────────────────
    /// <summary>Polar PMD Service UUID.</summary>
    public const string PmdService = "fb005c80-02e7-f387-1cad-8acd2d8df0c8";

    /// <summary>PMD Control Point Characteristic UUID (write commands, receive responses).</summary>
    public const string PmdControlPoint = "fb005c81-02e7-f387-1cad-8acd2d8df0c8";

    /// <summary>PMD Data Characteristic UUID (notification stream for ECG/ACC/etc.).</summary>
    public const string PmdData = "fb005c82-02e7-f387-1cad-8acd2d8df0c8";

    // ── Standard BLE descriptors ──────────────────────────────────────
    /// <summary>Client Characteristic Configuration Descriptor UUID (0x2902).</summary>
    public const string CccdDescriptor = "00002902-0000-1000-8000-00805f9b34fb";

    // ── Polar PMD measurement type identifiers ────────────────────────
    /// <summary>ECG measurement type byte.</summary>
    public const byte MeasurementTypeEcg = 0x00;

    /// <summary>ACC (accelerometer) measurement type byte.</summary>
    public const byte MeasurementTypeAcc = 0x02;

    /// <summary>PPG measurement type byte.</summary>
    public const byte MeasurementTypePpg = 0x01;

    /// <summary>PPI measurement type byte.</summary>
    public const byte MeasurementTypePpi = 0x03;

    /// <summary>Gyroscope measurement type byte.</summary>
    public const byte MeasurementTypeGyro = 0x05;

    /// <summary>Magnetometer measurement type byte.</summary>
    public const byte MeasurementTypeMag = 0x06;

    // ── PMD setting type identifiers ──────────────────────────────────
    /// <summary>Sample rate setting type in PMD responses.</summary>
    public const byte SettingTypeSampleRate = 0x00;

    /// <summary>Resolution setting type in PMD responses.</summary>
    public const byte SettingTypeResolution = 0x01;

    /// <summary>Range setting type in PMD responses.</summary>
    public const byte SettingTypeRange = 0x02;

    /// <summary>Channels setting type in PMD responses.</summary>
    public const byte SettingTypeChannels = 0x04;
}
