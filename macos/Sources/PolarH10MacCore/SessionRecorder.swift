import Foundation

public final class SessionRecorder {
    public let sessionId = UUID().uuidString.replacingOccurrences(of: "-", with: "").lowercased()
    public let deviceName: String
    public let deviceIdentifier: String
    public let startedAt: Date
    public private(set) var outputFolder: URL?
    public private(set) var isRecording = false

    private var heartRateSampleCount = 0
    private var ecgFrameCount = 0
    private var accelerometerFrameCount = 0
    private var transcriptEntryCount = 0
    private var heartRateFile: FileHandle?
    private var ecgFile: FileHandle?
    private var accelerometerFile: FileHandle?
    private var protocolFile: FileHandle?

    public init(deviceName: String, deviceIdentifier: String, startedAt: Date = Date()) {
        self.deviceName = deviceName
        self.deviceIdentifier = deviceIdentifier
        self.startedAt = startedAt
    }

    deinit {
        try? closeFiles()
    }

    @discardableResult
    public func start(baseFolder: URL? = nil) throws -> URL {
        guard !isRecording else {
            return outputFolder ?? defaultBaseFolder()
        }

        let base = baseFolder ?? defaultBaseFolder()
        let folder = base.appendingPathComponent(folderName(), isDirectory: true)
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)

        heartRateFile = try makeFile(
            at: folder.appendingPathComponent("hr_rr.csv"),
            header: "device_address,device_alias,heart_rate_bpm,rr_intervals_ms\n"
        )
        ecgFile = try makeFile(
            at: folder.appendingPathComponent("ecg.csv"),
            header: "device_address,device_alias,sensor_timestamp_ns,received_utc_ticks,sample_index,microvolts\n"
        )
        accelerometerFile = try makeFile(
            at: folder.appendingPathComponent("acc.csv"),
            header: "device_address,device_alias,sensor_timestamp_ns,received_utc_ticks,sample_index,x_mg,y_mg,z_mg\n"
        )
        protocolFile = try makeFile(
            at: folder.appendingPathComponent("protocol.jsonl"),
            header: ""
        )

        outputFolder = folder
        isRecording = true
        try writeMetadata(savedAt: nil)
        recordProtocol(direction: "system", event: "recording_started", detail: deviceName)
        return folder
    }

    public func stop() throws {
        guard isRecording else { return }
        recordProtocol(direction: "system", event: "recording_stopped", detail: deviceName)
        try closeFiles()
        isRecording = false
        try writeMetadata(savedAt: Date())
    }

    public func recordHeartRate(_ sample: HeartRateSample) {
        guard isRecording else { return }
        let rr = sample.rrIntervalsMs.map {
            String(format: "%.2f", locale: Self.csvLocale, $0)
        }.joined(separator: ";")
        let row = "\(csv(deviceIdentifier)),\(csv(deviceName)),\(sample.beatsPerMinute),\(rr)\n"
        write(row, to: heartRateFile)
        heartRateSampleCount += 1
    }

    public func recordEcg(_ frame: EcgFrame, receivedAt: Date = Date()) {
        guard isRecording else { return }
        let receivedTicks = dotNetUtcTicks(receivedAt)
        var rows = ""
        rows.reserveCapacity(frame.microVolts.count * 96)
        for (index, value) in frame.microVolts.enumerated() {
            rows.append("\(csv(deviceIdentifier)),\(csv(deviceName)),\(frame.sensorTimestampNs),\(receivedTicks),\(index),\(value)\n")
        }
        write(rows, to: ecgFile)
        ecgFrameCount += 1
    }

    public func recordAccelerometer(_ frame: AccelerometerFrame, receivedAt: Date = Date()) {
        guard isRecording else { return }
        let receivedTicks = dotNetUtcTicks(receivedAt)
        var rows = ""
        rows.reserveCapacity(frame.samples.count * 112)
        for (index, sample) in frame.samples.enumerated() {
            rows.append("\(csv(deviceIdentifier)),\(csv(deviceName)),\(frame.sensorTimestampNs),\(receivedTicks),\(index),\(sample.xMilliG),\(sample.yMilliG),\(sample.zMilliG)\n")
        }
        write(rows, to: accelerometerFile)
        accelerometerFrameCount += 1
    }

    public func recordProtocol(direction: String, event: String, detail: String) {
        guard isRecording else { return }
        let object: [String: Any] = [
            "timestampUtc": Self.iso8601.string(from: Date()),
            "direction": direction,
            "event": event,
            "detail": detail,
        ]
        guard let data = try? JSONSerialization.data(withJSONObject: object),
              var line = String(data: data, encoding: .utf8)
        else { return }
        line.append("\n")
        write(line, to: protocolFile)
        transcriptEntryCount += 1
    }

    private func folderName() -> String {
        let formatter = DateFormatter()
        formatter.locale = Locale(identifier: "en_US_POSIX")
        formatter.timeZone = TimeZone(secondsFromGMT: 0)
        formatter.dateFormat = "yyyyMMdd-HHmmss'Z'"
        let deviceTag = deviceName
            .replacingOccurrences(of: "[^A-Za-z0-9._-]", with: "-", options: .regularExpression)
        return "\(formatter.string(from: startedAt))_\(deviceTag.isEmpty ? deviceIdentifier : deviceTag)"
    }

    private func defaultBaseFolder() -> URL {
        let documents = FileManager.default.urls(for: .documentDirectory, in: .userDomainMask).first
            ?? FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Documents")
        return documents
            .appendingPathComponent("PolarH10", isDirectory: true)
            .appendingPathComponent("Sessions", isDirectory: true)
    }

    private func makeFile(at url: URL, header: String) throws -> FileHandle {
        FileManager.default.createFile(atPath: url.path, contents: Data())
        let handle = try FileHandle(forWritingTo: url)
        if !header.isEmpty {
            try handle.write(contentsOf: Data(header.utf8))
        }
        return handle
    }

    private func closeFiles() throws {
        for handle in [heartRateFile, ecgFile, accelerometerFile, protocolFile] {
            try handle?.synchronize()
            try handle?.close()
        }
        heartRateFile = nil
        ecgFile = nil
        accelerometerFile = nil
        protocolFile = nil
    }

    private func writeMetadata(savedAt: Date?) throws {
        guard let outputFolder else { return }
        var object: [String: Any] = [
            "SchemaVersion": 2,
            "SessionId": sessionId,
            "DeviceName": deviceName,
            "DeviceAddress": deviceIdentifier,
            "DeviceAlias": deviceName,
            "StartedAtUtc": Self.iso8601.string(from: startedAt),
            "HrRrSampleCount": heartRateSampleCount,
            "EcgFrameCount": ecgFrameCount,
            "AccFrameCount": accelerometerFrameCount,
            "TranscriptEntryCount": transcriptEntryCount,
            "Platform": "macOS",
        ]
        if let savedAt {
            object["SavedAtUtc"] = Self.iso8601.string(from: savedAt)
        }
        let data = try JSONSerialization.data(withJSONObject: object, options: [.prettyPrinted, .sortedKeys])
        try data.write(to: outputFolder.appendingPathComponent("session.json"), options: .atomic)
    }

    private func write(_ string: String, to handle: FileHandle?) {
        guard let handle else { return }
        try? handle.write(contentsOf: Data(string.utf8))
    }

    private func csv(_ value: String) -> String {
        guard value.contains(",") || value.contains("\"") || value.contains("\n") else { return value }
        return "\"\(value.replacingOccurrences(of: "\"", with: "\"\""))\""
    }

    private func dotNetUtcTicks(_ date: Date) -> Int64 {
        Int64((date.timeIntervalSince1970 * 10_000_000).rounded()) + 621_355_968_000_000_000
    }

    private static let iso8601: ISO8601DateFormatter = {
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return formatter
    }()

    private static let csvLocale = Locale(identifier: "en_US_POSIX")
}
