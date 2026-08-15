#if os(macOS)
import AppKit
import SwiftUI

private final class AppDelegate: NSObject, NSApplicationDelegate {
    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool {
        true
    }
}

@main
struct PolarH10MacApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) private var appDelegate
    @StateObject private var manager = PolarBLEManager()

    var body: some Scene {
        WindowGroup("Polar H10 // Telemetry Unit") {
            ContentView()
                .environmentObject(manager)
                .frame(minWidth: 1_080, minHeight: 720)
        }
        .defaultSize(width: 1_300, height: 860)
        .windowStyle(.titleBar)
    }
}
#else
import Foundation

@main
struct PolarH10MacUnsupportedHost {
    static func main() {
        print("PolarH10Mac is a macOS application. Build this target on macOS 13 or later.")
    }
}
#endif
