import Foundation

public enum PolarGatt {
    public static let heartRateService = "180D"
    public static let heartRateMeasurement = "2A37"
    public static let batteryService = "180F"
    public static let batteryLevel = "2A19"
    public static let pmdService = "FB005C80-02E7-F387-1CAD-8ACD2D8DF0C8"
    public static let pmdControlPoint = "FB005C81-02E7-F387-1CAD-8ACD2D8DF0C8"
    public static let pmdData = "FB005C82-02E7-F387-1CAD-8ACD2D8DF0C8"

    public static let ecgType: UInt8 = 0x00
    public static let accType: UInt8 = 0x02
}

public struct HeartRateSample: Equatable, Sendable {
    public let beatsPerMinute: UInt16
    public let rrIntervalsMs: [Double]

    public init(beatsPerMinute: UInt16, rrIntervalsMs: [Double]) {
        self.beatsPerMinute = beatsPerMinute
        self.rrIntervalsMs = rrIntervalsMs
    }
}

public struct AccelerometerSample: Equatable, Sendable {
    public let xMilliG: Int16
    public let yMilliG: Int16
    public let zMilliG: Int16

    public init(xMilliG: Int16, yMilliG: Int16, zMilliG: Int16) {
        self.xMilliG = xMilliG
        self.yMilliG = yMilliG
        self.zMilliG = zMilliG
    }

    public var magnitudeG: Double {
        let x = Double(xMilliG) / 1_000
        let y = Double(yMilliG) / 1_000
        let z = Double(zMilliG) / 1_000
        return (x * x + y * y + z * z).squareRoot()
    }
}

public struct EcgFrame: Equatable, Sendable {
    public let sensorTimestampNs: UInt64
    public let microVolts: [Int32]

    public init(sensorTimestampNs: UInt64, microVolts: [Int32]) {
        self.sensorTimestampNs = sensorTimestampNs
        self.microVolts = microVolts
    }
}

public struct AccelerometerFrame: Equatable, Sendable {
    public let sensorTimestampNs: UInt64
    public let samples: [AccelerometerSample]

    public init(sensorTimestampNs: UInt64, samples: [AccelerometerSample]) {
        self.sensorTimestampNs = sensorTimestampNs
        self.samples = samples
    }
}

public enum PolarProtocolError: Error, Equatable, LocalizedError {
    case frameTooShort
    case invalidEcgLength
    case invalidAccelerometerLength
    case unsupportedFrame

    public var errorDescription: String? {
        switch self {
        case .frameTooShort:
            return "The Polar PMD frame is too short."
        case .invalidEcgLength:
            return "The Polar ECG frame has an invalid payload length."
        case .invalidAccelerometerLength:
            return "The Polar accelerometer frame has an invalid payload length."
        case .unsupportedFrame:
            return "The Polar PMD frame type is not supported."
        }
    }
}

public enum PolarProtocol {
    private static let pmdHeaderSize = 10

    public static func decodeHeartRate(_ data: Data) -> HeartRateSample {
        let bytes = [UInt8](data)
        guard bytes.count >= 2 else {
            return HeartRateSample(beatsPerMinute: 0, rrIntervalsMs: [])
        }

        let flags = bytes[0]
        let usesUInt16HeartRate = (flags & 0x01) != 0
        let hasEnergyExpended = (flags & 0x08) != 0
        let hasRrIntervals = (flags & 0x10) != 0
        var index: Int
        let heartRate: UInt16

        if usesUInt16HeartRate {
            guard bytes.count >= 3 else {
                return HeartRateSample(beatsPerMinute: 0, rrIntervalsMs: [])
            }
            heartRate = UInt16(bytes[1]) | (UInt16(bytes[2]) << 8)
            index = 3
        } else {
            heartRate = UInt16(bytes[1])
            index = 2
        }

        if hasEnergyExpended {
            index += 2
        }

        var rrIntervals: [Double] = []
        if hasRrIntervals {
            while index + 1 < bytes.count {
                let raw = UInt16(bytes[index]) | (UInt16(bytes[index + 1]) << 8)
                rrIntervals.append(Double(raw) * 1_000 / 1_024)
                index += 2
            }
        }

        return HeartRateSample(beatsPerMinute: heartRate, rrIntervalsMs: rrIntervals)
    }

    public static func decodePmdFrame(_ data: Data) throws -> PmdFrame {
        let bytes = [UInt8](data)
        guard bytes.count >= pmdHeaderSize else {
            throw PolarProtocolError.frameTooShort
        }

        let measurementType = bytes[0]
        let frameType = bytes[9]
        let timestamp = readUInt64LE(bytes, at: 1)

        switch measurementType {
        case PolarGatt.ecgType where frameType == 0x00:
            let payloadCount = bytes.count - pmdHeaderSize
            guard payloadCount % 3 == 0 else {
                throw PolarProtocolError.invalidEcgLength
            }

            var samples: [Int32] = []
            samples.reserveCapacity(payloadCount / 3)
            var offset = pmdHeaderSize
            while offset + 2 < bytes.count {
                var raw = Int32(bytes[offset])
                    | (Int32(bytes[offset + 1]) << 8)
                    | (Int32(bytes[offset + 2]) << 16)
                if (raw & 0x0080_0000) != 0 {
                    raw |= Int32(bitPattern: 0xFF00_0000)
                }
                samples.append(raw)
                offset += 3
            }
            return .ecg(EcgFrame(sensorTimestampNs: timestamp, microVolts: samples))

        case PolarGatt.accType:
            let compressed = (frameType & 0x80) != 0
            let frameTypeBase = frameType & 0x7F
            let samples = try compressed || frameTypeBase != 0x01
                ? decodeCompressedAccelerometer(bytes)
                : decodeUncompressedAccelerometer(bytes)
            return .accelerometer(
                AccelerometerFrame(sensorTimestampNs: timestamp, samples: samples)
            )

        default:
            throw PolarProtocolError.unsupportedFrame
        }
    }

    public static func startEcgCommand(sampleRate: UInt16 = 130, resolution: UInt16 = 14) -> Data {
        Data([
            0x02, PolarGatt.ecgType,
            0x00, 0x01, UInt8(sampleRate & 0xFF), UInt8(sampleRate >> 8),
            0x01, 0x01, UInt8(resolution & 0xFF), UInt8(resolution >> 8),
        ])
    }

    public static func startAccelerometerCommand(
        sampleRate: UInt16 = 200,
        resolution: UInt16 = 16,
        rangeG: UInt16 = 8
    ) -> Data {
        Data([
            0x02, PolarGatt.accType,
            0x02, 0x01, UInt8(rangeG & 0xFF), UInt8(rangeG >> 8),
            0x00, 0x01, UInt8(sampleRate & 0xFF), UInt8(sampleRate >> 8),
            0x01, 0x01, UInt8(resolution & 0xFF), UInt8(resolution >> 8),
        ])
    }

    public static func stopStreamCommand(measurementType: UInt8) -> Data {
        Data([0x03, measurementType])
    }

    private static func decodeUncompressedAccelerometer(
        _ bytes: [UInt8]
    ) throws -> [AccelerometerSample] {
        let payloadCount = bytes.count - pmdHeaderSize
        guard payloadCount % 6 == 0 else {
            throw PolarProtocolError.invalidAccelerometerLength
        }

        var samples: [AccelerometerSample] = []
        samples.reserveCapacity(payloadCount / 6)
        var offset = pmdHeaderSize
        while offset + 5 < bytes.count {
            samples.append(
                AccelerometerSample(
                    xMilliG: readInt16LE(bytes, at: offset),
                    yMilliG: readInt16LE(bytes, at: offset + 2),
                    zMilliG: readInt16LE(bytes, at: offset + 4)
                )
            )
            offset += 6
        }
        return samples
    }

    private static func decodeCompressedAccelerometer(
        _ bytes: [UInt8]
    ) throws -> [AccelerometerSample] {
        guard bytes.count >= 16 else {
            throw PolarProtocolError.invalidAccelerometerLength
        }

        var x = Int(readInt16LE(bytes, at: 10))
        var y = Int(readInt16LE(bytes, at: 12))
        var z = Int(readInt16LE(bytes, at: 14))
        var samples = [makeAccelerometerSample(x: x, y: y, z: z)]
        var bitOffset = 0
        let remainingBits = (bytes.count - 16) * 8
        let sampleCount = remainingBits / 48

        for _ in 0..<sampleCount {
            x += readSignedBits(bytes, startByte: 16, bitOffset: &bitOffset, width: 16)
            y += readSignedBits(bytes, startByte: 16, bitOffset: &bitOffset, width: 16)
            z += readSignedBits(bytes, startByte: 16, bitOffset: &bitOffset, width: 16)
            samples.append(makeAccelerometerSample(x: x, y: y, z: z))
        }

        return samples
    }

    private static func makeAccelerometerSample(x: Int, y: Int, z: Int) -> AccelerometerSample {
        AccelerometerSample(
            xMilliG: Int16(clamping: x),
            yMilliG: Int16(clamping: y),
            zMilliG: Int16(clamping: z)
        )
    }

    private static func readUInt64LE(_ bytes: [UInt8], at offset: Int) -> UInt64 {
        var value: UInt64 = 0
        for index in 0..<8 {
            value |= UInt64(bytes[offset + index]) << UInt64(index * 8)
        }
        return value
    }

    private static func readInt16LE(_ bytes: [UInt8], at offset: Int) -> Int16 {
        let raw = UInt16(bytes[offset]) | (UInt16(bytes[offset + 1]) << 8)
        return Int16(bitPattern: raw)
    }

    private static func readSignedBits(
        _ bytes: [UInt8],
        startByte: Int,
        bitOffset: inout Int,
        width: Int
    ) -> Int {
        var value: UInt64 = 0
        var bitsRead = 0

        while bitsRead < width {
            let absoluteBit = bitOffset + bitsRead
            let byteIndex = startByte + absoluteBit / 8
            guard byteIndex < bytes.count else { break }
            let bitInByte = absoluteBit % 8
            let available = min(8 - bitInByte, width - bitsRead)
            let mask = UInt8((1 << available) - 1)
            let chunk = (bytes[byteIndex] >> bitInByte) & mask
            value |= UInt64(chunk) << UInt64(bitsRead)
            bitsRead += available
        }

        bitOffset += width
        let signBit = UInt64(1) << UInt64(width - 1)
        if (value & signBit) != 0 {
            let fullRange = UInt64(1) << UInt64(width)
            return Int(Int64(value) - Int64(fullRange))
        }
        return Int(value)
    }
}

public enum PmdFrame: Equatable, Sendable {
    case ecg(EcgFrame)
    case accelerometer(AccelerometerFrame)
}
