#if os(macOS)
import Foundation
import SwiftUI

private enum TelemetryView: String, CaseIterable, Identifiable {
    case heartRate = "Heart rate"
    case ecg = "ECG"
    case accelerometer = "ACC magnitude"

    var id: String { rawValue }
}

struct ContentView: View {
    @EnvironmentObject private var manager: PolarBLEManager
    @State private var selectedTelemetry: TelemetryView = .heartRate

    var body: some View {
        NavigationSplitView {
            deviceRail
                .navigationSplitViewColumnWidth(min: 245, ideal: 285, max: 340)
        } detail: {
            detailSurface
        }
        .toolbar {
            ToolbarItem(placement: .primaryAction) {
                Button {
                    manager.isScanning ? manager.stopScan() : manager.scan()
                } label: {
                    Label(
                        manager.isScanning ? "Stop scan" : "Scan",
                        systemImage: manager.isScanning ? "stop.fill" : "antenna.radiowaves.left.and.right"
                    )
                }
                .keyboardShortcut("r", modifiers: [.command])
            }
        }
    }

    private var deviceRail: some View {
        VStack(spacing: 0) {
            VStack(alignment: .leading, spacing: 8) {
                Text("DEVICES")
                    .font(.caption.weight(.bold))
                    .foregroundStyle(.secondary)
                    .tracking(1.6)
                Text(manager.isScanning ? "Scanning nearby..." : "Polar sensors")
                    .font(.title2.bold())
                Text("Bluetooth: \(manager.bluetoothAuthorizationText)")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            .padding(20)

            Divider()

            if manager.devices.isEmpty {
                VStack(spacing: 12) {
                    Image(systemName: "heart.slash")
                        .font(.system(size: 34))
                        .foregroundStyle(.secondary)
                    Text("No sensors yet")
                        .font(.headline)
                    Text("Wear the strap, wet its electrodes, then scan.")
                        .multilineTextAlignment(.center)
                        .foregroundStyle(.secondary)
                    Button("Scan for Polar H10") { manager.scan() }
                }
                .padding(24)
                .frame(maxHeight: .infinity)
            } else {
                List(manager.devices) { device in
                    DeviceRow(device: device, isConnected: manager.connectedDeviceIdentifier == device.id.uuidString)
                        .contentShape(Rectangle())
                        .onTapGesture { manager.connect(to: device) }
                        .contextMenu {
                            Button("Connect") { manager.connect(to: device) }
                        }
                }
                .listStyle(.sidebar)
            }

            Divider()
            VStack(alignment: .leading, spacing: 5) {
                Label("Direct CoreBluetooth transport", systemImage: "bolt.horizontal.circle")
                Text("Identifiers are macOS UUIDs; Apple does not expose BLE MAC addresses.")
                    .foregroundStyle(.secondary)
            }
            .font(.caption)
            .padding(16)
        }
        .background(Color(nsColor: .windowBackgroundColor))
    }

    private var detailSurface: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 18) {
                connectionHeader
                statusStrip
                metricGrid
                telemetryPanel
                analysisPanel
                recordingPanel
                activityPanel
                researchNotice
            }
            .padding(24)
            .frame(maxWidth: 1_180, alignment: .topLeading)
        }
        .background(Color(nsColor: .underPageBackgroundColor))
    }

    private var connectionHeader: some View {
        HStack(alignment: .center, spacing: 16) {
            VStack(alignment: .leading, spacing: 5) {
                Text("POLAR H10 // macOS")
                    .font(.caption.weight(.bold))
                    .foregroundStyle(Color.accentColor)
                    .tracking(1.5)
                Text(manager.connectedDeviceName)
                    .font(.largeTitle.bold())
                    .lineLimit(1)
                if !manager.connectedDeviceIdentifier.isEmpty {
                    Text(manager.connectedDeviceIdentifier)
                        .font(.caption.monospaced())
                        .foregroundStyle(.secondary)
                }
            }

            Spacer()

            if manager.isConnecting {
                ProgressView()
                    .controlSize(.small)
            }

            if manager.isConnected {
                Button("Disconnect", role: .destructive) { manager.disconnect() }
            }
        }
    }

    private var statusStrip: some View {
        HStack(spacing: 12) {
            Circle()
                .fill(manager.isConnected ? Color.green : manager.isConnecting ? Color.orange : Color.secondary)
                .frame(width: 9, height: 9)
            Text(manager.statusMessage)
                .font(.callout.weight(.medium))
            Spacer()
            if let battery = manager.batteryPercent {
                Label("\(battery)%", systemImage: batteryIcon(battery))
                    .font(.callout.monospacedDigit())
            }
            if manager.isStreaming {
                Label("PMD live", systemImage: "waveform.path.ecg")
                    .font(.callout.weight(.semibold))
                    .foregroundStyle(Color.green)
            }
        }
        .padding(.horizontal, 14)
        .padding(.vertical, 11)
        .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 10))
    }

    private var metricGrid: some View {
        LazyVGrid(columns: Array(repeating: GridItem(.flexible(), spacing: 12), count: 4), spacing: 12) {
            MetricCard(
                eyebrow: "HEART RATE",
                value: manager.heartRateBpm.map { String($0) } ?? "—",
                unit: "bpm",
                tint: .red
            )
            MetricCard(
                eyebrow: "LATEST RR",
                value: formatted(manager.latestRrMs, digits: 0),
                unit: "ms",
                tint: .blue
            )
            MetricCard(
                eyebrow: "RMSSD",
                value: formatted(manager.metrics.rmssdMs, digits: 1),
                unit: "ms",
                tint: .green
            )
            MetricCard(
                eyebrow: "COHERENCE",
                value: formatted(manager.metrics.coherenceScore, digits: 2),
                unit: "0–1",
                tint: .orange
            )
        }
    }

    private var telemetryPanel: some View {
        Panel(title: "Live telemetry", subtitle: telemetrySubtitle) {
            Picker("Telemetry", selection: $selectedTelemetry) {
                ForEach(TelemetryView.allCases) { item in
                    Text(item.rawValue).tag(item)
                }
            }
            .pickerStyle(.segmented)
            .labelsHidden()

            LineTraceChart(points: selectedTrace, color: selectedTraceColor)
                .frame(height: 220)
                .overlay(alignment: .topLeading) {
                    if selectedTrace.isEmpty {
                        Text(manager.isConnected ? "Waiting for samples..." : "Connect a Polar H10 to begin")
                            .font(.callout)
                            .foregroundStyle(.secondary)
                            .padding(12)
                    }
                }
        }
    }

    private var analysisPanel: some View {
        Panel(
            title: "RR analysis",
            subtitle: "Rolling accepted intervals; coherence becomes available after at least 25 seconds and 32 RR samples."
        ) {
            LazyVGrid(columns: Array(repeating: GridItem(.flexible(), spacing: 16), count: 4), spacing: 14) {
                DetailMetric(label: "Accepted RR", value: "\(manager.metrics.acceptedRrCount)")
                DetailMetric(label: "Mean NN", value: formatted(manager.metrics.meanRrMs, digits: 1, suffix: " ms"))
                DetailMetric(label: "SDNN", value: formatted(manager.metrics.sdnnMs, digits: 1, suffix: " ms"))
                DetailMetric(label: "pNN50", value: formatted(manager.metrics.pnn50Percent, digits: 1, suffix: "%"))
                DetailMetric(label: "Mean HR", value: formatted(manager.metrics.meanHeartRateBpm, digits: 1, suffix: " bpm"))
                DetailMetric(label: "Peak frequency", value: formatted(manager.metrics.peakFrequencyHz, digits: 3, suffix: " Hz"))
                DetailMetric(label: "Coherence ratio", value: formatted(manager.metrics.coherenceRatio, digits: 2))
                Button("Reset analysis") { manager.resetAnalysis() }
                    .frame(maxWidth: .infinity, alignment: .leading)
            }
        }
    }

    private var recordingPanel: some View {
        Panel(
            title: "Session capture",
            subtitle: "Writes the same session.json, hr_rr.csv, ecg.csv, acc.csv, and protocol.jsonl family used by the repo."
        ) {
            HStack(spacing: 12) {
                Button {
                    manager.toggleRecording()
                } label: {
                    Label(
                        manager.isRecording ? "Stop and save" : "Start recording",
                        systemImage: manager.isRecording ? "stop.circle.fill" : "record.circle"
                    )
                }
                .buttonStyle(.borderedProminent)
                .tint(manager.isRecording ? .red : .accentColor)
                .disabled(!manager.isConnected && !manager.isRecording)

                if manager.isRecording {
                    Label("Recording", systemImage: "circle.fill")
                        .foregroundStyle(.red)
                        .font(.callout.weight(.bold))
                }

                Spacer()

                if let folder = manager.recordingFolder {
                    Text(folder.lastPathComponent)
                        .font(.caption.monospaced())
                        .foregroundStyle(.secondary)
                        .lineLimit(1)
                    Button("Show in Finder") { manager.revealRecordingFolder() }
                }
            }
        }
    }

    private var activityPanel: some View {
        Panel(title: "Activity", subtitle: "Connection, subscription, and recording events from this run.") {
            if manager.eventLog.isEmpty {
                Text("No events yet")
                    .foregroundStyle(.secondary)
            } else {
                VStack(alignment: .leading, spacing: 6) {
                    ForEach(Array(manager.eventLog.prefix(10).enumerated()), id: \.offset) { _, line in
                        Text(line)
                            .font(.caption.monospaced())
                            .textSelection(.enabled)
                    }
                }
                .frame(maxWidth: .infinity, alignment: .leading)
            }
        }
    }

    private var researchNotice: some View {
        Text("Research preview. Unofficial and not affiliated with Polar Electro. Metrics are operator aids, not medical measurements or clinical interpretation.")
            .font(.caption)
            .foregroundStyle(.secondary)
            .frame(maxWidth: .infinity, alignment: .leading)
            .padding(.bottom, 8)
    }

    private var selectedTrace: [TracePoint] {
        switch selectedTelemetry {
        case .heartRate: manager.heartRateTrace
        case .ecg: manager.ecgTrace
        case .accelerometer: manager.accelerometerTrace
        }
    }

    private var selectedTraceColor: Color {
        switch selectedTelemetry {
        case .heartRate: .red
        case .ecg: .blue
        case .accelerometer: .green
        }
    }

    private var telemetrySubtitle: String {
        switch selectedTelemetry {
        case .heartRate: "Heart Rate Service notifications in beats per minute."
        case .ecg: "Polar PMD ECG samples at 130 Hz in microvolts."
        case .accelerometer: "Vector magnitude from Polar PMD ACC samples at 200 Hz."
        }
    }

    private func formatted(_ value: Double?, digits: Int, suffix: String = "") -> String {
        guard let value, value.isFinite else { return "—" }
        return String(format: "%.\(digits)f", value) + suffix
    }

    private func batteryIcon(_ percent: Int) -> String {
        switch percent {
        case 76...: "battery.100percent"
        case 51...: "battery.75percent"
        case 26...: "battery.50percent"
        case 10...: "battery.25percent"
        default: "battery.0percent"
        }
    }
}

private struct DeviceRow: View {
    let device: PolarDevice
    let isConnected: Bool

    var body: some View {
        HStack(spacing: 11) {
            Image(systemName: isConnected ? "heart.circle.fill" : "heart.circle")
                .font(.title2)
                .foregroundStyle(isConnected ? Color.green : Color.accentColor)
            VStack(alignment: .leading, spacing: 3) {
                Text(device.name)
                    .font(.headline)
                    .lineLimit(1)
                Text(device.id.uuidString)
                    .font(.caption2.monospaced())
                    .foregroundStyle(.secondary)
                    .lineLimit(1)
            }
            Spacer(minLength: 6)
            Text("\(device.rssi)")
                .font(.caption.monospacedDigit())
                .foregroundStyle(.secondary)
        }
        .padding(.vertical, 6)
    }
}

private struct MetricCard: View {
    let eyebrow: String
    let value: String
    let unit: String
    let tint: Color

    var body: some View {
        VStack(alignment: .leading, spacing: 9) {
            Text(eyebrow)
                .font(.caption2.weight(.bold))
                .tracking(1.2)
                .foregroundStyle(.secondary)
            HStack(alignment: .lastTextBaseline, spacing: 7) {
                Text(value)
                    .font(.system(size: 34, weight: .bold, design: .rounded))
                    .monospacedDigit()
                    .lineLimit(1)
                    .minimumScaleFactor(0.65)
                Text(unit)
                    .font(.caption.weight(.semibold))
                    .foregroundStyle(.secondary)
            }
        }
        .frame(maxWidth: .infinity, minHeight: 84, alignment: .leading)
        .padding(16)
        .background(Color(nsColor: .controlBackgroundColor), in: RoundedRectangle(cornerRadius: 12))
        .overlay(alignment: .top) {
            Rectangle()
                .fill(tint)
                .frame(height: 4)
                .clipShape(RoundedRectangle(cornerRadius: 12))
        }
        .overlay {
            RoundedRectangle(cornerRadius: 12)
                .stroke(Color.primary.opacity(0.08), lineWidth: 1)
        }
    }
}

private struct DetailMetric: View {
    let label: String
    let value: String

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            Text(label)
                .font(.caption)
                .foregroundStyle(.secondary)
            Text(value)
                .font(.headline.monospacedDigit())
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }
}

private struct Panel<Content: View>: View {
    let title: String
    let subtitle: String
    let content: Content

    init(title: String, subtitle: String, @ViewBuilder content: () -> Content) {
        self.title = title
        self.subtitle = subtitle
        self.content = content()
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            VStack(alignment: .leading, spacing: 3) {
                Text(title)
                    .font(.title2.bold())
                Text(subtitle)
                    .font(.callout)
                    .foregroundStyle(.secondary)
            }
            content
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(18)
        .background(Color(nsColor: .controlBackgroundColor), in: RoundedRectangle(cornerRadius: 12))
        .overlay {
            RoundedRectangle(cornerRadius: 12)
                .stroke(Color.primary.opacity(0.08), lineWidth: 1)
        }
    }
}

private struct LineTraceChart: View {
    let points: [TracePoint]
    let color: Color

    var body: some View {
        Canvas { context, size in
            context.stroke(gridPath(size: size), with: .color(Color.secondary.opacity(0.14)), lineWidth: 0.6)
            guard points.count >= 2 else { return }

            let values = points.map(\.value)
            guard let rawMin = values.min(), let rawMax = values.max() else { return }
            let span = max(0.000_001, rawMax - rawMin)
            let padding = span * 0.10
            let minimum = rawMin - padding
            let maximum = rawMax + padding
            let denominator = max(0.000_001, maximum - minimum)

            var path = Path()
            for (index, point) in points.enumerated() {
                let x = size.width * CGFloat(index) / CGFloat(points.count - 1)
                let normalized = (point.value - minimum) / denominator
                let y = size.height * CGFloat(1 - normalized)
                if index == 0 {
                    path.move(to: CGPoint(x: x, y: y))
                } else {
                    path.addLine(to: CGPoint(x: x, y: y))
                }
            }
            context.stroke(path, with: .color(color), lineWidth: 1.6)
        }
        .background(Color.primary.opacity(0.025), in: RoundedRectangle(cornerRadius: 8))
        .overlay {
            RoundedRectangle(cornerRadius: 8)
                .stroke(Color.primary.opacity(0.08), lineWidth: 1)
        }
    }

    private func gridPath(size: CGSize) -> Path {
        var grid = Path()
        for division in 1..<5 {
            let x = size.width * CGFloat(division) / 5
            grid.move(to: CGPoint(x: x, y: 0))
            grid.addLine(to: CGPoint(x: x, y: size.height))
        }
        for division in 1..<4 {
            let y = size.height * CGFloat(division) / 4
            grid.move(to: CGPoint(x: 0, y: y))
            grid.addLine(to: CGPoint(x: size.width, y: y))
        }
        return grid
    }
}
#endif
