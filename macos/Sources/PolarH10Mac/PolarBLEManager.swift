#if os(macOS)
import AppKit
import Combine
import CoreBluetooth
import Foundation
import PolarH10MacCore

struct PolarDevice: Identifiable, Hashable {
    let id: UUID
    let name: String
    let rssi: Int
}

struct TracePoint: Identifiable, Equatable {
    let id: UInt64
    let elapsedSeconds: Double
    let value: Double
}

final class PolarBLEManager: NSObject, ObservableObject {
    @Published private(set) var devices: [PolarDevice] = []
    @Published private(set) var isScanning = false
    @Published private(set) var isConnected = false
    @Published private(set) var isConnecting = false
    @Published private(set) var isStreaming = false
    @Published private(set) var connectedDeviceName = "No device connected"
    @Published private(set) var connectedDeviceIdentifier = ""
    @Published private(set) var statusMessage = "Bluetooth is initializing"
    @Published private(set) var batteryPercent: Int?
    @Published private(set) var heartRateBpm: Int?
    @Published private(set) var latestRrMs: Double?
    @Published private(set) var metrics = PolarHealthMetrics()
    @Published private(set) var heartRateTrace: [TracePoint] = []
    @Published private(set) var rrTrace: [TracePoint] = []
    @Published private(set) var ecgTrace: [TracePoint] = []
    @Published private(set) var accelerometerTrace: [TracePoint] = []
    @Published private(set) var eventLog: [String] = []
    @Published private(set) var isRecording = false
    @Published private(set) var recordingFolder: URL?

    private var centralManager: CBCentralManager!
    private var peripherals: [UUID: CBPeripheral] = [:]
    private var connectedPeripheral: CBPeripheral?
    private var pmdControlPoint: CBCharacteristic?
    private var pmdData: CBCharacteristic?
    private var scanStopWorkItem: DispatchWorkItem?
    private var recorder: SessionRecorder?
    private let analyzer = PolarAnalyzer()
    private let startedAt = ProcessInfo.processInfo.systemUptime
    private var nextTraceId: UInt64 = 0

    private let heartRateService = CBUUID(string: PolarGatt.heartRateService)
    private let heartRateMeasurement = CBUUID(string: PolarGatt.heartRateMeasurement)
    private let batteryService = CBUUID(string: PolarGatt.batteryService)
    private let batteryLevel = CBUUID(string: PolarGatt.batteryLevel)
    private let pmdService = CBUUID(string: PolarGatt.pmdService)
    private let pmdControlPointId = CBUUID(string: PolarGatt.pmdControlPoint)
    private let pmdDataId = CBUUID(string: PolarGatt.pmdData)

    override init() {
        super.init()
        centralManager = CBCentralManager(delegate: self, queue: .main)
    }

    deinit {
        scanStopWorkItem?.cancel()
        if recorder?.isRecording == true {
            try? recorder?.stop()
        }
        if let connectedPeripheral {
            centralManager?.cancelPeripheralConnection(connectedPeripheral)
        }
    }

    var bluetoothAuthorizationText: String {
        switch CBManager.authorization {
        case .allowedAlways:
            return "Allowed"
        case .denied:
            return "Denied in System Settings"
        case .restricted:
            return "Restricted"
        case .notDetermined:
            return "Not requested yet"
        @unknown default:
            return "Unknown"
        }
    }

    func scan(duration: TimeInterval = 10) {
        guard centralManager.state == .poweredOn else {
            statusMessage = bluetoothStateMessage(centralManager.state)
            appendLog(statusMessage)
            return
        }

        scanStopWorkItem?.cancel()
        devices.removeAll()
        peripherals.removeAll()
        isScanning = true
        statusMessage = "Scanning for Polar sensors..."
        appendLog("BLE scan started")
        centralManager.scanForPeripherals(
            withServices: nil,
            options: [CBCentralManagerScanOptionAllowDuplicatesKey: true]
        )

        let workItem = DispatchWorkItem { [weak self] in self?.stopScan() }
        scanStopWorkItem = workItem
        DispatchQueue.main.asyncAfter(deadline: .now() + duration, execute: workItem)
    }

    func stopScan() {
        guard isScanning else { return }
        scanStopWorkItem?.cancel()
        scanStopWorkItem = nil
        centralManager.stopScan()
        isScanning = false
        statusMessage = devices.isEmpty
            ? "No Polar sensors found. Wet the strap electrodes and try again."
            : "Found \(devices.count) Polar sensor\(devices.count == 1 ? "" : "s")"
        appendLog("BLE scan stopped with \(devices.count) result(s)")
    }

    func connect(to device: PolarDevice) {
        guard let peripheral = peripherals[device.id] else {
            statusMessage = "The selected device is no longer available; scan again."
            return
        }

        stopScan()
        if let current = connectedPeripheral,
           current.identifier != peripheral.identifier,
           current.state != .disconnected {
            statusMessage = "Disconnect \(connectedDeviceName) before connecting another sensor."
            return
        }
        if connectedPeripheral?.identifier == peripheral.identifier && (isConnected || isConnecting) {
            statusMessage = isConnected ? "Already connected to \(device.name)" : "Already connecting to \(device.name)"
            return
        }

        resetLiveState()
        connectedPeripheral = peripheral
        peripheral.delegate = self
        isConnecting = true
        connectedDeviceName = device.name
        connectedDeviceIdentifier = device.id.uuidString
        statusMessage = "Connecting to \(device.name)..."
        appendLog("Connecting to \(device.name) [\(device.id.uuidString)]")
        centralManager.connect(peripheral, options: nil)
    }

    func disconnect() {
        guard let connectedPeripheral else { return }
        stopRecordingIfNeeded()
        stopPmdStreams()
        centralManager.cancelPeripheralConnection(connectedPeripheral)
        statusMessage = "Disconnecting..."
    }

    func toggleRecording() {
        if isRecording {
            stopRecordingIfNeeded()
            return
        }

        guard isConnected else {
            statusMessage = "Connect a Polar H10 before starting a recording."
            return
        }

        let newRecorder = SessionRecorder(
            deviceName: connectedDeviceName,
            deviceIdentifier: connectedDeviceIdentifier
        )
        do {
            let folder = try newRecorder.start()
            recorder = newRecorder
            recordingFolder = folder
            isRecording = true
            statusMessage = "Recording to \(folder.lastPathComponent)"
            appendLog("Recording started: \(folder.path)")
        } catch {
            statusMessage = "Could not start recording: \(error.localizedDescription)"
            appendLog(statusMessage)
        }
    }

    func revealRecordingFolder() {
        guard let recordingFolder else { return }
        NSWorkspace.shared.activateFileViewerSelecting([recordingFolder])
    }

    func resetAnalysis() {
        analyzer.reset()
        metrics = PolarHealthMetrics()
        rrTrace.removeAll(keepingCapacity: true)
        appendLog("HRV and coherence analysis reset")
    }

    private func stopRecordingIfNeeded() {
        guard let recorder else { return }
        do {
            try recorder.stop()
            recordingFolder = recorder.outputFolder
            statusMessage = "Recording saved to \(recorder.outputFolder?.lastPathComponent ?? "Sessions")"
            appendLog("Recording stopped and saved")
        } catch {
            statusMessage = "Recording stopped with an error: \(error.localizedDescription)"
            appendLog(statusMessage)
        }
        self.recorder = nil
        isRecording = false
    }

    private func resetLiveState() {
        analyzer.reset()
        heartRateBpm = nil
        latestRrMs = nil
        metrics = PolarHealthMetrics()
        batteryPercent = nil
        heartRateTrace.removeAll(keepingCapacity: true)
        rrTrace.removeAll(keepingCapacity: true)
        ecgTrace.removeAll(keepingCapacity: true)
        accelerometerTrace.removeAll(keepingCapacity: true)
        pmdControlPoint = nil
        pmdData = nil
        isStreaming = false
    }

    private func tryStartPmdStreams() {
        guard let peripheral = connectedPeripheral,
              let control = pmdControlPoint,
              let data = pmdData,
              control.isNotifying,
              data.isNotifying,
              !isStreaming
        else { return }

        recorder?.recordProtocol(direction: "out", event: "pmd_start_ecg", detail: "130 Hz / 14 bit")
        peripheral.writeValue(PolarProtocol.startEcgCommand(), for: control, type: .withResponse)
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.35) { [weak self, weak peripheral, weak control] in
            guard let self, let peripheral, let control, self.isConnected else { return }
            self.recorder?.recordProtocol(direction: "out", event: "pmd_start_acc", detail: "200 Hz / 16 bit / 8 g")
            peripheral.writeValue(
                PolarProtocol.startAccelerometerCommand(),
                for: control,
                type: .withResponse
            )
            self.isStreaming = true
            self.statusMessage = "Connected — HR, ECG, and ACC streaming"
            self.appendLog("PMD ECG and ACC stream requests sent")
        }
    }

    private func stopPmdStreams() {
        guard let peripheral = connectedPeripheral, let control = pmdControlPoint else { return }
        peripheral.writeValue(
            PolarProtocol.stopStreamCommand(measurementType: PolarGatt.ecgType),
            for: control,
            type: .withResponse
        )
        peripheral.writeValue(
            PolarProtocol.stopStreamCommand(measurementType: PolarGatt.accType),
            for: control,
            type: .withResponse
        )
        isStreaming = false
    }

    private func ingestHeartRate(_ data: Data) {
        let sample = PolarProtocol.decodeHeartRate(data)
        heartRateBpm = Int(sample.beatsPerMinute)
        if let rr = sample.rrIntervalsMs.last {
            latestRrMs = rr
            appendTrace(rr, to: &rrTrace, limit: 360)
        }
        metrics = analyzer.ingest(rrIntervalsMs: sample.rrIntervalsMs)
        appendTrace(Double(sample.beatsPerMinute), to: &heartRateTrace, limit: 360)
        recorder?.recordHeartRate(sample)
    }

    private func ingestPmd(_ data: Data) {
        do {
            switch try PolarProtocol.decodePmdFrame(data) {
            case let .ecg(frame):
                appendTraceValues(frame.microVolts.map { Double($0) }, to: &ecgTrace, limit: 780)
                recorder?.recordEcg(frame)
            case let .accelerometer(frame):
                appendTraceValues(frame.samples.map(\.magnitudeG), to: &accelerometerTrace, limit: 600)
                recorder?.recordAccelerometer(frame)
            }
        } catch PolarProtocolError.unsupportedFrame {
            return
        } catch {
            appendLog("Skipped malformed PMD frame: \(error.localizedDescription)")
        }
    }

    private func appendTrace(_ value: Double, to points: inout [TracePoint], limit: Int) {
        appendTraceValues([value], to: &points, limit: limit)
    }

    private func appendTraceValues(_ values: [Double], to points: inout [TracePoint], limit: Int) {
        let elapsed = ProcessInfo.processInfo.systemUptime - startedAt
        points.reserveCapacity(min(limit, points.count + values.count))
        for value in values {
            nextTraceId &+= 1
            points.append(
                TracePoint(
                    id: nextTraceId,
                    elapsedSeconds: elapsed,
                    value: value
                )
            )
        }
        if points.count > limit {
            points.removeFirst(points.count - limit)
        }
    }

    private func appendLog(_ message: String) {
        let formatter = DateFormatter()
        formatter.dateFormat = "HH:mm:ss"
        eventLog.insert("\(formatter.string(from: Date()))  \(message)", at: 0)
        if eventLog.count > 80 {
            eventLog.removeLast(eventLog.count - 80)
        }
    }

    private func bluetoothStateMessage(_ state: CBManagerState) -> String {
        switch state {
        case .poweredOn:
            return "Bluetooth is ready"
        case .poweredOff:
            return "Bluetooth is off. Turn it on in System Settings."
        case .unauthorized:
            return "Bluetooth access is denied. Allow PolarH10 in Privacy & Security."
        case .unsupported:
            return "This Mac does not provide a supported Bluetooth LE adapter."
        case .resetting:
            return "Bluetooth is resetting; wait a moment and scan again."
        case .unknown:
            return "Bluetooth state is not available yet."
        @unknown default:
            return "Bluetooth state is unknown."
        }
    }
}

extension PolarBLEManager: CBCentralManagerDelegate {
    func centralManagerDidUpdateState(_ central: CBCentralManager) {
        statusMessage = bluetoothStateMessage(central.state)
        appendLog(statusMessage)
        if central.state != .poweredOn {
            isScanning = false
            isConnecting = false
        }
    }

    func centralManager(
        _ central: CBCentralManager,
        didDiscover peripheral: CBPeripheral,
        advertisementData: [String: Any],
        rssi RSSI: NSNumber
    ) {
        let advertisedName = advertisementData[CBAdvertisementDataLocalNameKey] as? String
        let name = peripheral.name ?? advertisedName ?? "Unnamed BLE device"
        let advertisedServices = advertisementData[CBAdvertisementDataServiceUUIDsKey] as? [CBUUID] ?? []
        let isPolar = name.localizedCaseInsensitiveContains("polar")
            || advertisedServices.contains(heartRateService)
            || advertisedServices.contains(pmdService)
        guard isPolar else { return }

        peripherals[peripheral.identifier] = peripheral
        let device = PolarDevice(id: peripheral.identifier, name: name, rssi: RSSI.intValue)
        if let index = devices.firstIndex(where: { $0.id == device.id }) {
            devices[index] = device
        } else {
            devices.append(device)
        }
        devices.sort { left, right in
            left.rssi == right.rssi ? left.name < right.name : left.rssi > right.rssi
        }
    }

    func centralManager(_ central: CBCentralManager, didConnect peripheral: CBPeripheral) {
        guard connectedPeripheral?.identifier == peripheral.identifier else {
            central.cancelPeripheralConnection(peripheral)
            return
        }
        isConnecting = false
        isConnected = true
        statusMessage = "Connected — discovering H10 services"
        appendLog("Connected to \(peripheral.name ?? peripheral.identifier.uuidString)")
        peripheral.discoverServices([heartRateService, pmdService, batteryService])
    }

    func centralManager(
        _ central: CBCentralManager,
        didFailToConnect peripheral: CBPeripheral,
        error: Error?
    ) {
        guard connectedPeripheral?.identifier == peripheral.identifier else { return }
        isConnecting = false
        isConnected = false
        connectedPeripheral = nil
        statusMessage = "Connection failed: \(error?.localizedDescription ?? "unknown error")"
        appendLog(statusMessage)
    }

    func centralManager(
        _ central: CBCentralManager,
        didDisconnectPeripheral peripheral: CBPeripheral,
        error: Error?
    ) {
        guard connectedPeripheral?.identifier == peripheral.identifier else { return }
        stopRecordingIfNeeded()
        isConnecting = false
        isConnected = false
        isStreaming = false
        pmdControlPoint = nil
        pmdData = nil
        connectedPeripheral = nil
        statusMessage = error.map { "Disconnected: \($0.localizedDescription)" } ?? "Disconnected"
        appendLog(statusMessage)
    }
}

extension PolarBLEManager: CBPeripheralDelegate {
    func peripheral(_ peripheral: CBPeripheral, didDiscoverServices error: Error?) {
        if let error {
            statusMessage = "Service discovery failed: \(error.localizedDescription)"
            appendLog(statusMessage)
            return
        }

        for service in peripheral.services ?? [] {
            switch service.uuid {
            case heartRateService:
                peripheral.discoverCharacteristics([heartRateMeasurement], for: service)
            case pmdService:
                peripheral.discoverCharacteristics([pmdControlPointId, pmdDataId], for: service)
            case batteryService:
                peripheral.discoverCharacteristics([batteryLevel], for: service)
            default:
                continue
            }
        }
    }

    func peripheral(
        _ peripheral: CBPeripheral,
        didDiscoverCharacteristicsFor service: CBService,
        error: Error?
    ) {
        if let error {
            appendLog("Characteristic discovery failed: \(error.localizedDescription)")
            return
        }

        for characteristic in service.characteristics ?? [] {
            switch characteristic.uuid {
            case heartRateMeasurement:
                peripheral.setNotifyValue(true, for: characteristic)
            case pmdControlPointId:
                pmdControlPoint = characteristic
                peripheral.setNotifyValue(true, for: characteristic)
            case pmdDataId:
                pmdData = characteristic
                peripheral.setNotifyValue(true, for: characteristic)
            case batteryLevel:
                peripheral.readValue(for: characteristic)
            default:
                continue
            }
        }
    }

    func peripheral(
        _ peripheral: CBPeripheral,
        didUpdateNotificationStateFor characteristic: CBCharacteristic,
        error: Error?
    ) {
        if let error {
            appendLog("Could not subscribe to \(characteristic.uuid): \(error.localizedDescription)")
            return
        }
        tryStartPmdStreams()
    }

    func peripheral(
        _ peripheral: CBPeripheral,
        didUpdateValueFor characteristic: CBCharacteristic,
        error: Error?
    ) {
        if let error {
            appendLog("Notification error on \(characteristic.uuid): \(error.localizedDescription)")
            return
        }
        guard let data = characteristic.value else { return }

        switch characteristic.uuid {
        case heartRateMeasurement:
            ingestHeartRate(data)
        case pmdDataId:
            ingestPmd(data)
        case pmdControlPointId:
            recorder?.recordProtocol(
                direction: "in",
                event: "pmd_control_response",
                detail: data.map { String(format: "%02X", $0) }.joined(separator: " ")
            )
        case batteryLevel:
            batteryPercent = data.first.map { Int($0) }
        default:
            break
        }
    }

    func peripheral(
        _ peripheral: CBPeripheral,
        didWriteValueFor characteristic: CBCharacteristic,
        error: Error?
    ) {
        if let error {
            appendLog("Write to \(characteristic.uuid) failed: \(error.localizedDescription)")
        }
    }
}
#endif
