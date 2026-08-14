import Foundation

public struct PolarHealthMetrics: Equatable, Sendable {
    public var acceptedRrCount = 0
    public var meanRrMs: Double?
    public var meanHeartRateBpm: Double?
    public var rmssdMs: Double?
    public var sdnnMs: Double?
    public var pnn50Percent: Double?
    public var coherenceScore: Double?
    public var coherenceRatio: Double?
    public var peakFrequencyHz: Double?

    public init() {}
}

public final class PolarAnalyzer {
    private let retainedIntervalCount: Int
    private var rrIntervalsMs: [Double] = []

    public init(retainedIntervalCount: Int = 512) {
        self.retainedIntervalCount = max(32, retainedIntervalCount)
    }

    public func reset() {
        rrIntervalsMs.removeAll(keepingCapacity: true)
    }

    @discardableResult
    public func ingest(rrIntervalsMs newValues: [Double]) -> PolarHealthMetrics {
        rrIntervalsMs.append(contentsOf: newValues.filter { value in
            value.isFinite && value >= 300 && value <= 2_000
        })
        if rrIntervalsMs.count > retainedIntervalCount {
            rrIntervalsMs.removeFirst(rrIntervalsMs.count - retainedIntervalCount)
        }
        return metrics()
    }

    public func metrics() -> PolarHealthMetrics {
        var result = PolarHealthMetrics()
        result.acceptedRrCount = rrIntervalsMs.count
        guard !rrIntervalsMs.isEmpty else { return result }

        let mean = rrIntervalsMs.reduce(0, +) / Double(rrIntervalsMs.count)
        result.meanRrMs = mean
        result.meanHeartRateBpm = mean > 0 ? 60_000 / mean : nil

        if rrIntervalsMs.count >= 2 {
            let differences = zip(rrIntervalsMs.dropFirst(), rrIntervalsMs).map { current, previous in
                current - previous
            }
            result.rmssdMs = (differences.map { $0 * $0 }.reduce(0, +) / Double(differences.count)).squareRoot()
            result.pnn50Percent = 100 * Double(differences.filter { abs($0) > 50 }.count) / Double(differences.count)

            let variance = rrIntervalsMs.reduce(0) { partial, value in
                let delta = value - mean
                return partial + delta * delta
            } / Double(rrIntervalsMs.count - 1)
            result.sdnnMs = variance.squareRoot()
        }

        if let coherence = Self.calculateCoherence(rrIntervalsMs) {
            result.coherenceScore = coherence.score
            result.coherenceRatio = coherence.ratio
            result.peakFrequencyHz = coherence.peakFrequencyHz
        }
        return result
    }

    private static func calculateCoherence(
        _ intervalsMs: [Double]
    ) -> (score: Double, ratio: Double, peakFrequencyHz: Double)? {
        guard intervalsMs.count >= 32 else { return nil }

        var eventTimes: [Double] = [0]
        eventTimes.reserveCapacity(intervalsMs.count)
        for interval in intervalsMs.dropLast() {
            eventTimes.append((eventTimes.last ?? 0) + interval / 1_000)
        }

        guard let duration = eventTimes.last, duration >= 25 else { return nil }
        let sampleRate = 4.0
        let sampleCount = Int(duration * sampleRate)
        guard sampleCount >= 100 else { return nil }

        var resampled: [Double] = []
        resampled.reserveCapacity(sampleCount)
        var sourceIndex = 0
        for sampleIndex in 0..<sampleCount {
            let time = Double(sampleIndex) / sampleRate
            while sourceIndex + 1 < eventTimes.count && eventTimes[sourceIndex + 1] < time {
                sourceIndex += 1
            }

            let nextIndex = min(sourceIndex + 1, intervalsMs.count - 1)
            let startTime = eventTimes[min(sourceIndex, eventTimes.count - 1)]
            let endTime = eventTimes[min(nextIndex, eventTimes.count - 1)]
            let fraction = endTime > startTime ? (time - startTime) / (endTime - startTime) : 0
            let startValue = intervalsMs[sourceIndex]
            let endValue = intervalsMs[nextIndex]
            resampled.append(startValue + (endValue - startValue) * min(1, max(0, fraction)))
        }

        let mean = resampled.reduce(0, +) / Double(resampled.count)
        let windowed = resampled.enumerated().map { index, value -> Double in
            let window = 0.5 - 0.5 * cos(2 * .pi * Double(index) / Double(resampled.count - 1))
            return (value - mean) * window
        }

        let resolution = sampleRate / Double(windowed.count)
        let minimumBin = max(1, Int(ceil(0.04 / resolution)))
        let maximumBin = min(windowed.count / 2, Int(floor(0.40 / resolution)))
        guard maximumBin > minimumBin else { return nil }

        var powers: [(frequency: Double, power: Double)] = []
        for bin in minimumBin...maximumBin {
            let frequency = Double(bin) * resolution
            var real = 0.0
            var imaginary = 0.0
            for (index, value) in windowed.enumerated() {
                let angle = 2 * Double.pi * Double(bin * index) / Double(windowed.count)
                real += value * cos(angle)
                imaginary -= value * sin(angle)
            }
            powers.append((frequency, real * real + imaginary * imaginary))
        }

        let resonanceBand = powers.filter { $0.frequency <= 0.26 }
        guard let peak = resonanceBand.max(by: { $0.power < $1.power }) else { return nil }
        let peakPower = powers
            .filter { abs($0.frequency - peak.frequency) <= 0.015 }
            .reduce(0) { $0 + $1.power }
        let totalPower = powers.reduce(0) { $0 + $1.power }
        let remainingPower = max(0.000_001, totalPower - peakPower)
        let ratio = peakPower / remainingPower
        let normalizedScore = ratio / (ratio + 1)
        return (min(1, max(0, normalizedScore)), ratio, peak.frequency)
    }
}
