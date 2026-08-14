import Foundation
import XCTest
@testable import PolarH10MacCore

final class PolarProtocolTests: XCTestCase {
    func testDecodesEightBitHeartRateAndRrIntervals() {
        let sample = PolarProtocol.decodeHeartRate(Data([0x10, 72, 0x00, 0x04, 0x33, 0x03]))

        XCTAssertEqual(sample.beatsPerMinute, 72)
        XCTAssertEqual(sample.rrIntervalsMs.count, 2)
        XCTAssertEqual(sample.rrIntervalsMs[0], 1_000, accuracy: 0.001)
        XCTAssertEqual(sample.rrIntervalsMs[1], 799.804_687_5, accuracy: 0.001)
    }

    func testDecodesSignedTwentyFourBitEcgSamples() throws {
        let frame = Data([
            0x00,
            0x08, 0x07, 0x06, 0x05, 0x04, 0x03, 0x02, 0x01,
            0x00,
            0x01, 0x00, 0x00,
            0xFF, 0xFF, 0xFF,
            0x00, 0x00, 0x80,
        ])

        guard case let .ecg(decoded) = try PolarProtocol.decodePmdFrame(frame) else {
            return XCTFail("Expected an ECG frame")
        }
        XCTAssertEqual(decoded.sensorTimestampNs, 0x0102_0304_0506_0708)
        XCTAssertEqual(decoded.microVolts, [1, -1, -8_388_608])
    }

    func testDecodesUncompressedAccelerometerSamples() throws {
        let frame = Data([
            0x02,
            0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x01,
            0xE8, 0x03,
            0x18, 0xFC,
            0xF4, 0x01,
        ])

        guard case let .accelerometer(decoded) = try PolarProtocol.decodePmdFrame(frame) else {
            return XCTFail("Expected an accelerometer frame")
        }
        XCTAssertEqual(decoded.samples, [
            AccelerometerSample(xMilliG: 1_000, yMilliG: -1_000, zMilliG: 500),
        ])
    }

    func testBuildsCommandsInH10FieldOrder() {
        XCTAssertEqual(
            [UInt8](PolarProtocol.startEcgCommand()),
            [0x02, 0x00, 0x00, 0x01, 130, 0, 0x01, 0x01, 14, 0]
        )
        XCTAssertEqual(
            [UInt8](PolarProtocol.startAccelerometerCommand()),
            [0x02, 0x02, 0x02, 0x01, 8, 0, 0x00, 0x01, 200, 0, 0x01, 0x01, 16, 0]
        )
    }
}

final class PolarAnalysisTests: XCTestCase {
    func testCalculatesTimeDomainHrv() {
        let analyzer = PolarAnalyzer()
        let metrics = analyzer.ingest(rrIntervalsMs: [800, 810, 790, 820])

        XCTAssertEqual(metrics.acceptedRrCount, 4)
        XCTAssertEqual(metrics.meanRrMs ?? 0, 805, accuracy: 0.001)
        XCTAssertEqual(metrics.rmssdMs ?? 0, 21.602_468, accuracy: 0.001)
        XCTAssertEqual(metrics.sdnnMs ?? 0, 12.909_944, accuracy: 0.001)
        XCTAssertEqual(metrics.pnn50Percent ?? -1, 0, accuracy: 0.001)
    }

    func testRejectsPhysiologicallyImplausibleIntervals() {
        let analyzer = PolarAnalyzer()
        let metrics = analyzer.ingest(rrIntervalsMs: [299, 300, 2_000, 2_001, .nan])

        XCTAssertEqual(metrics.acceptedRrCount, 2)
        XCTAssertEqual(metrics.meanRrMs, 1_150)
    }

    func testCoherenceDetectsAResonanceLikeRrOscillation() {
        let intervals = (0..<180).map { index in
            1_000 + 100 * sin(2 * .pi * 0.1 * Double(index))
        }
        let metrics = PolarAnalyzer(retainedIntervalCount: 256).ingest(rrIntervalsMs: intervals)

        XCTAssertNotNil(metrics.coherenceScore)
        XCTAssertGreaterThan(metrics.coherenceScore ?? 0, 0.65)
        XCTAssertEqual(metrics.peakFrequencyHz ?? 0, 0.1, accuracy: 0.02)
    }
}

final class SessionRecorderTests: XCTestCase {
    func testWritesTheCompatibleSessionFileFamily() throws {
        let root = FileManager.default.temporaryDirectory
            .appendingPathComponent("PolarH10MacTests-\(UUID().uuidString)", isDirectory: true)
        defer { try? FileManager.default.removeItem(at: root) }

        let recorder = SessionRecorder(
            deviceName: "Polar H10 Test",
            deviceIdentifier: "AABBCCDD-0000-1111-2222-334455667788"
        )
        let folder = try recorder.start(baseFolder: root)
        recorder.recordHeartRate(
            HeartRateSample(beatsPerMinute: 60, rrIntervalsMs: [1_000])
        )
        recorder.recordEcg(EcgFrame(sensorTimestampNs: 42, microVolts: [12, -8]))
        recorder.recordAccelerometer(
            AccelerometerFrame(
                sensorTimestampNs: 43,
                samples: [AccelerometerSample(xMilliG: 1, yMilliG: 2, zMilliG: 3)]
            )
        )
        recorder.recordProtocol(direction: "in", event: "test", detail: "fixture")
        try recorder.stop()

        for fileName in ["session.json", "hr_rr.csv", "ecg.csv", "acc.csv", "protocol.jsonl"] {
            XCTAssertTrue(
                FileManager.default.fileExists(atPath: folder.appendingPathComponent(fileName).path),
                "Expected \(fileName)"
            )
        }

        let metadataData = try Data(contentsOf: folder.appendingPathComponent("session.json"))
        let metadata = try XCTUnwrap(
            JSONSerialization.jsonObject(with: metadataData) as? [String: Any]
        )
        XCTAssertEqual(metadata["SchemaVersion"] as? Int, 2)
        XCTAssertEqual(metadata["Platform"] as? String, "macOS")
        XCTAssertEqual(metadata["HrRrSampleCount"] as? Int, 1)
        XCTAssertEqual(metadata["EcgFrameCount"] as? Int, 1)
        XCTAssertEqual(metadata["AccFrameCount"] as? Int, 1)
        XCTAssertNotNil(metadata["SavedAtUtc"])
    }
}
